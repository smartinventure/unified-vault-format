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
using System.Diagnostics; // For Stopwatch
#endif

namespace UvfLib.VaultHelpers
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
        private readonly byte[] _aadBuffer; // Buffer for AAD
        private int _bufferPosition = 0;
        private long _currentChunkNumber = 0;
        private bool _headerWritten = false;
        private bool _isDisposed = false;

#if DEBUG
        private readonly PerformanceMetrics _metrics;
#endif

        private const int CLEARTEXT_CHUNK_SIZE = UvfLib.Core.V3.Constants.PAYLOAD_SIZE; // Reverted to use constant
        private readonly Memory<byte> _ciphertextChunkBuffer; // Reusable buffer for encrypted output

        public EncryptingStream(Cryptor cryptor, Stream outputStream, bool leaveOpen)
        {
#if DEBUG
            Console.WriteLine($"DEBUG_EncryptingStream: CONSTRUCTOR - Outputting to stream type: {outputStream?.GetType().Name}, CanWrite: {outputStream?.CanWrite}");
#endif
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

            _fileHeader = _cryptor.FileHeaderCryptor().Create();

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
            }
            else
            {
                throw new NotSupportedException($"Unsupported FileHeader type: {_fileHeader.GetType().FullName}");
            }

            _cleartextChunkBuffer = new byte[CLEARTEXT_CHUNK_SIZE]; // Uses the constant
            // Ciphertext buffer size should also be based on the constant PAYLOAD_SIZE via CleartextChunkSize() or directly
            _ciphertextChunkBuffer = new Memory<byte>(new byte[_cryptor.FileContentCryptor().CiphertextChunkSize()]);
        }

        private void EnsureHeaderWritten()
        {
            if (!_headerWritten)
            {
#if DEBUG
                Console.WriteLine($"DEBUG_EncryptingStream: EnsureHeaderWritten - Attempting to write header. CurrentChunkNumber: {_currentChunkNumber}");
#endif
                var encryptedHeaderBytes = _cryptor.FileHeaderCryptor().EncryptHeader(_fileHeader);
                _outputStream.Write(encryptedHeaderBytes.Span);
#if DEBUG
                Console.WriteLine($"DEBUG_EncryptingStream: EnsureHeaderWritten - SUCCESSFULLY WROTE {encryptedHeaderBytes.Length} header bytes. CurrentChunkNumber: {_currentChunkNumber}");
#endif
                _headerWritten = true;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckDisposed();
            EnsureHeaderWritten();

            int bytesToProcess = count;
            int currentOffset = offset;

            while (bytesToProcess > 0)
            {
                int bytesToCopy = Math.Min(bytesToProcess, CLEARTEXT_CHUNK_SIZE - _bufferPosition);
                Buffer.BlockCopy(buffer, currentOffset, _cleartextChunkBuffer, _bufferPosition, bytesToCopy);
                _bufferPosition += bytesToCopy;
                currentOffset += bytesToCopy;
                bytesToProcess -= bytesToCopy;

                // If buffer is full, encrypt and write chunk
                if (_bufferPosition == CLEARTEXT_CHUNK_SIZE)
                {
                    EncryptAndWriteChunk(_cleartextChunkBuffer.AsMemory(0, CLEARTEXT_CHUNK_SIZE));
                    _bufferPosition = 0; // Reset buffer
                }
            }
        }

        private void EncryptAndWriteChunk(ReadOnlyMemory<byte> cleartextChunk)
        {
            _random.GetBytes(_perChunkNonce);

            BinaryPrimitives.WriteInt64BigEndian(_aadBuffer.AsSpan(0, 8), _currentChunkNumber);

            // Handle both V3 and CryptomatorV8 implementations
            if (_cryptor.FileContentCryptor() is UvfLib.Core.V3.FileContentCryptorImpl v3Cryptor)
            {
                // V3 implementation
                int expectedEncryptedLength = UvfLib.Core.V3.Constants.GCM_NONCE_SIZE + cleartextChunk.Length + UvfLib.Core.V3.Constants.GCM_TAG_SIZE;
                _ciphertextChunkBuffer.Slice(0, expectedEncryptedLength).Span.Clear();
                
                v3Cryptor.EncryptChunk(
                    _fileContentAesGcm,
                    cleartextChunk,
                    _ciphertextChunkBuffer, 
                    _currentChunkNumber,
                    _perChunkNonce, 
                    _aadBuffer 
                );
                
                _currentChunkNumber++; 
                
                int actualEncryptedLength = UvfLib.Core.V3.Constants.GCM_NONCE_SIZE + cleartextChunk.Length + UvfLib.Core.V3.Constants.GCM_TAG_SIZE;
                
                if (actualEncryptedLength != expectedEncryptedLength)
                {
                    throw new InvalidOperationException($"Encrypted length mismatch: expected {expectedEncryptedLength}, actual {actualEncryptedLength}");
                }
                
                _outputStream.Write(_ciphertextChunkBuffer.Slice(0, actualEncryptedLength).Span);
            }
            else if (_cryptor.FileContentCryptor() is Core.CryptomatorV8.FileContentCryptorImpl v8Cryptor)
            {
                // CryptomatorV8 implementation - use proper random nonce and AAD according to specification
                int expectedEncryptedLength = Core.CryptomatorV8.Constants.GCM_NONCE_SIZE + cleartextChunk.Length + Core.CryptomatorV8.Constants.GCM_TAG_SIZE;
                _ciphertextChunkBuffer.Slice(0, expectedEncryptedLength).Span.Clear();
                
                // Generate completely random nonce (as per Cryptomator specification)
                byte[] nonce = new byte[Core.CryptomatorV8.Constants.GCM_NONCE_SIZE];
                _random.GetBytes(nonce);
                
                // Copy nonce to the beginning of ciphertext buffer
                nonce.CopyTo(_ciphertextChunkBuffer.Span);
                
                // Construct AAD: bigEndian(chunkNumber) . headerNonce (as per specification)
                byte[] chunkNumberBytes = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(chunkNumberBytes, _currentChunkNumber);
                byte[] headerNonce = v8Cryptor.GetNonce().ToArray();
                
                // Encrypt using AES-GCM with proper AAD
                byte[] ciphertext = new byte[cleartextChunk.Length];
                byte[] tag = new byte[Core.CryptomatorV8.Constants.GCM_TAG_SIZE];
                
                // Pass AAD as two separate byte arrays to match Java's two updateAAD calls
                _fileContentAesGcm.Encrypt(nonce, cleartextChunk.Span, ciphertext, tag, associatedData: new[] { chunkNumberBytes, headerNonce });
                
                // Copy ciphertext and tag to the buffer
                ciphertext.CopyTo(_ciphertextChunkBuffer.Span.Slice(Core.CryptomatorV8.Constants.GCM_NONCE_SIZE));
                tag.CopyTo(_ciphertextChunkBuffer.Span.Slice(Core.CryptomatorV8.Constants.GCM_NONCE_SIZE + cleartextChunk.Length));
                
                long chunkNumberProcessed = _currentChunkNumber; // Capture for logging
                _currentChunkNumber++;
                
                _outputStream.Write(_ciphertextChunkBuffer.Slice(0, expectedEncryptedLength).Span);
                _outputStream.Flush();
                
#if DEBUG
                // Only log for very small cleartext (likely dirid.c9r files)
                if (cleartextChunk.Length <= 10)
                {
                    Console.WriteLine($"DEBUG_EncryptingStream: WriteChunk (V8) SMALL FILE - cleartext: {cleartextChunk.Length} bytes, wrote: {expectedEncryptedLength} bytes, chunk: {chunkNumberProcessed}");
                }
#endif
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

#if DEBUG
            Console.WriteLine($"DEBUG_EncryptingStream: Flush() called - BufferPosition: {_bufferPosition}, CurrentChunk: {_currentChunkNumber}");
#endif

            // Encrypt and write any remaining data in the buffer as the final chunk
            if (_bufferPosition > 0)
            {
#if DEBUG
                Console.WriteLine($"DEBUG_EncryptingStream: Flush() writing final chunk - BufferPosition: {_bufferPosition} bytes");
#endif
                EncryptAndWriteChunk(_cleartextChunkBuffer.AsMemory(0, _bufferPosition));
                _bufferPosition = 0; // Clear buffer after flushing
            }
            else
            {
#if DEBUG
                Console.WriteLine($"DEBUG_EncryptingStream: Flush() - No buffered data to write, but ensuring at least one chunk for empty file");
#endif
                // For empty files, we still need to write at least one chunk (even if 0 bytes)
                // This is critical for dirid.c9r files which contain empty strings
                if (_currentChunkNumber == 0)
                {
#if DEBUG
                    Console.WriteLine($"DEBUG_EncryptingStream: Flush() - Writing empty chunk for empty file");
#endif
                    EncryptAndWriteChunk(_cleartextChunkBuffer.AsMemory(0, 0)); // Write 0-byte chunk
                }
            }

            // CRITICAL: Ensure underlying stream is properly flushed
            _outputStream.Flush(); // Flush the underlying stream
#if DEBUG
            Console.WriteLine($"DEBUG_EncryptingStream: Flush() completed - FinalChunkNumber: {_currentChunkNumber}");
#endif
        }

        protected override void Dispose(bool disposing)
        {
#if DEBUG
            Console.WriteLine($"DEBUG_EncryptingStream: Dispose() called - disposing: {disposing}, CurrentChunk: {_currentChunkNumber}");
#endif
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
#if DEBUG
                            Console.WriteLine($"DEBUG_EncryptingStream: Dispose() - Forced OS flush on FileStream");
#endif
                        }
                    }
                    finally
                    {
                        _fileContentAesGcm?.Dispose(); 
                        _fileHeader?.Dispose(); 

                        if (!_leaveOpen)
                        {
#if DEBUG
                            Console.WriteLine($"DEBUG_EncryptingStream: Dispose() - Closing underlying stream");
#endif
                            _outputStream?.Dispose();
                        }
                        _random?.Dispose(); 
#if DEBUG
                        _metrics?.Report();
#endif
                    }
                }
                _isDisposed = true;
#if DEBUG
                Console.WriteLine($"DEBUG_EncryptingStream: Dispose() completed - Stream disposed");
#endif
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
        public override bool CanSeek => false;
        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException("EncryptingStream is non-seekable.");
        public override long Position
        {
            get => throw new NotSupportedException("EncryptingStream is non-seekable.");
            set => throw new NotSupportedException("EncryptingStream is non-seekable.");
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("EncryptingStream does not support reading.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("EncryptingStream is non-seekable.");
        public override void SetLength(long value) => throw new NotSupportedException("EncryptingStream length cannot be set.");

    }
}