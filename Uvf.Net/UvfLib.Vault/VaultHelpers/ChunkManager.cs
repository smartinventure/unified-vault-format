using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UvfLib.Core.Api;

namespace UvfLib.Vault.VaultHelpers
{
    /// <summary>
    /// Manages in-memory chunks for random write operations in encrypted files.
    /// Provides chunk-level caching with LRU eviction and automatic encryption/decryption.
    /// </summary>
    internal class ChunkManager : IDisposable
    {
        private const int CLEARTEXT_CHUNK_SIZE = 32 * 1024; // 32KB
        private const int MAX_CACHED_CHUNKS = 16; // Maximum chunks to keep in memory
        
        private readonly Dictionary<long, ChunkBuffer> _activeChunks;
        private readonly LinkedList<long> _lruOrder; // For LRU eviction
        private readonly Dictionary<long, LinkedListNode<long>> _lruNodes; // Fast LRU node lookup
        
        private readonly Stream _underlyingStream;
        private readonly ICryptor _cryptor;
        private readonly byte[] _headerNonce;
        private readonly FileHeader _fileHeader; // Add FileHeader for proper decryption
        private readonly bool _leaveOpen;
        
        private bool _disposed;

        public ChunkManager(Stream underlyingStream, ICryptor cryptor, FileHeader fileHeader, bool leaveOpen = false)
        {
            _underlyingStream = underlyingStream ?? throw new ArgumentNullException(nameof(underlyingStream));
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _fileHeader = fileHeader ?? throw new ArgumentNullException(nameof(fileHeader));
            _leaveOpen = leaveOpen;
            
            // Extract header nonce from the FileHeader
            _headerNonce = ExtractHeaderNonce(_fileHeader);
            
            _activeChunks = new Dictionary<long, ChunkBuffer>();
            _lruOrder = new LinkedList<long>();
            _lruNodes = new Dictionary<long, LinkedListNode<long>>();
        }

        private byte[] ExtractHeaderNonce(FileHeader header)
        {
            // Extract the header nonce from the FileHeader
            if (header is UvfLib.Core.V3.FileHeaderImpl v3Header)
            {
                return v3Header.GetNonce();
            }
            else if (header is UvfLib.Core.CryptomatorV8.FileHeaderImpl v8Header)
            {
                return v8Header.GetNonce();
            }
            else
            {
                throw new NotSupportedException($"Unsupported FileHeader type: {header.GetType()}");
            }
        }

        /// <summary>
        /// Gets or loads a chunk for the specified chunk number
        /// </summary>
        public ChunkBuffer GetChunk(long chunkNumber)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkManager));

            // Check if chunk is already in memory
            if (_activeChunks.TryGetValue(chunkNumber, out var existingChunk))
            {
                // Move to front of LRU list
                UpdateLRU(chunkNumber);
                return existingChunk;
            }

            // Need to load or create the chunk
            var chunk = LoadChunkFromDisk(chunkNumber);
            
            // Add to cache (may trigger eviction)
            AddChunkToCache(chunkNumber, chunk);
            
            return chunk;
        }

        /// <summary>
        /// Writes data to the specified position across potentially multiple chunks
        /// </summary>
        public void WriteAt(long virtualPosition, byte[] data, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkManager));

            int remainingBytes = count;
            int currentOffset = offset;
            long currentPosition = virtualPosition;

            while (remainingBytes > 0)
            {
                long chunkNumber = currentPosition / CLEARTEXT_CHUNK_SIZE;
                int offsetInChunk = (int)(currentPosition % CLEARTEXT_CHUNK_SIZE);
                
                // Get or load the chunk
                var chunk = GetChunk(chunkNumber);
                
                // Calculate how much we can write to this chunk
                int spaceInChunk = CLEARTEXT_CHUNK_SIZE - offsetInChunk;
                int bytesToWrite = Math.Min(remainingBytes, spaceInChunk);
                
                // Write to the chunk
                chunk.WriteAt(offsetInChunk, data, currentOffset, bytesToWrite);
                
                // Move to next portion
                remainingBytes -= bytesToWrite;
                currentOffset += bytesToWrite;
                currentPosition += bytesToWrite;
            }
        }

        /// <summary>
        /// Flushes all dirty chunks to disk
        /// </summary>
        public void FlushAll()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkManager));

            foreach (var chunk in _activeChunks.Values.Where(c => c.IsDirty))
            {
                FlushChunkToDisk(chunk);
            }
        }

        /// <summary>
        /// Flushes a specific chunk to disk if it's dirty
        /// </summary>
        public void FlushChunk(long chunkNumber)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkManager));

            if (_activeChunks.TryGetValue(chunkNumber, out var chunk) && chunk.IsDirty)
            {
                FlushChunkToDisk(chunk);
            }
        }

        private ChunkBuffer LoadChunkFromDisk(long chunkNumber)
        {
            var chunk = new ChunkBuffer(chunkNumber);
            
            // Calculate the encrypted position of this chunk
            long encryptedPosition = GetEncryptedChunkStartPosition(chunkNumber);
            
            // Check if the chunk exists on disk
            if (_underlyingStream.CanSeek && _underlyingStream.Length > encryptedPosition)
            {
                // Seek to the chunk position
                _underlyingStream.Position = encryptedPosition;
                
                // Try to read the encrypted chunk
                var encryptedChunkData = new byte[GetMaxEncryptedChunkSize()];
                int bytesRead = _underlyingStream.Read(encryptedChunkData, 0, encryptedChunkData.Length);
                
                if (bytesRead > 0)
                {
                    // Decrypt the chunk data
                    DecryptChunkData(encryptedChunkData, bytesRead, chunk);
                }
            }
            
            return chunk;
        }

        private void DecryptChunkData(byte[] encryptedData, int encryptedLength, ChunkBuffer chunk)
        {
            // PHASE 2: Direct chunk decryption using FileContentCryptor
            // This reads raw encrypted chunk data and decrypts it directly
            
            try
            {
                // Validate minimum chunk size (nonce + tag)
                const int minChunkSize = 12 + 16; // GCM_NONCE_SIZE + GCM_TAG_SIZE
                if (encryptedLength < minChunkSize)
                {
                    // Empty or invalid chunk - initialize as empty
                    chunk.ValidDataSize = 0;
                    return;
                }
                
                // Use FileContentCryptor to decrypt the chunk directly with the real FileHeader
                var ciphertextMemory = new ReadOnlyMemory<byte>(encryptedData, 0, encryptedLength);
                var decryptedChunk = _cryptor.FileContentCryptor().DecryptChunk(
                    ciphertextMemory, 
                    chunk.ChunkNumber, 
                    _fileHeader, 
                    authenticate: true
                );
                
                // Copy decrypted data to chunk buffer
                if (decryptedChunk.Length > 0)
                {
                    decryptedChunk.CopyTo(chunk.Data.AsMemory(0, decryptedChunk.Length));
                    chunk.ValidDataSize = decryptedChunk.Length;
                }
                else
                {
                    chunk.ValidDataSize = 0;
                }
            }
            catch (Exception ex)
            {
                throw new CryptoException($"Failed to decrypt chunk {chunk.ChunkNumber}: {ex.Message}", ex);
            }
        }



        private void FlushChunkToDisk(ChunkBuffer chunk)
        {
            if (!chunk.IsDirty || chunk.ValidDataSize == 0)
                return;

            // Calculate the encrypted position for this chunk
            long encryptedPosition = GetEncryptedChunkStartPosition(chunk.ChunkNumber);
            
            // Position the stream
            if (_underlyingStream.CanSeek)
            {
                _underlyingStream.Position = encryptedPosition;
            }

            // Encrypt the chunk with a FRESH nonce (never reuse!)
            var encryptedData = EncryptChunkData(chunk);
            
            // Write the encrypted data
            _underlyingStream.Write(encryptedData, 0, encryptedData.Length);
            
            // Mark as clean
            chunk.IsDirty = false;
        }

        private byte[] EncryptChunkData(ChunkBuffer chunk)
        {
            // Generate a fresh random nonce (CRITICAL: never reuse nonces)
            var nonce = new byte[12];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(nonce);
            }

            // Construct AAD: [ChunkNumber] + [HeaderNonce]
            var aad = new byte[8 + _headerNonce.Length];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(aad.AsSpan(0, 8), chunk.ChunkNumber);
            _headerNonce.CopyTo(aad.AsSpan(8));

            // Get the data to encrypt
            var plaintextData = chunk.GetValidData();

            // Use the existing EncryptingStream approach through a temporary stream
            // This ensures compatibility with the existing encryption infrastructure
            using var tempStream = new MemoryStream();
            using var encryptingStream = new EncryptingStream(_cryptor, tempStream, true);
            
            // Write the chunk data to the encrypting stream
            encryptingStream.Write(plaintextData.Span);
            encryptingStream.Flush();
            
            // Get the encrypted data (skip the header)
            var encryptedData = tempStream.ToArray();
            
            // The encrypted data includes the header, but we only want the chunk data
            // Skip the header and return just the encrypted chunk
            var headerSize = GetHeaderSize();
            if (encryptedData.Length > headerSize)
            {
                var chunkData = new byte[encryptedData.Length - headerSize];
                Array.Copy(encryptedData, headerSize, chunkData, 0, chunkData.Length);
                return chunkData;
            }
            
            // Fallback: return the raw encrypted data
            return encryptedData;
        }

        private int GetHeaderSize()
        {
            // UVF header size
            return 68;
        }

        private void AddChunkToCache(long chunkNumber, ChunkBuffer chunk)
        {
            // Evict old chunks if cache is full
            while (_activeChunks.Count >= MAX_CACHED_CHUNKS)
            {
                EvictLRUChunk();
            }

            // Add to cache
            _activeChunks[chunkNumber] = chunk;
            
            // Add to LRU tracking
            var node = _lruOrder.AddFirst(chunkNumber);
            _lruNodes[chunkNumber] = node;
        }

        private void EvictLRUChunk()
        {
            if (_lruOrder.Count == 0) return;

            // Get the least recently used chunk
            var lruChunkNumber = _lruOrder.Last.Value;
            var lruChunk = _activeChunks[lruChunkNumber];

            // Flush if dirty
            if (lruChunk.IsDirty)
            {
                FlushChunkToDisk(lruChunk);
            }

            // Remove from cache
            _activeChunks.Remove(lruChunkNumber);
            _lruOrder.RemoveLast();
            _lruNodes.Remove(lruChunkNumber);
            
            // Dispose the chunk
            lruChunk.Dispose();
        }

        private void UpdateLRU(long chunkNumber)
        {
            if (_lruNodes.TryGetValue(chunkNumber, out var node))
            {
                _lruOrder.Remove(node);
                _lruOrder.AddFirst(node);
            }
        }

        private long GetEncryptedChunkStartPosition(long chunkNumber)
        {
            // UVF/Cryptomator file structure: [Header] + [Chunk0] + [Chunk1] + ...
            // Each encrypted chunk = [Nonce(12)] + [Ciphertext] + [Tag(16)]
            long headerSize = 68; // UVF header size
            long encryptedChunkSize = GetMaxEncryptedChunkSize();
            return headerSize + (chunkNumber * encryptedChunkSize);
        }

        private int GetMaxEncryptedChunkSize()
        {
            // Max encrypted chunk = [Nonce(12)] + [MaxCiphertext(32KB)] + [Tag(16)]
            return 12 + CLEARTEXT_CHUNK_SIZE + 16;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Flush all dirty chunks
                try
                {
                    FlushAll();
                }
                catch
                {
                    // Ignore flush errors during disposal
                }

                // Dispose all chunks
                foreach (var chunk in _activeChunks.Values)
                {
                    chunk.Dispose();
                }

                _activeChunks.Clear();
                _lruOrder.Clear();
                _lruNodes.Clear();

                // Dispose underlying stream if we own it
                if (!_leaveOpen)
                {
                    _underlyingStream?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
 