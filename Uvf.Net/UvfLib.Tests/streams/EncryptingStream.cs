using System;
using System.IO;
using UvfLib.Core.Api;

namespace UvfLib.Tests.Streams
{
    /// <summary>
    /// A stream that encrypts data using a Cryptor.
    /// </summary>
    public class EncryptingStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly Cryptor _cryptor;
        private readonly byte[] _buffer;
        private readonly byte[] _encryptedBuffer;
        private int _bufferPosition = 0;
        private long _position = 0;
        private bool _headerWritten = false;
        private FileHeader _header;
        
        /// <summary>
        /// Creates a new encrypting stream.
        /// </summary>
        /// <param name="baseStream">The underlying stream to write encrypted data to</param>
        /// <param name="cryptor">The cryptor to use for encryption</param>
        public EncryptingStream(Stream baseStream, Cryptor cryptor)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            
            int cleartextChunkSize = _cryptor.FileContentCryptor().CleartextChunkSize();
            int ciphertextChunkSize = _cryptor.FileContentCryptor().CiphertextChunkSize();
            
            _buffer = new byte[cleartextChunkSize];
            _encryptedBuffer = new byte[ciphertextChunkSize];
            _header = _cryptor.FileHeaderCryptor().Create();
        }
        
        /// <summary>
        /// Gets whether the stream supports reading.
        /// </summary>
        public override bool CanRead => false;
        
        /// <summary>
        /// Gets whether the stream supports seeking.
        /// </summary>
        public override bool CanSeek => false;
        
        /// <summary>
        /// Gets whether the stream supports writing.
        /// </summary>
        public override bool CanWrite => true;
        
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
            throw new NotSupportedException();
        }
        
        /// <summary>
        /// Writes data to the stream.
        /// </summary>
        /// <param name="buffer">The buffer to write data from</param>
        /// <param name="offset">The offset in the buffer to start reading data from</param>
        /// <param name="count">The number of bytes to write</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            
            // Write the header if not done yet
            if (!_headerWritten)
            {
                WriteHeader();
            }
            
            int remaining = count;
            while (remaining > 0)
            {
                int toCopy = Math.Min(remaining, _buffer.Length - _bufferPosition);
                Buffer.BlockCopy(buffer, offset + (count - remaining), _buffer, _bufferPosition, toCopy);
                
                _bufferPosition += toCopy;
                _position += toCopy;
                remaining -= toCopy;
                
                if (_bufferPosition == _buffer.Length)
                {
                    FlushBuffer();
                }
            }
        }
        
        /// <summary>
        /// Flushes the stream.
        /// </summary>
        public override void Flush()
        {
            if (_bufferPosition > 0)
            {
                FlushBuffer();
            }
            
            _baseStream.Flush();
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
        
        /// <summary>
        /// Releases all resources used by the stream.
        /// </summary>
        /// <param name="disposing">Whether the method is being called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Flush();
            }
            
            base.Dispose(disposing);
        }
        
        private void WriteHeader()
        {
            var headerBytes = _cryptor.FileHeaderCryptor().EncryptHeader(_header);
            _baseStream.Write(headerBytes.ToArray(), 0, _cryptor.FileHeaderCryptor().HeaderSize());
            _headerWritten = true;
        }
        
        private void FlushBuffer()
        {
            if (_bufferPosition > 0)
            {
                // Calculate chunk number based on position
                long chunkNumber = (_position - _bufferPosition) / _cryptor.FileContentCryptor().CleartextChunkSize();
                
                // If buffer is not full, zero the remainder
                if (_bufferPosition < _buffer.Length)
                {
                    Array.Clear(_buffer, _bufferPosition, _buffer.Length - _bufferPosition);
                }
                
                // Encrypt the buffer - only encrypt the actual data, not the whole buffer
                ReadOnlyMemory<byte> cleartext = new ReadOnlyMemory<byte>(_buffer, 0, _bufferPosition);
                
                // Create a buffer of the exact size needed for the ciphertext
                int ciphertextSize = _cryptor.FileContentCryptor().CiphertextChunkSize();
                var ciphertextBuffer = new Memory<byte>(new byte[ciphertextSize]);
                
                // Encrypt directly into the ciphertext buffer
                _cryptor.FileContentCryptor().EncryptChunk(cleartext, ciphertextBuffer, chunkNumber, _header);
                
                // Write the encrypted data
                byte[] encryptedData = ciphertextBuffer.ToArray();
                _baseStream.Write(encryptedData, 0, encryptedData.Length);
                
                // Reset the buffer position
                _bufferPosition = 0;
            }
        }
    }
} 