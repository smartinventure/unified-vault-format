using System;
using System.IO;
using UvfLib.Api;

namespace UvfLib.Common
{
    /// <summary>
    /// A readable byte channel that decrypts data read from the underlying channel.
    /// </summary>
    public class DecryptingReadableByteChannel : IDisposable
    {
        private readonly Stream _source;
        private readonly Cryptor _cryptor;
        private readonly IFileContentCryptor _contentCryptor;
        private readonly int _blockSize;
        private readonly bool _authenticate;
        private FileHeader _header;
        private long _chunkNumber;
        private byte[] _cleartextBuffer;
        private int _bufferPosition;
        private int _bufferLimit;
        private bool _headerRead = false;
        private bool _endOfInput = false;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new decrypting readable byte channel.
        /// </summary>
        /// <param name="source">The source stream</param>
        /// <param name="cryptor">The cryptor to use</param>
        /// <param name="blockSize">The block size to use for reads</param>
        /// <param name="authenticate">Whether to authenticate the data</param>
        public DecryptingReadableByteChannel(Stream source, Cryptor cryptor, int blockSize, bool authenticate)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _contentCryptor = cryptor.FileContentCryptor();
            _blockSize = blockSize;
            _authenticate = authenticate;
            _chunkNumber = 0;
            _cleartextBuffer = new byte[_contentCryptor.CleartextChunkSize()];
            _bufferPosition = 0;
            _bufferLimit = 0;
        }

        /// <summary>
        /// Creates a new decrypting readable byte channel with a pre-configured header.
        /// </summary>
        /// <param name="source">The source stream</param>
        /// <param name="cryptor">The cryptor to use</param>
        /// <param name="blockSize">The block size to use for reads</param>
        /// <param name="authenticate">Whether to authenticate the data</param>
        /// <param name="header">The pre-configured header</param>
        /// <param name="initialChunkNumber">The initial chunk number</param>
        public DecryptingReadableByteChannel(Stream source, Cryptor cryptor, int blockSize, bool authenticate, FileHeader header, long initialChunkNumber)
            : this(source, cryptor, blockSize, authenticate)
        {
            _header = header ?? throw new ArgumentNullException(nameof(header));
            _headerRead = true;
            _chunkNumber = initialChunkNumber;
        }

        /// <summary>
        /// Reads bytes from this channel into the specified buffer.
        /// </summary>
        /// <param name="dst">The destination buffer</param>
        /// <param name="offset">The offset in the buffer at which to start storing bytes</param>
        /// <param name="count">The maximum number of bytes to read</param>
        /// <returns>The number of bytes read, or 0 if there is no more data</returns>
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

            if (_endOfInput)
                return 0;

            // Read the header if not already read
            if (!_headerRead)
            {
                ReadHeader();
            }

            int totalBytesRead = 0;
            int remaining = count;

            while (remaining > 0)
            {
                // If buffer is empty, refill it
                if (_bufferPosition >= _bufferLimit)
                {
                    if (!FillBuffer())
                    {
                        // End of input reached
                        break;
                    }
                }

                // Copy data from buffer to destination
                int bytesToCopy = Math.Min(remaining, _bufferLimit - _bufferPosition);
                Buffer.BlockCopy(_cleartextBuffer, _bufferPosition, dst, offset + totalBytesRead, bytesToCopy);

                _bufferPosition += bytesToCopy;
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

        private void ReadHeader()
        {
            // Determine header size
            int headerSize = _cryptor.FileHeaderCryptor().HeaderSize();

            // Read the encrypted header
            byte[] encryptedHeader = new byte[headerSize];
            int bytesRead = _source.Read(encryptedHeader, 0, headerSize);

            if (bytesRead < headerSize)
            {
                _endOfInput = true;
                throw new EndOfStreamException("Reached end of stream while reading header");
            }

            // Decrypt the header
            _header = _cryptor.FileHeaderCryptor().DecryptHeader(new ReadOnlyMemory<byte>(encryptedHeader));
            _headerRead = true;
        }

        private bool FillBuffer()
        {
            if (_endOfInput)
                return false;

            try
            {
                // Read the next chunk size based on the content cryptor's ciphertext chunk size
                int ciphertextChunkSize = _contentCryptor.CiphertextChunkSize();
                byte[] encryptedChunk = new byte[ciphertextChunkSize];

                int bytesRead = _source.Read(encryptedChunk, 0, ciphertextChunkSize);

                if (bytesRead <= 0)
                {
                    _endOfInput = true;
                    return false;
                }

                // Decrypt the chunk and get the actual number of decrypted bytes
                int decryptedBytesCount = _contentCryptor.DecryptChunk(
                    new ReadOnlyMemory<byte>(encryptedChunk, 0, bytesRead),
                    _cleartextBuffer, // Pass the buffer to write into
                    _chunkNumber,
                    _header,
                    _authenticate);

                // Set buffer limit to the actual number of bytes decrypted
                _bufferPosition = 0;
                _bufferLimit = decryptedBytesCount;

                // If bytesRead was less than a full ciphertext chunk, it must be the end of input.
                if (bytesRead < ciphertextChunkSize)
                {
                    _endOfInput = true; // Mark end of input since we didn't read a full chunk
                }

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