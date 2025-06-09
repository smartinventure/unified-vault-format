
namespace UvfLib.Storage.Abstractions
{
    public interface IStorage : IDisposable
    {
        // Properties
        EnumStorageType Type { get; set; }
        string BaseFolderOrContainer { get; set; }
        string ConnectionString { get; set; }

        /// <summary>
        /// Maximum read speed in bytes per second. 0 means no throttling.
        /// </summary>
        long MaxReadBytesPerSecond { get; set; }

        /// <summary>
        /// Maximum write speed in bytes per second. 0 means no throttling.
        /// </summary>
        long MaxWriteBytesPerSecond { get; set; }

        /// <summary>
        /// Initialize or set up the storage connection asynchronously
        /// </summary>
        Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Open a file asynchronously with the specified flags
        /// </summary>
        Task<IntPtr> OpenAsync(string realPath, int flags, CancellationToken cancellationToken = default);

        /// <summary>
        /// Close a file asynchronously
        /// </summary>
        Task CloseAsync(IntPtr fileHandle, CancellationToken cancellationToken = default);

        /// <summary>
        /// Read data from a file asynchronously
        /// </summary>
        Task ReadAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Write data to a file asynchronously
        /// </summary>
        Task WriteAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Read data from a file asynchronously using path
        /// </summary>
        Task ReadAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Write data to a file asynchronously using path
        /// </summary>
        Task WriteAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get files in a directory asynchronously with search pattern and options
        /// </summary>
        Task<IEnumerable<string>> GetFilesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all files in a directory asynchronously
        /// </summary>
        Task<IEnumerable<string>> GetFilesAsync(string directoryPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get subdirectories in a directory asynchronously with search pattern and options
        /// </summary>
        Task<IEnumerable<string>> GetDirectoriesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enumerate files and directories in a directory asynchronously
        /// </summary>
        Task<IEnumerable<FileObject>> EnumerateFilesAndDirectoriesAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Test read access to the storage asynchronously
        /// </summary>
        Task<bool> TestReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Test write access to the storage asynchronously
        /// </summary>
        Task<bool> TestWriteAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if a file exists asynchronously
        /// </summary>
        Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if a directory exists asynchronously
        /// </summary>
        Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete a directory asynchronously
        /// </summary>
        Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Enumerate file system entries asynchronously
        /// </summary>
        Task<IEnumerable<string>> EnumerateFileSystemEntriesAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get file information asynchronously
        /// </summary>
        Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete a file asynchronously
        /// </summary>
        Task DeleteAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Move a file asynchronously
        /// </summary>
        Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a directory asynchronously
        /// </summary>
        Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Shutdown the storage connection asynchronously
        /// </summary>
        Task ShutdownAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the total storage capacity asynchronously
        /// </summary>
        Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the available free space asynchronously
        /// </summary>
        Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Close all open file handles asynchronously
        /// </summary>
        Task CloseAllHandlesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Change file ownership asynchronously
        /// </summary>
        Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Set file timestamps asynchronously
        /// </summary>
        Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default);
    }
}
