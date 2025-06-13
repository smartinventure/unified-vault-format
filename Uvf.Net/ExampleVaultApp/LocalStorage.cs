using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using StorageLib.Abstractions;
using StorageLib.Throttling;
using StorageLib.Utilities;

namespace StorageLib.Connectors
{
    /*
    /// <summary>
    /// Local file system storage implementation
    /// </summary>
    public class LocalStorage : IStorage, IDisposable
    {
        private string _baseFolder = string.Empty;
        private readonly object _handleLock = new object();
        private readonly Dictionary<IntPtr, FileHandle> _handleMap = new Dictionary<IntPtr, FileHandle>();
        private readonly ILogger<LocalStorage>? _logger;
        private readonly IThrottlingService? _throttlingService;
        private bool _isDisposed;

        public EnumStorageType StorageType => EnumStorageType.Local;

        /// <summary>
        /// Maximum read speed in bytes per second. 0 means no throttling.
        /// </summary>
        public long MaxReadBytesPerSecond { get; set; } = 0;

        /// <summary>
        /// Maximum write speed in bytes per second. 0 means no throttling.
        /// </summary>
        public long MaxWriteBytesPerSecond { get; set; } = 0;

        public virtual EnumStorageType Type
        {
            get => EnumStorageType.Local;
            set { } // Empty setter - ignores the value
        }

        public string BaseFolderOrContainer
        {
            get
            {
                ThrowIfDisposed();
                return _baseFolder;
            }
            set
            {
                ThrowIfDisposed();
                _baseFolder = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        public virtual string ConnectionString { get => "file://"; set { } }

        public LocalStorage(ILogger<LocalStorage>? logger = null, IThrottlingService? throttlingService = null)
        {
            _logger = logger;
            _throttlingService = throttlingService;
        }

        /// <summary>
        /// Initialize or set up the storage connection asynchronously
        /// </summary>
        public virtual async Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(baseFolderOrContainer))
                    throw new ArgumentException("Base folder cannot be null or empty", nameof(baseFolderOrContainer));

                // For local storage, we need the actual Windows/Linux path, not the normalized storage path
                _baseFolder = baseFolderOrContainer;

                // Ensure directory exists
                if (!Directory.Exists(_baseFolder))
                {
                    throw new DirectoryNotFoundException($"Base directory does not exist: {_baseFolder}");
                }

                _logger?.LogDebug("LocalStorage initialized with base folder: {BaseFolder}", _baseFolder);
            }, cancellationToken);
        }

        /// <summary>
        /// Open a file asynchronously with the specified flags
        /// </summary>
        public virtual async Task<IntPtr> OpenAsync(string path, OpenFlags flags, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("Path cannot be null or empty", nameof(path));

                // Convert virtual path to real file system path
                string realPath = GetRealPath(path);

                FileAccess access = FileAccess.Read;
                FileMode mode = FileMode.Open;
                FileShare share = FileShare.Read;

                // Parse FUSE flags
                if (flags.HasFlag(OpenFlags.WriteOnly) || flags.HasFlag(OpenFlags.ReadWrite))
                {
                    access = flags.HasFlag(OpenFlags.ReadWrite) ? FileAccess.ReadWrite : FileAccess.Write;
                    share = FileShare.ReadWrite;
                }

                if (flags.HasFlag(OpenFlags.Create))
                {
                    mode = flags.HasFlag(OpenFlags.Exclusive) ? FileMode.CreateNew : FileMode.OpenOrCreate;
                }

                if (flags.HasFlag(OpenFlags.Truncate))
                {
                    mode = FileMode.Create;
                }

                try
                {
                    // Ensure parent directory exists if creating
                    if (flags.HasFlag(OpenFlags.Create))
                    {
                        string? parentDir = Path.GetDirectoryName(realPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            Directory.CreateDirectory(parentDir);
                        }
                    }

                    var stream = new FileStream(realPath, mode, access, share);
                    var handle = new FileHandle(realPath, stream, access == FileAccess.Read, this);

                    lock (_handleLock)
                    {
                        IntPtr handlePtr = handle.CreateContext();
                        _handleMap[handlePtr] = handle;
                        
                        _logger?.LogTrace("Opened file {Path} with handle {Handle}", realPath, handlePtr);
                        return handlePtr;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error opening file {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Close a file asynchronously
        /// </summary>
        public virtual async Task CloseAsync(IntPtr fileHandle, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                
                lock (_handleLock)
                {
                    if (_handleMap.TryGetValue(fileHandle, out var handle))
                    {
                        try
                        {
                            handle.Stream?.Dispose();
                            _handleMap.Remove(fileHandle);
                            
                            _logger?.LogTrace("Closed file handle {Handle} for {Path}", fileHandle, handle.Path);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error closing file handle {Handle}", fileHandle);
                            throw;
                        }
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Read data from a file asynchronously
        /// </summary>
        public virtual async Task ReadAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (buffer == IntPtr.Zero)
                    throw new ArgumentException("Buffer cannot be null", nameof(buffer));
                if (size < 0)
                    throw new ArgumentException("Size cannot be negative", nameof(size));

                lock (_handleLock)
                {
                    if (!_handleMap.TryGetValue(fileHandle, out var handle))
                        throw new InvalidOperationException($"Invalid file handle: {fileHandle}");

                    try
                    {
                        handle.Stream.Seek(offset, SeekOrigin.Begin);
                        byte[] bytes = new byte[size];
                        int bytesRead = handle.Stream.Read(bytes, 0, (int)size);
                        
                        if (bytesRead > 0)
                        {
                            Marshal.Copy(bytes, 0, buffer, bytesRead);
                        }
                        
                        _logger?.LogTrace("Read {BytesRead} bytes from {Path} at offset {Offset}", 
                            bytesRead, handle.Path, offset);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error reading from file {Path}", handle.Path);
                        throw;
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Write data to a file asynchronously
        /// </summary>
        public virtual async Task WriteAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (buffer == IntPtr.Zero)
                    throw new ArgumentException("Buffer cannot be null", nameof(buffer));
                if (size < 0)
                    throw new ArgumentException("Size cannot be negative", nameof(size));

                lock (_handleLock)
                {
                    if (!_handleMap.TryGetValue(fileHandle, out var handle))
                        throw new InvalidOperationException($"Invalid file handle: {fileHandle}");

                    if (handle.IsReadOnly)
                        throw new UnauthorizedAccessException("Cannot write to read-only file");

                    try
                    {
                        handle.Stream.Seek(offset, SeekOrigin.Begin);
                        byte[] bytes = new byte[size];
                        Marshal.Copy(buffer, bytes, 0, (int)size);
                        handle.Stream.Write(bytes, 0, (int)size);
                        handle.Stream.Flush();
                        
                        _logger?.LogTrace("Wrote {Size} bytes to {Path} at offset {Offset}", 
                            size, handle.Path, offset);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error writing to file {Path}", handle.Path);
                        throw;
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Read data from a file asynchronously using path
        /// </summary>
        public virtual async Task ReadAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            IntPtr handle = await OpenAsync(path, OpenFlags.ReadOnly, cancellationToken);
            try
            {
                await ReadAsync(handle, offset, size, buffer, cancellationToken);
            }
            finally
            {
                await CloseAsync(handle, cancellationToken);
            }
        }

        /// <summary>
        /// Write data to a file asynchronously using path
        /// </summary>
        public virtual async Task WriteAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            IntPtr handle = await OpenAsync(path, OpenFlags.WriteOnly, cancellationToken);
            try
            {
                await WriteAsync(handle, offset, size, buffer, cancellationToken);
            }
            finally
            {
                await CloseAsync(handle, cancellationToken);
            }
        }

        /// <summary>
        /// Read entire file asynchronously
        /// </summary>
        public virtual async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                string realPath = GetRealPath(path);
                
                try
                {
                    byte[] data = File.ReadAllBytes(realPath);
                    _logger?.LogTrace("Read entire file {Path}, {Size} bytes", realPath, data.Length);
                    return data;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error reading entire file {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }



        /// <summary>
        /// Read directory contents (files and subdirectories) asynchronously
        /// </summary>
        public virtual async Task<IEnumerable<FileObject>> ReadDirAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(realPath))
                    throw new ArgumentException("Path cannot be null or empty", nameof(realPath));

                string fsPath = GetRealPath(realPath);
                var fileObjects = new List<FileObject>();

                try
                {
                    // Enumerate files
                    foreach (var file in Directory.GetFiles(fsPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var fileObject = new FileObject(GetVirtualPath(file))
                            {
                                Filename = fileInfo.Name,
                                RealPath = GetVirtualPath(file),
                                VirtualPath = GetVirtualPath(file),
                                IsDirectory = false,
                                Size = fileInfo.Length,
                                CreationTime = fileInfo.CreationTime,
                                LastModified = fileInfo.LastWriteTime,
                                LastAccessTime = fileInfo.LastAccessTime,
                                SC = this
                            };
                            fileObjects.Add(fileObject);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Error processing file {File}", file);
                            // Continue with next file
                        }
                    }

                    // Enumerate directories
                    foreach (var directory in Directory.GetDirectories(fsPath))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(directory);
                            var dirObject = new FileObject(GetVirtualPath(directory))
                            {
                                Filename = dirInfo.Name,
                                RealPath = GetVirtualPath(directory),
                                VirtualPath = GetVirtualPath(directory),
                                IsDirectory = true,
                                Size = 0,
                                CreationTime = dirInfo.CreationTime,
                                LastModified = dirInfo.LastWriteTime,
                                LastAccessTime = dirInfo.LastAccessTime,
                                SC = this
                            };
                            fileObjects.Add(dirObject);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Error processing directory {Directory}", directory);
                            // Continue with next directory
                        }
                    }

                    _logger?.LogDebug("Enumerated {Count} entries in directory: {Path}", fileObjects.Count, fsPath);
                    return fileObjects.AsEnumerable();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error enumerating files and directories in {Path}", fsPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Test if storage is readable asynchronously
        /// </summary>
        public virtual async Task<bool> TestReadAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(_baseFolder))
                    return false;

                try
                {
                    // Test if we can read the directory
                    return Directory.Exists(_baseFolder);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error testing read access for {BaseFolder}", _baseFolder);
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Test if storage is writable asynchronously
        /// </summary>
        public virtual async Task<bool> TestWriteAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(_baseFolder))
                    return false;

                try
                {
                    var testFile = Path.Combine(_baseFolder, $"test_{Guid.NewGuid()}.tmp");
                    using (var stream = File.Create(testFile))
                    {
                        stream.WriteByte(0);
                    }

                    try
                    {
                        File.Delete(testFile);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to delete test file {TestFile}", testFile);
                        return true; // Still return true as write was successful
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error testing write access for {BaseFolder}", _baseFolder);
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Check if a file exists asynchronously
        /// </summary>
        public virtual async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

                string realPath = GetRealPath(filePath);
                
                try
                {
                    bool exists = File.Exists(realPath);
                    _logger?.LogTrace("File exists check for {Path}: {Exists}", realPath, exists);
                    return exists;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error checking file existence for {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Check if a directory exists asynchronously
        /// </summary>
        public virtual async Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                string realPath = GetRealPath(directoryPath);
                
                try
                {
                    bool exists = Directory.Exists(realPath);
                    _logger?.LogTrace("Directory exists check for {Path}: {Exists}", realPath, exists);
                    return exists;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error checking directory existence for {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Delete a directory asynchronously
        /// </summary>
        public virtual async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("Path cannot be null or empty", nameof(path));

                string realPath = GetRealPath(path);
                
                try
                {
                    if (Directory.Exists(realPath))
                    {
                        Directory.Delete(realPath, true);
                        _logger?.LogDebug("Deleted directory: {Path}", realPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error deleting directory {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }



        /// <summary>
        /// Get file information asynchronously
        /// </summary>
        public virtual async Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("Path cannot be null or empty", nameof(filePath));

                string realPath = GetRealPath(filePath);
                
                try
                {
                    bool isDirectory = Directory.Exists(realPath);
                    bool isFile = File.Exists(realPath);

                    if (!isDirectory && !isFile)
                    {
                        throw new FileNotFoundException("The specified path does not exist.", realPath);
                    }

                    var fileObject = new FileObject(filePath)
                    {
                        IsDirectory = isDirectory,
                        RealPath = filePath,
                        VirtualPath = filePath,
                        SC = this
                    };

                    if (isFile)
                    {
                        var fileInfo = new FileInfo(realPath);
                        fileObject.Size = fileInfo.Length;
                        fileObject.CreationTime = fileInfo.CreationTime;
                        fileObject.LastModified = fileInfo.LastWriteTime;
                        fileObject.LastAccessTime = fileInfo.LastAccessTime;
                        fileObject.Filename = fileInfo.Name;
                    }
                    else
                    {
                        var dirInfo = new DirectoryInfo(realPath);
                        fileObject.CreationTime = dirInfo.CreationTime;
                        fileObject.LastModified = dirInfo.LastWriteTime;
                        fileObject.LastAccessTime = dirInfo.LastAccessTime;
                        fileObject.Filename = dirInfo.Name;
                        fileObject.Size = 0;
                    }

                    _logger?.LogTrace("Retrieved file info for: {Path}", realPath);
                    return fileObject;
                }
                catch (Exception ex) when (!(ex is FileNotFoundException))
                {
                    _logger?.LogError(ex, "Error getting file info for {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Delete a file asynchronously
        /// </summary>
        public virtual async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("Path cannot be null or empty", nameof(filePath));

                string realPath = GetRealPath(filePath);
                
                try
                {
                    if (File.Exists(realPath))
                    {
                        File.Delete(realPath);
                        _logger?.LogDebug("Deleted file: {Path}", realPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error deleting file {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Move/rename a file asynchronously
        /// </summary>
        public virtual async Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(sourceFilePath))
                    throw new ArgumentException("Source path cannot be null or empty", nameof(sourceFilePath));
                if (string.IsNullOrEmpty(destinationFilePath))
                    throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationFilePath));

                string realSourcePath = GetRealPath(sourceFilePath);
                string realDestPath = GetRealPath(destinationFilePath);
                
                try
                {
                    if (overwrite && File.Exists(realDestPath))
                    {
                        File.Delete(realDestPath);
                    }
                    
                    // Ensure destination directory exists
                    string? destDir = Path.GetDirectoryName(realDestPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    
                    File.Move(realSourcePath, realDestPath);
                    _logger?.LogDebug("Moved file from {Source} to {Destination}", realSourcePath, realDestPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error moving file from {Source} to {Destination}", realSourcePath, realDestPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Create a directory asynchronously
        /// </summary>
        public virtual async Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                string realPath = GetRealPath(directoryPath);
                
                try
                {
                    if (!Directory.Exists(realPath))
                    {
                        Directory.CreateDirectory(realPath);
                        _logger?.LogDebug("Created directory: {Path}", realPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error creating directory {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Shutdown the storage connection asynchronously
        /// </summary>
        public virtual async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            await CloseAllHandlesAsync(cancellationToken);
            _logger?.LogDebug("LocalStorage shutdown completed");
        }

        /// <summary>
        /// Get the total storage capacity asynchronously
        /// </summary>
        public virtual async Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                try
                {
                    if (string.IsNullOrEmpty(_baseFolder) || !Directory.Exists(_baseFolder))
                        return 0;

                    var drive = new DriveInfo(Path.GetPathRoot(_baseFolder) ?? _baseFolder);
                    return drive.IsReady ? drive.TotalSize : 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error getting total capacity for {BaseFolder}", _baseFolder);
                    return 0;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Get the available free space asynchronously
        /// </summary>
        public virtual async Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                try
                {
                    if (string.IsNullOrEmpty(_baseFolder) || !Directory.Exists(_baseFolder))
                        return 0;

                    var drive = new DriveInfo(Path.GetPathRoot(_baseFolder) ?? _baseFolder);
                    return drive.IsReady ? drive.AvailableFreeSpace : 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error getting available free space for {BaseFolder}", _baseFolder);
                    return 0;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Close all open file handles asynchronously
        /// </summary>
        public virtual async Task CloseAllHandlesAsync(CancellationToken cancellationToken = default)
        {
            List<IntPtr> handlesCopy;
            lock (_handleLock)
            {
                handlesCopy = _handleMap.Keys.ToList();
            }
            
            // Close handles outside the lock to avoid deadlock
            foreach (var handle in handlesCopy)
            {
                await CloseAsync(handle, cancellationToken);
            }
            
            _logger?.LogDebug("Closed all file handles");
        }

        /// <summary>
        /// Change file ownership asynchronously (no-op for local storage on Windows, limited on Linux)
        /// </summary>
        public virtual async Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                string realPath = GetRealPath(path);
                _logger?.LogDebug("Chown operation (limited/no-op) for {Path} (UID={Uid}, GID={Gid})", realPath, uid, gid);
                
                // On Windows, this is essentially a no-op
                // On Linux, we could use native calls but that's complex and not commonly needed
            }, cancellationToken);
        }

        /// <summary>
        /// Set file times asynchronously
        /// </summary>
        public virtual async Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                string realPath = GetRealPath(path);
                
                try
                {
                    File.SetLastAccessTime(realPath, accessTime);
                    File.SetLastWriteTime(realPath, modifiedTime);
                    
                    _logger?.LogDebug("Updated times for {Path}", realPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error setting timestamps for {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Convert virtual storage path to real file system path
        /// </summary>
        private string GetRealPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return _baseFolder;

            // Normalize the virtual path using our PathHelper
            string normalizedVirtualPath = PathHelper.NormalizePath(virtualPath);
            
            // Remove leading slash for combination
            string relativePath = normalizedVirtualPath.TrimStart('/');
            
            return Path.Combine(_baseFolder, relativePath);
        }

        /// <summary>
        /// Convert real file system path to virtual storage path
        /// </summary>
        private string GetVirtualPath(string realPath)
        {
            if (string.IsNullOrEmpty(realPath) || !realPath.StartsWith(_baseFolder))
                return "/";

            string relativePath = realPath.Substring(_baseFolder.Length);
            return PathHelper.NormalizePath("/" + relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(LocalStorage));
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                lock (_handleLock)
                {
                    foreach (var handle in _handleMap.Values)
                    {
                        handle.Stream?.Dispose();
                    }
                    _handleMap.Clear();
                }
                
                _isDisposed = true;
                _logger?.LogDebug("LocalStorage disposed");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
    */
} 