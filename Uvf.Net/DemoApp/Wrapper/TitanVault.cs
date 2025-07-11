using System.Text;
using System.Runtime.InteropServices;
using System.Reflection;

namespace DemoApp.Wrapper
{
    /// <summary>
    /// High-level C# wrapper for TitanVault operations
    /// Provides an easy-to-use API for vault operations with automatic resource management
    /// </summary>
    public class TitanVault : IDisposable
    {
        private IntPtr _vaultHandle;
        private bool _disposed = false;

        private TitanVault(IntPtr handle)
        {
            _vaultHandle = handle;
        }

        /// <summary>
        /// Set debug verbosity for encryption streams (reduces console spam)
        /// </summary>
        public static void SetVerboseDebug(bool enabled)
        {
            // Set environment variable that works across AOT compilation boundary
            Environment.SetEnvironmentVariable("UVF_DEBUG_VERBOSE", enabled ? "true" : "false");
            
            if (!enabled)
            {
                Console.WriteLine("🔇 Quiet mode enabled - reduced debug output");
            }
            
            Console.WriteLine($"🔧 Set UVF_DEBUG_VERBOSE = {(enabled ? "true" : "false")}");
        }

        #region Factory Methods

        /// <summary>
        /// Create a new UVF vault
        /// </summary>
        public static TitanVault CreateUvfVault(string vaultPath, char[] adminPassword, bool encryptFilenames)
        {
            try
            {
                Console.WriteLine($"🔧 Creating UVF vault at: {vaultPath}");
                Console.WriteLine($"🔧 Encrypt filenames: {encryptFilenames}");
                
                unsafe
                {
                    var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                    var adminPasswordBytes = System.Text.Encoding.UTF8.GetBytes(adminPassword);

                    fixed (byte* vaultPathPtr = vaultPathBytes)
                    fixed (byte* adminPasswordPtr = adminPasswordBytes)
                    {
                        Console.WriteLine($"🔧 Calling native CreateUvfVault...");
                        var result = TitanVaultNativeMethods.CreateUvfVault(
                            vaultPathPtr, vaultPathBytes.Length,
                            adminPasswordPtr, adminPasswordBytes.Length,
                            encryptFilenames ? 1 : 0,
                            TitanVaultUtils.KdfMethod.PBKDF2, 64000);

                        Console.WriteLine($"🔧 Native CreateUvfVault result: {result}");
                        
                        if (result != 0)
                        {
                            string errorMsg = TitanVaultUtils.GetLastErrorString();
                            Console.WriteLine($"❌ Native CreateUvfVault failed with code {result}: {errorMsg}");
                            throw new InvalidOperationException($"Failed to create UVF vault: {errorMsg}");
                        }
                        
                        Console.WriteLine($"✅ Native CreateUvfVault succeeded, now loading vault...");
                        
                        // Now load the vault
                        return LoadUvfVault(vaultPath, adminPassword, "admin");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ CreateUvfVault exception: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"❌ Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                throw new InvalidOperationException($"Failed to create UVF vault: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Load an existing UVF vault
        /// </summary>
        public static TitanVault LoadUvfVault(string vaultPath, char[] userPassword, string? userId = null)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (userPassword == null)
                throw new ArgumentNullException(nameof(userPassword));

            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var userPasswordBytes = Encoding.UTF8.GetBytes(userPassword);
                var userIdBytes = string.IsNullOrEmpty(userId) ? new byte[0] : TitanVaultUtils.StringToUtf8Bytes(userId);

                try
                {
                    fixed (byte* vaultPathPtr = vaultPathBytes)
                    fixed (byte* userPasswordPtr = userPasswordBytes)
                    fixed (byte* userIdPtr = userIdBytes.Length > 0 ? userIdBytes : null)
                    {
                        var handle = TitanVaultNativeMethods.LoadUvfVault(
                            vaultPathPtr, vaultPathBytes.Length,
                            userPasswordPtr, userPasswordBytes.Length,
                            userIdPtr, userIdBytes.Length);

                        if (handle == IntPtr.Zero)
                        {
                            throw new InvalidOperationException($"Failed to load UVF vault: {TitanVaultUtils.GetLastErrorString()}");
                        }

                        return new TitanVault(handle);
                    }
                }
                finally
                {
                    Array.Clear(userPasswordBytes, 0, userPasswordBytes.Length);
                }
            }
        }

        /// <summary>
        /// Create a new Cryptomator vault
        /// </summary>
        public static TitanVault CreateCryptomatorVault(string vaultPath, char[] password)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var passwordBytes = Encoding.UTF8.GetBytes(password);

                try
                {
                    fixed (byte* vaultPathPtr = vaultPathBytes)
                    fixed (byte* passwordPtr = passwordBytes)
                    {
                        var result = TitanVaultNativeMethods.CreateCryptomatorVault(
                            vaultPathPtr, vaultPathBytes.Length,
                            passwordPtr, passwordBytes.Length);

                        if (result != TitanVaultUtils.ReturnCodes.Success)
                        {
                            throw new InvalidOperationException($"Failed to create Cryptomator vault: {TitanVaultUtils.GetLastErrorString()}");
                        }

                        // Load the newly created vault
                        return LoadCryptomatorVault(vaultPath, password);
                    }
                }
                finally
                {
                    Array.Clear(passwordBytes, 0, passwordBytes.Length);
                }
            }
        }

        /// <summary>
        /// Load an existing Cryptomator vault
        /// </summary>
        public static TitanVault LoadCryptomatorVault(string vaultPath, char[] password)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var passwordBytes = Encoding.UTF8.GetBytes(password);

                try
                {
                    fixed (byte* vaultPathPtr = vaultPathBytes)
                    fixed (byte* passwordPtr = passwordBytes)
                    {
                        var handle = TitanVaultNativeMethods.LoadCryptomatorVault(
                            vaultPathPtr, vaultPathBytes.Length,
                            passwordPtr, passwordBytes.Length);

                        if (handle == IntPtr.Zero)
                        {
                            throw new InvalidOperationException($"Failed to load Cryptomator vault: {TitanVaultUtils.GetLastErrorString()}");
                        }

                        return new TitanVault(handle);
                    }
                }
                finally
                {
                    Array.Clear(passwordBytes, 0, passwordBytes.Length);
                }
            }
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Write data to a file in the vault
        /// </summary>
        public void WriteAllBytes(string filePath, byte[] data)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                fixed (byte* dataPtr = data)
                {
                    var result = TitanVaultNativeMethods.WriteFile(_vaultHandle, filePathPtr, filePathBytes.Length, dataPtr, data.Length);
                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to write file: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        /// <summary>
        /// Read all data from a file in the vault
        /// </summary>
        public byte[] ReadAllBytes(string filePath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    // First call to get the required buffer size
                    int bufferSize = 0;
                    var result = TitanVaultNativeMethods.ReadFile(_vaultHandle, filePathPtr, filePathBytes.Length, null, &bufferSize);

                    if (result == TitanVaultUtils.ReturnCodes.InsufficientBuffer)
                    {
                        // Allocate buffer and read the file
                        var buffer = new byte[bufferSize];
                        fixed (byte* bufferPtr = buffer)
                        {
                            result = TitanVaultNativeMethods.ReadFile(_vaultHandle, filePathPtr, filePathBytes.Length, bufferPtr, &bufferSize);
                            if (result == TitanVaultUtils.ReturnCodes.Success)
                            {
                                return buffer;
                            }
                        }
                    }

                    throw new InvalidOperationException($"Failed to read file: {TitanVaultUtils.GetLastErrorString()}");
                }
            }
        }

        /// <summary>
        /// Check if a file exists in the vault
        /// </summary>
        public bool FileExists(string filePath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    var result = TitanVaultNativeMethods.FileExists(_vaultHandle, filePathPtr, filePathBytes.Length);
                    if (result < 0)
                    {
                        throw new InvalidOperationException($"Failed to check file existence: {TitanVaultUtils.GetLastErrorString()}");
                    }
                    return result == 1;
                }
            }
        }

        #endregion

        #region Directory Operations

        /// <summary>
        /// Creates a directory in the vault
        /// </summary>
        public void CreateDirectory(string directoryPath)
        {
            EnsureOpen();
            unsafe
            {
                var pathBytes = Encoding.UTF8.GetBytes(directoryPath);
                fixed (byte* pathPtr = pathBytes)
                {
                    var result = TitanVaultNativeMethods.CreateDirectory(_vaultHandle, pathPtr, pathBytes.Length);
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"Failed to create directory: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        /// <summary>
        /// Checks if a directory exists in the vault
        /// </summary>
        public bool DirectoryExists(string directoryPath)
        {
            EnsureOpen();
            unsafe
            {
                var pathBytes = Encoding.UTF8.GetBytes(directoryPath);
                fixed (byte* pathPtr = pathBytes)
                {
                    var result = TitanVaultNativeMethods.DirectoryExists(_vaultHandle, pathPtr, pathBytes.Length);
                    if (result < 0)
                    {
                        throw new InvalidOperationException($"Failed to check directory existence: {TitanVaultUtils.GetLastErrorString()}");
                    }
                    return result == 1;
                }
            }
        }

        /// <summary>
        /// Deletes a directory from the vault
        /// </summary>
        public void DeleteDirectory(string directoryPath)
        {
            EnsureOpen();
            unsafe
            {
                var pathBytes = Encoding.UTF8.GetBytes(directoryPath);
                fixed (byte* pathPtr = pathBytes)
                {
                    var result = TitanVaultNativeMethods.DeleteDirectory(_vaultHandle, pathPtr, pathBytes.Length);
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"Failed to delete directory: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        /// <summary>
        /// Deletes a file from the vault
        /// </summary>
        public void DeleteFile(string filePath)
        {
            EnsureOpen();
            unsafe
            {
                var pathBytes = Encoding.UTF8.GetBytes(filePath);
                fixed (byte* pathPtr = pathBytes)
                {
                    var result = TitanVaultNativeMethods.DeleteFile(_vaultHandle, pathPtr, pathBytes.Length);
                    if (result != 0)
                    {
                        throw new InvalidOperationException($"Failed to delete file: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        #endregion

        #region Stream Operations

        /// <summary>
        /// Opens a file for reading only
        /// </summary>
        public TitanVaultStream OpenReadStream(string filePath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVault));
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    var handle = TitanVaultNativeMethods.OpenReadStream(_vaultHandle, filePathPtr, filePathBytes.Length);

                    if (handle == IntPtr.Zero)
                    {
                        throw new IOException($"Failed to open read stream for '{filePath}': {TitanVaultUtils.GetLastErrorString()}");
                    }

                    return new TitanVaultStream(handle, this, canWrite: false);
                }
            }
        }

        /// <summary>
        /// Opens a file for reading and writing
        /// </summary>
        public TitanVaultStream OpenWriteStream(string filePath)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVault));
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    var handle = TitanVaultNativeMethods.OpenWriteStream(_vaultHandle, filePathPtr, filePathBytes.Length);

                    if (handle == IntPtr.Zero)
                    {
                        throw new IOException($"Failed to open write stream for '{filePath}': {TitanVaultUtils.GetLastErrorString()}");
                    }

                    return new TitanVaultStream(handle, this, canWrite: true);
                }
            }
        }

        /// <summary>
        /// Opens a file for reading and writing (alias for OpenWriteStream for clarity)
        /// </summary>
        public TitanVaultStream OpenReadWriteStream(string filePath)
        {
            return OpenWriteStream(filePath);
        }

        /// <summary>
        /// Opens a file in the specified mode
        /// </summary>
        public TitanVaultStream OpenStream(string filePath, FileAccess access)
        {
            return access switch
            {
                FileAccess.Read => OpenReadStream(filePath),
                FileAccess.Write => OpenWriteStream(filePath),
                FileAccess.ReadWrite => OpenWriteStream(filePath),
                _ => throw new ArgumentException("Invalid file access mode", nameof(access))
            };
        }

        /// <summary>
        /// Open a stream with specific flags for fine-grained control over file opening behavior.
        /// Use TitanVaultUtils.OpenFlags constants to combine flags.
        /// </summary>
        /// <param name="filePath">Path to the file in the vault</param>
        /// <param name="flags">Combination of TitanVaultUtils.OpenFlags constants</param>
        /// <returns>TitanVaultStream for the file</returns>
        /// <example>
        /// // Create file if it doesn't exist, truncate if it does
        /// var stream = vault.OpenStreamWithFlags("/test.txt", TitanVaultUtils.OpenFlags.WriteOnly | TitanVaultUtils.OpenFlags.Create | TitanVaultUtils.OpenFlags.Truncate);
        /// 
        /// // Open for appending (create if doesn't exist)
        /// var stream = vault.OpenStreamWithFlags("/log.txt", TitanVaultUtils.OpenFlags.WriteOnly | TitanVaultUtils.OpenFlags.Create | TitanVaultUtils.OpenFlags.Append);
        /// 
        /// // Open existing file for reading and writing (fail if doesn't exist)
        /// var stream = vault.OpenStreamWithFlags("/data.txt", TitanVaultUtils.OpenFlags.ReadWrite);
        /// </example>
        public TitanVaultStream OpenStreamWithFlags(string filePath, int flags)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            EnsureOpen();

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    var streamHandle = TitanVaultNativeMethods.OpenStreamWithFlags(
                        _vaultHandle,
                        filePathPtr,
                        filePathBytes.Length,
                        flags);

                    if (streamHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to open stream with flags: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    // Determine if the stream is writable based on the flags
                    bool canWrite = (flags & (TitanVaultUtils.OpenFlags.WriteOnly | TitanVaultUtils.OpenFlags.ReadWrite)) != 0;

                    return new TitanVaultStream(streamHandle, this, canWrite);
                }
            }
        }

        /// <summary>
        /// List directory contents (returns filenames only, for compatibility with native exports)
        /// </summary>
        public string[] ListDirectory(string directoryPath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            unsafe
            {
                var pathBytes = TitanVaultUtils.StringToUtf8Bytes(directoryPath);
                
                // Allocate buffer for entry pointers (max 100 entries)
                const int maxEntries = 100;
                IntPtr[] entryPtrs = new IntPtr[maxEntries];
                int maxEntriesCount = maxEntries;

                fixed (byte* pathPtr = pathBytes)
                fixed (IntPtr* entriesBuffer = entryPtrs)
                {
                    var result = TitanVaultNativeMethods.ListDirectory(
                        _vaultHandle, pathPtr, pathBytes.Length,
                        (IntPtr)entriesBuffer, (IntPtr)(&maxEntriesCount));

                    if (result < 0)
                    {
                        throw new InvalidOperationException($"Failed to list directory: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    // Convert the returned entry strings
                    var entries = new string[result];
                    for (int i = 0; i < result; i++)
                    {
                        if (entryPtrs[i] != IntPtr.Zero)
                        {
                            entries[i] = Marshal.PtrToStringUTF8(entryPtrs[i]) ?? "";
                            TitanVaultNativeMethods.FreeString(entryPtrs[i]);
                        }
                    }

                    return entries;
                }
            }
        }

        /// <summary>
        /// List directory contents with full metadata (FileObject with IsDirectory, Size, timestamps, etc.)
        /// This is a higher-level method that combines ListDirectory + GetFileInfo for each entry
        /// </summary>
        public FileObject[] ListDirectoryDetailed(string directoryPath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            // First get the list of entry names
            var entryNames = ListDirectory(directoryPath);
            var fileObjects = new List<FileObject>();

            foreach (var entryName in entryNames)
            {
                if (!string.IsNullOrEmpty(entryName))
                {
                    // Build full path for the entry
                    string entryPath = directoryPath == "/" ? $"/{entryName}" : $"{directoryPath}/{entryName}";
                    
                    // Check if it's a directory
                    bool isDirectory = DirectoryExists(entryPath);
                    
                    // Create FileObject with full metadata
                    var fileObject = CreateFileObjectWithMetadata(entryPath, entryName, isDirectory);
                    fileObjects.Add(fileObject);
                }
            }

            return fileObjects.ToArray();
        }

        /// <summary>
        /// Helper method to create FileObject with full metadata
        /// </summary>
        private FileObject CreateFileObjectWithMetadata(string fullPath, string entryName, bool isDirectory)
        {
            var fileObject = new FileObject(fullPath)
            {
                IsDirectory = isDirectory,
                Filename = entryName,
                RealPath = fullPath,
                VirtualPath = fullPath
            };

            if (isDirectory)
            {
                // For directories, set basic metadata
                fileObject.Size = 0;
                fileObject.CreationTime = DateTime.UtcNow;
                fileObject.LastModified = DateTime.UtcNow;
                fileObject.LastAccessTime = DateTime.UtcNow;
            }
            else
            {
                // For files, get full metadata from native layer
                try
                {
                    unsafe
                    {
                        var pathBytes = TitanVaultUtils.StringToUtf8Bytes(fullPath);
                        long fileSize = 0;
                        long lastModified = 0;

                        fixed (byte* pathPtr = pathBytes)
                        {
                            var result = TitanVaultNativeMethods.GetFileInfo(
                                _vaultHandle, pathPtr, pathBytes.Length,
                                (IntPtr)(&fileSize), (IntPtr)(&lastModified));

                            if (result == TitanVaultUtils.ReturnCodes.Success)
                            {
                                // Convert Unix timestamp back to DateTime
                                var lastModifiedDateTime = DateTimeOffset.FromUnixTimeSeconds(lastModified).DateTime;
                                
                                fileObject.Size = fileSize; // This is the DECRYPTED size!
                                fileObject.CreationTime = lastModifiedDateTime;
                                fileObject.LastModified = lastModifiedDateTime;
                                fileObject.LastAccessTime = lastModifiedDateTime;
                            }
                            else
                            {
                                // Fallback if GetFileInfo fails
                                fileObject.Size = 0;
                                fileObject.CreationTime = DateTime.UtcNow;
                                fileObject.LastModified = DateTime.UtcNow;
                                fileObject.LastAccessTime = DateTime.UtcNow;
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback if metadata retrieval fails
                    fileObject.Size = 0;
                    fileObject.CreationTime = DateTime.UtcNow;
                    fileObject.LastModified = DateTime.UtcNow;
                    fileObject.LastAccessTime = DateTime.UtcNow;
                }
            }

            return fileObject;
        }

        #endregion

        #region Resource Management

        /// <summary>
        /// Ensure the vault is open
        /// </summary>
        internal void EnsureOpen()
        {
            if (_vaultHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Vault is not open");
            }
        }

        /// <summary>
        /// Closes the vault and releases all resources
        /// </summary>
        public void Close()
        {
            if (_vaultHandle != IntPtr.Zero)
            {
                TitanVaultNativeMethods.CloseVault(_vaultHandle);
                _vaultHandle = IntPtr.Zero;
            }
        }

        /// <summary>
        /// Dispose the vault and free resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                Close();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizer to ensure resources are cleaned up
        /// </summary>
        ~TitanVault()
        {
            Dispose();
        }

        /// <summary>
        /// Get file information (size, modified time, etc.)
        /// </summary>
        public FileObject GetFileInfo(string filePath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var pathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                long fileSize = 0;
                long lastModified = 0;

                fixed (byte* pathPtr = pathBytes)
                {
                    var result = TitanVaultNativeMethods.GetFileInfo(
                        _vaultHandle, pathPtr, pathBytes.Length,
                        (IntPtr)(&fileSize), (IntPtr)(&lastModified));

                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to get file info: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    // Convert Unix timestamp back to DateTime
                    var lastModifiedDateTime = DateTimeOffset.FromUnixTimeSeconds(lastModified).DateTime;
                    
                    // Create FileObject consistent with StorageLib
                    return new FileObject(filePath)
                    {
                        IsDirectory = false,
                        Filename = Path.GetFileName(filePath),
                        RealPath = filePath,
                        VirtualPath = filePath,
                        Size = fileSize,
                        CreationTime = lastModifiedDateTime, // Use same timestamp for creation time
                        LastModified = lastModifiedDateTime,
                        LastAccessTime = lastModifiedDateTime // Use same timestamp for access time
                    };
                }
            }
        }

        /// <summary>
        /// Move/rename file or directory
        /// </summary>
        public void Move(string sourcePath, string destinationPath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(sourcePath))
                throw new ArgumentNullException(nameof(sourcePath));
            if (string.IsNullOrEmpty(destinationPath))
                throw new ArgumentNullException(nameof(destinationPath));

            unsafe
            {
                var sourcePathBytes = TitanVaultUtils.StringToUtf8Bytes(sourcePath);
                var destPathBytes = TitanVaultUtils.StringToUtf8Bytes(destinationPath);

                fixed (byte* sourcePtr = sourcePathBytes)
                fixed (byte* destPtr = destPathBytes)
                {
                    var result = TitanVaultNativeMethods.MoveEntry(
                        _vaultHandle, 
                        sourcePtr, sourcePathBytes.Length,
                        destPtr, destPathBytes.Length);

                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to move entry: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        /// <summary>
        /// Read all text from a file as UTF-8 string
        /// </summary>
        public string ReadAllText(string filePath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var pathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);

                fixed (byte* pathPtr = pathBytes)
                {
                    var textPtr = TitanVaultNativeMethods.ReadAllText(_vaultHandle, pathPtr, pathBytes.Length);
                    
                    if (textPtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to read text file: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    try
                    {
                        return Marshal.PtrToStringUTF8(textPtr) ?? "";
                    }
                    finally
                    {
                        TitanVaultNativeMethods.FreeString(textPtr);
                    }
                }
            }
        }

        /// <summary>
        /// Write all text to a file as UTF-8
        /// </summary>
        public void WriteAllText(string filePath, string text)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            unsafe
            {
                var pathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                var textBytes = TitanVaultUtils.StringToUtf8Bytes(text);

                fixed (byte* pathPtr = pathBytes)
                fixed (byte* textPtr = textBytes)
                {
                    var result = TitanVaultNativeMethods.WriteAllText(
                        _vaultHandle, pathPtr, pathBytes.Length,
                        textPtr, textBytes.Length);

                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to write text file: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        /// <summary>
        /// Append text to a file as UTF-8
        /// </summary>
        public void AppendAllText(string filePath, string text)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            unsafe
            {
                var pathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                var textBytes = TitanVaultUtils.StringToUtf8Bytes(text);

                fixed (byte* pathPtr = pathBytes)
                fixed (byte* textPtr = textBytes)
                {
                    var result = TitanVaultNativeMethods.AppendAllText(
                        _vaultHandle, pathPtr, pathBytes.Length,
                        textPtr, textBytes.Length);

                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to append text file: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        #endregion

        #region Async Wrapper Methods

        /// <summary>
        /// Async wrapper for RemoveUserFromVault static method
        /// </summary>
        public static Task RemoveUserFromVaultAsync(string vaultPath, char[] adminPassword, string userIdToRemove)
        {
            return Task.Run(() => TitanVaultStatic.RemoveUserFromVault(vaultPath, adminPassword, userIdToRemove));
        }

        /// <summary>
        /// Async wrapper for RotateVaultKeys static method
        /// </summary>
        public static Task RotateVaultKeysAsync(string vaultPath, char[] adminPassword, TitanVaultUtils.VaultFormat vaultFormat)
        {
            return Task.Run(() => TitanVaultStatic.RotateVaultKeys(vaultPath, adminPassword, vaultFormat));
        }

        /// <summary>
        /// Async wrapper for BackupVaultFiles static method
        /// </summary>
        public static Task BackupVaultFilesAsync(string vaultPath, string backupPath, bool overwriteExisting = false)
        {
            return Task.Run(() => TitanVaultStatic.BackupVaultFiles(vaultPath, backupPath, overwriteExisting));
        }

        /// <summary>
        /// Async wrapper for ChangeCryptomatorVaultPassword static method
        /// </summary>
        public static Task ChangeCryptomatorVaultPasswordAsync(string vaultPath, char[] oldPassword, char[] newPassword)
        {
            return Task.Run(() => TitanVaultStatic.ChangeCryptomatorVaultPassword(vaultPath, oldPassword, newPassword));
        }

        /// <summary>
        /// Async wrapper for ChangeUvfAdminPassword static method
        /// </summary>
        public static Task ChangeUvfAdminPasswordAsync(string vaultPath, char[] oldPassword, char[] newPassword)
        {
            return Task.Run(() => TitanVaultStatic.ChangeUvfAdminPassword(vaultPath, oldPassword, newPassword));
        }

        /// <summary>
        /// Async wrapper for ChangeUvfUserPassword static method
        /// </summary>
        public static Task ChangeUvfUserPasswordAsync(string vaultPath, char[] adminPassword, string userId, char[] newUserPassword)
        {
            return Task.Run(() => TitanVaultStatic.ChangeUvfUserPassword(vaultPath, adminPassword, userId, newUserPassword));
        }

        /// <summary>
        /// Close the vault asynchronously
        /// </summary>
        public Task CloseVaultAsync()
        {
            return Task.Run(() => Close());
        }

        #endregion
    }
} 