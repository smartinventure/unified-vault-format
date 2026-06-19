// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting


using System.Security.Cryptography;
using System.Buffers.Binary;
using UvfLib.Core.V3;
using UvfLib.Core.Api;
using UvfLib.Core.CryptomatorV8;
using System;
using System.IO;
using System.Text;
using UvfLib.Core.Common;



#if DEBUG
using System.Diagnostics; // For Stopwatch
#endif

namespace UvfLib.Vault.VaultHelpers
{
    /// <summary>
    /// Stream wrapper that encrypts data using Cryptomator V3 file format as it's written.
    /// </summary>
    internal class EncryptingStream : Stream
    {        
        private readonly Cryptor _cryptor;
        private readonly Stream _outputStream;
        private readonly bool _leaveOpen;
        private readonly FileHeader _fileHeader;
        private AesGcm _fileContentAesGcm;
        private readonly RandomNumberGenerator _random;
        private readonly byte[] _cleartextChunkBuffer;
        private readonly byte[] _perChunkNonce;
        private readonly byte[] _aadBuffer; // Buffer for AAD (chunkNumber + headerNonce)
        private int _bufferPosition = 0;
        private long _currentChunkNumber = 0;
        private bool _headerWritten = false;
        private bool _isDisposed = false;

#if DEBUG
        private readonly PerformanceMetrics _metrics;
#endif

        private const int CLEARTEXT_CHUNK_SIZE = UvfLib.Core.V3.Constants.PAYLOAD_SIZE; // 32KB
        private readonly Memory<byte> _ciphertextChunkBuffer; // Reusable buffer for encrypted output

        // Virtual position tracking for random access support
        private long _virtualPosition = 0;  // Position in the virtual (unencrypted) stream
        private long _virtualLength = 0;    // Length of the virtual (unencrypted) stream

        // Chunk calculation constants (same as DecryptingStream)
        private const int CIPHERTEXT_CHUNK_SIZE = 12 + 32 * 1024 + 16; // nonce + payload + tag
        private readonly int _headerSize;

        public EncryptingStream(Cryptor cryptor, Stream outputStream, bool leaveOpen)
            : this(cryptor, outputStream, leaveOpen, null)
        {
        }

        public EncryptingStream(Cryptor cryptor, Stream outputStream, bool leaveOpen, FileHeader? existingHeader)
        {
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
            _leaveOpen = leaveOpen;
            _random = RandomNumberGenerator.Create();

#if DEBUG
            _metrics = new PerformanceMetrics("EncryptingStream")
            {
                Operation1Name = "NonceGen",
                Operation2Name = "AADPrep",
                Operation3Name = "EncryptOp",
                Operation4Name = "StreamWrite"
            };
#endif

            if (!_outputStream.CanWrite) throw new ArgumentException("Output stream must be writable.", nameof(outputStream));
            if (_cryptor.FileHeaderCryptor() == null || _cryptor.FileContentCryptor() == null)
                throw new InvalidOperationException("Cryptor not fully initialized for file operations.");

            // Use existing header if provided, otherwise create new one
            _fileHeader = existingHeader ?? _cryptor.FileHeaderCryptor().Create();
            
            // If using existing header, mark it as already written
            if (existingHeader != null)
            {
                _headerWritten = true;
            }

            // Handle both V3 and CryptomatorV8 header types
            if (_fileHeader is UvfLib.Core.V3.FileHeaderImpl v3Header)
            {
                // V3 implementation
                var fileContentKeyBytes = v3Header.GetContentKey().GetEncoded();
                _fileContentAesGcm = new AesGcm(fileContentKeyBytes);
                _perChunkNonce = new byte[UvfLib.Core.V3.Constants.GCM_NONCE_SIZE];
                
                // Initialize AAD buffer for V3: 8 bytes for chunk number + header nonce length
                ReadOnlySpan<byte> headerNonce = v3Header.GetNonce();
                _aadBuffer = new byte[8 + headerNonce.Length];
                headerNonce.CopyTo(_aadBuffer.AsSpan(8));
                _headerSize = UvfLib.Core.V3.FileHeaderImpl.SIZE;
            }
            else if (_fileHeader is Core.CryptomatorV8.FileHeaderImpl v8Header)
            {
                // CryptomatorV8 implementation - copy key bytes to prevent destruction issues
                var payload = v8Header.GetPayload();
                var contentKey = payload.GetContentKey();
                
                if (contentKey.IsDestroyed)
                {
                    throw new InvalidOperationException("Content key has been destroyed before use");
                }
                
                // Make a copy of the key bytes to prevent access after destruction
                var originalBytes = contentKey.GetEncoded();
                byte[] fileContentKeyBytes = new byte[originalBytes.Length];
                Buffer.BlockCopy(originalBytes, 0, fileContentKeyBytes, 0, originalBytes.Length);
                
                _fileContentAesGcm = new AesGcm(fileContentKeyBytes);
                _perChunkNonce = new byte[Core.CryptomatorV8.Constants.GCM_NONCE_SIZE];
                
                // Initialize AAD buffer for V8: 8 bytes for chunk number + header nonce length  
                ReadOnlySpan<byte> headerNonce = v8Header.GetNonce();
                _aadBuffer = new byte[8 + headerNonce.Length];
                headerNonce.CopyTo(_aadBuffer.AsSpan(8));
                _headerSize = Core.CryptomatorV8.FileHeaderImpl.SIZE;
            }
            else
            {
                throw new NotSupportedException($"Unsupported FileHeader type: {_fileHeader.GetType().FullName}");
            }

            _cleartextChunkBuffer = new byte[CLEARTEXT_CHUNK_SIZE];
            _ciphertextChunkBuffer = new Memory<byte>(new byte[_cryptor.FileContentCryptor().CiphertextChunkSize()]);
        }

        private void EnsureHeaderWritten()
        {
            if (!_headerWritten)
            {
                Memory<byte> encryptedHeaderMemory = _cryptor.FileHeaderCryptor().EncryptHeader(_fileHeader);
                byte[] encryptedHeaderBytes = encryptedHeaderMemory.ToArray();
                _outputStream.Write(encryptedHeaderBytes, 0, encryptedHeaderBytes.Length);
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

            EnsureHeaderWritten();

            int bytesToProcess = count;
            int currentOffset = offset;

            while (bytesToProcess > 0)
            {
                // Calculate how much we can write to current chunk
                int spaceInCurrentChunk = CLEARTEXT_CHUNK_SIZE - _bufferPosition;
                int bytesToCopy = Math.Min(bytesToProcess, spaceInCurrentChunk);
                
                Buffer.BlockCopy(buffer, currentOffset, _cleartextChunkBuffer, _bufferPosition, bytesToCopy);
                _bufferPosition += bytesToCopy;
                currentOffset += bytesToCopy;
                bytesToProcess -= bytesToCopy;
                _virtualPosition += bytesToCopy;

                // Update virtual length if we're extending the file
                if (_virtualPosition > _virtualLength)
                {
                    _virtualLength = _virtualPosition;
                }

                // If buffer is full, encrypt and write chunk
                if (_bufferPosition == CLEARTEXT_CHUNK_SIZE)
                {
                    EncryptAndWriteChunk(_cleartextChunkBuffer.AsMemory(0, CLEARTEXT_CHUNK_SIZE));
                    _bufferPosition = 0;
                    _currentChunkNumber++; // Move to next chunk
                }
            }
        }




        private void DecryptChunkForUpdate(byte[] encryptedData, int encryptedLength, long chunkNumber)
        {
            if (DebugLog.IsEnabled)
            {
                Console.WriteLine($"🔍 DecryptChunkForUpdate: chunkNumber={chunkNumber}, encryptedLength={encryptedLength}");
            }
            
            try
            {
                if (_cryptor.FileContentCryptor() is UvfLib.Core.V3.FileContentCryptorImpl v3Cryptor)
                {
                    // Extract nonce, ciphertext, and tag
                    var nonce = encryptedData.AsSpan(0, UvfLib.Core.V3.Constants.GCM_NONCE_SIZE);
                    var ciphertext = encryptedData.AsSpan(UvfLib.Core.V3.Constants.GCM_NONCE_SIZE, 
                        encryptedLength - UvfLib.Core.V3.Constants.GCM_NONCE_SIZE - UvfLib.Core.V3.Constants.GCM_TAG_SIZE);
                    var tag = encryptedData.AsSpan(encryptedLength - UvfLib.Core.V3.Constants.GCM_TAG_SIZE);

                    if (DebugLog.IsEnabled)
                    {
                        Console.WriteLine($"🔍 Original nonce from chunk: {Convert.ToHexString(nonce)}");
                        Console.WriteLine($"🔍 Ciphertext length: {ciphertext.Length}");
                        Console.WriteLine($"🔍 Tag: {Convert.ToHexString(tag)}");
                    }

                    // CRITICAL: Preserve the original nonce for re-encryption
                    nonce.CopyTo(_perChunkNonce);
                    if (DebugLog.IsEnabled)
                    {
                        Console.WriteLine($"🔍 Preserved nonce in _perChunkNonce: {Convert.ToHexString(_perChunkNonce)}");
                    }

                    // Prepare AAD
                    BinaryPrimitives.WriteInt64BigEndian(_aadBuffer.AsSpan(0, 8), chunkNumber);
                    if (DebugLog.IsEnabled)
                    {
                        Console.WriteLine($"🔍 AAD for decryption: {Convert.ToHexString(_aadBuffer)}");
                    }

                    // Decrypt
                    _fileContentAesGcm.Decrypt(nonce, ciphertext, tag, _cleartextChunkBuffer.AsSpan(0, ciphertext.Length), _aadBuffer);
                    if (DebugLog.IsEnabled)
                    {
                        Console.WriteLine($"🔍 Successfully decrypted chunk {chunkNumber}");
                        
                        // Show first 20 bytes of decrypted content
                        var preview = _cleartextChunkBuffer.AsSpan(0, Math.Min(20, ciphertext.Length));
                        Console.WriteLine($"🔍 Decrypted content preview: '{Encoding.UTF8.GetString(preview)}'");
                    }
                    
                    // Clear remaining buffer
                    if (ciphertext.Length < CLEARTEXT_CHUNK_SIZE)
                    {
                        Array.Clear(_cleartextChunkBuffer, ciphertext.Length, CLEARTEXT_CHUNK_SIZE - ciphertext.Length);
                    }
                }
                else if (_cryptor.FileContentCryptor() is Core.CryptomatorV8.FileContentCryptorImpl v8Cryptor)
                {
                    // Similar logic for CryptomatorV8
                    var nonce = encryptedData.AsSpan(0, Core.CryptomatorV8.Constants.GCM_NONCE_SIZE);
                    var ciphertext = encryptedData.AsSpan(Core.CryptomatorV8.Constants.GCM_NONCE_SIZE,
                        encryptedLength - Core.CryptomatorV8.Constants.GCM_NONCE_SIZE - Core.CryptomatorV8.Constants.GCM_TAG_SIZE);
                    var tag = encryptedData.AsSpan(encryptedLength - Core.CryptomatorV8.Constants.GCM_TAG_SIZE);

                    // CRITICAL: Preserve the original nonce for re-encryption
                    nonce.CopyTo(_perChunkNonce);

                    // Prepare AAD
                    BinaryPrimitives.WriteInt64BigEndian(_aadBuffer.AsSpan(0, 8), chunkNumber);

                    // Decrypt
                    _fileContentAesGcm.Decrypt(nonce, ciphertext, tag, _cleartextChunkBuffer.AsSpan(0, ciphertext.Length), _aadBuffer);
                    
                    // Clear remaining buffer
                    if (ciphertext.Length < CLEARTEXT_CHUNK_SIZE)
                    {
                        Array.Clear(_cleartextChunkBuffer, ciphertext.Length, CLEARTEXT_CHUNK_SIZE - ciphertext.Length);
                    }
                }
                else
                {
                    throw new NotSupportedException($"Unsupported FileContentCryptor type: {_cryptor.FileContentCryptor().GetType().FullName}");
                }
            }
            catch (Exception ex)
            {
                if (DebugLog.IsEnabled)
                {
                    Console.WriteLine($"🔍 DecryptChunkForUpdate FAILED: {ex.Message}");
                }
                throw new InvalidOperationException($"Failed to decrypt existing chunk {chunkNumber} for update: {ex.Message}", ex);
            }
        }

        private long GetChunkNumber(long position)
        {
            return position / CLEARTEXT_CHUNK_SIZE;
        }

        private int GetOffsetInChunk(long position)
        {
            return (int)(position % CLEARTEXT_CHUNK_SIZE);
        }

        private long GetEncryptedChunkStartPosition(long chunkNumber)
        {
            return _headerSize + (chunkNumber * CIPHERTEXT_CHUNK_SIZE);
        }

        private void EncryptAndWriteChunk(ReadOnlyMemory<byte> cleartextChunk)
        {
            // Check environment variable directly - works across AOT compilation boundary
            bool isVerbose = DebugLog.IsEnabled;
            
            if (isVerbose)
            {
                Console.WriteLine($"🔍 EncryptAndWriteChunk: chunkSize={cleartextChunk.Length}, _currentChunkNumber={_currentChunkNumber}");
            }
            
            // Only generate new nonce if we haven't preserved one from decryption
            // This happens for new chunks or when _perChunkNonce is all zeros
            bool hasPreservedNonce = !_perChunkNonce.All(b => b == 0);
            if (isVerbose)
            {
                Console.WriteLine($"🔍 hasPreservedNonce: {hasPreservedNonce}");
            }
            
            if (!hasPreservedNonce)
        {
            _random.GetBytes(_perChunkNonce);
                if (isVerbose)
                {
                    Console.WriteLine($"🔍 Generated NEW random nonce: {Convert.ToHexString(_perChunkNonce)}");
                }
            }
            else
            {
                if (isVerbose)
                {
                    Console.WriteLine($"🔍 Using PRESERVED nonce: {Convert.ToHexString(_perChunkNonce)}");
                }
            }

            BinaryPrimitives.WriteInt64BigEndian(_aadBuffer.AsSpan(0, 8), _currentChunkNumber);
            if (isVerbose)
            {
                Console.WriteLine($"🔍 AAD for encryption: {Convert.ToHexString(_aadBuffer)}");
                
                // Show first 20 bytes of content being encrypted
                var preview = cleartextChunk.Span.Slice(0, Math.Min(20, cleartextChunk.Length));
                Console.WriteLine($"🔍 Content being encrypted: '{Encoding.UTF8.GetString(preview)}'");
            }

            // Handle both V3 and CryptomatorV8 implementations
            if (_cryptor.FileContentCryptor() is UvfLib.Core.V3.FileContentCryptorImpl v3Cryptor)
            {
                // V3 implementation
                int expectedEncryptedLength = UvfLib.Core.V3.Constants.GCM_NONCE_SIZE + cleartextChunk.Length + UvfLib.Core.V3.Constants.GCM_TAG_SIZE;
                _ciphertextChunkBuffer.Slice(0, expectedEncryptedLength).Span.Clear();
                
                if (isVerbose)
                {
                    Console.WriteLine($"🔍 About to encrypt with V3 cryptor, expectedLength={expectedEncryptedLength}");
                }
                
                v3Cryptor.EncryptChunk(
                    _fileContentAesGcm,
                    cleartextChunk,
                    _ciphertextChunkBuffer, 
                    _currentChunkNumber,
                    _perChunkNonce, 
                    _aadBuffer 
                );
                
                int actualEncryptedLength = UvfLib.Core.V3.Constants.GCM_NONCE_SIZE + cleartextChunk.Length + UvfLib.Core.V3.Constants.GCM_TAG_SIZE;
                
                if (actualEncryptedLength != expectedEncryptedLength)
                {
                    throw new InvalidOperationException($"Encrypted length mismatch: expected {expectedEncryptedLength}, actual {actualEncryptedLength}");
                }
                
                if (isVerbose)
                {
                    Console.WriteLine($"🔍 Encryption successful, writing {actualEncryptedLength} bytes to stream");
                }
                _outputStream.Write(_ciphertextChunkBuffer.Slice(0, actualEncryptedLength).Span);
                
                if (isVerbose)
                {
                    // Show the encrypted nonce that was written
                    var writtenNonce = _ciphertextChunkBuffer.Span.Slice(0, UvfLib.Core.V3.Constants.GCM_NONCE_SIZE);
                    Console.WriteLine($"🔍 Nonce written to file: {Convert.ToHexString(writtenNonce)}");
                }
                
                // Clear preserved nonce after use
                Array.Clear(_perChunkNonce, 0, _perChunkNonce.Length);
                if (isVerbose)
                {
                    Console.WriteLine($"🔍 Cleared preserved nonce after encryption");
                }
            }
            else if (_cryptor.FileContentCryptor() is Core.CryptomatorV8.FileContentCryptorImpl v8Cryptor)
            {
                // CryptomatorV8 implementation
                int expectedEncryptedLength = Core.CryptomatorV8.Constants.GCM_NONCE_SIZE + cleartextChunk.Length + Core.CryptomatorV8.Constants.GCM_TAG_SIZE;
                _ciphertextChunkBuffer.Slice(0, expectedEncryptedLength).Span.Clear();
                
                // Use preserved nonce if available, otherwise generate random nonce
                byte[] nonce = new byte[Core.CryptomatorV8.Constants.GCM_NONCE_SIZE];
                bool hasPreservedNonceV8 = !_perChunkNonce.All(b => b == 0);
                if (hasPreservedNonceV8)
                {
                    Array.Copy(_perChunkNonce, nonce, Core.CryptomatorV8.Constants.GCM_NONCE_SIZE);
                }
                else
                {
                _random.GetBytes(nonce);
                }
                
                // Copy nonce to the beginning of ciphertext buffer
                nonce.CopyTo(_ciphertextChunkBuffer.Span);
                
                // Construct AAD: bigEndian(chunkNumber) . headerNonce (as per specification)
                BinaryPrimitives.WriteInt64BigEndian(_aadBuffer.AsSpan(0, 8), _currentChunkNumber);
                
                // Encrypt using AES-GCM with proper AAD
                byte[] ciphertext = new byte[cleartextChunk.Length];
                byte[] tag = new byte[Core.CryptomatorV8.Constants.GCM_TAG_SIZE];
                
                _fileContentAesGcm.Encrypt(nonce, cleartextChunk.Span, ciphertext, tag, _aadBuffer);
                
                // Copy ciphertext and tag to the buffer
                ciphertext.CopyTo(_ciphertextChunkBuffer.Span.Slice(Core.CryptomatorV8.Constants.GCM_NONCE_SIZE));
                tag.CopyTo(_ciphertextChunkBuffer.Span.Slice(Core.CryptomatorV8.Constants.GCM_NONCE_SIZE + cleartextChunk.Length));
                
                _outputStream.Write(_ciphertextChunkBuffer.Slice(0, expectedEncryptedLength).Span);
                
                // Clear preserved nonce after use
                Array.Clear(_perChunkNonce, 0, _perChunkNonce.Length);
            }
            else
            {
                throw new NotSupportedException($"Unsupported FileContentCryptor type: {_cryptor.FileContentCryptor().GetType().FullName}");
            }
        }

        public override void Flush()
        {
            CheckDisposed();

            EnsureHeaderWritten(); // Ensure header is written even if no data follows

            // Encrypt and write any remaining data in the buffer as the final chunk
            if (_bufferPosition > 0)
            {
                // CRITICAL FIX: Position stream to correct chunk location before writing
                long correctChunkPosition = GetEncryptedChunkStartPosition(_currentChunkNumber);
                if (_outputStream.CanSeek)
                {
                    _outputStream.Position = correctChunkPosition;
                }
                
                EncryptAndWriteChunk(_cleartextChunkBuffer.AsMemory(0, _bufferPosition));
                _bufferPosition = 0; // Clear buffer after flushing
            }

            // CRITICAL: Ensure underlying stream is properly flushed
            _outputStream.Flush(); // Flush the underlying stream
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    try
                    {
                        Flush();
                        
                        // ADDITIONAL SAFETY: Explicit flush and sync before closing
                        _outputStream.Flush();
                        
                        // Force OS to sync to disk if it's a FileStream
                        if (_outputStream is FileStream fileStream)
                        {
                            fileStream.Flush(true); // Force OS flush
                        }
                    }
                    finally
                    {
                        _fileContentAesGcm?.Dispose(); 
                        _fileHeader?.Dispose(); 

                        if (!_leaveOpen)
                        {
                            _outputStream?.Dispose();
                        }
                        _random?.Dispose(); 
#if DEBUG
                        _metrics?.Report();
#endif
                    }
                }
                _isDisposed = true;
            }
            base.Dispose(disposing);
        }

        private void CheckDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        // --- Stream abstract members implementation ---

        public override bool CanRead => false;
        public override bool CanSeek => false;  // Sequential encryption stream does not support seeking - use chunk-aware stream instead
        public override bool CanWrite => true;

        public override long Length => _virtualLength;
        public override long Position
        {
            get => _virtualPosition;
            set => throw new NotSupportedException("Sequential EncryptingStream does not support seeking. Use OpenWriteAsync with requestRandomWrite=true for random write support.");
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("EncryptingStream does not support reading.");
        
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Sequential EncryptingStream does not support seeking. Use OpenWriteAsync with requestRandomWrite=true for random write support.");
        }
        
        public override void SetLength(long value)
        {
            throw new NotSupportedException("Sequential EncryptingStream does not support SetLength. Use OpenWriteAsync with requestRandomWrite=true for random write support.");
        }
    }
}