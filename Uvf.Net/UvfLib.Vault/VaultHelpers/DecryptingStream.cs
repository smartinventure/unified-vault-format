// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

/*******************************************************************************
 * Copyright (c) 2016 Sebastian Stenzel and others.
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the accompanying LICENSE.txt.
 *
 * Contributors:
 *     Sebastian Stenzel - initial API and implementation
 *******************************************************************************/

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting


using System.Security.Cryptography;
using System.Buffers.Binary;
using UvfLib.Core.V3;
using UvfLib.Core.Api;
using UvfLib.Core.CryptomatorV8;



#if DEBUG
using System.Diagnostics;
#endif

namespace UvfLib.Vault.VaultHelpers
{
    /// <summary>
    /// Stream wrapper that decrypts Cryptomator V3 file format data as it's read.
    /// </summary>
    internal class DecryptingStream : Stream
    {
        private readonly Cryptor _cryptor;
        private readonly Stream _inputStream;
        private readonly bool _leaveOpen;
        private readonly FileHeader _fileHeader;
        private AesGcm _fileContentAesGcm;
        private readonly byte[] _ciphertextChunkBuffer;
        private readonly Memory<byte> _plaintextChunkBuffer;
        private readonly byte[] _aadBuffer;
        private int _plaintextBufferPosition = 0;
        private int _plaintextBufferLength = 0;
        private long _currentChunkNumber = 0;
        private bool _isDisposed = false;
        private bool _endOfStreamReached = false;
        private long _virtualPosition = 0;  // Track position in decrypted stream
        private readonly long _virtualLength;  // Total decrypted length
        private readonly int _plaintextChunkSize;
        private readonly int _ciphertextChunkSize;
        private readonly int _headerSize;

#if DEBUG
        private readonly PerformanceMetrics _metrics;
#endif

        // Constants - will be the same for both formats
        private const int PLAINTEXT_CHUNK_SIZE = 32 * 1024; // 32KB
        private const int CIPHERTEXT_CHUNK_SIZE = 12 + 32 * 1024 + 16; // nonce + payload + tag

        public DecryptingStream(Cryptor cryptor, Stream inputStream, bool leaveOpen)
        {
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _inputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
            _leaveOpen = leaveOpen;

            // Determine format-specific constants
            if (_cryptor.FileContentCryptor() is UvfLib.Core.CryptomatorV8.FileContentCryptorImpl)
            {
                _plaintextChunkSize = UvfLib.Core.CryptomatorV8.Constants.PAYLOAD_SIZE;
                _ciphertextChunkSize = UvfLib.Core.CryptomatorV8.Constants.CHUNK_SIZE;
                _headerSize = UvfLib.Core.CryptomatorV8.FileHeaderImpl.SIZE;
            }
            else
            {
                _plaintextChunkSize = UvfLib.Core.V3.Constants.PAYLOAD_SIZE;
                _ciphertextChunkSize = UvfLib.Core.V3.Constants.CHUNK_SIZE;
                _headerSize = UvfLib.Core.V3.FileHeaderImpl.SIZE;
            }

#if DEBUG
            _metrics = new PerformanceMetrics("DecryptingStream")
            {
                Operation1Name = "NonceExt",
                Operation2Name = "AADPrep", 
                Operation3Name = "DecryptOp",
                Operation4Name = "BuffCopy"
            };
#endif

            if (!_inputStream.CanRead) throw new ArgumentException("Input stream must be readable.", nameof(inputStream));
            if (_cryptor.FileHeaderCryptor() == null || _cryptor.FileContentCryptor() == null)
                throw new InvalidOperationException("Cryptor not fully initialized for file operations.");

            // Calculate virtual length
            _virtualLength = CalculateDecryptedLength(_inputStream.Length);
            _ciphertextChunkBuffer = new byte[CIPHERTEXT_CHUNK_SIZE];
            _plaintextChunkBuffer = new Memory<byte>(new byte[PLAINTEXT_CHUNK_SIZE]);

            // 1. Read and decrypt header
            byte[] encryptedHeader = new byte[UvfLib.Core.V3.FileHeaderImpl.SIZE];
            int bytesRead = ReadExactly(_inputStream, encryptedHeader, 0, encryptedHeader.Length);
            if (bytesRead < encryptedHeader.Length)
            {
                throw new InvalidCiphertextException("Input stream ended before header could be fully read.");
            }
            _fileHeader = _cryptor.FileHeaderCryptor().DecryptHeader(encryptedHeader);

            // Handle both V3 and CryptomatorV8 header types
            if (_fileHeader is UvfLib.Core.V3.FileHeaderImpl v3Header)
            {
                // V3 implementation
                var fileContentKeyBytes = v3Header.GetContentKey().GetEncoded();
                _fileContentAesGcm = new AesGcm(fileContentKeyBytes);
                
                // Initialize AAD buffer: 8 bytes for chunk number + header nonce length
                ReadOnlySpan<byte> headerNonce = v3Header.GetNonce();
                _aadBuffer = new byte[8 + headerNonce.Length];
                headerNonce.CopyTo(_aadBuffer.AsSpan(8)); // Copy header nonce to the latter part of AAD buffer
            }
            else if (_fileHeader is UvfLib.Core.CryptomatorV8.FileHeaderImpl v8Header)
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
                
                // Initialize AAD buffer: 8 bytes for chunk number + header nonce length  
                ReadOnlySpan<byte> headerNonce = v8Header.GetNonce();
                _aadBuffer = new byte[8 + headerNonce.Length];
                headerNonce.CopyTo(_aadBuffer.AsSpan(8)); // Copy header nonce to the latter part of AAD buffer
            }
            else
            {
                throw new NotSupportedException($"Unsupported FileHeader type: {_fileHeader.GetType().FullName}");
            }
        }

        private long CalculateDecryptedLength(long encryptedLength)
        {
            // Remove header size
            long contentLength = encryptedLength - UvfLib.Core.V3.FileHeaderImpl.SIZE;
            if (contentLength <= 0) return 0;

            // Calculate number of complete chunks
            long completeChunks = contentLength / CIPHERTEXT_CHUNK_SIZE;
            long remainingBytes = contentLength % CIPHERTEXT_CHUNK_SIZE;

            // Each chunk has GCM_NONCE_SIZE + GCM_TAG_SIZE overhead
            long decryptedBytes = completeChunks * PLAINTEXT_CHUNK_SIZE;

            // Handle last partial chunk if any
            if (remainingBytes > UvfLib.Core.V3.Constants.GCM_NONCE_SIZE + UvfLib.Core.V3.Constants.GCM_TAG_SIZE)
            {
                decryptedBytes += remainingBytes - (UvfLib.Core.V3.Constants.GCM_NONCE_SIZE + UvfLib.Core.V3.Constants.GCM_TAG_SIZE);
            }

            return decryptedBytes;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (buffer.Length - offset < count) throw new ArgumentException("Invalid offset length combination.");

            // Check if we've reached the end of the decrypted stream
            if (_virtualPosition >= _virtualLength)
            {
                return 0;
            }

            // Adjust count if it would read beyond the end of the stream
            count = (int)Math.Min(count, _virtualLength - _virtualPosition);

            int totalBytesRead = 0;
            while (count > 0)
            {
                // Calculate which chunk we need based on virtual position
                long requiredChunk = GetChunkNumber(_virtualPosition);
                
                // If we're not at the right chunk or buffer is empty, seek to and decrypt the needed chunk
                if (requiredChunk != _currentChunkNumber || _plaintextBufferLength == 0)
                {
                    SeekToChunk(requiredChunk);
                }

                // Calculate where in the current chunk's buffer we should start reading
                int bufferOffset = GetOffsetInChunk(_virtualPosition);
                
                // Calculate how many bytes we can read from this chunk
                int bytesAvailableInChunk = _plaintextBufferLength - bufferOffset;
                if (bytesAvailableInChunk <= 0)
                {
                    // We've reached the end of the current chunk
                    if (!ReadAndDecryptNextChunk())
                    {
                        break; // End of stream reached
                    }
                    continue; // Retry with the new chunk
                }

                // Calculate how many bytes to copy from this chunk
                int bytesToCopy = Math.Min(count, bytesAvailableInChunk);

                // Copy the data
                _plaintextChunkBuffer.Slice(bufferOffset, bytesToCopy).Span.CopyTo(
                    buffer.AsSpan(offset, bytesToCopy));

                // Update positions and counters
                _virtualPosition += bytesToCopy;
                _plaintextBufferPosition = bufferOffset + bytesToCopy;
                offset += bytesToCopy;
                count -= bytesToCopy;
                totalBytesRead += bytesToCopy;
            }

            return totalBytesRead;
        }

        private bool ReadAndDecryptNextChunk(bool incrementChunkNumber = true)
        {
            if (_endOfStreamReached) return false;

            int bytesRead = ReadUpTo(_inputStream, _ciphertextChunkBuffer, 0, CIPHERTEXT_CHUNK_SIZE);

            if (bytesRead == 0)
            {
                _endOfStreamReached = true;
                _plaintextBufferLength = 0;
                _plaintextBufferPosition = 0;
                return false;
            }

            int minCiphertextSize = UvfLib.Core.V3.Constants.GCM_NONCE_SIZE + UvfLib.Core.V3.Constants.GCM_TAG_SIZE;
            if (bytesRead < minCiphertextSize)
            {
                _endOfStreamReached = true;
                throw new InvalidCiphertextException($"Incomplete ciphertext chunk read (read {bytesRead}, needed at least {minCiphertextSize}). Possible truncation or corruption.");
            }

            // CRITICAL FIX: Clear the plaintext buffer before decryption to prevent leftover data
            _plaintextChunkBuffer.Span.Clear();

            BinaryPrimitives.WriteInt64BigEndian(_aadBuffer.AsSpan(0, 8), _currentChunkNumber);

            // Handle both V3 and CryptomatorV8 implementations
            if (_cryptor.FileContentCryptor() is UvfLib.Core.V3.FileContentCryptorImpl v3Cryptor)
            {
                // V3 implementation
                _plaintextBufferLength = v3Cryptor.DecryptChunk(
                    _fileContentAesGcm,
                    new ReadOnlyMemory<byte>(_ciphertextChunkBuffer, 0, bytesRead),
                    _plaintextChunkBuffer,
                    _currentChunkNumber, 
                    _aadBuffer 
                );
            }
            else if (_cryptor.FileContentCryptor() is UvfLib.Core.CryptomatorV8.FileContentCryptorImpl v8Cryptor)
            {
                // CryptomatorV8 implementation - use Core method for consistency
                try
                {
                    var decryptedChunk = v8Cryptor.DecryptChunk(
                        new ReadOnlyMemory<byte>(_ciphertextChunkBuffer, 0, bytesRead),
                        _currentChunkNumber,
                        _fileHeader,
                        true // authenticate
                    );
                    
                    // Copy decrypted data to buffer
                    decryptedChunk.CopyTo(_plaintextChunkBuffer);
                    _plaintextBufferLength = decryptedChunk.Length;
                }
                catch (Exception ex) when (ex is CryptographicException || ex.GetType().Name.Contains("Authentication"))
                {
                    throw new System.Security.Cryptography.AuthenticationTagMismatchException($"Chunk {_currentChunkNumber} authentication failed", ex);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported FileContentCryptor type: {_cryptor.FileContentCryptor().GetType().FullName}");
            }

            _plaintextBufferPosition = 0;
            if (bytesRead < CIPHERTEXT_CHUNK_SIZE)
            {
                _endOfStreamReached = true;
            }
            
            // FIXED: Only increment chunk number if requested (for sequential reads)
            if (incrementChunkNumber)
            {
                _currentChunkNumber++; 
            }
            
            return true;
        }

        // Helper to read exactly N bytes or throw
        private static int ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0) break; // End of stream
                totalRead += read;
            }
            return totalRead;
        }

        // Helper to read up to N bytes
        private static int ReadUpTo(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0) break; // End of stream
                totalRead += read;
            }
            return totalRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // Clean up resources
                    _fileContentAesGcm?.Dispose(); // Dispose AesGcm
                    _fileHeader?.Dispose(); // Dispose the content key within the header

                    if (!_leaveOpen)
                    {
                        _inputStream?.Dispose();
                    }
#if DEBUG
                    _metrics?.Report();
#endif
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

        public override bool CanRead => true;
        public override bool CanSeek => _inputStream.CanSeek;
        public override bool CanWrite => false;

        public override long Length => _virtualLength;
        
        public override long Position
        {
            get => _virtualPosition;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { /* No-op for read-only stream */ }
        public override long Seek(long offset, SeekOrigin origin)
        {
            CheckDisposed();
            if (!_inputStream.CanSeek)
                throw new NotSupportedException("Underlying stream must support seeking.");

            // Calculate the target position
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
                    throw new ArgumentException("Invalid seek origin", nameof(origin));
            }

            if (targetPosition < 0)
                throw new IOException("Cannot seek before the beginning of the stream.");

            if (targetPosition > _virtualLength)
                targetPosition = _virtualLength;

            // If we're already at the right position, no need to do anything
            if (targetPosition == _virtualPosition)
                return _virtualPosition;

            // Calculate which chunk we need and the offset within it
            long targetChunk = GetChunkNumber(targetPosition);
            int offsetInChunk = GetOffsetInChunk(targetPosition);

            // Seek to the correct chunk and position within it
            SeekToChunk(targetChunk);
            _plaintextBufferPosition = offsetInChunk;
            _virtualPosition = targetPosition;

            return _virtualPosition;
        }

        private long GetChunkNumber(long position)
        {
            return position / PLAINTEXT_CHUNK_SIZE;
        }

        private int GetOffsetInChunk(long position)
        {
            return (int)(position % PLAINTEXT_CHUNK_SIZE);
        }

        private long GetChunkStartPosition(long chunkNumber)
        {
            return UvfLib.Core.V3.FileHeaderImpl.SIZE + (chunkNumber * CIPHERTEXT_CHUNK_SIZE);
        }

        private void SeekToChunk(long targetChunkNumber)
        {
            if (targetChunkNumber == _currentChunkNumber && _plaintextBufferLength > 0)
            {
                // Already at the correct chunk
                return;
            }

#if DEBUG
            _metrics.StartTiming();
#endif

            // Position the stream at the start of the target chunk
            long targetPosition = GetChunkStartPosition(targetChunkNumber);
            _inputStream.Position = targetPosition;

            // Reset state to target chunk
            _currentChunkNumber = targetChunkNumber;
            _plaintextBufferPosition = 0;
            _plaintextBufferLength = 0;
            _endOfStreamReached = false;

            // Read and decrypt the chunk WITHOUT incrementing chunk number
            ReadAndDecryptNextChunk(incrementChunkNumber: false);

#if DEBUG
            _metrics.StopTiming(ref _metrics.TotalOperation4TimeMs); // Seek
#endif
        }

        public override void SetLength(long value) => throw new NotSupportedException("DecryptingStream length cannot be set.");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("DecryptingStream does not support writing.");
    }
}