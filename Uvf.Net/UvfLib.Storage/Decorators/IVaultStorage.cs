using StorageLib.Abstractions;
using UvfLib.Core.Api;
using System.Security.Cryptography;

namespace UvfLib.Storage.Decorators
{
    /// <summary>
    /// Base class for vault storage decorators that implement IStorage with encryption/decryption.
    /// </summary>
    public abstract class VaultStorageDecorator : IStorage
    {
        protected readonly IStorage _underlyingStorage;
        protected readonly string _password;
        protected bool _disposed = false;

        protected VaultStorageDecorator(IStorage underlyingStorage, string password)
        {
            _underlyingStorage = underlyingStorage ?? throw new ArgumentNullException(nameof(underlyingStorage));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        // Abstract methods for vault-specific implementation
        protected abstract Task<Stream> CreateDecryptingStreamAsync(Stream encryptedStream, string virtualPath);
        protected abstract Task<Stream> CreateEncryptingStreamAsync(Stream outputStream, string virtualPath);
        protected abstract string MapVirtualToPhysicalPath(string virtualPath);
        protected abstract string MapPhysicalToVirtualPath(string physicalPath);

        // IStorage implementation with encryption/decryption
        public virtual async Task<IntPtr> OpenAsync(string realPath, int flags, CancellationToken cancellationToken = default)
        {
            // Map virtual path to encrypted physical path
            string physicalPath = MapVirtualToPhysicalPath(realPath);
            
            // Open the underlying encrypted file
            return await _underlyingStorage.OpenAsync(physicalPath, flags, cancellationToken);
        }

        public virtual async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            string physicalPath = MapVirtualToPhysicalPath(filePath);
            return await _underlyingStorage.FileExistsAsync(physicalPath, cancellationToken);
        }

        public virtual async Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string physicalPath = MapVirtualToPhysicalPath(directoryPath);
            return await _underlyingStorage.DirectoryExistsAsync(physicalPath, cancellationToken);
        }

        // Delegate other IStorage methods to underlying storage with path mapping
        public virtual Task ReadAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
            => _underlyingStorage.ReadAsync(fileHandle, offset, size, buffer, cancellationToken);

        public virtual Task WriteAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
            => _underlyingStorage.WriteAsync(fileHandle, offset, size, buffer, cancellationToken);

        public virtual Task CloseAsync(IntPtr fileHandle, CancellationToken cancellationToken = default)
            => _underlyingStorage.CloseAsync(fileHandle, cancellationToken);

        public virtual Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string physicalPath = MapVirtualToPhysicalPath(directoryPath);
            return _underlyingStorage.CreateDirectoryAsync(physicalPath, cancellationToken);
        }

        public virtual Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            string physicalPath = MapVirtualToPhysicalPath(filePath);
            return _underlyingStorage.DeleteFileAsync(physicalPath, cancellationToken);
        }

        public virtual Task DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string physicalPath = MapVirtualToPhysicalPath(directoryPath);
            return _underlyingStorage.DeleteDirectoryAsync(physicalPath, cancellationToken);
        }

        public virtual async Task<IEnumerable<FileObject>> GetFilesAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string physicalPath = MapVirtualToPhysicalPath(directoryPath);
            var physicalFiles = await _underlyingStorage.GetFilesAsync(physicalPath, cancellationToken);
            
            // Map physical paths back to virtual paths and decrypt filenames
            return physicalFiles.Select(pf => new FileObject
            {
                Name = MapPhysicalToVirtualPath(pf.FullPath), // Decrypt filename
                FullPath = MapPhysicalToVirtualPath(pf.FullPath),
                Size = pf.Size, // Will need adjustment for encrypted size vs decrypted size
                LastModified = pf.LastModified
            });
        }

        public virtual async Task<IEnumerable<FileObject>> GetDirectoriesAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            string physicalPath = MapVirtualToPhysicalPath(directoryPath);
            var physicalDirs = await _underlyingStorage.GetDirectoriesAsync(physicalPath, cancellationToken);
            
            // Map physical paths back to virtual paths and decrypt directory names
            return physicalDirs.Select(pd => new FileObject
            {
                Name = MapPhysicalToVirtualPath(pd.FullPath), // Decrypt directory name
                FullPath = MapPhysicalToVirtualPath(pd.FullPath),
                Size = pd.Size,
                LastModified = pd.LastModified
            });
        }

        public virtual Task InitializeAsync(string connectionStringOrPath, string basePath, CancellationToken cancellationToken = default)
            => _underlyingStorage.InitializeAsync(connectionStringOrPath, basePath, cancellationToken);

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _underlyingStorage?.Dispose();
                _disposed = true;
            }
        }
    }
} 