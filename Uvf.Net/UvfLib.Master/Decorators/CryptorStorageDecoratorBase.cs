using StorageLib.Abstractions;
using StorageLib.Streaming;
using UvfLib.Master.Abstractions;
using UvfLib.Vault;
using System.Runtime.InteropServices;
using System.Text;

namespace UvfLib.Master.Decorators
{
    /// <summary>
    /// Base class for storage decorators that use VaultHandler for encryption/decryption.
    /// Provides shared functionality for both handle-based and stream-based file operations while allowing
    /// format-specific implementations of path translation and directory operations.
    /// Implements both IStorage (IntPtr-based) and IStreamStorage (Stream-based) for maximum flexibility.
    /// </summary>
    public abstract class CryptorStorageDecoratorBase : IStorage, IStreamStorage
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
            throw new NotImplementedException("Path-based ReadAsync is not supported - use handle-based operations");
        }

        public Task WriteAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Path-based WriteAsync is not supported - use handle-based operations");
        }

        #endregion

        #region System Operations - Delegated

        public Task<bool> TestReadAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("TestReadAsync is not implemented for vault storage");
        }

        public Task<bool> TestWriteAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("TestWriteAsync is not implemented for vault storage");
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

        // File operations - format specific
        public abstract Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
        public abstract Task DeleteAsync(string filePath, CancellationToken cancellationToken = default);
        public abstract Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);
        public abstract Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default);

        // Enumeration operations - format specific
        public abstract Task<IEnumerable<FileObject>> ReadDirAsync(string directoryPath, bool readOnly = true, CancellationToken cancellationToken = default);

        #endregion

        #region IStreamStorage Implementation - Optimized Stream Access

        /// <summary>
        /// Gets the underlying IStorage instance that this stream storage wraps
        /// </summary>
        public IStorage UnderlyingStorage => _underlyingStorage;

        /// <summary>
        /// Opens a file for reading as a Stream - optimized to use internal crypto streams directly
        /// </summary>
        public virtual async Task<Stream> OpenReadAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptorStorageDecoratorBase));
            
            try
            {
                // 1. Translate virtual path to physical path
                var pathResult = await _pathTranslator.TranslateToStoragePathAsync(virtualPath);
                string physicalPath = pathResult.StoragePath;
                
                // 2. Open underlying file handle
                var underlyingHandle = await _underlyingStorage.OpenAsync(physicalPath, OpenFlags.ReadOnly, cancellationToken);
                
                // 3. Get underlying stream and wrap with decryption - NO IntPtr conversion!
                var underlyingStream = GetStreamFromUnderlyingHandle(underlyingHandle);
                return _vault.GetDecryptingStream(underlyingStream, leaveOpen: false);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to open file '{virtualPath}' for reading: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a file for writing as a Stream - optimized to use internal crypto streams directly
        /// </summary>
        public virtual async Task<Stream> OpenWriteAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            // Default to regular sequential encryption (better performance)
            return await OpenWriteAsync(virtualPath, requestRandomWrite: false, cancellationToken);
        }

        /// <summary>
        /// Opens a file for writing as a Stream with choice of encryption mode
        /// </summary>
        /// <param name="virtualPath">Virtual path to the file</param>
        /// <param name="requestRandomWrite">If true, uses chunk-aware encryption for random write support (higher overhead). If false, uses regular sequential encryption (better performance)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public virtual async Task<Stream> OpenWriteAsync(string virtualPath, bool requestRandomWrite = false, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptorStorageDecoratorBase));
            
            try
            {
                // 1. Ensure parent directory exists for the virtual path
                string? parentDir = Path.GetDirectoryName(virtualPath);
                // Normalize empty parent directory to root "/"
                if (string.IsNullOrEmpty(parentDir))
                {
                    parentDir = "/";
                }
                if (parentDir != "/" && !await DirectoryExistsAsync(parentDir, cancellationToken))
                {
                    await CreateDirectoryAsync(parentDir, cancellationToken);
                }
                
                // 2. Translate virtual path to physical path
                var pathResult = await _pathTranslator.TranslateToStoragePathAsync(virtualPath);
                string physicalPath = pathResult.StoragePath;
                
                // 3. Ensure physical parent directory exists
                string? physicalParentDir = Path.GetDirectoryName(physicalPath);
                if (!string.IsNullOrEmpty(physicalParentDir) && !await _underlyingStorage.DirectoryExistsAsync(physicalParentDir, cancellationToken))
                {
                    await _underlyingStorage.CreateDirectoryAsync(physicalParentDir, cancellationToken);
                }
                
                // 4. Open underlying file handle - use ReadWrite if file exists for random access support
                bool physicalFileExists = await _underlyingStorage.FileExistsAsync(physicalPath, cancellationToken);
                OpenFlags underlyingFlags = physicalFileExists 
                    ? OpenFlags.ReadWrite  // Need read access for existing chunks in random writes
                    : OpenFlags.Create | OpenFlags.WriteOnly;  // Pure write for new files
                var underlyingHandle = await _underlyingStorage.OpenAsync(physicalPath, underlyingFlags, cancellationToken);
                
                // 5. Get underlying stream and wrap with encryption
                var underlyingStream = GetStreamFromUnderlyingHandle(underlyingHandle);
                
                // Choose encryption mode based on requestRandomWrite parameter
                if (requestRandomWrite)
                {
                    // Use chunk-aware stream for random write support (higher overhead)
                    return physicalFileExists 
                        ? _vault.GetRandomWriteEncryptingStream(underlyingStream, existingHeader: null, leaveOpen: false)
                        : _vault.GetRandomWriteEncryptingStream(underlyingStream, existingHeader: null, leaveOpen: false);
                }
                else
                {
                    // Use regular sequential encryption stream (better performance)
                    return physicalFileExists 
                        ? _vault.GetEncryptingStreamWithExistingHeader(underlyingStream, leaveOpen: false)
                        : _vault.GetEncryptingStream(underlyingStream, leaveOpen: false);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to open file '{virtualPath}' for writing: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a file for both reading and writing as a Stream
        /// </summary>
        public virtual async Task<Stream> OpenReadWriteAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptorStorageDecoratorBase));
            
            try
            {
                // 1. Translate virtual path to physical path
                var pathResult = await _pathTranslator.TranslateToStoragePathAsync(virtualPath);
                string physicalPath = pathResult.StoragePath;
                
                // 2. Open underlying file handle
                var underlyingHandle = await _underlyingStorage.OpenAsync(physicalPath, OpenFlags.ReadWrite, cancellationToken);
                
                // 3. Get underlying stream - for read/write, we need to handle both encryption and decryption
                var underlyingStream = GetStreamFromUnderlyingHandle(underlyingHandle);
                
                // Note: For read/write streams, we might need a more sophisticated wrapper
                // For now, return the decrypting stream (most common use case)
                return _vault.GetDecryptingStream(underlyingStream, leaveOpen: false);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to open file '{virtualPath}' for read/write: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a file with specific flags as a Stream
        /// </summary>
        async Task<Stream> IStreamStorage.OpenAsync(string path, OpenFlags flags, CancellationToken cancellationToken)
        {
            // Extract access mode (first 2 bits)
            var accessMode = flags & OpenFlags.AccessMode;
            
            // Check if file should be created
            bool shouldCreate = (flags & OpenFlags.Create) != 0;
            bool shouldTruncate = (flags & OpenFlags.Truncate) != 0;
            bool isExclusive = (flags & OpenFlags.Exclusive) != 0;
            bool isAppend = (flags & OpenFlags.Append) != 0;

            // Validate flag combinations
            if (isExclusive && !shouldCreate)
            {
                throw new ArgumentException("Exclusive flag can only be used with Create flag", nameof(flags));
            }

            if (isAppend && shouldTruncate)
            {
                throw new ArgumentException("Append and Truncate flags cannot be used together", nameof(flags));
            }

            // Check file existence
            bool fileExists = await FileExistsAsync(path, cancellationToken);

            // Handle Create + Exclusive combination
            if (shouldCreate && isExclusive && fileExists)
            {
                throw new IOException($"File already exists and Exclusive flag was specified: {path}");
            }

            // Handle Create flag - create file if it doesn't exist
            if (shouldCreate && !fileExists)
            {
                // Create an empty file first
                using var createStream = await OpenWriteAsync(path, cancellationToken);
                // File is created, now close it and proceed with the requested access
            }

            // Handle case where file doesn't exist and Create flag is not set
            if (!fileExists && !shouldCreate)
            {
                throw new FileNotFoundException($"File not found and Create flag was not specified: {path}");
            }

            // Now open with the appropriate access mode
            Stream stream = accessMode switch
            {
                OpenFlags.ReadOnly => await OpenReadAsync(path, cancellationToken),
                OpenFlags.WriteOnly => await OpenWriteAsync(path, cancellationToken),
                OpenFlags.ReadWrite => await OpenReadWriteAsync(path, cancellationToken),
                _ => throw new ArgumentException($"Invalid access mode: {accessMode}", nameof(flags))
            };

            // Handle Truncate flag - clear the file content
            if (shouldTruncate && stream.CanWrite)
            {
                stream.SetLength(0);
                stream.Position = 0;
            }

            // Handle Append flag - position at end of file
            if (isAppend && stream.CanSeek && stream.CanWrite)
            {
                stream.Position = stream.Length;
            }

            return stream;
        }

        /// <summary>
        /// Reads all bytes from a file - convenience method
        /// </summary>
        public virtual async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            using var stream = await OpenReadAsync(path, cancellationToken);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }

        /// <summary>
        /// Writes all bytes to a file - convenience method
        /// </summary>
        public virtual async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            using var stream = await OpenWriteAsync(path, cancellationToken);
            await stream.WriteAsync(data, 0, data.Length, cancellationToken);
        }

        /// <summary>
        /// Reads all text from a file with optional encoding - convenience method
        /// </summary>
        public virtual async Task<string> ReadAllTextAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            using var stream = await OpenReadAsync(path, cancellationToken);
            using var reader = new StreamReader(stream, encoding);
            return await reader.ReadToEndAsync();
        }

        /// <summary>
        /// Writes all text to a file with optional encoding - convenience method
        /// </summary>
        public virtual async Task WriteAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            using var stream = await OpenWriteAsync(path, cancellationToken);
            using var writer = new StreamWriter(stream, encoding);
            await writer.WriteAsync(content);
        }

        /// <summary>
        /// Reads all lines from a text file - convenience method
        /// </summary>
        public virtual async Task<string[]> ReadAllLinesAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            using var stream = await OpenReadAsync(path, cancellationToken);
            using var reader = new StreamReader(stream, encoding);
            var lines = new List<string>();
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lines.Add(line);
            }
            return lines.ToArray();
        }

        /// <summary>
        /// Writes all lines to a text file - convenience method
        /// </summary>
        public virtual async Task WriteAllLinesAsync(string path, IEnumerable<string> lines, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            using var stream = await OpenWriteAsync(path, cancellationToken);
            using var writer = new StreamWriter(stream, encoding);
            foreach (var line in lines)
            {
                await writer.WriteLineAsync(line);
            }
        }

        /// <summary>
        /// Appends text to a file - convenience method
        /// </summary>
        public virtual async Task AppendAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            // For encrypted files, we need to read existing content, append, and rewrite
            // This is not as efficient as regular file append, but necessary for encryption
            string existingContent = "";
            if (await FileExistsAsync(path))
            {
                existingContent = await ReadAllTextAsync(path, encoding, cancellationToken);
            }
            await WriteAllTextAsync(path, existingContent + content, encoding, cancellationToken);
        }

        /// <summary>
        /// Copies data from one stream to another through the storage system - convenience method
        /// </summary>
        public virtual async Task CopyFromStreamAsync(string destinationPath, Stream sourceStream, CancellationToken cancellationToken = default)
        {
            using var destinationStream = await OpenWriteAsync(destinationPath, cancellationToken);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        /// <summary>
        /// Copies a file to a stream - convenience method
        /// </summary>
        public virtual async Task CopyToStreamAsync(string sourcePath, Stream destinationStream, CancellationToken cancellationToken = default)
        {
            using var sourceStream = await OpenReadAsync(sourcePath, cancellationToken);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        // Delegate methods - pass through to underlying IStorage (no translation needed)
        // These methods already exist as abstract methods, so we implement them as explicit interface members
        // to avoid conflicts while still delegating to the abstract implementations

        Task<bool> IStreamStorage.FileExistsAsync(string filePath, CancellationToken cancellationToken) 
            => FileExistsAsync(filePath, cancellationToken);

        Task<bool> IStreamStorage.DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken) 
            => DirectoryExistsAsync(directoryPath, cancellationToken);

        Task IStreamStorage.CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken) 
            => CreateDirectoryAsync(directoryPath, cancellationToken);

        Task IStreamStorage.DeleteAsync(string filePath, CancellationToken cancellationToken) 
            => DeleteAsync(filePath, cancellationToken);

        Task IStreamStorage.DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken) 
            => DeleteDirectoryAsync(directoryPath, cancellationToken);

        Task IStreamStorage.MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken) 
            => MoveAsync(sourceFilePath, destinationFilePath, overwrite, cancellationToken);

        Task<IEnumerable<FileObject>> IStreamStorage.ReadDirAsync(string directoryPath, bool readOnly, CancellationToken cancellationToken) 
            => ReadDirAsync(directoryPath, readOnly, cancellationToken);

        Task<FileObject> IStreamStorage.GetFileInfoAsync(string filePath, CancellationToken cancellationToken) 
            => GetFileInfoAsync(filePath, cancellationToken);

        Task IStreamStorage.ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken) 
            => ChownAsync(path, uid, gid, cancellationToken);

        Task IStreamStorage.SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken) 
            => SetTimesAsync(path, accessTime, modifiedTime, cancellationToken);

        Task<bool> IStreamStorage.TestReadAsync(CancellationToken cancellationToken) 
            => TestReadAsync(cancellationToken);

        Task<bool> IStreamStorage.TestWriteAsync(CancellationToken cancellationToken) 
            => TestWriteAsync(cancellationToken);

        Task<long> IStreamStorage.GetTotalCapacityAsync(CancellationToken cancellationToken) 
            => GetTotalCapacityAsync(cancellationToken);

        Task<long> IStreamStorage.GetAvailableFreeSpaceAsync(CancellationToken cancellationToken) 
            => GetAvailableFreeSpaceAsync(cancellationToken);

        #endregion

        #region Helper Methods

        internal async Task<Stream> CreateCryptoStreamAsync(IntPtr underlyingHandle, FileAccess fileAccess, string virtualPath, string physicalPath, CancellationToken cancellationToken)
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


    }
} 