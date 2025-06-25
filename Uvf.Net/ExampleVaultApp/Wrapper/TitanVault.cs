using System.Text;
using System.Runtime.InteropServices;

namespace ExampleVaultApp.Wrapper
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

        /// <summary>
        /// Create a directory in the vault
        /// </summary>
        public void CreateDirectory(string directoryPath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            unsafe
            {
                var directoryPathBytes = TitanVaultUtils.StringToUtf8Bytes(directoryPath);
                fixed (byte* directoryPathPtr = directoryPathBytes)
                {
                    var result = TitanVaultNativeMethods.CreateDirectory(_vaultHandle, directoryPathPtr, directoryPathBytes.Length);
                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to create directory: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        /// <summary>
        /// Check if a directory exists in the vault
        /// </summary>
        public bool DirectoryExists(string directoryPath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            unsafe
            {
                var directoryPathBytes = TitanVaultUtils.StringToUtf8Bytes(directoryPath);
                fixed (byte* directoryPathPtr = directoryPathBytes)
                {
                    var result = TitanVaultNativeMethods.DirectoryExists(_vaultHandle, directoryPathPtr, directoryPathBytes.Length);
                    return result == 1;
                }
            }
        }

        /// <summary>
        /// Delete a directory from the vault
        /// </summary>
        public void DeleteDirectory(string directoryPath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            unsafe
            {
                var directoryPathBytes = TitanVaultUtils.StringToUtf8Bytes(directoryPath);
                fixed (byte* directoryPathPtr = directoryPathBytes)
                {
                    var result = TitanVaultNativeMethods.DeleteDirectory(_vaultHandle, directoryPathPtr, directoryPathBytes.Length);
                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to delete directory: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        /// <summary>
        /// Delete a file from the vault
        /// </summary>
        public void DeleteFile(string filePath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    var result = TitanVaultNativeMethods.DeleteFile(_vaultHandle, filePathPtr, filePathBytes.Length);
                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to delete file: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        #endregion

        #region Stream Operations

        /// <summary>
        /// Open a file for reading as a stream
        /// </summary>
        public TitanVaultStream OpenReadStream(string filePath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    var streamHandle = TitanVaultNativeMethods.OpenStream(_vaultHandle, filePathPtr, filePathBytes.Length, (int)FileAccess.Read);
                    if (streamHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to open stream for reading: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    return new TitanVaultStream(streamHandle, this, false);
                }
            }
        }

        /// <summary>
        /// Open a file for writing as a stream
        /// </summary>
        public TitanVaultStream OpenWriteStream(string filePath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    var streamHandle = TitanVaultNativeMethods.OpenStream(_vaultHandle, filePathPtr, filePathBytes.Length, (int)FileAccess.Write);
                    if (streamHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to open stream for writing: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    return new TitanVaultStream(streamHandle, this, true);
                }
            }
        }

        /// <summary>
        /// Open a file for reading and writing as a stream
        /// </summary>
        public TitanVaultStream OpenReadWriteStream(string filePath)
        {
            return OpenStream(filePath, FileAccess.ReadWrite);
        }

        /// <summary>
        /// Open a file as a stream with specified access
        /// </summary>
        public TitanVaultStream OpenStream(string filePath, FileAccess access)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                fixed (byte* filePathPtr = filePathBytes)
                {
                    var streamHandle = TitanVaultNativeMethods.OpenStream(_vaultHandle, filePathPtr, filePathBytes.Length, (int)access);
                    if (streamHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to open stream: {TitanVaultUtils.GetLastErrorString()}");
                    }

                    return new TitanVaultStream(streamHandle, this, access != FileAccess.Read);
                }
            }
        }

        #endregion

        #region Directory Operations

        /// <summary>
        /// List files and directories in a directory
        /// </summary>
        public string[] ListDirectory(string directoryPath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            unsafe
            {
                var directoryPathBytes = TitanVaultUtils.StringToUtf8Bytes(directoryPath);
                fixed (byte* directoryPathPtr = directoryPathBytes)
                {
                    // First call to get the required buffer size
                    int bufferSize = 0;
                    int entryCount = 0;
                    Console.WriteLine($"🔍 DEBUG TitanVault.ListDirectory '{directoryPath}': First call (bufferSize={bufferSize}, entryCount={entryCount})");
                    var result = TitanVaultNativeMethods.ListDirectory(_vaultHandle, directoryPathPtr, directoryPathBytes.Length, null, &bufferSize, &entryCount);
                    Console.WriteLine($"🔍 DEBUG TitanVault.ListDirectory '{directoryPath}': First call result={result}, bufferSize={bufferSize}, entryCount={entryCount}");

                    if (result == TitanVaultUtils.ReturnCodes.Success && entryCount == 0)
                    {
                        // Empty directory - return empty array
                        return new string[0];
                    }
                    else if (result == TitanVaultUtils.ReturnCodes.InsufficientBuffer && bufferSize > 0)
                    {
                        // Allocate buffer and get the list
                        var buffer = new byte[bufferSize];
                        int secondCallBufferSize = bufferSize; // Use a separate variable for the second call
                        int secondCallEntryCount = 0; // Use a separate variable for entry count
                        Console.WriteLine($"🔍 DEBUG TitanVault.ListDirectory '{directoryPath}': Second call (bufferSize={secondCallBufferSize}, entryCount={secondCallEntryCount})");
                        fixed (byte* bufferPtr = buffer)
                        {
                            result = TitanVaultNativeMethods.ListDirectory(_vaultHandle, directoryPathPtr, directoryPathBytes.Length, bufferPtr, &secondCallBufferSize, &secondCallEntryCount);
                            Console.WriteLine($"🔍 DEBUG TitanVault.ListDirectory '{directoryPath}': Second call result={result}, bufferSize={secondCallBufferSize}, entryCount={secondCallEntryCount}");
                            if (result == TitanVaultUtils.ReturnCodes.Success)
                            {
                                // Parse the buffer to get entry names
                                var entries = new string[secondCallEntryCount];
                                int offset = 0;
                                for (int i = 0; i < secondCallEntryCount; i++)
                                {
                                    int entryLength = BitConverter.ToInt32(buffer, offset);
                                    offset += 4;
                                    entries[i] = Encoding.UTF8.GetString(buffer, offset, entryLength);
                                    offset += entryLength;
                                }
                                return entries;
                            }
                        }
                    }

                    throw new InvalidOperationException($"Failed to list directory: {TitanVaultUtils.GetLastErrorString()}");
                }
            }
        }

        /// <summary>
        /// List files and directories with detailed information
        /// </summary>
        public FileObject[] ListDirectoryDetailed(string directoryPath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentNullException(nameof(directoryPath));

            var entries = ListDirectory(directoryPath);
            var fileObjects = new FileObject[entries.Length];

            for (int i = 0; i < entries.Length; i++)
            {
                string fullPath = directoryPath == "/" ? $"/{entries[i]}" : $"{directoryPath}/{entries[i]}";
                fileObjects[i] = CreateFileObjectWithMetadata(fullPath, entries[i], DirectoryExists(fullPath));
            }

            return fileObjects;
        }

        /// <summary>
        /// Create a FileObject with metadata for a given path
        /// </summary>
        private FileObject CreateFileObjectWithMetadata(string fullPath, string entryName, bool isDirectory)
        {
            try
            {
                long size = 0;
                DateTime lastModified = DateTime.UtcNow;

                if (!isDirectory)
                {
                    // For files, try to get the size
                    try
                    {
                        var data = ReadAllBytes(fullPath);
                        size = data.Length;
                    }
                    catch
                    {
                        // If we can't read the file, size remains 0
                    }
                }

                return new FileObject(fullPath)
                {
                    Name = entryName,
                    IsDirectory = isDirectory,
                    Size = size,
                    LastModified = lastModified
                };
            }
            catch
            {
                // If metadata retrieval fails, return basic info
                return new FileObject(fullPath)
                {
                    Name = entryName,
                    IsDirectory = isDirectory,
                    Size = 0,
                    LastModified = DateTime.UtcNow
                };
            }
        }

        #endregion

        #region Vault Management

        /// <summary>
        /// Ensure the vault is open and ready for operations
        /// </summary>
        internal void EnsureOpen()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TitanVault));
            if (_vaultHandle == IntPtr.Zero)
                throw new InvalidOperationException("Vault is not open");
        }

        /// <summary>
        /// Close the vault
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

        #endregion

        #region Additional File Operations

        /// <summary>
        /// Get file information for a specific file
        /// </summary>
        public FileObject GetFileInfo(string filePath)
        {
            EnsureOpen();
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            bool isDirectory = DirectoryExists(filePath);
            bool exists = isDirectory || FileExists(filePath);

            if (!exists)
                throw new FileNotFoundException($"File or directory not found: {filePath}");

            long size = 0;
            if (!isDirectory)
            {
                try
                {
                    var data = ReadAllBytes(filePath);
                    size = data.Length;
                }
                catch
                {
                    // If we can't read the file, size remains 0
                }
            }

            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
                fileName = filePath;

            return new FileObject(filePath)
            {
                Name = fileName,
                IsDirectory = isDirectory,
                Size = size,
                LastModified = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Move a file or directory to a new location
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
                var destinationPathBytes = TitanVaultUtils.StringToUtf8Bytes(destinationPath);

                fixed (byte* sourcePathPtr = sourcePathBytes)
                fixed (byte* destinationPathPtr = destinationPathBytes)
                {
                    var result = TitanVaultNativeMethods.MoveFile(_vaultHandle, sourcePathPtr, sourcePathBytes.Length, destinationPathPtr, destinationPathBytes.Length);
                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        throw new InvalidOperationException($"Failed to move file: {TitanVaultUtils.GetLastErrorString()}");
                    }
                }
            }
        }

        #endregion

        #region Text File Operations

        /// <summary>
        /// Read all text from a file
        /// </summary>
        public string ReadAllText(string filePath)
        {
            var data = ReadAllBytes(filePath);
            return Encoding.UTF8.GetString(data);
        }

        /// <summary>
        /// Read all text from a file with specified encoding
        /// </summary>
        public string ReadAllText(string filePath, Encoding encoding)
        {
            var data = ReadAllBytes(filePath);
            return encoding.GetString(data);
        }

        /// <summary>
        /// Write text to a file
        /// </summary>
        public void WriteAllText(string filePath, string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            WriteAllBytes(filePath, data);
        }

        /// <summary>
        /// Write text to a file with specified encoding
        /// </summary>
        public void WriteAllText(string filePath, string text, Encoding encoding)
        {
            var data = encoding.GetBytes(text);
            WriteAllBytes(filePath, data);
        }

        /// <summary>
        /// Append text to a file
        /// </summary>
        public void AppendAllText(string filePath, string text)
        {
            string existingText = "";
            if (FileExists(filePath))
            {
                existingText = ReadAllText(filePath);
            }
            WriteAllText(filePath, existingText + text);
        }

        /// <summary>
        /// Append text to a file with specified encoding
        /// </summary>
        public void AppendAllText(string filePath, string text, Encoding encoding)
        {
            string existingText = "";
            if (FileExists(filePath))
            {
                existingText = ReadAllText(filePath, encoding);
            }
            WriteAllText(filePath, existingText + text, encoding);
        }

        #endregion

        #region Async Operations (Placeholder)

        /// <summary>
        /// Remove a user from the vault asynchronously (placeholder)
        /// </summary>
        public static Task RemoveUserFromVaultAsync(string vaultPath, char[] adminPassword, string userIdToRemove)
        {
            return Task.Run(() => TitanVaultStatic.RemoveUserFromVault(vaultPath, adminPassword, userIdToRemove));
        }

        /// <summary>
        /// Rotate vault keys asynchronously (placeholder)
        /// </summary>
        public static Task RotateVaultKeysAsync(string vaultPath, char[] adminPassword, TitanVaultUtils.VaultFormat vaultFormat)
        {
            return Task.Run(() => TitanVaultStatic.RotateVaultKeys(vaultPath, adminPassword, vaultFormat));
        }

        /// <summary>
        /// Backup vault files asynchronously (placeholder)
        /// </summary>
        public static Task BackupVaultFilesAsync(string vaultPath, string backupPath, bool overwriteExisting = false)
        {
            return Task.Run(() => TitanVaultStatic.BackupVaultFiles(vaultPath, backupPath, overwriteExisting));
        }

        /// <summary>
        /// Change Cryptomator vault password asynchronously (placeholder)
        /// </summary>
        public static Task ChangeCryptomatorVaultPasswordAsync(string vaultPath, char[] oldPassword, char[] newPassword)
        {
            return Task.Run(() => TitanVaultStatic.ChangeCryptomatorVaultPassword(vaultPath, oldPassword, newPassword));
        }

        /// <summary>
        /// Change UVF admin password asynchronously (placeholder)
        /// </summary>
        public static Task ChangeUvfAdminPasswordAsync(string vaultPath, char[] oldPassword, char[] newPassword)
        {
            return Task.Run(() => TitanVaultStatic.ChangeUvfAdminPassword(vaultPath, oldPassword, newPassword));
        }

        /// <summary>
        /// Change UVF user password asynchronously (placeholder)
        /// </summary>
        public static Task ChangeUvfUserPasswordAsync(string vaultPath, char[] adminPassword, string userId, char[] newUserPassword)
        {
            return Task.Run(() => TitanVaultStatic.ChangeUvfUserPassword(vaultPath, adminPassword, userId, newUserPassword));
        }

        /// <summary>
        /// Close vault asynchronously
        /// </summary>
        public Task CloseVaultAsync()
        {
            return Task.Run(() => Close());
        }

        #endregion
    }
} 