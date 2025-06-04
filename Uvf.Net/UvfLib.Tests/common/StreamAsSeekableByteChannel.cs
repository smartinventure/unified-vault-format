using System;
using System.IO;
using System.Threading.Tasks;
using UvfLib.Core.Common;
using UvfLib.Core.Api;    // Need this for IWritableByteChannel

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// Adapter that converts a Stream to a seekable byte channel.
    /// This class was moved from FileContentDecryptorBenchmark.cs.
    /// </summary>
    public class StreamAsSeekableByteChannel : ISeekableByteChannel, IWritableByteChannel, IDisposable
    {
        private readonly Stream _stream;
        private bool _closed = false;

        public StreamAsSeekableByteChannel(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        public bool IsOpen => !_closed && (_stream.CanRead || _stream.CanWrite);

        public long CurrentPosition
        {
            get
            {
                if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
                return _stream.Position;
            }
        }

        public long CurrentSize
        {
            get
            {
                if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
                return _stream.Length;
            }
        }

        public void Close()
        {
            if (!_closed)
            {
                _stream.Close();
                _closed = true;
            }
        }

        public void Dispose()
        {
            Close();
        }

        public int Read(byte[] dst, int offset, int count)
        {
            if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
            return _stream.Read(dst, offset, count);
        }

        public int Write(byte[] src, int offset, int count)
        {
            if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
            _stream.Write(src, offset, count);
            return count;
        }

        public long Seek(long position)
        {
            if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
            return _stream.Seek(position, SeekOrigin.Begin);
        }

        public long Position()
        {
            if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
            return _stream.Position;
        }

        public ISeekableByteChannel Position(long newPosition)
        {
            if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
            _stream.Position = newPosition;
            return this;
        }

        public long Size()
        {
            if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
            return _stream.Length;
        }

        public Task<int> Write(byte[] src)
        {
            if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
            _stream.Write(src, 0, src.Length);
            return Task.FromResult(src.Length);
        }

        // Truncate was missing, but ISeekableByteChannel requires it? No, it doesn't.
        /*
        public ISeekableByteChannel Truncate(long size)
        {
            if (_closed) throw new ObjectDisposedException(nameof(StreamAsSeekableByteChannel));
            _stream.SetLength(size);
            return this;
        }
        */
    }
}