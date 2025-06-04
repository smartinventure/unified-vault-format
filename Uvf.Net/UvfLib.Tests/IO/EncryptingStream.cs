using System;
using System.IO;
using System.Security.Cryptography;
using UvfLib.Core.Common;

namespace UvfLib.IO
{
    /// <summary>
    /// A stream that encrypts data using AES-CTR.
    /// </summary>
    public class AesCtrEncryptingStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly ICryptoTransform _encryptor;
        private readonly byte[] _buffer;
        private int _bufferPosition = 0;
        private readonly bool _leaveOpen;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new encrypting stream.
        /// </summary>
        /// <param name="baseStream">The underlying stream to write encrypted data to</param>
        /// <param name="aes">The AES algorithm instance to use</param>
        /// <param name="leaveOpen">Whether to leave the base stream open when this stream is closed</param>
        public AesCtrEncryptingStream(Stream baseStream, Aes aes, bool leaveOpen = false)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            if (aes == null) throw new ArgumentNullException(nameof(aes));
            _leaveOpen = leaveOpen;

            // Use the library's AES-CTR implementation
            _encryptor = CipherSupplier.AES_CTR.EncryptionCipher(aes.Key, aes.IV);

            // Buffer for encryption
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
        public override bool CanWrite => _baseStream.CanWrite;

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
            if (_disposed) throw new ObjectDisposedException(nameof(AesCtrEncryptingStream));
            if (!CanRead) throw new NotSupportedException("Stream does not support reading");

            int bytesRead = _baseStream.Read(buffer, offset, count);
            if (bytesRead > 0)
            {
                byte[] tempBuffer = new byte[bytesRead];
                Buffer.BlockCopy(buffer, offset, tempBuffer, 0, bytesRead);

                // Encrypt the data
                byte[] encryptedData = _encryptor.TransformFinalBlock(tempBuffer, 0, bytesRead);

                // Copy back to original buffer
                Buffer.BlockCopy(encryptedData, 0, buffer, offset, bytesRead);
            }

            return bytesRead;
        }

        /// <summary>
        /// Writes data to the stream.
        /// </summary>
        /// <param name="buffer">The buffer to write data from</param>
        /// <param name="offset">The offset in the buffer to start reading data from</param>
        /// <param name="count">The number of bytes to write</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesCtrEncryptingStream));
            if (!CanWrite) throw new NotSupportedException("Stream does not support writing");

            byte[] tempBuffer = new byte[count];
            Buffer.BlockCopy(buffer, offset, tempBuffer, 0, count);

            // Encrypt the data
            byte[] encryptedData = _encryptor.TransformFinalBlock(tempBuffer, 0, count);

            // Write to the base stream
            _baseStream.Write(encryptedData, 0, encryptedData.Length);
        }

        /// <summary>
        /// Flushes the stream.
        /// </summary>
        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AesCtrEncryptingStream));

            // Flush any buffered data
            if (_bufferPosition > 0)
            {
                byte[] tempBuffer = new byte[_bufferPosition];
                Buffer.BlockCopy(_buffer, 0, tempBuffer, 0, _bufferPosition);

                // Encrypt and write the data
                byte[] encryptedData = _encryptor.TransformFinalBlock(tempBuffer, 0, _bufferPosition);
                _baseStream.Write(encryptedData, 0, encryptedData.Length);

                _bufferPosition = 0;
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
                // Flush any remaining data
                Flush();

                // Dispose the encryptor
                _encryptor.Dispose();

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