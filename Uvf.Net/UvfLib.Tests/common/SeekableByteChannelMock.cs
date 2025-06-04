using System;
using System.IO;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// Mock implementation of a seekable byte channel for testing
    /// </summary>
    public class SeekableByteChannelMock : ISeekableByteChannel, IDisposable
    {
        private readonly MemoryStream _memoryStream;
        private bool _isOpen = true;
        
        public SeekableByteChannelMock(byte[] data)
        {
            _memoryStream = new MemoryStream(data);
        }
        
        public MemoryStream GetStream()
        {
            return _memoryStream;
        }

        public bool IsOpen => _isOpen && _memoryStream.CanRead;

        public long CurrentPosition
        {
            get
            {
                if (!_isOpen) throw new ObjectDisposedException(nameof(SeekableByteChannelMock));
                return _memoryStream.Position;
            }
        }

        public long CurrentSize
        {
            get
            {
                if (!_isOpen) throw new ObjectDisposedException(nameof(SeekableByteChannelMock));
                return _memoryStream.Length;
            }
        }
        
        public long Position()
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(SeekableByteChannelMock));
            return _memoryStream.Position;
        }
        
        public ISeekableByteChannel Position(long newPosition)
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(SeekableByteChannelMock));
            _memoryStream.Position = newPosition;
            return this;
        }
        
        public int Read(byte[] buffer, int offset, int count)
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(SeekableByteChannelMock));
            return _memoryStream.Read(buffer, offset, count);
        }
        
        public int Write(byte[] buffer, int offset, int count)
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(SeekableByteChannelMock));
            _memoryStream.Write(buffer, offset, count);
            return count;
        }
        
        public long Seek(long position)
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(SeekableByteChannelMock));
            return _memoryStream.Seek(position, SeekOrigin.Begin);
        }
        
        public long Size()
        {
            if (!_isOpen) throw new ObjectDisposedException(nameof(SeekableByteChannelMock));
            return _memoryStream.Length;
        }
        
        public void Close()
        {
            if (_isOpen)
            {
                _memoryStream.Close();
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