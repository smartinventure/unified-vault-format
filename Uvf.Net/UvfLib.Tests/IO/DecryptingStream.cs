using System;
using System.IO;
using System.Security.Cryptography;
using UvfLib.Core.Common;

namespace UvfLib.IO
{
    /// <summary>
    /// A stream that decrypts data using AES-CTR.
    /// </summary>
    public class AesCtrDecryptingStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly ICryptoTransform _decryptor;
        private readonly byte[] _buffer;
        private int _bufferPosition = 0;
        private int _bufferFilled = 0;
        private readonly bool _leaveOpen;
        private bool _disposed = false;
        
        /// <summary>
        /// Creates a new decrypting stream.
        /// </summary>
        /// <param name="baseStream">The underlying stream to read encrypted data from</param>
        /// <param name="aes">The AES algorithm instance to use</param>
        /// <param name="leaveOpen">Whether to leave the base stream open when this stream is closed</param>
        public AesCtrDecryptingStream(Stream baseStream, Aes aes, bool leaveOpen = false)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            if (aes == null) throw new ArgumentNullException(nameof(aes));
            _leaveOpen = leaveOpen;
            
            // Use the library's AES-CTR implementation
            _decryptor = CipherSupplier.AES_CTR.DecryptionCipher(aes.Key, aes.IV);
            
            // Buffer for decryption
            _buffer = new byte[8192]; // Default buffer size
        }
        
        /// <summary>
        /// Gets whether the stream supports reading.
        /// </summary>
        public override bool CanRead => _baseStream.CanRead;
        
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
            get => throw new NotSupportedException();
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
            if (_disposed) throw new ObjectDisposedException(nameof(AesCtrDecryptingStream));
            if (!CanRead) throw new NotSupportedException("Stream does not support reading");
            
            // Check arguments
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (offset + count > buffer.Length) throw new ArgumentException("The sum of offset and count is larger than the buffer length");
            
            // If buffer is empty, refill it
            if (_bufferPosition >= _bufferFilled)
            {
                FillBuffer();
                if (_bufferFilled == 0) // End of stream
                {
                    return 0;
                }
            }
            
            // Copy data from buffer to output
            int bytesToCopy = Math.Min(count, _bufferFilled - _bufferPosition);
            Buffer.BlockCopy(_buffer, _bufferPosition, buffer, offset, bytesToCopy);
            _bufferPosition += bytesToCopy;
            
            return bytesToCopy;
        }
        
        private void FillBuffer()
        {
            // Reset buffer state
            _bufferPosition = 0;
            _bufferFilled = 0;
            
            // Read encrypted data
            int bytesRead = _baseStream.Read(_buffer, 0, _buffer.Length);
            if (bytesRead > 0)
            {
                // Decrypt the data
                byte[] tempBuffer = new byte[bytesRead];
                Buffer.BlockCopy(_buffer, 0, tempBuffer, 0, bytesRead);
                
                byte[] decryptedData = _decryptor.TransformFinalBlock(tempBuffer, 0, bytesRead);
                
                // Copy back to buffer
                Buffer.BlockCopy(decryptedData, 0, _buffer, 0, decryptedData.Length);
                _bufferFilled = decryptedData.Length;
            }
        }
        
        /// <summary>
        /// Writes data to the stream.
        /// </summary>
        /// <param name="buffer">The buffer to write data from</param>
        /// <param name="offset">The offset in the buffer to start reading data from</param>
        /// <param name="count">The number of bytes to write</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Stream does not support writing");
        }
        
        /// <summary>
        /// Flushes the stream.
        /// </summary>
        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesCtrDecryptingStream));
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
            throw new NotSupportedException("Seeking is not supported");
        }
        
        /// <summary>
        /// Sets the length of the stream.
        /// </summary>
        /// <param name="value">The new length of the stream</param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException("Setting length is not supported");
        }
        
        /// <summary>
        /// Releases all resources used by the stream.
        /// </summary>
        /// <param name="disposing">Whether the method is being called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                // Dispose the decryptor
                _decryptor.Dispose();
                
                // Close the base stream if required
                if (!_leaveOpen)
                {
                    _baseStream.Dispose();
                }
                
                _disposed = true;
            }
            
            base.Dispose(disposing);
        }
    }
} 