using System;
using System.IO;
using UvfLib.Api;

namespace UvfLib.Common
{
    /// <summary>
    /// A readable byte channel that encrypts data as it is read from the underlying channel.
    /// </summary>
    public class EncryptingReadableByteChannel : IDisposable
    {
        private readonly Stream _source;
        private readonly Cryptor _cryptor;
        private readonly IFileContentCryptor _contentCryptor;
        private readonly FileHeaderCryptor _headerCryptor;
        private readonly FileHeader _header;
        private readonly int _blockSize;
        private byte[] _cleartextBuffer;
        private byte[] _encryptedBuffer;
        private int _encryptedBufferPosition;
        private int _encryptedBufferLimit;
        private long _chunkNumber = 0;
        private bool _headerWritten = false;
        private bool _endOfInput = false;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new encrypting readable byte channel.
        /// </summary>
        /// <param name="source">The source stream</param>
        /// <param name="cryptor">The cryptor to use</param>
        /// <param name="blockSize">The block size to use for reads</param>
        public EncryptingReadableByteChannel(Stream source, Cryptor cryptor, int blockSize)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _contentCryptor = cryptor.FileContentCryptor();
            _headerCryptor = cryptor.FileHeaderCryptor();
            _header = _headerCryptor.Create();
            _blockSize = blockSize;
            _cleartextBuffer = new byte[_contentCryptor.CleartextChunkSize()];
            _encryptedBuffer = new byte[_contentCryptor.CiphertextChunkSize()];
            _encryptedBufferPosition = 0;
            _encryptedBufferLimit = 0;
        }

        /// <summary>
        /// Reads encrypted bytes from this channel into the specified buffer.
        /// </summary>
        /// <param name="dst">The destination buffer</param>
        /// <param name="offset">The offset in the buffer at which to start storing bytes</param>
        /// <param name="count">The maximum number of bytes to read</param>
        /// <returns>The number of bytes read, or -1 if there is no more data</returns>
        public int Read(byte[] dst, int offset, int count)
        {
            ThrowIfDisposed();

            if (dst == null)
                throw new ArgumentNullException(nameof(dst));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative");
            if (offset + count > dst.Length)
                throw new ArgumentException("The sum of offset and count is greater than the buffer length");

            // If we've reached the end of input and have no more buffered data, we're done
            if (_endOfInput && _encryptedBufferPosition >= _encryptedBufferLimit)
                return 0;

            // Write header first if not written yet
            if (!_headerWritten)
            {
                WriteHeader();
            }

            int totalBytesRead = 0;
            int remaining = count;

            while (remaining > 0)
            {
                // If buffer is empty, refill it
                if (_encryptedBufferPosition >= _encryptedBufferLimit)
                {
                    if (!FillEncryptedBuffer())
                    {
                        // End of input reached
                        break;
                    }
                }

                // Copy data from buffer to destination
                int bytesToCopy = Math.Min(remaining, _encryptedBufferLimit - _encryptedBufferPosition);
                Buffer.BlockCopy(_encryptedBuffer, _encryptedBufferPosition, dst, offset + totalBytesRead, bytesToCopy);

                _encryptedBufferPosition += bytesToCopy;
                totalBytesRead += bytesToCopy;
                remaining -= bytesToCopy;
            }

            return totalBytesRead;
        }

        /// <summary>
        /// Closes this channel.
        /// </summary>
        public void Close()
        {
            if (_disposed)
                return;

            try
            {
                _source.Close();
            }
            finally
            {
                _disposed = true;
            }
        }

        /// <summary>
        /// Disposes this channel.
        /// </summary>
        public void Dispose()
        {
            Close();
        }

        private void WriteHeader()
        {
            Memory<byte> encryptedHeader = _headerCryptor.EncryptHeader(_header);
            byte[] headerBytes = encryptedHeader.ToArray();

            // Copy the header to our buffer
            Buffer.BlockCopy(headerBytes, 0, _encryptedBuffer, 0, headerBytes.Length);
            _encryptedBufferPosition = 0;
            _encryptedBufferLimit = headerBytes.Length;

            _headerWritten = true;
        }

        private bool FillEncryptedBuffer()
        {
            if (_endOfInput)
                return false;

            try
            {
                // Read a chunk of cleartext
                int bytesRead = _source.Read(_cleartextBuffer, 0, _cleartextBuffer.Length);

                if (bytesRead <= 0)
                {
                    _endOfInput = true;
                    return false;
                }

                // Encrypt the chunk
                ReadOnlyMemory<byte> cleartextChunk = new ReadOnlyMemory<byte>(_cleartextBuffer, 0, bytesRead);
                Memory<byte> encryptedChunk = _contentCryptor.EncryptChunk(cleartextChunk, _chunkNumber, _header);

                // Copy to buffer
                byte[] encryptedArray = encryptedChunk.ToArray();
                Buffer.BlockCopy(encryptedArray, 0, _encryptedBuffer, 0, encryptedArray.Length);

                _encryptedBufferPosition = 0;
                _encryptedBufferLimit = encryptedArray.Length;
                _chunkNumber++;

                return true;
            }
            catch (EndOfStreamException)
            {
                _endOfInput = true;
                return false;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}