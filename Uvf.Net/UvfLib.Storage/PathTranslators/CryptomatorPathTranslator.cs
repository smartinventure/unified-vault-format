using StorageLib.Abstractions;
using UvfLib.Core.Api;
using UvfLib.Storage.Abstractions;
using UvfLib.Storage.Common;
using UvfLib.Vault;
using System.Runtime.InteropServices;
using DirectoryMetadata = UvfLib.Core.Api.DirectoryMetadata;
using UvfLib.Core.CryptomatorV8;
using System.Text;

namespace UvfLib.Storage.PathTranslators
{
    /// <summary>
    /// Path translator for Cryptomator V8 format.
    /// 
    /// Cryptomator V8 structure:
    /// - Reference directories: /vault/root/encrypted_dirname/
    /// - Dir files: /vault/root/encrypted_dirname/dir.c9r (contains UUID in plaintext)
    /// - Content directories: /vault/d/XX/YYYYYYYY/ (based on UUID)
    /// - Content files: /vault/d/XX/YYYYYYYY/encrypted_filename.c9r
    /// </summary>
    public class CryptomatorPathTranslator : IVaultPathTranslator
    {
        private readonly VaultHandler _vault;
        private readonly IStorage _underlyingStorage;
        private readonly string _vaultBasePath;
        private bool _disposed;

        public CryptomatorPathTranslator(VaultHandler vault, IStorage underlyingStorage, string? vaultBasePath)
        {
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _underlyingStorage = underlyingStorage ?? throw new ArgumentNullException(nameof(underlyingStorage));
            
            // vaultBasePath is required when LocalStorage is initialized with root path
            _vaultBasePath = vaultBasePath ?? throw new ArgumentNullException(nameof(vaultBasePath));
            
            if (!_vault.IsCryptomatorV8())
            {
                throw new ArgumentException("VaultHandler must be configured for Cryptomator V8 format", nameof(vault));
            }
        }

        #region IVaultPathTranslator Properties

        public UvfLib.Storage.Abstractions.VaultFormat Format => UvfLib.Storage.Abstractions.VaultFormat.CryptomatorV8;
        public bool IsEncryptionEnabled => true; // Cryptomator is always encrypted
        public string BaseStoragePath => _vaultBasePath;

        #endregion

        #region Path Translation

        public async Task<VaultPathResult> TranslateToStoragePathAsync(string virtualPath)
        {
            // Normalize the virtual path first
            virtualPath = PathNormalizer.NormalizeVirtualPath(virtualPath);
            
            if (virtualPath == PathNormalizer.VirtualRoot)
            {
                // Root directory - return the root directory path
                string rootDirPath = PathNormalizer.NormalizeVaultDirectoryPath(_vault.GetRootDirectoryPath());
                string rootStoragePath = PathNormalizer.CombineWithMountPoint(_vaultBasePath, rootDirPath);
                
                return new VaultPathResult
                {
                    StoragePath = rootStoragePath,
                    ContentDirectoryPath = rootStoragePath,
                    IsEncrypted = true,
                    RequiresDirectoryCreation = false
                };
            }

            // Split path into components
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            // Navigate through the directory hierarchy using REFERENCE directories
            DirectoryMetadata currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string rootDirPathNormalized = PathNormalizer.NormalizeVaultDirectoryPath(_vault.GetRootDirectoryPath());
            string currentReferenceDir = PathNormalizer.CombineWithMountPoint(_vaultBasePath, rootDirPathNormalized);

            // Process each directory part except the last (which might be a file)
            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                string dirName = pathParts[i];
                
                // Encrypt directory name using current directory's metadata
                string encryptedDirName = _vault.EncryptFilename(dirName, currentDirMetadata);
                
                // Check if name shortening is needed and get the actual directory name
                string actualDirectoryName;
                if (NameShorteningHelper.NeedsShortening(encryptedDirName))
                {
                    actualDirectoryName = NameShorteningHelper.CreateShortenedDirectoryName(encryptedDirName);
                }
                else
                {
                    actualDirectoryName = encryptedDirName;
                }
                
                // Move to the reference directory (this contains dir.c9r)
                currentReferenceDir = Path.Combine(currentReferenceDir, actualDirectoryName);
                
                // Load the subdirectory metadata from dir.c9r in the REFERENCE directory
                currentDirMetadata = await LoadDirectoryMetadataFromDirC9rAsync(currentReferenceDir, CancellationToken.None);
            }

            // Handle the final path component (could be file or directory)
            string finalName = pathParts[pathParts.Length - 1];
            string encryptedFinalName = _vault.EncryptFilename(finalName, currentDirMetadata);
            
            if (IsDirectory(virtualPath))
            {
                // Check if the final directory name needs shortening
                string actualFinalDirectoryName;
                if (NameShorteningHelper.NeedsShortening(encryptedFinalName))
                {
                    actualFinalDirectoryName = NameShorteningHelper.CreateShortenedDirectoryName(encryptedFinalName);
                }
                else
                {
                    actualFinalDirectoryName = encryptedFinalName;
                }
                
                // It's a directory - return the reference directory path
                return new VaultPathResult
                {
                    StoragePath = Path.Combine(currentReferenceDir, actualFinalDirectoryName),
                    ContentDirectoryPath = Path.Combine(currentReferenceDir, actualFinalDirectoryName),
                    EncryptedFilename = actualFinalDirectoryName,
                    ParentMetadata = currentDirMetadata,
                    IsEncrypted = true,
                    RequiresDirectoryCreation = true
                };
            }
            else
            {
                // It's a file - files are stored in the CONTENT directory, not the reference directory
                // The currentDirMetadata tells us which content directory to use
                string vaultDirectoryPath = _vault.GetDirectoryPath(currentDirMetadata);
                string normalizedVaultDirPath = PathNormalizer.NormalizeVaultDirectoryPath(vaultDirectoryPath);
                string contentDir = PathNormalizer.CombineWithMountPoint(_vaultBasePath, normalizedVaultDirPath);
                
                // The encryptedFinalName already includes the .c9r extension from VaultHandler.EncryptFilename
                // Check if the file name needs shortening
                string actualFileName;
                bool isShortened = false;
                
                if (NameShorteningHelper.NeedsShortening(encryptedFinalName))
                {
                    // For files, we create a shortened directory structure with contents.c9r
                    actualFileName = NameShorteningHelper.CreateShortenedDirectoryName(encryptedFinalName);
                    isShortened = true;
                }
                else
                {
                    actualFileName = encryptedFinalName;
                }
                
                return new VaultPathResult
                {
                    // For shortened files, StoragePath points to the .c9s directory, not the contents.c9r file
                    // The OpenWriteAsync/OpenReadAsync methods will handle accessing contents.c9r inside this directory
                    StoragePath = Path.Combine(contentDir, actualFileName),
                    ContentDirectoryPath = contentDir,
                    EncryptedFilename = actualFileName,
                    ParentMetadata = currentDirMetadata,
                    IsEncrypted = true,
                    RequiresDirectoryCreation = isShortened // Shortened files need directory creation
                };
            }
        }

        public async Task<string?> TranslateToVirtualPathAsync(string storagePath)
        {
            // This is complex for Cryptomator - would need to reverse the process
            // For now, return a placeholder implementation
            throw new NotImplementedException("Cryptomator physical-to-virtual translation not yet implemented");
        }

        #endregion

        #region Vault Format Methods

        public string GetEncryptedFileExtension()
        {
            return ".c9r";
        }

        public string GetMetadataFileName()
        {
            return "dir.c9r";
        }

        #endregion

        #region Helper Methods

        private bool IsDirectory(string virtualPath)
        {
            // Simple heuristic - directories typically don't have extensions
            // This might need refinement based on your specific use case
            return !Path.HasExtension(virtualPath);
        }

        /// <summary>
        /// Loads directory metadata from a dir.c9r file in a reference directory.
        /// Follows the exact pattern from Program.cs ProcessDirectory.
        /// </summary>
        public async Task<DirectoryMetadata> LoadDirectoryMetadataFromDirC9rAsync(string referenceDir, CancellationToken cancellationToken)
        {
            string dirC9rPath = Path.Combine(referenceDir, "dir.c9r");
            
            if (!await _underlyingStorage.FileExistsAsync(dirC9rPath, cancellationToken))
            {
                throw new DirectoryNotFoundException($"dir.c9r file not found at {dirC9rPath}");
            }

            // Read the UUID from dir.c9r (plaintext) using IStorage interface
            // Since dir.c9r is plaintext, we can read it directly through the underlying storage
            IntPtr fileHandle = await _underlyingStorage.OpenAsync(dirC9rPath, OpenFlags.ReadOnly, cancellationToken);
            try
            {
                // Get file size
                var fileInfo = await _underlyingStorage.GetFileInfoAsync(dirC9rPath, cancellationToken);
                long fileSize = fileInfo.Size;
                
                if (fileSize <= 0)
                {
                    throw new InvalidDataException($"dir.c9r file is empty at {dirC9rPath}");
                }
                
                // Read file content
                IntPtr dataPtr = Marshal.AllocHGlobal((int)fileSize);
                try
                {
                    await _underlyingStorage.ReadAsync(fileHandle, 0, fileSize, dataPtr, cancellationToken);
                    
                    // Copy to managed array
                    byte[] fileBytes = new byte[fileSize];
                    Marshal.Copy(dataPtr, fileBytes, 0, (int)fileSize);
                    
                    // Convert to string and trim
                    string uuidString = System.Text.Encoding.UTF8.GetString(fileBytes).Trim();
                    
                    // Create directory metadata from the UUID
                    return _vault.CreateCryptomatorV8DirectoryMetadataFromUuid(uuidString);
                }
                finally
                {
                    Marshal.FreeHGlobal(dataPtr);
                }
            }
            finally
            {
                await _underlyingStorage.CloseAsync(fileHandle, cancellationToken);
            }
        }

        /// <summary>
        /// Creates a new directory in the Cryptomator V8 structure.
        /// This follows the exact Program.cs CreateDirectory pattern.
        /// </summary>
        public async Task CreateDirectoryAsync(string virtualDirPath, CancellationToken cancellationToken = default)
        {
            // Normalize the virtual path
            virtualDirPath = PathNormalizer.NormalizeVirtualPath(virtualDirPath);
            
            if (virtualDirPath == PathNormalizer.VirtualRoot)
            {
                // Root directory should already exist
                return;
            }

            string[] pathParts = virtualDirPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            DirectoryMetadata currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string rootDirPathNormalized = PathNormalizer.NormalizeVaultDirectoryPath(_vault.GetRootDirectoryPath());
            string currentReferenceDir = PathNormalizer.CombineWithMountPoint(_vaultBasePath, rootDirPathNormalized);

            // Navigate through the path, creating directories as needed
            foreach (string dirName in pathParts)
            {
                // 1. Create new directory metadata (UUID)
                DirectoryMetadata subDirMetadata = _vault.CreateNewDirectoryMetadata();
                
                // 2. Encrypt directory name
                string encryptedSubDirName = _vault.EncryptFilename(dirName, currentDirMetadata);
                
                // 3. Check if name shortening is needed
                string actualDirectoryName;
                bool needsShortening = NameShorteningHelper.NeedsShortening(encryptedSubDirName);
                
                if (needsShortening)
                {
                    // Create shortened directory name
                    actualDirectoryName = NameShorteningHelper.CreateShortenedDirectoryName(encryptedSubDirName);
                }
                else
                {
                    actualDirectoryName = encryptedSubDirName;
                }
                
                // 4. Create reference directory
                string subDirReferenceDir = Path.Combine(currentReferenceDir, actualDirectoryName);
                await _underlyingStorage.CreateDirectoryAsync(subDirReferenceDir, cancellationToken);
                
                // 5. If name shortening was used, create the name.c9s file
                if (needsShortening)
                {
                    await CreateNameFileForShortenedDirectoryAsync(
                        currentReferenceDir, actualDirectoryName, encryptedSubDirName, cancellationToken);
                }
                
                // 4. Create dir.c9r file (plaintext UUID) using IStorage interface
                byte[] decodedDirIdBytes = Convert.FromBase64String(subDirMetadata.DirId);
                string rawUuidString = System.Text.Encoding.ASCII.GetString(decodedDirIdBytes);
                string dirC9rPath = Path.Combine(subDirReferenceDir, "dir.c9r");
                
                // Write dir.c9r file using IStorage interface for consistency
                byte[] uuidBytes = System.Text.Encoding.UTF8.GetBytes(rawUuidString);
                IntPtr dataPtr = Marshal.AllocHGlobal(uuidBytes.Length);
                try
                {
                    Marshal.Copy(uuidBytes, 0, dataPtr, uuidBytes.Length);
                    
                    // Use IStorage interface to create and write the file
                    IntPtr fileHandle = await _underlyingStorage.OpenAsync(dirC9rPath, 
                        OpenFlags.Create | OpenFlags.WriteOnly | OpenFlags.Truncate, cancellationToken);
                    try
                    {
                        await _underlyingStorage.WriteAsync(fileHandle, 0, uuidBytes.Length, dataPtr, cancellationToken);
                    }
                    finally
                    {
                        await _underlyingStorage.CloseAsync(fileHandle, cancellationToken);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(dataPtr);
                }
                
                // 5. Create actual content directory
                string vaultDirPath = _vault.GetDirectoryPath(subDirMetadata);
                // Use PathNormalizer for consistent path handling
                string normalizedVaultDirPath = PathNormalizer.NormalizeVaultDirectoryPath(vaultDirPath);
                string actualContentPath = PathNormalizer.CombineWithMountPoint(_vaultBasePath, normalizedVaultDirPath);
                await _underlyingStorage.CreateDirectoryAsync(actualContentPath, cancellationToken);
                
                // 6. Create dirid.c9r in content directory (encrypted own UUID)
                // CRITICAL: Root directory has empty dirid.c9r, subdirectories have their own UUID
                string diridFilePath = Path.Combine(actualContentPath, "dirid.c9r");
                
                // Ensure parent directory exists using IStorage
                string? diridParentDir = Path.GetDirectoryName(diridFilePath);
                if (!string.IsNullOrEmpty(diridParentDir) && !await _underlyingStorage.DirectoryExistsAsync(diridParentDir, cancellationToken))
                {
                    await _underlyingStorage.CreateDirectoryAsync(diridParentDir, cancellationToken);
                }

                // Check if this is effectively a root directory
                // Note: In our case, we're creating subdirectories under root, so they're never root
                bool isRootDirectory = false; // This method only creates subdirectories

                // Create dirid.c9r file using IStorage
                string actualDirIdToEncrypt;
                if (isRootDirectory)
                {
                    actualDirIdToEncrypt = ""; // Root directory ID is empty string for Cryptomator
                }
                else
                {
                    // For CryptomatorV8 subdirectories: dirid.c9r should contain raw UUID string (36 bytes)
                    if (string.IsNullOrEmpty(subDirMetadata.DirId))
                    {
                        actualDirIdToEncrypt = "";
                    }
                    else
                    {
                        // Decode Base64 DirId to get raw UUID string
                        byte[] dirIdDecodedBytes = Convert.FromBase64String(subDirMetadata.DirId);
                        actualDirIdToEncrypt = System.Text.Encoding.ASCII.GetString(dirIdDecodedBytes);
                    }
                }

                // Write dirid.c9r using IStorage - create encrypted file
                byte[] dirIdBytes = System.Text.Encoding.ASCII.GetBytes(actualDirIdToEncrypt);
                
                // Create encrypted content in memory first
                byte[] encryptedData;
                using (var memoryStream = new MemoryStream())
                {
                    using (var encryptingStream = _vault.GetEncryptingStream(memoryStream))
                    {
                        await encryptingStream.WriteAsync(dirIdBytes, 0, dirIdBytes.Length, cancellationToken);
                        await encryptingStream.FlushAsync(cancellationToken);
                    }
                    encryptedData = memoryStream.ToArray();
                }
                
                // Write the encrypted data to storage using OpenAsync
                var diridHandle = await _underlyingStorage.OpenAsync(diridFilePath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
                try
                {
                    // Write the encrypted data using Marshal to convert byte array to IntPtr
                    IntPtr diridDataPtr = Marshal.AllocHGlobal(encryptedData.Length);
                    try
                    {
                        Marshal.Copy(encryptedData, 0, diridDataPtr, encryptedData.Length);
                        await _underlyingStorage.WriteAsync(diridHandle, 0, encryptedData.Length, diridDataPtr, cancellationToken);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(diridDataPtr);
                    }
                }
                finally
                {
                    await _underlyingStorage.CloseAsync(diridHandle, cancellationToken);
                }
                
                // Move to the next level
                currentDirMetadata = subDirMetadata;
                currentReferenceDir = subDirReferenceDir;
            }
        }

        /// <summary>
        /// Deletes a directory in the Cryptomator V8 structure.
        /// This deletes both the content directory (recursively) and the reference directory.
        /// </summary>
        public async Task DeleteDirectoryAsync(string virtualDirPath, CancellationToken cancellationToken = default)
        {
            // Normalize the virtual path
            virtualDirPath = PathNormalizer.NormalizeVirtualPath(virtualDirPath);
            
            if (virtualDirPath == PathNormalizer.VirtualRoot)
            {
                throw new ArgumentException("Cannot delete root directory", nameof(virtualDirPath));
            }

            // Navigate to the directory and get its paths
            string[] pathParts = virtualDirPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            DirectoryMetadata currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string rootDirPathNormalized = PathNormalizer.NormalizeVaultDirectoryPath(_vault.GetRootDirectoryPath());
            string currentReferenceDir = PathNormalizer.CombineWithMountPoint(_vaultBasePath, rootDirPathNormalized);

            // Navigate through the path, but stop before the last part (the directory to delete)
            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                string dirName = pathParts[i];
                
                // Encrypt directory name using current directory's metadata
                string encryptedDirName = _vault.EncryptFilename(dirName, currentDirMetadata);
                
                // Move to the reference directory
                currentReferenceDir = Path.Combine(currentReferenceDir, encryptedDirName);
                
                // Load the subdirectory metadata from dir.c9r
                currentDirMetadata = await LoadDirectoryMetadataFromDirC9rAsync(currentReferenceDir, cancellationToken);
            }

            // Get the directory to delete
            string targetDirName = pathParts[pathParts.Length - 1];
            string encryptedTargetDirName = _vault.EncryptFilename(targetDirName, currentDirMetadata);
            
            // Reference directory path
            string targetReferenceDir = Path.Combine(currentReferenceDir, encryptedTargetDirName);
            
            // Get the target directory's metadata to find its content directory
            DirectoryMetadata targetDirMetadata = await LoadDirectoryMetadataFromDirC9rAsync(targetReferenceDir, cancellationToken);
            
            // Content directory path
            string targetVaultDirPath = _vault.GetDirectoryPath(targetDirMetadata);
            // Use PathNormalizer for consistent path handling
            string normalizedTargetVaultDirPath = PathNormalizer.NormalizeVaultDirectoryPath(targetVaultDirPath);
            string targetContentDir = PathNormalizer.CombineWithMountPoint(_vaultBasePath, normalizedTargetVaultDirPath);

            try
            {
                // Step 1: Delete content directory recursively (this contains all files and subdirectories)
                if (await _underlyingStorage.DirectoryExistsAsync(targetContentDir, cancellationToken))
                {
                    await _underlyingStorage.DeleteDirectoryAsync(targetContentDir, cancellationToken);
                }

                // Step 2: Delete reference directory (this contains dir.c9r file)
                if (await _underlyingStorage.DirectoryExistsAsync(targetReferenceDir, cancellationToken))
                {
                    await _underlyingStorage.DeleteDirectoryAsync(targetReferenceDir, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to delete directory {virtualDirPath}: {ex.Message}", ex);
            }
        }

        #endregion

        #region Name Shortening Support

        /// <summary>
        /// Creates the name.c9s file for a shortened directory containing the original encrypted filename.
        /// </summary>
        /// <param name="parentDirectoryPath">The parent directory path where the shortened directory is located</param>
        /// <param name="shortenedDirectoryName">The shortened directory name (e.g., "ABC123.c9s")</param>
        /// <param name="originalEncryptedFilename">The original long encrypted filename to store</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task CreateNameFileForShortenedDirectoryAsync(
            string parentDirectoryPath,
            string shortenedDirectoryName,
            string originalEncryptedFilename,
            CancellationToken cancellationToken)
        {
            // Get the path for the name.c9s file
            string nameFilePath = NameShorteningHelper.GetInflatedNameFilePath(shortenedDirectoryName);
            string fullNameFilePath = Path.Combine(parentDirectoryPath, nameFilePath);

            // Write the original filename to the name.c9s file
            byte[] filenameBytes = Encoding.UTF8.GetBytes(originalEncryptedFilename);
            
            IntPtr fileHandle = await _underlyingStorage.OpenAsync(fullNameFilePath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
            try
            {
                IntPtr dataPtr = Marshal.AllocHGlobal(filenameBytes.Length);
                try
                {
                    Marshal.Copy(filenameBytes, 0, dataPtr, filenameBytes.Length);
                    await _underlyingStorage.WriteAsync(fileHandle, 0, filenameBytes.Length, dataPtr, cancellationToken);
                }
                finally
                {
                    Marshal.FreeHGlobal(dataPtr);
                }
            }
            finally
            {
                await _underlyingStorage.CloseAsync(fileHandle, cancellationToken);
            }
        }

        /// <summary>
        /// Creates the shortened file structure for files that exceed the length threshold.
        /// This creates a .c9s directory with name.c9s and prepares for contents.c9r.
        /// </summary>
        /// <param name="contentDirectoryPath">The content directory path</param>
        /// <param name="shortenedFileName">The shortened file name (e.g., "ABC123.c9s")</param>
        /// <param name="originalEncryptedFilename">The original long encrypted filename</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task CreateShortenedFileStructureAsync(
            string contentDirectoryPath,
            string shortenedFileName,
            string originalEncryptedFilename,
            CancellationToken cancellationToken)
        {
            string shortenedDirectoryPath = Path.Combine(contentDirectoryPath, shortenedFileName);
            
            // Create the .c9s directory
            await _underlyingStorage.CreateDirectoryAsync(shortenedDirectoryPath, cancellationToken);
            
            // Create the name.c9s file with the original filename
            await CreateNameFileForShortenedDirectoryAsync(contentDirectoryPath, shortenedFileName, originalEncryptedFilename, cancellationToken);
            
            // Note: contents.c9r will be created when the file content is written
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        #endregion
    }
} 