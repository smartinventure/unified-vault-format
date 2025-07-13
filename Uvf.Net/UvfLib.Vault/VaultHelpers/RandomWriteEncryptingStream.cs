using System;
using System.IO;
using UvfLib.Core.Api;

namespace UvfLib.Vault.VaultHelpers
{
    /// <summary>
    /// Stream wrapper that encrypts data using chunk-aware random write management.
    /// This stream supports random writes within encrypted files by managing chunks in memory.
    /// </summary>
    internal class RandomWriteEncryptingStream : Stream
    {
        private readonly ChunkManager _chunkManager;
        private readonly Stream _underlyingStream;
        private readonly ICryptor _cryptor;
        private readonly FileHeader _fileHeader;
        private readonly bool _leaveOpen;
        
        private long _virtualPosition = 0;
        private long _virtualLength = 0;
        private bool _disposed = false;
        private bool _headerWritten = false;

        public RandomWriteEncryptingStream(Stream underlyingStream, ICryptor cryptor, FileHeader fileHeader, bool leaveOpen = false)
        {
            _underlyingStream = underlyingStream ?? throw new ArgumentNullException(nameof(underlyingStream));
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _fileHeader = fileHeader ?? throw new ArgumentNullException(nameof(fileHeader));
            _leaveOpen = leaveOpen;
            
            if (!_underlyingStream.CanWrite)
                throw new ArgumentException("Underlying stream must be writable.", nameof(underlyingStream));
            
            // Check if header already exists (for existing files)
            if (_underlyingStream.CanSeek && _underlyingStream.Length > 0)
            {
                _headerWritten = true;
            }
            
            _chunkManager = new ChunkManager(_underlyingStream, cryptor, fileHeader, leaveOpen: true);
            
            // Calculate initial virtual length based on underlying stream
            _virtualLength = CalculateVirtualLength();
        }

        private long CalculateVirtualLength()
        {
            if (!_underlyingStream.CanSeek)
                return 0;
                
            // Phase 2: Calculate virtual length from encrypted file structure
            long encryptedLength = _underlyingStream.Length;
            
            // Account for header size
            const long headerSize = 68; // UVF header size
            if (encryptedLength <= headerSize)
                return 0;
                
            long contentLength = encryptedLength - headerSize;
            
            // Each encrypted chunk: [Nonce(12)] + [Ciphertext(up to 32KB)] + [Tag(16)]
            const int chunkOverhead = 12 + 16; // nonce + tag
            const int maxCleartextChunkSize = 32 * 1024;
            const int maxEncryptedChunkSize = maxCleartextChunkSize + chunkOverhead;
            
            // Calculate number of complete chunks
            long completeChunks = contentLength / maxEncryptedChunkSize;
            long remainingBytes = contentLength % maxEncryptedChunkSize;
            
            // Calculate virtual length
            long virtualLength = completeChunks * maxCleartextChunkSize;
            
            // Handle partial last chunk
            if (remainingBytes > chunkOverhead)
            {
                virtualLength += remainingBytes - chunkOverhead;
            }
            
            return virtualLength;
        }

        private void EnsureHeaderWritten()
        {
            if (!_headerWritten)
            {
                Memory<byte> encryptedHeaderMemory = _cryptor.FileHeaderCryptor().EncryptHeader(_fileHeader);
                byte[] encryptedHeaderBytes = encryptedHeaderMemory.ToArray();
                _underlyingStream.Write(encryptedHeaderBytes, 0, encryptedHeaderBytes.Length);
                _headerWritten = true;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer.Length - offset < count) throw new ArgumentException("Invalid offset/count combination.");

            // Ensure header is written before any data
            EnsureHeaderWritten();

            // Use ChunkManager to handle the write across potentially multiple chunks
            _chunkManager.WriteAt(_virtualPosition, buffer, offset, count);
            
            // Update virtual position and length
            _virtualPosition += count;
            if (_virtualPosition > _virtualLength)
            {
                _virtualLength = _virtualPosition;
            }
        }

        public override void Flush()
        {
            CheckDisposed();
            
            // Ensure header is written before flushing
            EnsureHeaderWritten();
            
            // Flush all dirty chunks to disk
            _chunkManager.FlushAll();
            
            // Flush the underlying stream
            _underlyingStream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            CheckDisposed();
            
            if (!CanSeek)
                throw new NotSupportedException("Stream does not support seeking.");

            long targetPosition;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    targetPosition = offset;
                    break;
                case SeekOrigin.Current:
                    targetPosition = _virtualPosition + offset;
                    break;
                case SeekOrigin.End:
                    targetPosition = _virtualLength + offset;
                    break;
                default:
                    throw new ArgumentException("Invalid seek origin.", nameof(origin));
            }

            if (targetPosition < 0)
                throw new IOException("Cannot seek before the beginning of the stream.");

            _virtualPosition = targetPosition;
            return _virtualPosition;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            // Phase 2: Add read support using ChunkManager
            CheckDisposed();
            
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer.Length - offset < count) throw new ArgumentException("Invalid offset/count combination.");

            // Check if we've reached the end of the stream
            if (_virtualPosition >= _virtualLength)
                return 0;

            // Adjust count if it would read beyond the end of the stream
            count = (int)Math.Min(count, _virtualLength - _virtualPosition);

            int totalBytesRead = 0;
            int remainingBytes = count;
            int currentOffset = offset;
            long currentPosition = _virtualPosition;

            while (remainingBytes > 0)
            {
                long chunkNumber = currentPosition / (32 * 1024); // CLEARTEXT_CHUNK_SIZE
                int offsetInChunk = (int)(currentPosition % (32 * 1024));
                
                // Get the chunk (will load from disk if needed)
                var chunk = _chunkManager.GetChunk(chunkNumber);
                
                // Calculate how much we can read from this chunk
                int availableInChunk = Math.Max(0, chunk.ValidDataSize - offsetInChunk);
                if (availableInChunk == 0)
                    break; // No more data available
                
                int bytesToRead = Math.Min(remainingBytes, availableInChunk);
                
                // Read from the chunk
                int bytesRead = chunk.ReadAt(offsetInChunk, buffer, currentOffset, bytesToRead);
                if (bytesRead == 0)
                    break; // No more data available
                
                // Update positions
                totalBytesRead += bytesRead;
                remainingBytes -= bytesRead;
                currentOffset += bytesRead;
                currentPosition += bytesRead;
            }

            _virtualPosition = currentPosition;
            return totalBytesRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        // Flush any remaining data
                        Flush();
                    }
                    catch
                    {
                        // Ignore flush errors during disposal
                    }

                    // Dispose the chunk manager
                    _chunkManager?.Dispose();

                    // Dispose underlying stream if we own it
                    if (!_leaveOpen)
                    {
                        _underlyingStream?.Dispose();
                    }
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        private void CheckDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RandomWriteEncryptingStream));
        }

        // Stream properties
        public override bool CanRead => true; // Phase 2: Read-write support
        public override bool CanSeek => _underlyingStream.CanSeek;
        public override bool CanWrite => true;

        public override long Length => _virtualLength;
        
        public override long Position
        {
            get => _virtualPosition;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Setting length is not supported for encrypted streams.");
        }
    }
} 