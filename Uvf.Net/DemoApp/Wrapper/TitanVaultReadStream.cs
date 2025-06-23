using System;
using System.IO;

namespace DemoApp.Wrapper
{
    /// <summary>
    /// Stream implementation for reading from TitanVault files using native streaming exports.
    /// Supports random access and small buffer operations for large files.
    /// </summary>
    public class TitanVaultReadStream : Stream
    {
        private readonly IntPtr _streamHandle;
        private readonly TitanVault _vault;
        private long _position;
        private bool _disposed;

        internal TitanVaultReadStream(IntPtr streamHandle, TitanVault vault)
        {
            _streamHandle = streamHandle;
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _position = 0;
        }

        public override bool CanRead => !_disposed;
        public override bool CanSeek => false; // Native streaming doesn't support seeking
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException("Length is not supported for streaming operations");
        
        public override long Position 
        { 
            get => _position; 
            set => throw new NotSupportedException("Seeking is not supported for streaming operations");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVaultReadStream));
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
            throw new NotSupportedException("Write is not supported on read-only stream");
        }

        public override void Flush()
        {
            // No-op for read stream
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Seeking is not supported for streaming operations");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("SetLength is not supported on read-only stream");
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
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