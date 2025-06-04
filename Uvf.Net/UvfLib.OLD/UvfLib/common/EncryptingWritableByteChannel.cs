using System;
using System.IO;
using UvfLib.Api;

namespace UvfLib.Common
{
    /// <summary>
    /// A writable byte channel that encrypts data before writing it to the underlying channel.
    /// </summary>
    public class EncryptingWritableByteChannel : IDisposable
    {
        private readonly Stream _destination;
        private readonly IFileContentCryptor _contentCryptor;
        private readonly FileHeaderCryptor _headerCryptor;
        private readonly FileHeader _header;
        private readonly int _cleartextChunkSize;
        private readonly byte[] _cleartextBuffer;
        private readonly bool _leaveOpen;
        private int _bytesInBuffer = 0;
        private long _chunkNumber = 0;
        private bool _headerWritten = false;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new encrypting writable byte channel.
        /// </summary>
        /// <param name="destination">The destination stream</param>
        /// <param name="cryptor">The cryptor to use</param>
        /// <param name="leaveOpen">Whether to leave the destination stream open when this channel is disposed.</param>
        public EncryptingWritableByteChannel(Stream destination, Cryptor cryptor, bool leaveOpen = false)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (cryptor == null)
                throw new ArgumentNullException(nameof(cryptor));

            _destination = destination;
            _leaveOpen = leaveOpen;
            _contentCryptor = cryptor.FileContentCryptor();
            _headerCryptor = cryptor.FileHeaderCryptor();
            _header = _headerCryptor.Create();
            _cleartextChunkSize = _contentCryptor.CleartextChunkSize();
            _cleartextBuffer = new byte[_cleartextChunkSize];
        }

        /// <summary>
        /// Writes data to the channel.
        /// </summary>
        /// <param name="src">The source buffer</param>
        /// <param name="offset">The offset within the buffer</param>
        /// <param name="count">The number of bytes to write</param>
        /// <returns>The number of bytes written</returns>
        public int Write(byte[] src, int offset, int count)
        {
            ThrowIfDisposed();

            if (src == null)
                throw new ArgumentNullException(nameof(src));
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative");
            if (offset + count > src.Length)
                throw new ArgumentException("The sum of offset and count is greater than the buffer length");

            // Write header first if not written yet
            if (!_headerWritten)
            {
                WriteHeader();
            }

            int totalBytesWritten = 0;
            int remaining = count;

            while (remaining > 0)
            {
                // Calculate how many bytes we can add to the buffer
                int bytesToWrite = Math.Min(remaining, _cleartextChunkSize - _bytesInBuffer);

                // Copy data into our buffer
                Buffer.BlockCopy(src, offset + totalBytesWritten, _cleartextBuffer, _bytesInBuffer, bytesToWrite);
                _bytesInBuffer += bytesToWrite;
                totalBytesWritten += bytesToWrite;
                remaining -= bytesToWrite;

                // If the buffer is full, encrypt and write it
                if (_bytesInBuffer == _cleartextChunkSize)
                {
                    FlushBuffer(false);
                }
            }

            return totalBytesWritten;
        }

        /// <summary>
        /// Closes this channel and flushes any remaining data.
        /// </summary>
        public void Close()
        {
            if (_disposed)
                return;

            try
            {
                bool headerWasWritten = _headerWritten;
                // Write header if not written yet
                if (!headerWasWritten)
                {
                    WriteHeader();
                }

                // Flush remaining bytes if any, OR flush an empty chunk if this is the very first chunk (empty file case)
                if (_bytesInBuffer > 0 || !headerWasWritten) // If buffer has data OR header was just written (meaning no data ever came)
                {
                    FlushBuffer(true);
                }

                // Only close destination if _leaveOpen is false
                if (!_leaveOpen)
                {
                    _destination.Close();
                }
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
            _destination.Write(encryptedHeader.ToArray(), 0, encryptedHeader.Length);
            _headerWritten = true;
        }

        private void FlushBuffer(bool isLastChunk)
        {
            // Prepare cleartext chunk
            ReadOnlyMemory<byte> cleartextChunk = new ReadOnlyMemory<byte>(_cleartextBuffer, 0, _bytesInBuffer);

            // Encrypt the chunk
            Memory<byte> encryptedChunk = _contentCryptor.EncryptChunk(cleartextChunk, _chunkNumber, _header);

            // Write to destination
            _destination.Write(encryptedChunk.ToArray(), 0, encryptedChunk.Length);

            // Reset buffer and increment chunk number
            _bytesInBuffer = 0;
            _chunkNumber++;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }
    }
}