using StorageLib.Abstractions;
using UvfLib.Storage.Abstractions;
using UvfLib.Vault;
using System.Runtime.InteropServices;

namespace UvfLib.Storage.Decorators
{
    /// <summary>
    /// Base class for storage decorators that use VaultHandler for encryption/decryption.
    /// Provides shared functionality for stream-based file operations while allowing
    /// format-specific implementations of path translation and directory operations.
    /// </summary>
    public abstract class CryptorStorageDecoratorBase : IStorage
    {
        protected readonly IStorage _underlyingStorage;
        protected readonly VaultHandler _vault;
        protected readonly IVaultPathTranslator _pathTranslator;
        protected readonly string _vaultBasePath;
        protected bool _disposed;

        // Track open file handles for proper cleanup
        private readonly Dictionary<IntPtr, CryptoHandle> _openHandles = new();
        private readonly object _handlesLock = new object();

        protected CryptorStorageDecoratorBase(
            IStorage underlyingStorage,
            VaultHandler vault,
            IVaultPathTranslator pathTranslator,
            string vaultBasePath)
        {
            _underlyingStorage = underlyingStorage ?? throw new ArgumentNullException(nameof(underlyingStorage));
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _pathTranslator = pathTranslator ?? throw new ArgumentNullException(nameof(pathTranslator));
            _vaultBasePath = vaultBasePath ?? throw new ArgumentNullException(nameof(vaultBasePath));
        }

        #region Property Delegation

        public EnumStorageType Type { get => _underlyingStorage.Type; set => _underlyingStorage.Type = value; }
        public string BaseFolderOrContainer { get => _underlyingStorage.BaseFolderOrContainer; set => _underlyingStorage.BaseFolderOrContainer = value; }
        public string ConnectionString { get => _underlyingStorage.ConnectionString; set => _underlyingStorage.ConnectionString = value; }
        public long MaxReadBytesPerSecond { get => _underlyingStorage.MaxReadBytesPerSecond; set => _underlyingStorage.MaxReadBytesPerSecond = value; }
        public long MaxWriteBytesPerSecond { get => _underlyingStorage.MaxWriteBytesPerSecond; set => _underlyingStorage.MaxWriteBytesPerSecond = value; }

        #endregion

        #region Core Operations - Shared Implementation

        public Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            return _underlyingStorage.InitializeAsync(connectionString, baseFolderOrContainer, cancellationToken);
        }

        /// <summary>
        /// Opens a file with encryption/decryption using VaultHandler streams.
        /// This is the core shared logic that both UVF and Cryptomator formats use.
        /// Streams are created lazily during read/write operations.
        /// </summary>
        public virtual async Task<IntPtr> OpenAsync(string virtualPath, OpenFlags flags, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptorStorageDecoratorBase));
            
            try
            {
                // 1. Translate virtual path to physical path (via vault path translator)
                var pathResult = await _pathTranslator.TranslateToStoragePathAsync(virtualPath);
                string physicalPath = pathResult.StoragePath;
                
                // 2. Open underlying file handle
                var underlyingHandle = await _underlyingStorage.OpenAsync(physicalPath, flags, cancellationToken);
                
                // 3. Create our handle wrapper (no crypto streams yet - created lazily)
                var cryptoHandle = new CryptoHandle(virtualPath, physicalPath, underlyingHandle, _vault, flags, this);
                IntPtr handlePtr = cryptoHandle.CreateContext();
                
                // 4. Track the handle for cleanup
                lock (_handlesLock)
                {
                    _openHandles[handlePtr] = cryptoHandle;
                }
                
                return handlePtr;
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to open file '{virtualPath}': {ex.Message}", ex);
            }
        }

        public virtual async Task CloseAsync(IntPtr fileHandle, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptorStorageDecoratorBase));
            if (fileHandle == IntPtr.Zero) return;

            CryptoHandle? cryptoHandle = null;
            
            lock (_handlesLock)
            {
                if (_openHandles.TryGetValue(fileHandle, out cryptoHandle))
                {
                    _openHandles.Remove(fileHandle);
                }
            }

            if (cryptoHandle != null)
            {
                try
                {
                    // Close crypto streams (both if they exist)
                    cryptoHandle.Dispose();
                    
                    // Close underlying handle
                    if (cryptoHandle.UnderlyingHandle != IntPtr.Zero)
                    {
                        await _underlyingStorage.CloseAsync(cryptoHandle.UnderlyingHandle, cancellationToken);
                    }
                }
                finally
                {
                    // Handle disposal is already done in cryptoHandle.Dispose()
                }
            }
        }

        public virtual async Task ReadAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptorStorageDecoratorBase));
            
            var cryptoHandle = GetCryptoHandle(fileHandle);
            
            // Lazy creation of decrypting stream
            var decryptingStream = await cryptoHandle.GetDecryptingStreamAsync(cancellationToken);
            
            // Use the decrypting stream
            if (decryptingStream.CanSeek)
            {
                decryptingStream.Seek(offset, SeekOrigin.Begin);
            }
            
            // Read decrypted data
            byte[] managedBuffer = new byte[size];
            int bytesRead = await decryptingStream.ReadAsync(managedBuffer, 0, (int)size, cancellationToken);
            
            // Copy to unmanaged buffer
            Marshal.Copy(managedBuffer, 0, buffer, bytesRead);
        }

        public virtual async Task WriteAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptorStorageDecoratorBase));
            
            var cryptoHandle = GetCryptoHandle(fileHandle);
            
            // Lazy creation of encrypting stream
            var encryptingStream = await cryptoHandle.GetEncryptingStreamAsync(cancellationToken);
            
            // Use the encrypting stream
            if (encryptingStream.CanSeek)
            {
                encryptingStream.Seek(offset, SeekOrigin.Begin);
            }
            
            // Copy from unmanaged buffer
            byte[] managedBuffer = new byte[size];
            Marshal.Copy(buffer, managedBuffer, 0, (int)size);
            
            // Write encrypted data
            await encryptingStream.WriteAsync(managedBuffer, 0, (int)size, cancellationToken);
        }

        // Path-based read/write - not implemented as we use handle-based operations
        public Task ReadAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Use handle-based ReadAsync method instead");
        }

        public Task WriteAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Use handle-based WriteAsync method instead");
        }

        #endregion

        #region System Operations - Delegated

        public Task<bool> TestReadAsync(CancellationToken cancellationToken = default)
        {
            return _underlyingStorage.TestReadAsync(cancellationToken);
        }

        public Task<bool> TestWriteAsync(CancellationToken cancellationToken = default)
        {
            return _underlyingStorage.TestWriteAsync(cancellationToken);
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            // Phase 1: Clean up our crypto handles first
            await CloseAllCryptoHandlesInternalAsync();
            
            // Phase 2: Delegate shutdown to underlying storage
            await _underlyingStorage.ShutdownAsync(cancellationToken);
            
            // Phase 3: Mark as disposed
            _disposed = true;
        }

        public Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default)
        {
            return _underlyingStorage.GetTotalCapacityAsync(cancellationToken);
        }

        public Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default)
        {
            return _underlyingStorage.GetAvailableFreeSpaceAsync(cancellationToken);
        }

        public async Task CloseAllHandlesAsync(CancellationToken cancellationToken = default)
        {
            // Phase 1: Clean up our crypto handles first
            await CloseAllCryptoHandlesInternalAsync();
            
            // Phase 2: Delegate close all to underlying storage
            await _underlyingStorage.CloseAllHandlesAsync(cancellationToken);
            
            // Note: Don't mark as disposed - storage still usable for new opens
        }

        public Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default)
        {
            return _underlyingStorage.ChownAsync(path, uid, gid, cancellationToken);
        }

        public Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default)
        {
            return _underlyingStorage.SetTimesAsync(path, accessTime, modifiedTime, cancellationToken);
        }

        /// <summary>
        /// Internal method to clean up all crypto handles without closing underlying handles individually.
        /// Used by ShutdownAsync and CloseAllHandlesAsync to prevent resource leaks.
        /// </summary>
        private async Task CloseAllCryptoHandlesInternalAsync()
        {
            List<CryptoHandle> handlesToClose;
            
            // Get all handles and clear tracking immediately to maintain consistent state
            lock (_handlesLock)
            {
                handlesToClose = new List<CryptoHandle>(_openHandles.Values);
                _openHandles.Clear();
            }
            
            // Dispose crypto streams only (don't close underlying handles individually)
            // The underlying storage will handle closing its handles in bulk
            foreach (var handle in handlesToClose)
            {
                handle.Dispose(); // Disposes crypto streams and frees GC handles
            }
            
            // No await needed - Dispose() is synchronous for crypto streams
            await Task.CompletedTask;
        }

        #endregion

        #region Abstract Methods - Format Specific

        // Path translation is now handled by the IVaultPathTranslator

        // Directory operations - format specific
        public abstract Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
        public abstract Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);
        public abstract Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<string>> GetDirectoriesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<string>> GetDirectoriesAsync(string directoryPath, CancellationToken cancellationToken = default);

        // File operations - format specific
        public abstract Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
        public abstract Task DeleteAsync(string filePath, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<string>> GetFilesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<string>> GetFilesAsync(string directoryPath, CancellationToken cancellationToken = default);
        public abstract Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);
        public abstract Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default);

        // Enumeration operations - format specific
        public abstract Task<IEnumerable<FileObject>> EnumerateFilesAndDirectoriesAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default);
        public abstract Task<IEnumerable<string>> EnumerateFileSystemEntriesAsync(string path, CancellationToken cancellationToken = default);

        #endregion

        #region Helper Methods

        protected async Task<Stream> CreateCryptoStreamAsync(IntPtr underlyingHandle, FileAccess fileAccess, string virtualPath, string physicalPath, CancellationToken cancellationToken)
        {
            // Get the actual stream from the underlying storage handle
            Stream underlyingStream = GetStreamFromUnderlyingHandle(underlyingHandle);
            
            if (underlyingStream == null)
            {
                throw new InvalidOperationException($"Could not get stream from underlying storage handle");
            }
            
            // Wrap the underlying stream with encryption/decryption
            if (fileAccess == FileAccess.Read)
            {
                return _vault.GetDecryptingStream(underlyingStream, leaveOpen: false);
            }
            else
            {
                return _vault.GetEncryptingStream(underlyingStream, leaveOpen: false);
            }
        }

        private Stream GetStreamFromUnderlyingHandle(IntPtr underlyingHandle)
        {
            // Convert the underlying storage's handle back to a FileHandle to get the stream
            var fileHandle = FileHandle.FromContext(underlyingHandle);
            if (fileHandle?.Stream == null)
            {
                throw new InvalidOperationException("Could not retrieve stream from underlying storage handle");
            }
            
            return fileHandle.Stream;
        }

        private CryptoHandle GetCryptoHandle(IntPtr fileHandle)
        {
            if (fileHandle == IntPtr.Zero)
                throw new ArgumentException("Invalid file handle", nameof(fileHandle));

            lock (_handlesLock)
            {
                if (_openHandles.TryGetValue(fileHandle, out var cryptoHandle))
                {
                    return cryptoHandle;
                }
            }

            throw new ArgumentException("File handle not found", nameof(fileHandle));
        }

        #endregion

        #region Disposal

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                // Use the same cleanup logic as shutdown, but synchronously
                CloseAllCryptoHandlesSynchronously();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Synchronous version of crypto handle cleanup for Dispose() method.
        /// </summary>
        private void CloseAllCryptoHandlesSynchronously()
        {
            List<CryptoHandle> handlesToClose;
            
            // Get all handles and clear tracking immediately to maintain consistent state
            lock (_handlesLock)
            {
                handlesToClose = new List<CryptoHandle>(_openHandles.Values);
                _openHandles.Clear();
            }
            
            // Dispose crypto streams only (synchronously)
            foreach (var handle in handlesToClose)
            {
                handle.Dispose(); // Disposes crypto streams and frees GC handles
            }
        }

        #endregion

        /// <summary>
        /// Wrapper for encrypted file handles that combines virtual/physical paths with crypto streams.
        /// Supports lazy creation of both encrypting and decrypting streams for ReadWrite access.
        /// </summary>
        protected class CryptoHandle : IDisposable
        {
            public string VirtualPath { get; }
            public string PhysicalPath { get; }
            public IntPtr UnderlyingHandle { get; }
            public OpenFlags Flags { get; }
            
            private readonly VaultHandler _vault;
            private readonly CryptorStorageDecoratorBase _parent;
            private Stream? _encryptingStream;
            private Stream? _decryptingStream;
            
            private GCHandle _gcHandle;
            private bool _disposed = false;

            public CryptoHandle(string virtualPath, string physicalPath, IntPtr underlyingHandle, VaultHandler vault, OpenFlags flags, CryptorStorageDecoratorBase parent)
            {
                VirtualPath = virtualPath;
                PhysicalPath = physicalPath;
                UnderlyingHandle = underlyingHandle;
                _vault = vault;
                Flags = flags;
                _parent = parent;
            }

            public IntPtr CreateContext()
            {
                _gcHandle = GCHandle.Alloc(this);
                return GCHandle.ToIntPtr(_gcHandle);
            }

            public async Task<Stream> GetDecryptingStreamAsync(CancellationToken cancellationToken)
            {
                if (_decryptingStream == null)
                {
                    // Lazy creation of decrypting stream
                    _decryptingStream = await _parent.CreateCryptoStreamAsync(UnderlyingHandle, FileAccess.Read, VirtualPath, PhysicalPath, cancellationToken);
                }
                return _decryptingStream;
            }

            public async Task<Stream> GetEncryptingStreamAsync(CancellationToken cancellationToken)
            {
                if (_encryptingStream == null)
                {
                    // Lazy creation of encrypting stream
                    _encryptingStream = await _parent.CreateCryptoStreamAsync(UnderlyingHandle, FileAccess.Write, VirtualPath, PhysicalPath, cancellationToken);
                }
                return _encryptingStream;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    // Dispose both crypto streams if they exist
                    _encryptingStream?.Dispose();
                    _decryptingStream?.Dispose();
                    
                    if (_gcHandle.IsAllocated)
                        _gcHandle.Free();
                        
                    _disposed = true;
                }
            }
        }
    }
} 