using System;
using System.IO;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// A test implementation of a seekable byte channel that wraps a MemoryStream
    /// </summary>
    public class StreamTestByteChannel : ISeekableByteChannel, IDisposable
    {
        private readonly MemoryStream _stream;
        private bool _isOpen = true;
        
        public StreamTestByteChannel(MemoryStream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }
        
        /// <summary>
        /// Gets whether the channel is open.
        /// </summary>
        public bool IsOpen => _isOpen && _stream.CanRead;

        /// <summary>
        /// Gets the current position of the channel.
        /// </summary>
        public long CurrentPosition
        {
            get
            {
                if (!_isOpen) throw new ObjectDisposedException(nameof(StreamTestByteChannel));
                return _stream.Position;
            }
        }

        /// <summary>
        /// Gets the current size of the channel.
        /// </summary>
        public long CurrentSize
        {
            get
            {
                if (!_isOpen) throw new ObjectDisposedException(nameof(StreamTestByteChannel));
                return _stream.Length;
            }
        }
        
        public long Position()
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(StreamTestByteChannel));
            return _stream.Position;
        }
        
        public ISeekableByteChannel Position(long newPosition)
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(StreamTestByteChannel));
            _stream.Position = newPosition;
            return this;
        }
        
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_isOpen)
                throw new ObjectDisposedException(nameof(StreamTestByteChannel));
                
            return _stream.Read(buffer, offset, count);
        }
        
        public int Write(byte[] buffer, int offset, int count)
        {
            if (!_isOpen)
                throw new ObjectDisposedException(nameof(StreamTestByteChannel));
                
            _stream.Write(buffer, offset, count);
            return count;
        }
        
        public long Seek(long position)
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(StreamTestByteChannel));
            return _stream.Seek(position, SeekOrigin.Begin);
        }
        
        public long Size()
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(StreamTestByteChannel));
            return _stream.Length;
        }
        
        public void Close()
        {
            if (_isOpen)
            {
                _isOpen = false;
            }
        }
        
        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }
} 