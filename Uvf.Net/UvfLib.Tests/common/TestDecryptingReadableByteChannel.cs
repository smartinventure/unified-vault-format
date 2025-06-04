using System;
using System.IO;
using System.Threading.Tasks;
using UvfLib._old.common;
using UvfLib._old.api;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// Simplified test version of DecryptingReadableByteChannel.
    /// Does not perform actual decryption, only logs.
    /// </summary>
    internal sealed class TestDecryptingReadableByteChannel : ISeekableByteChannel, IDisposable // Renamed class
    {
        private readonly ISeekableByteChannel _source;
        private readonly ICryptor _cryptor;
        private bool _closed;

        /// <summary>
        /// Simplified constructor for testing.
        /// </summary>
        /// <param name="source">The underlying channel to read (non-decrypted) data from.</param>
        /// <param name="cryptor">The cryptor (ignored in this test version).</param>
        public TestDecryptingReadableByteChannel(ISeekableByteChannel source, ICryptor cryptor) // Updated constructor name
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _closed = false;
            Console.WriteLine("Warning: Using simplified TestDecryptingReadableByteChannel (Test Version). No decryption will occur.");
        }

        public bool IsOpen => !_closed;

        // Rename property to avoid conflict
        private long CurrentPositionProp
        {
            get => _source.Position();
            set => _source.Position(value);
        }

        // Implement Position() method required by interface
        public long Position()
        {
            return CurrentPositionProp;
        }

        // Implement Position(long) method required by interface (fluent)
        public ISeekableByteChannel Position(long newPosition)
        {
            CurrentPositionProp = newPosition;
            return this;
        }

        // Add CurrentPosition property required by interface
        public long CurrentPosition => Position();

        public long Size() => _source.Size();

        // Add CurrentSize property required by interface
        public long CurrentSize => Size();

        public int Read(byte[] dst)
        {
            return Read(dst, 0, dst.Length);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (_closed)
            {
                throw new IOException("Channel closed");
            }
            Console.WriteLine($"Warning: Simplified sync Read requesting {count} bytes. Data not decrypted.");
            int bytesRead = _source.Read(buffer, offset, count);
            if (bytesRead <= 0)
            {
                Close();
            }
            return bytesRead;
        }

        public int Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Write operation is not supported by TestDecryptingReadableByteChannel.");
        }

        public long Seek(long offset) // Assuming Seek(long) signature from error
        {
            // Delegate seek to source if possible, otherwise throw
            // This assumes _source has a compatible Seek method.
            if (_source is Stream sourceStream)
            {
                // SeekOrigin.Begin is assumed; adjust if interface differs
                return sourceStream.Seek(offset, SeekOrigin.Begin);
            }
            throw new NotImplementedException("Seek not implemented in simplified test version or source doesn't support it.");
        }

        public void Close()
        {
            if (!_closed)
            {
                _closed = true;
                _source.Close();
                Console.WriteLine("Simplified TestDecryptingReadableByteChannel closed.");
            }
        }

        // Added Dispose method
        public void Dispose()
        {
            // Nothing specific to dispose in this simplified version, but implement the interface.
            Close();
        }

        public ISeekableByteChannel Truncate(long size)
        {
            throw new NotSupportedException("Truncate is not part of ISeekableByteChannel.");
        }
    }
}