using System;
using System.IO;

namespace ExampleVaultApp.Wrapper
{
    /// <summary>
    /// Unified stream implementation for TitanVault files supporting both read and write operations.
    /// Supports random access with 64-bit offsets for large files.
    /// </summary>
    public class TitanVaultStream : Stream
    {
        private readonly IntPtr _streamHandle;
        private readonly TitanVault _vault;
        private readonly bool _canWrite;
        private long _position;
        private long? _length;
        private bool _disposed;

        internal TitanVaultStream(IntPtr streamHandle, TitanVault vault, bool canWrite)
        {
            _streamHandle = streamHandle;
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _canWrite = canWrite;
            _position = 0;
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => !_disposed && _canWrite;
        
        public override long Length 
        { 
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(TitanVaultStream));
                
                if (!_length.HasValue)
                {
                    var length = TitanVaultNativeMethods.StreamGetLength(_streamHandle);
                    if (length < 0)
                    {
                        throw new IOException($"Failed to get stream length: {TitanVaultUtils.GetLastErrorString()}");
                    }
                    _length = length;
                }
                return _length.Value;
            }
        }
        
        public override long Position 
        { 
            get 
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(TitanVaultStream));
                
                var position = TitanVaultNativeMethods.StreamGetPosition(_streamHandle);
                if (position < 0)
                {
                    throw new IOException($"Failed to get stream position: {TitanVaultUtils.GetLastErrorString()}");
                }
                _position = position;
                return _position;
            }
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVaultStream));
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();

            if (count == 0)
                return 0;

            unsafe
            {
                // Create a temporary buffer for the native call
                var tempBuffer = new byte[count];
                fixed (byte* tempPtr = tempBuffer)
                {
                    var bytesRead = TitanVaultNativeMethods.StreamRead(_streamHandle, tempPtr, count);
                    
                    if (bytesRead < 0)
                    {
                        throw new IOException($"Failed to read from stream: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    if (bytesRead > 0)
                    {
                        Array.Copy(tempBuffer, 0, buffer, offset, bytesRead);
                        _position += bytesRead;
                    }

                    return bytesRead;
                }
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVaultStream));
            if (!_canWrite)
                throw new NotSupportedException("Stream was opened in read-only mode");
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException();

            if (count == 0)
                return;

            unsafe
            {
                // Create a temporary buffer for the native call
                var tempBuffer = new byte[count];
                Array.Copy(buffer, offset, tempBuffer, 0, count);
                
                fixed (byte* tempPtr = tempBuffer)
                {
                    var bytesWritten = TitanVaultNativeMethods.StreamWrite(_streamHandle, tempPtr, count);
                    
                    if (bytesWritten < 0)
                    {
                        throw new IOException($"Failed to write to stream: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    if (bytesWritten != count)
                    {
                        throw new IOException($"Partial write: expected {count} bytes, wrote {bytesWritten} bytes");
                    }

                    _position += bytesWritten;
                    
                    // Invalidate cached length since we may have extended the stream
                    _length = null;
                }
            }
        }

        public override void Flush()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVaultStream));

            var result = TitanVaultNativeMethods.StreamFlush(_streamHandle);
            if (result < 0)
            {
                throw new IOException($"Failed to flush stream: {TitanVaultUtils.GetLastErrorString()}");
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVaultStream));

            int nativeOrigin = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => 1,
                SeekOrigin.End => 2,
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };

            var newPosition = TitanVaultNativeMethods.StreamSeek(_streamHandle, offset, nativeOrigin);
            if (newPosition < 0)
            {
                throw new IOException($"Failed to seek stream: {TitanVaultUtils.GetLastErrorString()}");
            }

            _position = newPosition;
            return newPosition;
        }

        public override void SetLength(long value)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVaultStream));
            if (!_canWrite)
                throw new NotSupportedException("Cannot set length on a read-only stream");
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Length cannot be negative");

            var result = TitanVaultNativeMethods.StreamSetLength(_streamHandle, value);
            if (result < 0)
            {
                throw new IOException($"Failed to set stream length: {TitanVaultUtils.GetLastErrorString()}");
            }

            _length = value;
            
            // If current position is beyond the new length, adjust it
            if (_position > value)
            {
                Seek(value, SeekOrigin.Begin);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Ensure all data is flushed before closing (if writable)
                    if (_canWrite)
                    {
                        Flush();
                    }
                }
                catch
                {
                    // Ignore flush errors during disposal
                }

                if (_streamHandle != IntPtr.Zero)
                {
                    TitanVaultNativeMethods.CloseStream(_streamHandle);
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }
    }
} 