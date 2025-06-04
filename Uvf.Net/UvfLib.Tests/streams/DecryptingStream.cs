using System;
using System.IO;
using UvfLib._old.api;
using UvfLib._old.v3;
using UvfLib.Common;

namespace UvfLib.Tests.Streams
{
    /// <summary>
    /// A stream that decrypts data using a Cryptor.
    /// </summary>
    public class DecryptingStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly Cryptor _cryptor;
        private readonly bool _authenticate;
        private readonly byte[] _buffer;
        private readonly byte[] _decryptedBuffer;
        private int _bufferPosition = 0;
        private int _bufferFilled = 0;
        private long _position = 0;
        private long _headerSize;
        private long _chunksRead = 0;
        private bool _headerRead = false;
        private bool _endOfStream = false;
        private FileHeader? _header = null;
        
        /// <summary>
        /// Creates a new decrypting stream.
        /// </summary>
        /// <param name="baseStream">The underlying stream to read encrypted data from</param>
        /// <param name="cryptor">The cryptor to use for decryption</param>
        /// <param name="authenticate">Whether to authenticate the data</param>
        public DecryptingStream(Stream baseStream, Cryptor cryptor, bool authenticate)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _authenticate = authenticate;
            
            _headerSize = _cryptor.FileHeaderCryptor().HeaderSize();
            int ciphertextChunkSize = _cryptor.FileContentCryptor().CiphertextChunkSize();
            int cleartextChunkSize = _cryptor.FileContentCryptor().CleartextChunkSize();
            
            _buffer = new byte[ciphertextChunkSize];
            _decryptedBuffer = new byte[cleartextChunkSize];
        }
        
        /// <summary>
        /// Gets whether the stream supports reading.
        /// </summary>
        public override bool CanRead => true;
        
        /// <summary>
        /// Gets whether the stream supports seeking.
        /// </summary>
        public override bool CanSeek => false;
        
        /// <summary>
        /// Gets whether the stream supports writing.
        /// </summary>
        public override bool CanWrite => false;
        
        /// <summary>
        /// Gets the length of the stream.
        /// </summary>
        public override long Length => throw new NotSupportedException();
        
        /// <summary>
        /// Gets or sets the position within the stream.
        /// </summary>
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }
        
        /// <summary>
        /// Reads data from the stream.
        /// </summary>
        /// <param name="buffer">The buffer to read data into</param>
        /// <param name="offset">The offset in the buffer to start writing data to</param>
        /// <param name="count">The maximum number of bytes to read</param>
        /// <returns>The number of bytes read</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            
            if (_endOfStream)
            {
                return 0;
            }
            
            // Read header if not done yet
            if (!_headerRead)
            {
                ReadHeader();
            }
            
            int totalRead = 0;
            while (totalRead < count && !_endOfStream)
            {
                // If buffer is empty, fill it
                if (_bufferPosition >= _bufferFilled)
                {
                    FillBuffer();
                    
                    // If still empty after filling, we've reached the end
                    if (_bufferPosition >= _bufferFilled)
                    {
                        _endOfStream = true;
                        break;
                    }
                }
                
                // Copy data from buffer to output
                int toCopy = Math.Min(count - totalRead, _bufferFilled - _bufferPosition);
                Buffer.BlockCopy(_decryptedBuffer, _bufferPosition, buffer, offset + totalRead, toCopy);
                
                _bufferPosition += toCopy;
                _position += toCopy;
                totalRead += toCopy;
            }
            
            return totalRead;
        }
        
        /// <summary>
        /// Writes data to the stream.
        /// </summary>
        /// <param name="buffer">The buffer to write data from</param>
        /// <param name="offset">The offset in the buffer to start reading data from</param>
        /// <param name="count">The number of bytes to write</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
        
        /// <summary>
        /// Flushes the stream.
        /// </summary>
        public override void Flush()
        {
            // No-op for read-only stream
        }
        
        /// <summary>
        /// Seeks to a position in the stream.
        /// </summary>
        /// <param name="offset">The offset to seek to</param>
        /// <param name="origin">The origin of the seek</param>
        /// <returns>The new position in the stream</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        
        /// <summary>
        /// Sets the length of the stream.
        /// </summary>
        /// <param name="value">The new length of the stream</param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        
        private void ReadHeader()
        {
            byte[] headerBytes = new byte[_headerSize];
            int bytesRead = _baseStream.Read(headerBytes, 0, headerBytes.Length);
            
            if (bytesRead < headerBytes.Length)
            {
                throw new IOException("Incomplete file header");
            }
            
            ReadOnlyMemory<byte> headerMemory = new ReadOnlyMemory<byte>(headerBytes);
            _header = _cryptor.FileHeaderCryptor().DecryptHeader(headerMemory);
            _headerRead = true;
        }
        
        private void FillBuffer()
        {
            // Reset buffer position
            _bufferPosition = 0;
            _bufferFilled = 0;
            
            // Calculate the expected size for the current chunk
            int expectedChunkSize = _cryptor.FileContentCryptor().CiphertextChunkSize();
            
            // Read from the base stream
            int bytesRead = _baseStream.Read(_buffer, 0, expectedChunkSize);
            
            if (bytesRead == 0)
            {
                // End of stream
                return;
            }
            
            try
            {
                // Decrypt the chunk
                if (_header == null)
                {
                    throw new InvalidOperationException("Header not initialized");
                }
                
                // Make sure we have enough data for a valid chunk (at minimum nonce + tag)
                int minSize = Constants.GCM_NONCE_SIZE + Constants.GCM_TAG_SIZE;
                if (bytesRead < minSize)
                {
                    throw new IOException($"Incomplete chunk: expected at least {minSize} bytes, got {bytesRead}");
                }
                
                ReadOnlyMemory<byte> ciphertext = new ReadOnlyMemory<byte>(_buffer, 0, bytesRead);
                
                // Allocate buffer for the decrypted data
                var cleartextBuffer = new Memory<byte>(new byte[_cryptor.FileContentCryptor().CleartextChunkSize()]);
                
                try
                {
                    // Decrypt directly into the cleartext buffer
                    _cryptor.FileContentCryptor().DecryptChunk(ciphertext, cleartextBuffer, _chunksRead, _header, _authenticate);
                    
                    // Calculate actual cleartext size (payload size)
                    int payloadSize = bytesRead - Constants.GCM_NONCE_SIZE - Constants.GCM_TAG_SIZE;
                    
                    // Copy only the valid data to the decrypted buffer
                    cleartextBuffer.Slice(0, payloadSize).CopyTo(_decryptedBuffer);
                    
                    // Set the buffer filled size based on the actual cleartext size
                    _bufferFilled = payloadSize;
                }
                catch (Exception ex) when (ex is AuthenticationFailedException || 
                                          (ex is IOException && ex.InnerException is AuthenticationFailedException))
                {
                    throw new IOException($"Authentication failed for chunk {_chunksRead}", ex);
                }
                
                // Increment chunks read
                _chunksRead++;
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to decrypt chunk {_chunksRead}", ex);
            }
        }
    }
} 