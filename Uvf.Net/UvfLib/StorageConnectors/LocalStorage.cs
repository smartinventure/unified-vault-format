using System.Runtime.InteropServices;
using FolderMagicLib.VirtualFolderEmulators.Cbfs;
using FolderMagicLib.Layers;
using System.Collections.Concurrent;
using FolderMagicLib.Application;
using FolderMagicLib.StorageConnectors.Throttling;
using ThrottledStreamStopWatch = FolderMagicLib.StorageConnectors.Throttling.ThrottledStreamStopWatch;

namespace FolderMagicLib.StorageConnectors
{
    public class LocalStorage : IStorage, IDisposable
    {
        private string _baseFolder = string.Empty;
        private readonly object _handleLock = new object();
        private readonly Dictionary<IntPtr, FileHandle> _handleMap = new Dictionary<IntPtr, FileHandle>();
        private static readonly Logging.Logging _logger = Logging.Logging.Instance;
        private readonly StorageConnectors.Throttling.IThrottlingService _throttlingService;
        private bool _isDisposed;
        private Dictionary<string, FileObject> _memoryFiles;
        private Dictionary<string, HashSet<string>> _directoryContents;
        private Layer? _layer;
        private readonly ConcurrentDictionary<string, LocalStorage> _localStorageMap = new();

        public EnumStorageType StorageType => EnumStorageType.Local;

        /// <summary>
        /// Maximum read speed in bytes per second. 0 means no throttling.
        /// </summary>
        public long MaxReadBytesPerSecond { get; set; } = 0;

        /// <summary>
        /// Maximum write speed in bytes per second. 0 means no throttling.
        /// </summary>
        public long MaxWriteBytesPerSecond { get; set; } = 0;

        public EnumStorageType Type
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

        public string ConnectionString { get => "/"; set { } }

        public LocalStorage()
        {
            _throttlingService = new StorageConnectors.Throttling.ThrottlingService();
            _memoryFiles = new Dictionary<string, FileObject>();
            _directoryContents = new Dictionary<string, HashSet<string>>();
        }

        /// <summary>
        /// Initialize or set up the storage connection asynchronously
        /// </summary>
        public async Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(baseFolderOrContainer))
                throw new ArgumentException("Base folder cannot be null or empty", nameof(baseFolderOrContainer));

            await InitializeAsync(baseFolderOrContainer, cancellationToken);
        }

        /// <summary>
        /// Initialize storage with base folder asynchronously
        /// </summary>
        private async Task InitializeAsync(string baseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(baseFolderOrContainer))
                throw new ArgumentException("Base folder cannot be null or empty", nameof(baseFolderOrContainer));

            _baseFolder = baseFolderOrContainer;

            bool directoryExists = await Task.Run(() => TestDirectoryExists(), cancellationToken);
            if (!directoryExists)
            {
                throw new DirectoryNotFoundException($"The base folder of a layer: {_baseFolder} does not exist.");
            }
        }

        /// <summary>
        /// Open a file asynchronously with the specified flags
        /// </summary>
        public async Task<IntPtr> OpenAsync(string path, int flags, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                lock (_handleLock)
                {
                    path = LayerHelper.NormalizePath(path, EnumOperatingSystem.Windows);
                    string normalizedPath = Path.Combine(_baseFolder, path);
                    normalizedPath = LayerHelper.NormalizePath(normalizedPath, EnumOperatingSystem.Windows);

                    bool isCreating = FuseFlags.HasFlag(flags, FuseFlags.O_CREAT);

                    // Check if we need to create a file
                    if (isCreating)
                    {
                        // If the file doesn't exist and O_CREAT is set, verify parent directory exists
                        // Note: We deliberately avoid auto-creating parent directories to match standard 
                        // filesystem behavior
                        string parentDir = Path.GetDirectoryName(normalizedPath);
                        if (!Directory.Exists(parentDir))
                        {
                            throw new DirectoryNotFoundException($"Parent directory does not exist: {parentDir}");
                        }
                    }
                    else if (!File.Exists(normalizedPath))
                    {
                        // If O_CREAT is not set and the file doesn't exist, throw FileNotFoundException
                        throw new FileNotFoundException($"File not found: {normalizedPath}");
                    }

                    bool isReadOnly = FuseFlags.IsReadAccess(flags) && !FuseFlags.IsWriteAccess(flags);
                    FileAccess fileAccess = isReadOnly ? FileAccess.Read : FileAccess.ReadWrite;
                    FileMode fileMode = GetFileMode(flags);
                    FileShare fileShare = FileShare.ReadWrite;

                    try
                    {
                        FileStream stream = new FileStream(normalizedPath, fileMode, fileAccess, fileShare);

                        // Apply throttling if needed
                        Stream finalStream = stream;
                        if ((MaxReadBytesPerSecond > 0 && isReadOnly) ||
                            (MaxWriteBytesPerSecond > 0 && !isReadOnly))
                        {
                            var throttledStream = new ThrottledStreamStopWatch(stream);

                            if (isReadOnly && MaxReadBytesPerSecond > 0)
                            {
                                throttledStream.MaximumBytesPerSecond = MaxReadBytesPerSecond;
                            }
                            else if (!isReadOnly && MaxWriteBytesPerSecond > 0)
                            {
                                throttledStream.MaximumBytesPerSecond = MaxWriteBytesPerSecond;
                            }

                            finalStream = throttledStream;
                        }

                        FileHandle handle = new FileHandle(normalizedPath, finalStream, isReadOnly, this);
                        IntPtr handlePtr = handle.CreateContext();
                        _handleMap[handlePtr] = handle;

                        return handlePtr;
                    }
                    catch (Exception ex)
                    {
                        throw new IOException($"Failed to open file: {normalizedPath}. {ex.Message}", ex);
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Close a file asynchronously
        /// </summary>
        public async Task CloseAsync(IntPtr fileHandle, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                lock (_handleLock)
                {
                    if (_handleMap.TryGetValue(fileHandle, out var handle))
                    {
                        handle.Dispose();
                        _handleMap.Remove(fileHandle);
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Read data from a file asynchronously
        /// </summary>
        public async Task ReadAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
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
                        int numBytes = handle.Stream.Read(bytes, 0, (int)size);

                        if (numBytes != size)
                        {
                            throw new IOException($"Failed to read {size} bytes from file. Only read {numBytes} bytes.");
                        }

                        Marshal.Copy(bytes, 0, buffer, (int)size);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error reading from file {handle.Path}: {ex.Message}");
                        throw;
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Write data to a file asynchronously
        /// </summary>
        public async Task WriteAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
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
                        Marshal.Copy(buffer, bytes, 0, (int)size);
                        handle.Stream.Write(bytes, 0, (int)size);
                        handle.Stream.Flush();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error writing to file {handle.Path}: {ex.Message}");
                        throw;
                    }
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Read data from a file asynchronously using path
        /// </summary>
        public async Task ReadAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            IntPtr handle = IntPtr.Zero;
            bool newHandle = false;

            try
            {
                handle = await OpenAsync(path, FuseFlags.O_RDONLY, cancellationToken);
                newHandle = true;

                await ReadAsync(handle, offset, size, buffer, cancellationToken);
            }
            catch
            {
                if (newHandle && handle != IntPtr.Zero)
                {
                    await CloseAsync(handle, cancellationToken);
                }
                throw;
            }
            finally
            {
                if (newHandle && handle != IntPtr.Zero)
                {
                    await CloseAsync(handle, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Write data to a file asynchronously using path
        /// </summary>
        public async Task WriteAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            IntPtr handle = IntPtr.Zero;
            bool newHandle = false;

            try
            {
                handle = await OpenAsync(path, FuseFlags.O_WRONLY | FuseFlags.O_CREAT, cancellationToken);
                newHandle = true;

                await WriteAsync(handle, offset, size, buffer, cancellationToken);
            }
            catch
            {
                if (newHandle && handle != IntPtr.Zero)
                {
                    await CloseAsync(handle, cancellationToken);
                }
                throw;
            }
            finally
            {
                if (newHandle && handle != IntPtr.Zero)
                {
                    await CloseAsync(handle, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Get files in a directory asynchronously with search pattern and options
        /// </summary>
        public async Task<IEnumerable<string>> GetFilesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                try
                {
                    return Directory.GetFileSystemEntries(directoryPath, searchPattern, searchOption);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error enumerating files in {directoryPath}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Get all files in a directory asynchronously
        /// </summary>
        public async Task<IEnumerable<string>> GetFilesAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                try
                {
                    return Directory.GetFiles(directoryPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error getting files in {directoryPath}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Get subdirectories in a directory asynchronously with search pattern and options
        /// </summary>
        public async Task<IEnumerable<string>> GetDirectoriesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                try
                {
                    return Directory.GetDirectories(directoryPath, searchPattern, searchOption);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error getting directories in {directoryPath}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Enumerate files and directories in a directory asynchronously
        /// </summary>
        public async Task<IEnumerable<FileObject>> EnumerateFilesAndDirectoriesAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(realPath))
                    throw new ArgumentException("Path cannot be null or empty", nameof(realPath));

                var fileObjects = new List<FileObject>();

                try
                {
                    // Enumerate files
                    foreach (var file in Directory.GetFiles(realPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var fileObject = new FileObject(file, this)
                            {
                                Filename = fileInfo.Name,
                                RealPath = file,
                                IsReadOnly = readOnly,
                                IsDirectory = false,
                                Size = fileInfo.Length,
                                CreationTime = fileInfo.CreationTime,
                                LastModified = fileInfo.LastWriteTime,
                                LastAccessTime = fileInfo.LastAccessTime,
                                Mode = 0,
                                Uid = 0,
                                Gid = 0
                            };
                            fileObjects.Add(fileObject);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error processing file {file}: {ex.Message}");
                            // Continue with next file
                        }
                    }

                    // Enumerate directories
                    foreach (var directory in Directory.GetDirectories(realPath))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(directory);
                            var fileObject = new FileObject(directory, this)
                            {
                                Filename = dirInfo.Name,
                                RealPath = directory,
                                IsReadOnly = readOnly,
                                IsDirectory = true,
                                Size = 0,
                                CreationTime = dirInfo.CreationTime,
                                LastModified = dirInfo.LastWriteTime,
                                LastAccessTime = dirInfo.LastAccessTime,
                                Mode = 0,
                                Uid = 0,
                                Gid = 0
                            };
                            fileObjects.Add(fileObject);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error processing directory {directory}: {ex.Message}");
                            // Continue with next directory
                        }
                    }

                    return fileObjects;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error enumerating files and directories in {realPath}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Test read access to the storage asynchronously
        /// </summary>
        public async Task<bool> TestReadAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(_baseFolder))
                    return false;

                try
                {
                    // Create a temporary file for testing
                    var testFile = Path.Combine(_baseFolder, $"test_{Guid.NewGuid()}.tmp");
                    File.WriteAllText(testFile, "test");

                    try
                    {
                        using var stream = File.OpenRead(testFile);
                        return true;
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(testFile))
                                File.Delete(testFile);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Failed to delete test file {testFile}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error testing read access for {_baseFolder}: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Test write access to the storage asynchronously
        /// </summary>
        public async Task<bool> TestWriteAsync(CancellationToken cancellationToken = default)
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
                        _logger.LogWarning($"Failed to delete test file {testFile}: {ex.Message}");
                        return true; // Still return true as write was successful
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error testing write access for {_baseFolder}: {ex.Message}");
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Helper method to check if base directory exists
        /// </summary>
        public bool TestDirectoryExists()
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(_baseFolder))
                return false;

            try
            {
                return Directory.Exists(_baseFolder);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking directory existence for {_baseFolder}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if a file exists asynchronously
        /// </summary>
        public async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

                try
                {
                    return File.Exists(filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error checking file existence for {filePath}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Check if a directory exists asynchronously
        /// </summary>
        public async Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                try
                {
                    return Directory.Exists(directoryPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error checking directory existence for {directoryPath}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Delete a directory asynchronously
        /// </summary>
        public async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("Path cannot be null or empty", nameof(path));

                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error deleting directory {path}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Enumerate file system entries asynchronously
        /// </summary>
        public async Task<IEnumerable<string>> EnumerateFileSystemEntriesAsync(string path, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("Path cannot be null or empty", nameof(path));

                try
                {
                    return Directory.EnumerateFileSystemEntries(path);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error enumerating file system entries in {path}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Get file information asynchronously
        /// </summary>
        public async Task<FileObject> GetFileInfoAsync(string path, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("Path cannot be null or empty", nameof(path));

                try
                {
                    bool isDirectory = Directory.Exists(path);
                    bool isFile = File.Exists(path);

                    if (!isDirectory && !isFile)
                    {
                        throw new FileNotFoundException("The specified path does not exist.", path);
                    }

                    var fileObject = new FileObject(path, this)
                    {
                        IsDirectory = isDirectory
                    };

                    if (isFile)
                    {
                        var fileInfo = new FileInfo(path);
                        fileObject.Size = fileInfo.Length;
                        fileObject.CreationTime = fileInfo.CreationTime;
                        fileObject.LastModified = fileInfo.LastWriteTime;
                        fileObject.LastAccessTime = fileInfo.LastAccessTime;
                        fileObject.RealPath = path;
                        fileObject.Filename = fileInfo.Name;
                    }
                    else
                    {
                        var dirInfo = new DirectoryInfo(path);
                        fileObject.CreationTime = dirInfo.CreationTime;
                        fileObject.LastModified = dirInfo.LastWriteTime;
                        fileObject.LastAccessTime = dirInfo.LastAccessTime;
                        fileObject.Filename = dirInfo.Name;
                        fileObject.Size = 0;
                        fileObject.RealPath = path;
                    }

                    return fileObject;
                }
                catch (Exception ex) when (!(ex is FileNotFoundException))
                {
                    _logger.LogError($"Error getting file info for {path}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Attempts to release any locks on a specific file path to ensure it can be deleted
        /// </summary>
        private async Task ReleaseFileLocksAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            _ = _logger.LogInfoAsync($"Attempting to release locks on {filePath}");

            List<IntPtr> handlesToClose = new();

            // Find any handles in our _handleMap that might be for this file
            lock (_handleLock)
            {
                foreach (var kvp in _handleMap)
                {
                    var handle = kvp.Value;
                    if (string.Equals(handle.Path, filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = _logger.LogInfoAsync($"Found open handle for {filePath}, will close it");
                        handlesToClose.Add(kvp.Key);
                    }
                }

                // Close all identified handles
                foreach (var handlePtr in handlesToClose)
                {
                    if (_handleMap.TryGetValue(handlePtr, out var handle))
                    {
                        try
                        {
                            handle.Dispose();
                            _handleMap.Remove(handlePtr);
                            _ = _logger.LogInfoAsync($"Successfully closed handle for {filePath}");
                        }
                        catch (Exception ex)
                        {
                            _ = _logger.LogInfoAsync($"Error closing handle for {filePath}: {ex.Message}");
                        }
                    }
                }
            }

            // Give the system a moment to fully release any handles
            if (handlesToClose.Count > 0)
            {
                await Task.Delay(50, cancellationToken);
            }
        }

        /// <summary>
        /// Delete a file asynchronously using true async IO operations
        /// </summary>
        public async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            try
            {
                // Quick check if file exists first - this is synchronous but very fast
                if (!File.Exists(filePath))
                    return; // Nothing to delete

                // First make sure we don't have any open handles to this file
                await ReleaseFileLocksAsync(filePath, cancellationToken);

                // First approach: FileOptions.DeleteOnClose with asynchronous option
                try
                {
                    using var fs = new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.Delete,
                        4096,
                        FileOptions.Asynchronous | FileOptions.DeleteOnClose);

                    // Register cancellation
                    cancellationToken.Register(() => fs.Dispose());

                    // Ensure stream is properly closed asynchronously
                    await fs.FlushAsync(cancellationToken);

                    // The file will be deleted when the stream is disposed 
                    return;
                }
                catch (IOException ex)
                {
                    // File might be locked - log details and fall back to alternative approach
                    _ = _logger.LogInfoAsync($"File {filePath} is locked: {ex.Message}, trying alternative deletion approach");
                }

                // Second approach: If the file is still locked, try again with Task.Run
                // This moves the blocking operation to a background thread
                await Task.Run(() =>
                {
                    // Just a simple check and delete - no retries
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _ = _logger.LogInfoAsync($"Error deleting file {filePath}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Move a file asynchronously
        /// </summary>
        public async Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(sourceFilePath))
                    throw new ArgumentException("Source path cannot be null or empty", nameof(sourceFilePath));
                if (string.IsNullOrEmpty(destinationFilePath))
                    throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationFilePath));

                try
                {
                    if (overwrite && File.Exists(destinationFilePath))
                    {
                        File.Delete(destinationFilePath);
                    }
                    File.Move(sourceFilePath, destinationFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error moving file from {sourceFilePath} to {destinationFilePath}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Create a directory asynchronously
        /// </summary>
        public async Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                try
                {
                    if (!Directory.Exists(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error creating directory {directoryPath}: {ex.Message}");
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Shutdown the storage connection asynchronously
        /// </summary>
        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInfo($"ShutdownAsync: Starting shutdown for LocalStorage at {_baseFolder}");

            // Guard against disposed state
            if (_isDisposed)
            {
                _logger.LogInfo($"ShutdownAsync: LocalStorage for {_baseFolder} is already disposed, skipping shutdown");
                return;
            }

            try
            {
                // Close all handles without holding the lock while awaiting
                _logger.LogInfo($"ShutdownAsync: Closing all handles for {_baseFolder}");
                await CloseAllHandlesAsync(cancellationToken);
                _logger.LogInfo($"ShutdownAsync: Successfully closed all handles for {_baseFolder}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ShutdownAsync: Error closing handles during LocalStorage shutdown for {_baseFolder}: {ex.Message}");
                // Continue with shutdown even if handle closing fails
            }

            // Only lock for the final cleanup steps
            lock (_handleLock)
            {
                try
                {
                    // Clear any remaining handles just to be safe
                    _handleMap.Clear();
                    _logger.LogInfo($"ShutdownAsync: LocalStorage shutdown for {_baseFolder} completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"ShutdownAsync: Final error during LocalStorage shutdown for {_baseFolder}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get the total storage capacity asynchronously
        /// </summary>
        public async Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                try
                {
                    if (string.IsNullOrEmpty(_baseFolder) || !Directory.Exists(_baseFolder))
                        return 0;

                    var drive = new DriveInfo(Path.GetPathRoot(_baseFolder));
                    return drive.IsReady ? drive.TotalSize : 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error getting total capacity for {_baseFolder}: {ex.Message}");
                    return 0;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Get the available free space asynchronously
        /// </summary>
        public async Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                try
                {
                    if (string.IsNullOrEmpty(_baseFolder) || !Directory.Exists(_baseFolder))
                        return 0;

                    var drive = new DriveInfo(Path.GetPathRoot(_baseFolder));
                    return drive.IsReady ? drive.AvailableFreeSpace : 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error getting available free space for {_baseFolder}: {ex.Message}");
                    return 0;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Close all open file handles asynchronously
        /// </summary>
        public async Task CloseAllHandlesAsync(CancellationToken cancellationToken = default)
        {
            // Wrap the synchronous internal method to fulfill the async interface contract
            await Task.Run(() => CloseAllHandlesInternal(), cancellationToken);
        }

        /// <summary>
        /// Synchronously closes all open file handles. Intended for Dispose.
        /// </summary>
        private void CloseAllHandlesInternal()
        {
            _logger.LogInfo($"CloseAllHandlesInternal: Closing {_handleMap.Count} handles synchronously.");
            // Need to lock because we might be modifying the collection while iterating keys
            // Although ideally, Dispose shouldn't be called concurrently with other operations.
            lock (_handleLock)
            {
                // Create a copy of keys to avoid modification during enumeration issues
                var handleKeys = _handleMap.Keys.ToList();
                foreach (var handlePtr in handleKeys)
                {
                    if (_handleMap.TryGetValue(handlePtr, out var handle))
                    {
                        try
                        {
                            handle.Dispose(); // Dispose the handle synchronously
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error disposing handle {handlePtr} during synchronous close: {ex.Message}");
                            // Continue trying to close other handles
                        }
                    }
                }
                _handleMap.Clear(); // Clear the map after disposing all
            }
            _logger.LogInfo("CloseAllHandlesInternal: Finished closing handles.");
        }

        /// <summary>
        /// Converts FUSE open flags to FileMode for .NET file operations
        /// </summary>
        private FileMode GetFileMode(int flags)
        {
            if (FuseFlags.HasFlag(flags, FuseFlags.O_CREAT))
            {
                if (FuseFlags.HasFlag(flags, FuseFlags.O_EXCL))
                {
                    return FileMode.CreateNew; // Create a new file, fail if exists
                }
                else if (FuseFlags.HasFlag(flags, FuseFlags.O_TRUNC))
                {
                    return FileMode.Create; // Create or overwrite
                }
                else
                {
                    return FileMode.OpenOrCreate; // Open if exists, create if not
                }
            }
            else if (FuseFlags.HasFlag(flags, FuseFlags.O_TRUNC))
            {
                return FileMode.Truncate; // Open and truncate existing file
            }
            else if (FuseFlags.HasFlag(flags, FuseFlags.O_APPEND))
            {
                return FileMode.Append; // Open for appending
            }
            else
            {
                return FileMode.Open; // Open existing file
            }
        }

        /// <summary>
        /// Change file ownership asynchronously
        /// </summary>
        public async Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                // On Windows, ownership changes are mostly ignored
                // On Unix systems, this would use native APIs to change ownership
                _logger.LogInfo($"Chown operation simulated for {path} (UID={uid}, GID={gid})");
                return Task.CompletedTask;
            }, cancellationToken);
        }

        /// <summary>
        /// Set file timestamps asynchronously
        /// </summary>
        public async Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                try
                {
                    File.SetLastAccessTime(path, accessTime);
                    File.SetLastWriteTime(path, modifiedTime);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error setting timestamps for {path}: {ex.Message}");
                    throw;
                }
                return Task.CompletedTask;
            }, cancellationToken);
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(LocalStorage));
        }

        /// <summary>
        /// Disposes the local storage instance, implementing IDisposable
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            // Use lock for thread safety during disposal, though ideally Dispose isn't called concurrently
            lock (_handleLock)
            {
                // Double-check disposal status after acquiring lock
                if (_isDisposed)
                    return;

                _logger.LogInfo($"Dispose: Starting synchronous disposal for LocalStorage at {_baseFolder}");
                try
                {
                    // Call the synchronous internal method
                    CloseAllHandlesInternal();
                    _isDisposed = true;
                    _logger.LogInfo($"Dispose: Completed synchronous disposal for LocalStorage at {_baseFolder}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error during synchronous disposal of LocalStorage at {_baseFolder}: {ex.Message}");
                    // Avoid throwing from Dispose if possible, but log the error.
                    // Depending on severity, might need to re-throw or handle differently.
                }
                finally
                {
                    // Ensure disposed flag is set even if CloseAllHandlesInternal throws
                    _isDisposed = true;
                }
            }
            // Suppress finalization is typically done outside the lock
            GC.SuppressFinalize(this);
        }
    }
}

