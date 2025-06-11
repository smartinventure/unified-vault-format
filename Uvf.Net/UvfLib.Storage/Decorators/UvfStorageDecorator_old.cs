using StorageLib.Abstractions;
using UvfLib.Core.Api;
using UvfLib.Vault;

namespace UvfLib.Storage.Decorators
{
    /*
    /// <summary>
    /// UVF Storage Decorator - implements IStorage for UVF vault operations.
    /// 
    /// For UVF format:
    /// - Simple encrypted filenames approach
    /// - Files stored with encrypted names + .uvf extension
    /// - Directory structure mirrors virtual structure but with encrypted names
    /// </summary>
    public class UvfStorageDecorator : CryptorStorageDecoratorBase
    {
        private readonly bool _encryptFilenames;

        public UvfStorageDecorator(
            IStorage underlyingStorage, 
            VaultHandler vault, 
            bool encryptFilenames = true,
            string? vaultBasePath = null)
            : base(underlyingStorage, vault, vaultBasePath ?? underlyingStorage.BaseFolderOrContainer)
        {
            _encryptFilenames = encryptFilenames;
            
            if (!_vault.IsUvfFormat())
            {
                throw new ArgumentException("VaultHandler must be configured for UVF format", nameof(vault));
            }
        }

        #region Format-Specific Path Translation

        /// <summary>
        /// Translates a virtual UVF path to the physical encrypted file path.
        /// 
        /// For UVF:
        /// 1. Virtual: /myFolder/file.txt
        /// 2. Split path into directory and filename
        /// 3. Encrypt directory names for directory structure
        /// 4. Encrypt filename and add .uvf extension
        /// 5. Physical: /vault/encrypted_myFolder/encrypted_file.txt.uvf
        /// </summary>
        protected override async Task<string> TranslateVirtualToPhysicalAsync(string virtualPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/")
            {
                throw new ArgumentException("Invalid virtual path", nameof(virtualPath));
            }

            if (!_encryptFilenames)
            {
                // Simple mode: just use path directly with .uvf extension for files
                string simplePath = Path.Combine(_vaultBasePath, virtualPath.TrimStart('/'));
                if (!Path.HasExtension(virtualPath))
                {
                    return simplePath; // Directory
                }
                return simplePath + ".uvf"; // File
            }

            // TODO: Implement full UVF encrypted path translation
            // This requires:
            // 1. Split path into directory parts and filename
            // 2. Encrypt each directory name using appropriate DirectoryMetadata
            // 3. Encrypt filename and add .uvf extension
            // 4. Return: /vault/encrypted_dir1/encrypted_dir2/encrypted_file.txt.uvf
            throw new NotImplementedException("Full UVF encrypted path translation not yet implemented");
        }

        #endregion

        #region Core IStorage Methods

        public Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            return _underlyingStorage.InitializeAsync(connectionString, baseFolderOrContainer, cancellationToken);
        }

        /// <summary>
        /// THE KEY METHOD: Creates encrypted directory structure following Program.cs patterns
        /// </summary>
        public async Task CreateDirectoryAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            if (!_encryptFilenames)
            {
                // Simple mode: just create directory with .uvf extension for files
                string storagePath = Path.Combine(_vaultBasePath, virtualPath.TrimStart('/'));
                await _underlyingStorage.CreateDirectoryAsync(storagePath, cancellationToken);
                return;
            }

            // Encrypted mode: Follow EXACT Program.cs ProcessDirectory pattern
            await CreateEncryptedDirectoryStructure(virtualPath, cancellationToken);
        }

        /// <summary>
        /// Creates the complex directory structure exactly like Program.cs ProcessDirectory
        /// </summary>
        private async Task CreateEncryptedDirectoryStructure(string virtualPath, CancellationToken cancellationToken)
        {
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            UvfLib.Core.Api.DirectoryMetadata currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string currentDirPhysicalVaultPath = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());

            // Ensure root exists
            await _underlyingStorage.CreateDirectoryAsync(currentDirPhysicalVaultPath, cancellationToken);

            foreach (string dirName in pathParts)
            {
                // 1. Create new directory metadata (UUID)
                UvfLib.Core.Api.DirectoryMetadata subDirMetadata = _vault.CreateNewDirectoryMetadata();
                
                // 2. Encrypt directory name
                string encryptedSubDirName = _vault.EncryptFilename(dirName, currentDirMetadata);

                if (_vault.IsCryptomatorV8())
                {
                    // CRYPTOMATOR V8 PATTERN (from Program.cs)
                    
                    // 3a. Create reference directory
                    string subDirPhysicalVaultPath = Path.Combine(currentDirPhysicalVaultPath, encryptedSubDirName);
                    await _underlyingStorage.CreateDirectoryAsync(subDirPhysicalVaultPath, cancellationToken);

                    // 4a. Create dir.c9r file (plaintext UUID)
                    byte[] decodedDirIdBytes = Convert.FromBase64String(subDirMetadata.DirId);
                    string rawUuidString = System.Text.Encoding.ASCII.GetString(decodedDirIdBytes);
                    string dirFilePath = Path.Combine(subDirPhysicalVaultPath, "dir.c9r");
                    await File.WriteAllTextAsync(dirFilePath, rawUuidString, cancellationToken);

                    // 5a. Create actual content directory
                    string actualContentPath = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(subDirMetadata));
                    await _underlyingStorage.CreateDirectoryAsync(actualContentPath, cancellationToken);

                    // 6a. Create dirid.c9r in content directory (encrypted own UUID)
                    string diridFilePath = Path.Combine(actualContentPath, "dirid.c9r");
                    using (FileStream diridStream = File.Create(diridFilePath))
                    using (Stream encryptingStream = _vault.GetEncryptingStream(diridStream))
                    {
                        byte[] dirIdBytes = System.Text.Encoding.ASCII.GetBytes(rawUuidString);
                        await encryptingStream.WriteAsync(dirIdBytes, 0, dirIdBytes.Length, cancellationToken);
                    }

                    currentDirPhysicalVaultPath = subDirPhysicalVaultPath;
                }
                else
                {
                    // UVF PATTERN (from Program.cs)
                    
                    // 3b. Create reference directory  
                    string subDirPhysicalVaultPath = Path.Combine(currentDirPhysicalVaultPath, encryptedSubDirName);
                    await _underlyingStorage.CreateDirectoryAsync(subDirPhysicalVaultPath, cancellationToken);

                    // 4b. Create actual content directory
                    string actualContentPath = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(subDirMetadata));
                    await _underlyingStorage.CreateDirectoryAsync(actualContentPath, cancellationToken);

                    // 5b. Save encrypted UVF metadata in reference directory
                    byte[] encryptedMetadata = _vault.EncryptDirectoryMetadata(subDirMetadata);
                    string dirUvfPath = Path.Combine(subDirPhysicalVaultPath, _vault.GetDirectoryMetadataFilename());
                    await File.WriteAllBytesAsync(dirUvfPath, encryptedMetadata, cancellationToken);

                    currentDirPhysicalVaultPath = subDirPhysicalVaultPath;
                }

                currentDirMetadata = subDirMetadata;
            }
        }

        public async Task<IntPtr> OpenAsync(string virtualPath, OpenFlags flags, CancellationToken cancellationToken = default)
        {
            // Translate virtual path to actual storage path
            string storagePath = await TranslateToStoragePathAsync(virtualPath);
            
            // Ensure parent directory exists if creating file
            if (FuseFlags.HasFlag(flags, OpenFlags.Create))
            {
                string? parentDir = Path.GetDirectoryName(virtualPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    await CreateDirectoryAsync(parentDir, cancellationToken);
                }
            }

            // Open underlying file
            IntPtr underlyingHandle = await _underlyingStorage.OpenAsync(storagePath, flags, cancellationToken);

            // Create vault file handle for encryption/decryption
            var vaultHandle = new VaultFileHandle(
                virtualPath, storagePath, underlyingHandle, _vault.FileContentCryptor, _underlyingStorage, flags, _encryptFilenames);

            IntPtr vaultHandlePtr = vaultHandle.CreateContext();
            lock (_handleLock)
            {
                _openHandles[vaultHandlePtr] = vaultHandle;
            }

            return vaultHandlePtr;
        }

        public async Task CloseAsync(IntPtr fileHandle, CancellationToken cancellationToken = default)
        {
            VaultFileHandle? handle = null;
            lock (_handleLock)
            {
                if (_openHandles.TryGetValue(fileHandle, out handle))
                {
                    _openHandles.Remove(fileHandle);
                }
            }

            if (handle != null)
            {
                await handle.CloseAsync(cancellationToken);
                handle.Dispose();
            }
        }

        public async Task ReadAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            var handle = GetVaultHandle(fileHandle);
            await handle.ReadAsync(offset, size, buffer, cancellationToken);
        }

        public async Task WriteAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            var handle = GetVaultHandle(fileHandle);
            await handle.WriteAsync(offset, size, buffer, cancellationToken);
        }

        #endregion

        #region File Operations

        public async Task<bool> FileExistsAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            try
            {
                string storagePath = await TranslateToStoragePathAsync(virtualPath);
                return await _underlyingStorage.FileExistsAsync(storagePath, cancellationToken);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> DirectoryExistsAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            try
            {
                string directoryPath = await TranslateToDirectoryPathAsync(virtualPath);
                return await _underlyingStorage.DirectoryExistsAsync(directoryPath, cancellationToken);
            }
            catch
            {
                return false;
            }
        }

        public async Task DeleteAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            string storagePath = await TranslateToStoragePathAsync(virtualPath);
            await _underlyingStorage.DeleteAsync(storagePath, cancellationToken);
        }

        public async Task DeleteDirectoryAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            string directoryPath = await TranslateToDirectoryPathAsync(virtualPath);
            await _underlyingStorage.DeleteDirectoryAsync(directoryPath, cancellationToken);
        }

        #endregion

        #region Path Translation (Real Implementation)

        private async Task<string> TranslateToStoragePathAsync(string virtualPath)
        {
            if (!_encryptFilenames)
            {
                // Unencrypted mode: simple .uvf appending
                return Path.Combine(_vaultBasePath, virtualPath.TrimStart('/') + GetFileExtension());
            }

            // Encrypted mode: follow Program.cs path resolution
            return await TranslateEncryptedPathAsync(virtualPath);
        }

        private async Task<string> TranslateToDirectoryPathAsync(string virtualPath)
        {
            if (!_encryptFilenames)
            {
                return Path.Combine(_vaultBasePath, virtualPath.TrimStart('/'));
            }

            // For encrypted mode, directories are complex - need to traverse hierarchy
            return await TranslateEncryptedDirectoryPathAsync(virtualPath);
        }

        private async Task<string> TranslateEncryptedPathAsync(string virtualPath)
        {
            // This follows the exact Program.cs pattern for file path translation
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            UvfLib.Core.Api.DirectoryMetadata currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string currentContentPath = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(currentDirMetadata));

            // Navigate to parent directory
            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                string dirName = pathParts[i];
                currentDirMetadata = await FindSubdirectoryMetadataAsync(currentContentPath, dirName, currentDirMetadata);
                currentContentPath = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(currentDirMetadata));
            }

            // Encrypt final filename
            string fileName = pathParts[pathParts.Length - 1];
            string encryptedFileName = _vault.EncryptFilename(fileName, currentDirMetadata);
            
            return Path.Combine(currentContentPath, encryptedFileName + GetFileExtension());
        }

        private async Task<string> TranslateEncryptedDirectoryPathAsync(string virtualPath)
        {
            // Similar to file path but for directories
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            UvfLib.Core.Api.DirectoryMetadata currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string currentDirPhysicalVaultPath = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());

            foreach (string dirName in pathParts)
            {
                string encryptedSubDirName = _vault.EncryptFilename(dirName, currentDirMetadata);
                currentDirPhysicalVaultPath = Path.Combine(currentDirPhysicalVaultPath, encryptedSubDirName);
                
                // Load subdirectory metadata
                currentDirMetadata = await LoadSubdirectoryMetadataAsync(currentDirPhysicalVaultPath);
            }

            return currentDirPhysicalVaultPath;
        }

        private async Task<UvfLib.Core.Api.DirectoryMetadata> FindSubdirectoryMetadataAsync(string parentContentPath, string subdirName, UvfLib.Core.Api.DirectoryMetadata parentMetadata)
        {
            // Implementation depends on vault format
            if (_vault.IsCryptomatorV8())
            {
                // For Cryptomator: read dir.c9r files
                throw new NotImplementedException("Cryptomator directory metadata lookup needs implementation");
            }
            else
            {
                // For UVF: read dir.uvf files
                string encryptedSubdirName = _vault.EncryptFilename(subdirName, parentMetadata);
                string subdirMetadataPath = Path.Combine(parentContentPath, encryptedSubdirName);
                string dirUvfPath = Path.Combine(subdirMetadataPath, _vault.GetDirectoryMetadataFilename());
                
                if (await _underlyingStorage.FileExistsAsync(dirUvfPath))
                {
                    byte[] encryptedMetadata = await File.ReadAllBytesAsync(dirUvfPath);
                    return _vault.DecryptDirectoryMetadata(encryptedMetadata);
                }
                
                throw new DirectoryNotFoundException($"Directory metadata not found for {subdirName}");
            }
        }

        private async Task<UvfLib.Core.Api.DirectoryMetadata> LoadSubdirectoryMetadataAsync(string subdirPath)
        {
            if (_vault.IsCryptomatorV8())
            {
                string dirC9rPath = Path.Combine(subdirPath, "dir.c9r");
                if (await _underlyingStorage.FileExistsAsync(dirC9rPath))
                {
                    string uuidString = await File.ReadAllTextAsync(dirC9rPath);
                    return _vault.CreateCryptomatorV8DirectoryMetadataFromUuid(uuidString.Trim());
                }
            }
            else
            {
                string dirUvfPath = Path.Combine(subdirPath, _vault.GetDirectoryMetadataFilename());
                if (await _underlyingStorage.FileExistsAsync(dirUvfPath))
                {
                    byte[] encryptedMetadata = await File.ReadAllBytesAsync(dirUvfPath);
                    return _vault.DecryptDirectoryMetadata(encryptedMetadata);
                }
            }

            throw new DirectoryNotFoundException($"Directory metadata not found at {subdirPath}");
        }

        private string GetFileExtension()
        {
            return _vault.IsCryptomatorV8() ? ".c9r" : ".uvf";
        }

        private string TranslateFromStoragePathToVirtual(string storagePath)
        {
            if (!storagePath.StartsWith(_vaultBasePath))
                return storagePath;

            string relativePath = storagePath.Substring(_vaultBasePath.Length).TrimStart(Path.DirectorySeparatorChar, '/');
            
            if (!_encryptFilenames)
            {
                // Remove .uvf/.c9r extension from files
                string extension = GetFileExtension();
                if (relativePath.EndsWith(extension))
                {
                    relativePath = relativePath.Substring(0, relativePath.Length - extension.Length);
                }
            }

            return "/" + relativePath.Replace('\\', '/');
        }

        private async Task<IEnumerable<string>> GetEncryptedFilesAsync(string virtualDirectoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken)
        {
            var files = new List<string>();
            
            // Get the content directory for this virtual directory
            string contentDirectory = await TranslateToDirectoryContentPathAsync(virtualDirectoryPath);
            
            // List files in the content directory
            var encryptedFiles = await _underlyingStorage.GetFilesAsync(contentDirectory, "*" + GetFileExtension(), SearchOption.TopDirectoryOnly, cancellationToken);
            
            // Decrypt filenames and check against search pattern
            var directoryMetadata = await GetDirectoryMetadataAsync(virtualDirectoryPath);
            
            foreach (string encryptedFile in encryptedFiles)
            {
                try
                {
                    string encryptedFileName = Path.GetFileNameWithoutExtension(encryptedFile);
                    string decryptedFileName = _vault.DecryptFilename(encryptedFileName, directoryMetadata);
                    
                    // Check if matches search pattern
                    if (MatchesPattern(decryptedFileName, searchPattern))
                    {
                        string virtualFilePath = Path.Combine(virtualDirectoryPath, decryptedFileName).Replace('\\', '/');
                        files.Add(virtualFilePath);
                    }
                }
                catch (Exception)
                {
                    // Skip files that can't be decrypted
                    continue;
                }
            }
            
            // Handle recursive search if needed
            if (searchOption == SearchOption.AllDirectories)
            {
                var subdirectories = await GetEncryptedDirectoriesAsync(virtualDirectoryPath, "*", SearchOption.TopDirectoryOnly, cancellationToken);
                foreach (string subdir in subdirectories)
                {
                    var subFiles = await GetEncryptedFilesAsync(subdir, searchPattern, searchOption, cancellationToken);
                    files.AddRange(subFiles);
                }
            }
            
            return files;
        }

        private async Task<IEnumerable<string>> GetEncryptedDirectoriesAsync(string virtualDirectoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken)
        {
            var directories = new List<string>();
            
            // Get the reference directory for this virtual directory
            string referenceDirectory = await TranslateToDirectoryPathAsync(virtualDirectoryPath);
            
            // List subdirectories in the reference directory
            var encryptedDirs = await _underlyingStorage.GetDirectoriesAsync(referenceDirectory, "*", SearchOption.TopDirectoryOnly, cancellationToken);
            
            // Decrypt directory names
            var directoryMetadata = await GetDirectoryMetadataAsync(virtualDirectoryPath);
            
            foreach (string encryptedDir in encryptedDirs)
            {
                try
                {
                    string encryptedDirName = Path.GetFileName(encryptedDir);
                    string decryptedDirName = _vault.DecryptFilename(encryptedDirName, directoryMetadata);
                    
                    // Check if matches search pattern
                    if (MatchesPattern(decryptedDirName, searchPattern))
                    {
                        string virtualDirPath = Path.Combine(virtualDirectoryPath, decryptedDirName).Replace('\\', '/');
                        directories.Add(virtualDirPath);
                    }
                }
                catch (Exception)
                {
                    // Skip directories that can't be decrypted
                    continue;
                }
            }
            
            // Handle recursive search if needed
            if (searchOption == SearchOption.AllDirectories)
            {
                var currentDirs = new List<string>(directories);
                foreach (string subdir in currentDirs)
                {
                    var subDirs = await GetEncryptedDirectoriesAsync(subdir, searchPattern, searchOption, cancellationToken);
                    directories.AddRange(subDirs);
                }
            }
            
            return directories;
        }

        private async Task<IEnumerable<FileObject>> EnumerateUnencryptedAsync(string virtualPath, bool readOnly, CancellationToken cancellationToken)
        {
            var fileObjects = new List<FileObject>();
            string storagePath = Path.Combine(_vaultBasePath, virtualPath.TrimStart('/'));
            
            // Enumerate files and directories from underlying storage
            var entries = await _underlyingStorage.EnumerateFilesAndDirectoriesAsync(storagePath, readOnly, cancellationToken);
            
            foreach (var entry in entries)
            {
                try
                {
                    var fileObject = new FileObject(TranslateFromStoragePathToVirtual(entry.RealPath))
                    {
                        IsDirectory = entry.IsDirectory,
                        RealPath = TranslateFromStoragePathToVirtual(entry.RealPath),
                        VirtualPath = TranslateFromStoragePathToVirtual(entry.RealPath),
                        Size = entry.Size,
                        CreationTime = entry.CreationTime,
                        LastModified = entry.LastModified,
                        LastAccessTime = entry.LastAccessTime,
                        SC = this
                    };

                    if (!entry.IsDirectory)
                    {
                        // Remove .uvf extension from filename display
                        string extension = GetFileExtension();
                        string fileName = Path.GetFileName(entry.RealPath);
                        if (fileName.EndsWith(extension))
                        {
                            fileName = fileName.Substring(0, fileName.Length - extension.Length);
                        }
                        fileObject.Filename = fileName;
                    }
                    else
                    {
                        fileObject.Filename = Path.GetFileName(entry.RealPath);
                    }

                    fileObjects.Add(fileObject);
                }
                catch (Exception)
                {
                    // Skip entries that can't be processed
                    continue;
                }
            }
            
            return fileObjects;
        }

        private async Task<IEnumerable<FileObject>> EnumerateEncryptedAsync(string virtualPath, bool readOnly, CancellationToken cancellationToken)
        {
            var fileObjects = new List<FileObject>();
            
            try
            {
                // Get directory metadata
                var directoryMetadata = await GetDirectoryMetadataAsync(virtualPath);
                
                // Get reference directory (for subdirectories)
                string referenceDirectory = await TranslateToDirectoryPathAsync(virtualPath);
                
                // Get content directory (for files)
                string contentDirectory = await TranslateToDirectoryContentPathAsync(virtualPath);
                
                // Enumerate subdirectories
                if (await _underlyingStorage.DirectoryExistsAsync(referenceDirectory))
                {
                    var encryptedDirs = await _underlyingStorage.GetDirectoriesAsync(referenceDirectory, "*", SearchOption.TopDirectoryOnly, cancellationToken);
                    foreach (string encryptedDir in encryptedDirs)
                    {
                        try
                        {
                            string encryptedDirName = Path.GetFileName(encryptedDir);
                            string decryptedDirName = _vault.DecryptFilename(encryptedDirName, directoryMetadata);
                            
                            var dirObject = new FileObject(Path.Combine(virtualPath, decryptedDirName).Replace('\\', '/'))
                            {
                                IsDirectory = true,
                                Filename = decryptedDirName,
                                RealPath = Path.Combine(virtualPath, decryptedDirName).Replace('\\', '/'),
                                VirtualPath = Path.Combine(virtualPath, decryptedDirName).Replace('\\', '/'),
                                Size = 0,
                                SC = this
                            };
                            
                            // Try to get directory timestamps
                            try
                            {
                                var dirInfo = await _underlyingStorage.GetFileInfoAsync(encryptedDir);
                                dirObject.CreationTime = dirInfo.CreationTime;
                                dirObject.LastModified = dirInfo.LastModified;
                                dirObject.LastAccessTime = dirInfo.LastAccessTime;
                            }
                            catch
                            {
                                // Use current time if can't get timestamps
                                dirObject.CreationTime = DateTime.Now;
                                dirObject.LastModified = DateTime.Now;
                                dirObject.LastAccessTime = DateTime.Now;
                            }
                            
                            fileObjects.Add(dirObject);
                        }
                        catch (Exception)
                        {
                            // Skip directories that can't be decrypted
                            continue;
                        }
                    }
                }
                
                // Enumerate files
                if (await _underlyingStorage.DirectoryExistsAsync(contentDirectory))
                {
                    var encryptedFiles = await _underlyingStorage.GetFilesAsync(contentDirectory, "*" + GetFileExtension(), SearchOption.TopDirectoryOnly, cancellationToken);
                    foreach (string encryptedFile in encryptedFiles)
                    {
                        try
                        {
                            string encryptedFileName = Path.GetFileNameWithoutExtension(encryptedFile);
                            string decryptedFileName = _vault.DecryptFilename(encryptedFileName, directoryMetadata);
                            
                            var fileInfo = await _underlyingStorage.GetFileInfoAsync(encryptedFile);
                            
                            var fileObject = new FileObject(Path.Combine(virtualPath, decryptedFileName).Replace('\\', '/'))
                            {
                                IsDirectory = false,
                                Filename = decryptedFileName,
                                RealPath = Path.Combine(virtualPath, decryptedFileName).Replace('\\', '/'),
                                VirtualPath = Path.Combine(virtualPath, decryptedFileName).Replace('\\', '/'),
                                Size = UvfLib.Vault.VaultHandler.CalculateExpectedDecryptedSize(fileInfo.Size), // Decrypt size
                                CreationTime = fileInfo.CreationTime,
                                LastModified = fileInfo.LastModified,
                                LastAccessTime = fileInfo.LastAccessTime,
                                SC = this
                            };
                            
                            fileObjects.Add(fileObject);
                        }
                        catch (Exception)
                        {
                            // Skip files that can't be decrypted
                            continue;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Return empty list if directory can't be read
                return new List<FileObject>();
            }
            
            return fileObjects;
        }

        private async Task<string> TranslateToDirectoryContentPathAsync(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/")
            {
                // Root directory
                return Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(_vault.GetRootDirectoryMetadata()));
            }

            // For subdirectories, get their content path
            var directoryMetadata = await GetDirectoryMetadataAsync(virtualPath);
            return Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(directoryMetadata));
        }

        private async Task<UvfLib.Core.Api.DirectoryMetadata> GetDirectoryMetadataAsync(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/")
            {
                return _vault.GetRootDirectoryMetadata();
            }

            // Navigate to get the directory metadata
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            UvfLib.Core.Api.DirectoryMetadata currentMetadata = _vault.GetRootDirectoryMetadata();
            
            foreach (string pathPart in pathParts)
            {
                // Find subdirectory metadata
                currentMetadata = await FindSubdirectoryMetadataAsync(
                    Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(currentMetadata)), 
                    pathPart, 
                    currentMetadata);
            }
            
            return currentMetadata;
        }

        private static bool MatchesPattern(string name, string pattern)
        {
            if (pattern == "*") return true;
            if (string.IsNullOrEmpty(pattern)) return string.IsNullOrEmpty(name);
            
            // Simple pattern matching - could be enhanced with regex
            if (pattern.Contains("*"))
            {
                string[] parts = pattern.Split('*');
                if (parts.Length == 2)
                {
                    return name.StartsWith(parts[0]) && name.EndsWith(parts[1]);
                }
            }
            
            return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Helper Methods

        private VaultFileHandle GetVaultHandle(IntPtr fileHandle)
        {
            lock (_handleLock)
            {
                if (_openHandles.TryGetValue(fileHandle, out var handle))
                {
                    return handle;
                }
            }
            throw new InvalidOperationException("Invalid file handle");
        }

        #endregion

        #region Other IStorage Methods (Delegated)

        public Task ReadAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Use handle-based operations for encrypted files");

        public Task WriteAsync(string path, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Use handle-based operations for encrypted files");

        /// <summary>
        /// Get files in a directory with search pattern and options - following LocalStorage pattern
        /// </summary>
        public async Task<IEnumerable<string>> GetFilesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

            if (!_encryptFilenames)
            {
                // Unencrypted mode: delegate to underlying storage with path translation
                string storagePath = Path.Combine(_vaultBasePath, directoryPath.TrimStart('/'));
                var files = await _underlyingStorage.GetFilesAsync(storagePath, searchPattern, searchOption, cancellationToken);
                // Remove .uvf extensions and convert back to virtual paths
                return files.Select(f => TranslateFromStoragePathToVirtual(f));
            }

            // Encrypted mode: traverse vault structure to find files
            return await GetEncryptedFilesAsync(directoryPath, searchPattern, searchOption, cancellationToken);
        }

        /// <summary>
        /// Get all files in a directory - following LocalStorage pattern
        /// </summary>
        public async Task<IEnumerable<string>> GetFilesAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return await GetFilesAsync(directoryPath, "*", SearchOption.TopDirectoryOnly, cancellationToken);
        }

        /// <summary>
        /// Get subdirectories with search pattern - following LocalStorage pattern
        /// </summary>
        public async Task<IEnumerable<string>> GetDirectoriesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

            if (!_encryptFilenames)
            {
                // Unencrypted mode: delegate to underlying storage
                string storagePath = Path.Combine(_vaultBasePath, directoryPath.TrimStart('/'));
                var directories = await _underlyingStorage.GetDirectoriesAsync(storagePath, searchPattern, searchOption, cancellationToken);
                return directories.Select(d => TranslateFromStoragePathToVirtual(d));
            }

            // Encrypted mode: traverse vault structure to find directories
            return await GetEncryptedDirectoriesAsync(directoryPath, searchPattern, searchOption, cancellationToken);
        }

        /// <summary>
        /// CRUCIAL: Enumerate files and directories - following LocalStorage pattern exactly
        /// </summary>
        public async Task<IEnumerable<FileObject>> EnumerateFilesAndDirectoriesAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(realPath))
                throw new ArgumentException("Path cannot be null or empty", nameof(realPath));

            var fileObjects = new List<FileObject>();

            try
            {
                if (!_encryptFilenames)
                {
                    // Unencrypted mode: enumerate underlying storage and decrypt content
                    return await EnumerateUnencryptedAsync(realPath, readOnly, cancellationToken);
                }
                else
                {
                    // Encrypted mode: decrypt directory structure and enumerate
                    return await EnumerateEncryptedAsync(realPath, readOnly, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash browsing
                throw new DirectoryNotFoundException($"Error enumerating vault directory {realPath}: {ex.Message}", ex);
            }
        }

        public Task<bool> TestReadAsync(CancellationToken cancellationToken = default) 
            => _underlyingStorage.TestReadAsync(cancellationToken);

        public Task<bool> TestWriteAsync(CancellationToken cancellationToken = default) 
            => _underlyingStorage.TestWriteAsync(cancellationToken);

        public Task<IEnumerable<string>> EnumerateFileSystemEntriesAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task ShutdownAsync(CancellationToken cancellationToken = default) 
            => _underlyingStorage.ShutdownAsync(cancellationToken);

        public Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default) 
            => _underlyingStorage.GetTotalCapacityAsync(cancellationToken);

        public Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default) 
            => _underlyingStorage.GetAvailableFreeSpaceAsync(cancellationToken);

        public Task CloseAllHandlesAsync(CancellationToken cancellationToken = default)
        {
            // Close all vault handles
            List<VaultFileHandle> handlesToClose;
            lock (_handleLock)
            {
                handlesToClose = new List<VaultFileHandle>(_openHandles.Values);
                _openHandles.Clear();
            }

            var tasks = handlesToClose.Select(h => h.CloseAsync()).ToArray();
            return Task.WhenAll(tasks);
        }

        public Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default)
            => Task.CompletedTask; // Not applicable for vault files

        public Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    CloseAllHandlesAsync().Wait();
                }
                catch { }

                _vault?.Dispose();
                _underlyingStorage?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Simplified vault file handle that uses the vault's streaming methods directly
    /// </summary>
    public class VaultFileHandleFixed : IDisposable
    {
        private readonly string _virtualPath;
        private readonly string _storagePath;
        private readonly IntPtr _underlyingHandle;
        private readonly UvfLib.Vault.VaultHandler _vault;
        private readonly IStorage _underlyingStorage;
        private bool _disposed;

        public VaultFileHandleFixed(string virtualPath, string storagePath, IntPtr underlyingHandle, UvfLib.Vault.VaultHandler vault, IStorage underlyingStorage)
        {
            _virtualPath = virtualPath;
            _storagePath = storagePath;
            _underlyingHandle = underlyingHandle;
            _vault = vault;
            _underlyingStorage = underlyingStorage;
        }

        public IntPtr CreateContext()
        {
            // Implementation needed - create GC handle
            throw new NotImplementedException("GC handle creation");
        }

        public async Task ReadAsync(long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            // Use vault's decrypting stream directly
            throw new NotImplementedException("Read with vault decrypting stream");
        }

        public async Task WriteAsync(long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            // Use vault's encrypting stream directly
            throw new NotImplementedException("Write with vault encrypting stream");
        }

        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            await _underlyingStorage.CloseAsync(_underlyingHandle, cancellationToken);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    CloseAsync().Wait();
                }
                catch { }
                _disposed = true;
            }
        }
    }
    */
} 