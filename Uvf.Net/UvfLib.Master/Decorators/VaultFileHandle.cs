using StorageLib.Abstractions;
using UvfLib.Core.Api;
using System.Runtime.InteropServices;

namespace UvfLib.Master.Decorators
{
    /// <summary>
    /// Represents an open vault file handle that manages encryption/decryption operations.
    /// Based on the stream patterns used in the working Program.cs testrun --cryptomator.
    /// </summary>
    public class VaultFileHandle : IDisposable
    {
        private readonly string _virtualPath;
        private readonly string _storagePath;
        private readonly IntPtr _underlyingHandle;
        private readonly IFileContentCryptor _cryptor;
        private readonly IStorage _underlyingStorage;
        private readonly OpenFlags _flags;
        private readonly bool _isEncrypted;
        private GCHandle _gcHandle;
        private bool _disposed;

        // For encrypted files, we might need to buffer operations
        private Stream? _encryptingStream;
        private Stream? _decryptingStream;
        private MemoryStream? _bufferStream;

        public string VirtualPath => _virtualPath;
        public string StoragePath => _storagePath;
        public bool IsEncrypted => _isEncrypted;
        public bool IsReadOnly => !FuseFlags.IsWriteAccess(_flags);

        public VaultFileHandle(
            string virtualPath,
            string storagePath,
            IntPtr underlyingHandle,
            IFileContentCryptor cryptor,
            IStorage underlyingStorage,
            OpenFlags flags,
            bool isEncrypted)
        {
            _virtualPath = virtualPath;
            _storagePath = storagePath;
            _underlyingHandle = underlyingHandle;
            _cryptor = cryptor;
            _underlyingStorage = underlyingStorage;
            _flags = flags;
            _isEncrypted = isEncrypted;
        }

        public IntPtr CreateContext()
        {
            _gcHandle = GCHandle.Alloc(this);
            return GCHandle.ToIntPtr(_gcHandle);
        }

        public static VaultFileHandle? FromContext(IntPtr context)
        {
            if (context == IntPtr.Zero)
                return null;

            GCHandle handle = GCHandle.FromIntPtr(context);
            return handle.Target as VaultFileHandle;
        }

        public async Task ReadAsync(long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            if (_isEncrypted)
            {
                await ReadEncryptedAsync(offset, size, buffer, cancellationToken);
            }
            else
            {
                await ReadUnencryptedAsync(offset, size, buffer, cancellationToken);
            }
        }

        public async Task WriteAsync(long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            if (_isEncrypted)
            {
                await WriteEncryptedAsync(offset, size, buffer, cancellationToken);
            }
            else
            {
                await WriteUnencryptedAsync(offset, size, buffer, cancellationToken);
            }
        }

        private async Task ReadEncryptedAsync(long offset, long size, IntPtr buffer, CancellationToken cancellationToken)
        {
            // For encrypted files, we need to decrypt the content
            // This follows the pattern from Program.cs DecryptDirectory method

            if (_decryptingStream == null)
            {
                // Create decrypting stream if not already created
                // This mimics: vault.GetDecryptingStream(encryptedStream)
                // For now, delegate to underlying storage directly
                // TODO: Implement proper decrypting stream creation using cryptor
                throw new NotImplementedException("Decrypting stream creation needs proper cryptor integration");
            }

            // Read from decrypting stream
            _decryptingStream.Seek(offset, SeekOrigin.Begin);
            
            byte[] managedBuffer = new byte[size];
            int bytesRead = await _decryptingStream.ReadAsync(managedBuffer, 0, (int)size, cancellationToken);
            
            // Copy to unmanaged buffer
            Marshal.Copy(managedBuffer, 0, buffer, bytesRead);
        }

        private async Task WriteEncryptedAsync(long offset, long size, IntPtr buffer, CancellationToken cancellationToken)
        {
            // For encrypted files, we need to encrypt the content
            // This follows the pattern from Program.cs ProcessDirectory method

            if (_encryptingStream == null)
            {
                // Create encrypting stream if not already created
                // This mimics: vault.GetEncryptingStream(targetStream)
                // For now, delegate to underlying storage directly
                // TODO: Implement proper encrypting stream creation using cryptor
                throw new NotImplementedException("Encrypting stream creation needs proper cryptor integration");
            }

            // Write to encrypting stream
            _encryptingStream.Seek(offset, SeekOrigin.Begin);
            
            // Copy from unmanaged buffer
            byte[] managedBuffer = new byte[size];
            Marshal.Copy(buffer, managedBuffer, 0, (int)size);
            
            await _encryptingStream.WriteAsync(managedBuffer, 0, (int)size, cancellationToken);
        }

        private async Task ReadUnencryptedAsync(long offset, long size, IntPtr buffer, CancellationToken cancellationToken)
        {
            // For unencrypted files, we still need to decrypt the content but the filename is not encrypted
            // The file still has encrypted content (ReadMe.txt.uvf contains encrypted content)
            await ReadEncryptedAsync(offset, size, buffer, cancellationToken);
        }

        private async Task WriteUnencryptedAsync(long offset, long size, IntPtr buffer, CancellationToken cancellationToken)
        {
            // For unencrypted files, we still need to encrypt the content but the filename is not encrypted
            // The file still has encrypted content (ReadMe.txt.uvf contains encrypted content)
            await WriteEncryptedAsync(offset, size, buffer, cancellationToken);
        }

        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            // Flush and close encryption streams
            if (_encryptingStream != null)
            {
                await _encryptingStream.FlushAsync(cancellationToken);
                _encryptingStream.Dispose();
                _encryptingStream = null;
            }

            if (_decryptingStream != null)
            {
                _decryptingStream.Dispose();
                _decryptingStream = null;
            }

            if (_bufferStream != null)
            {
                _bufferStream.Dispose();
                _bufferStream = null;
            }

            // Close underlying handle
            await _underlyingStorage.CloseAsync(_storagePath, _underlyingHandle);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Close streams
                    _encryptingStream?.Dispose();
                    _decryptingStream?.Dispose();
                    _bufferStream?.Dispose();

                    // Try to close underlying handle
                    try
                    {
                        _underlyingStorage.CloseAsync(_storagePath, _underlyingHandle, CancellationToken.None).Wait();
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }

                // Free unmanaged resources
                if (_gcHandle.IsAllocated)
                    _gcHandle.Free();

                _disposed = true;
            }
        }

        ~VaultFileHandle()
        {
            Dispose(false);
        }
    }
} 