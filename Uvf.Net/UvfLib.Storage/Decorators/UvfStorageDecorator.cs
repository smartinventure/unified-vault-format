using StorageLib.Abstractions;
using UvfLib.Core.Api;
using UvfLib.Storage.PathTranslators;
using UvfLib.Vault;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.InteropServices;

namespace UvfLib.Storage.Decorators
{
    /// <summary>
    /// UVF Storage Decorator - implements IStorage for UVF vault operations.
    /// 
    /// UVF vault structure (simpler than Cryptomator):
    /// - Reference directories: contain dir.uvf files with encrypted metadata
    /// - Content directories: calculated from DirId, contain actual encrypted files
    /// - Encrypted files: stored with encrypted names (no .uvf extension for files)
    /// - Directory metadata: dir.uvf files contain encrypted DirectoryMetadata
    /// </summary>
    public class UvfStorageDecorator : CryptorStorageDecoratorBase
    {
        private readonly UvfPathTranslator _uvfTranslator;
        private readonly Microsoft.Extensions.Logging.ILogger? _logger;

        public UvfStorageDecorator(
            IStorage underlyingStorage, 
            VaultHandler vault, 
            bool encryptFilenames = true,
            string? vaultBasePath = null,
            ILogger? logger = null)
            : base(underlyingStorage, vault, 
                   new UvfPathTranslator(vault, underlyingStorage, vaultBasePath ?? underlyingStorage.BaseFolderOrContainer, encryptFilenames),
                   vaultBasePath ?? underlyingStorage.BaseFolderOrContainer)
        {
            // Validate that this is for UVF format (not Cryptomator)
            if (_vault.IsCryptomatorV8())
            {
                throw new ArgumentException("VaultHandler must be configured for UVF format, not Cryptomator V8", nameof(vault));
            }
            
            _uvfTranslator = (UvfPathTranslator)_pathTranslator;
            _logger = logger;
        }

        #region Directory Operations

        /// <summary>
        /// Creates UVF directory structure:
        /// 1. Reference directory with encrypted name in parent
        /// 2. dir.uvf file containing encrypted metadata
        /// 3. Content directory calculated from DirId
        /// </summary>
        public override async Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Handle root directory - it should already exist
                if (string.IsNullOrEmpty(directoryPath) || directoryPath == "/")
                {
                    string rootDirectory = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());
                    if (!await _underlyingStorage.DirectoryExistsAsync(rootDirectory, cancellationToken))
                    {
                        await _underlyingStorage.CreateDirectoryAsync(rootDirectory, cancellationToken);
                    }
                    return;
                }

                // Get parent directory metadata to encrypt the new directory name
                string parentPath = Path.GetDirectoryName(directoryPath)?.Replace('\\', '/') ?? "/";
                var parentMetadata = await GetDirectoryMetadataAsync(parentPath);
                
                // Create new metadata for this directory
                var newDirMetadata = _vault.CreateNewDirectoryMetadata();
                
                // Encrypt the directory name using parent metadata
                string dirName = Path.GetFileName(directoryPath);
                string encryptedDirName = _vault.EncryptFilename(dirName, parentMetadata);
                
                // Determine parent content directory
                string parentContentDir;
                if (parentPath == "/")
                {
                    parentContentDir = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());
                }
                else
                {
                    parentContentDir = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(parentMetadata));
                }
                
                // Create reference directory in parent content directory
                string referenceDirectory = Path.Combine(parentContentDir, encryptedDirName);
                await _underlyingStorage.CreateDirectoryAsync(referenceDirectory, cancellationToken);
                
                // Create dir.uvf metadata file in reference directory
                byte[] encryptedMetadata = _vault.EncryptDirectoryMetadata(newDirMetadata);
                string dirUvfPath = Path.Combine(referenceDirectory, _vault.GetDirectoryMetadataFilename());
                IntPtr fileHandle = await _underlyingStorage.OpenAsync(dirUvfPath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
                try
                {
                    await _underlyingStorage.WriteAsync(fileHandle, 0, encryptedMetadata.Length, Marshal.UnsafeAddrOfPinnedArrayElement(encryptedMetadata, 0), cancellationToken);
                }
                finally
                {
                    await _underlyingStorage.CloseAsync(fileHandle, cancellationToken);
                }
                
                // Create content directory where files will be stored
                string contentDirectory = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(newDirMetadata));
                await _underlyingStorage.CreateDirectoryAsync(contentDirectory, cancellationToken);
                
                _logger?.LogDebug("Created UVF directory: {VirtualPath} -> Reference: {ReferenceDir}, Content: {ContentDir}", 
                    directoryPath, referenceDirectory, contentDirectory);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating UVF directory: {DirectoryPath}", directoryPath);
                throw new IOException($"Failed to create directory '{directoryPath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deletes UVF directory structure:
        /// 1. Delete all files in content directory
        /// 2. Delete content directory
        /// 3. Delete dir.uvf file
        /// 4. Delete reference directory
        /// </summary>
        public override async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                // Handle root directory - cannot delete
                if (string.IsNullOrEmpty(path) || path == "/")
                {
                    throw new ArgumentException("Cannot delete root directory", nameof(path));
                }

                // Get directory metadata
                var dirMetadata = await GetDirectoryMetadataAsync(path);
                
                // Get content directory path
                string contentDirectory = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(dirMetadata));
                
                // Delete content directory and all its contents
                if (await _underlyingStorage.DirectoryExistsAsync(contentDirectory, cancellationToken))
                {
                    await _underlyingStorage.DeleteDirectoryAsync(contentDirectory, cancellationToken);
                }
                
                // Get reference directory path
                var pathResult = await _uvfTranslator.TranslateToStoragePathAsync(path);
                string referenceDirectory = pathResult.StoragePath;
                
                // Delete reference directory and its contents (including dir.uvf)
                if (await _underlyingStorage.DirectoryExistsAsync(referenceDirectory, cancellationToken))
                {
                    await _underlyingStorage.DeleteDirectoryAsync(referenceDirectory, cancellationToken);
                }
                
                _logger?.LogDebug("Deleted UVF directory: {VirtualPath} -> Reference: {ReferenceDir}, Content: {ContentDir}", 
                    path, referenceDirectory, contentDirectory);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting UVF directory: {DirectoryPath}", path);
                throw new IOException($"Failed to delete directory '{path}': {ex.Message}", ex);
            }
        }

        public override async Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Handle root directory - it should always exist if vault is initialized
                if (string.IsNullOrEmpty(directoryPath) || directoryPath == "/")
                {
                    string rootDirectory = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());
                    return await _underlyingStorage.DirectoryExistsAsync(rootDirectory, cancellationToken);
                }

                // For other directories, check if the reference directory exists and contains dir.uvf
                var pathResult = await _uvfTranslator.TranslateToStoragePathAsync(directoryPath);
                string referenceDirectory = pathResult.StoragePath;
                
                // Check if reference directory exists
                if (!await _underlyingStorage.DirectoryExistsAsync(referenceDirectory, cancellationToken))
                {
                    return false;
                }
                
                // Check if dir.uvf file exists in the reference directory
                string dirUvfPath = Path.Combine(referenceDirectory, _vault.GetDirectoryMetadataFilename());
                if (!await _underlyingStorage.FileExistsAsync(dirUvfPath, cancellationToken))
                {
                    return false;
                }
                
                // Optionally verify that the content directory also exists
                try
                {
                    var directoryMetadata = await LoadDirectoryMetadataFromDirUvfAsync(referenceDirectory, cancellationToken);
                    string contentDirectory = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(directoryMetadata));
                    return await _underlyingStorage.DirectoryExistsAsync(contentDirectory, cancellationToken);
                }
                catch
                {
                    // If we can't load the directory metadata, the directory is invalid
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking directory existence for: {DirectoryPath}", directoryPath);
                return false;
            }
        }

        public override async Task<IEnumerable<FileObject>> ReadDirAsync(string directoryPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            return await ReadDirInternalAsync(directoryPath, readOnly, cancellationToken);
        }

        #endregion

        #region File Operations

        public override async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Handle empty or root path - files can't exist at root level
                if (string.IsNullOrEmpty(filePath) || filePath == "/")
                {
                    return false;
                }

                // Translate the virtual file path to the physical encrypted file path
                var pathResult = await _uvfTranslator.TranslateToStoragePathAsync(filePath);
                string encryptedFilePath = pathResult.StoragePath;
                
                // Check if the encrypted file exists in the content directory
                return await _underlyingStorage.FileExistsAsync(encryptedFilePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking file existence for: {FilePath}", filePath);
                return false;
            }
        }

        public override async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Handle empty or root path - can't delete at root level
                if (string.IsNullOrEmpty(filePath) || filePath == "/")
                {
                    throw new ArgumentException("Cannot delete root directory or empty path", nameof(filePath));
                }

                // Translate the virtual file path to the physical encrypted file path
                var pathResult = await _uvfTranslator.TranslateToStoragePathAsync(filePath);
                string encryptedFilePath = pathResult.StoragePath;
                
                // Verify the file exists before attempting deletion
                if (!await _underlyingStorage.FileExistsAsync(encryptedFilePath, cancellationToken))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }
                
                // Delete the encrypted file
                await _underlyingStorage.DeleteAsync(encryptedFilePath, cancellationToken);
                
                _logger?.LogDebug("Deleted UVF file: {VirtualPath} -> {EncryptedPath}", filePath, encryptedFilePath);
            }
            catch (Exception ex) when (!(ex is FileNotFoundException || ex is ArgumentException))
            {
                _logger?.LogError(ex, "Error deleting UVF file: {FilePath}", filePath);
                throw new IOException($"Failed to delete file '{filePath}': {ex.Message}", ex);
            }
        }

        public override async Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Handle empty or root path - can't get file info at root level
                if (string.IsNullOrEmpty(filePath) || filePath == "/")
                {
                    throw new ArgumentException("Cannot get file info for root directory or empty path", nameof(filePath));
                }

                // Translate the virtual file path to the physical encrypted file path
                var pathResult = await _uvfTranslator.TranslateToStoragePathAsync(filePath);
                string encryptedFilePath = pathResult.StoragePath;
                
                // Get the encrypted file info from underlying storage
                var encryptedFileInfo = await _underlyingStorage.GetFileInfoAsync(encryptedFilePath, cancellationToken);
                
                // Calculate the decrypted size using VaultHandler
                long decryptedSize = VaultHandler.CalculateExpectedDecryptedSize(encryptedFileInfo.Size);
                
                // Create virtual file object with decrypted information
                var virtualFileInfo = new FileObject(filePath.Replace('\\', '/'))
                {
                    IsDirectory = false,
                    Filename = Path.GetFileName(filePath),
                    RealPath = filePath.Replace('\\', '/'),
                    VirtualPath = filePath.Replace('\\', '/'),
                    Size = decryptedSize,
                    CreationTime = encryptedFileInfo.CreationTime,
                    LastModified = encryptedFileInfo.LastModified,
                    LastAccessTime = encryptedFileInfo.LastAccessTime,
                    SC = this
                };
                
                _logger?.LogDebug("Retrieved UVF file info: {VirtualPath} -> Size: {DecryptedSize} (encrypted: {EncryptedSize})", 
                    filePath, decryptedSize, encryptedFileInfo.Size);
                
                return virtualFileInfo;
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                _logger?.LogError(ex, "Error getting UVF file info: {FilePath}", filePath);
                throw new IOException($"Failed to get file info for '{filePath}': {ex.Message}", ex);
            }
        }

        public override async Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate input paths
                if (string.IsNullOrEmpty(sourceFilePath) || sourceFilePath == "/")
                {
                    throw new ArgumentException("Cannot move root directory or empty source path", nameof(sourceFilePath));
                }
                
                if (string.IsNullOrEmpty(destinationFilePath) || destinationFilePath == "/")
                {
                    throw new ArgumentException("Cannot move to root directory or empty destination path", nameof(destinationFilePath));
                }

                // Check if source exists (could be file or directory)
                bool sourceIsFile = await FileExistsAsync(sourceFilePath, cancellationToken);
                bool sourceIsDirectory = !sourceIsFile && await DirectoryExistsAsync(sourceFilePath, cancellationToken);
                
                if (!sourceIsFile && !sourceIsDirectory)
                {
                    throw new FileNotFoundException($"Source not found: {sourceFilePath}");
                }

                // Check if destination already exists
                bool destIsFile = await FileExistsAsync(destinationFilePath, cancellationToken);
                bool destIsDirectory = !destIsFile && await DirectoryExistsAsync(destinationFilePath, cancellationToken);
                
                if ((destIsFile || destIsDirectory) && !overwrite)
                {
                    throw new IOException($"Destination already exists and overwrite is false: {destinationFilePath}");
                }

                if (sourceIsFile)
                {
                    // Moving a file
                    await MoveFileAsync(sourceFilePath, destinationFilePath, overwrite, cancellationToken);
                }
                else
                {
                    // Moving a directory - complex for UVF due to the 2-component structure
                    await MoveDirectoryAsync(sourceFilePath, destinationFilePath, overwrite, cancellationToken);
                }
                
                _logger?.LogDebug("Moved UVF item: {SourcePath} -> {DestinationPath}", sourceFilePath, destinationFilePath);
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is FileNotFoundException || ex is IOException))
            {
                _logger?.LogError(ex, "Error moving UVF item: {SourcePath} -> {DestinationPath}", sourceFilePath, destinationFilePath);
                throw new IOException($"Failed to move '{sourceFilePath}' to '{destinationFilePath}': {ex.Message}", ex);
            }
        }

        private async Task MoveFileAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken)
        {
            // Translate both paths to encrypted storage paths
            var sourcePathResult = await _uvfTranslator.TranslateToStoragePathAsync(sourceFilePath);
            var destPathResult = await _uvfTranslator.TranslateToStoragePathAsync(destinationFilePath);
            
            string sourceEncryptedPath = sourcePathResult.StoragePath;
            string destEncryptedPath = destPathResult.StoragePath;
            
            // Use underlying storage to move the encrypted file
            await _underlyingStorage.MoveAsync(sourceEncryptedPath, destEncryptedPath, overwrite, cancellationToken);
        }

        private async Task MoveDirectoryAsync(string sourceDirectoryPath, string destinationDirectoryPath, bool overwrite, CancellationToken cancellationToken)
        {
            // Moving directories in UVF is complex because of the 2-component structure:
            // 1. Reference directory (with encrypted name and dir.uvf)
            // 2. Content directory (calculated from DirId)
            
            // For now, throw NotImplementedException as this requires careful handling
            // to avoid breaking the vault structure
            throw new NotImplementedException("Directory moving in UVF vaults requires careful implementation to maintain vault integrity. Use file-level operations instead.");
        }

        #endregion

        #region Directory Enumeration

        private async Task<IEnumerable<FileObject>> ReadDirInternalAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            var fileObjects = new List<FileObject>();
            
            try
            {
                // Get directory metadata for the virtual path
                var directoryMetadata = await GetDirectoryMetadataAsync(realPath);
                
                // Get the content directory where files are stored
                string contentDirectory = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(directoryMetadata));
                
                // Enumerate content directory
                if (await _underlyingStorage.DirectoryExistsAsync(contentDirectory, cancellationToken))
                {
                    var allContentItems = await _underlyingStorage.ReadDirAsync(contentDirectory, true, cancellationToken);
                    
                    // Process subdirectories (these are reference directories with encrypted names)
                    var encryptedDirs = allContentItems.Where(item => item.IsDirectory).Select(item => item.RealPath);
                    foreach (string encryptedDir in encryptedDirs)
                    {
                        try
                        {
                            string encryptedDirName = Path.GetFileName(encryptedDir);
                            
                            // Decrypt the directory name
                            string decryptedDirName = _vault.DecryptFilename(encryptedDirName, directoryMetadata);
                            
                            var dirObject = new FileObject(Path.Combine(realPath, decryptedDirName).Replace('\\', '/'))
                            {
                                IsDirectory = true,
                                Filename = decryptedDirName,
                                RealPath = Path.Combine(realPath, decryptedDirName).Replace('\\', '/'),
                                VirtualPath = Path.Combine(realPath, decryptedDirName).Replace('\\', '/'),
                                Size = 0,
                                SC = this
                            };
                            
                            // Try to get directory timestamps from the reference directory
                            try
                            {
                                var dirInfo = await _underlyingStorage.GetFileInfoAsync(encryptedDir, cancellationToken);
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
                    
                    // Process files (these are encrypted files with encrypted names, no extension)
                    var encryptedFiles = allContentItems.Where(item => !item.IsDirectory && 
                        item.Filename != _vault.GetDirectoryMetadataFilename()).Select(item => item.RealPath);
                    foreach (string encryptedFile in encryptedFiles)
                    {
                        try
                        {
                            string encryptedFileName = Path.GetFileName(encryptedFile);
                            
                            // Decrypt the filename
                            string decryptedFileName = _vault.DecryptFilename(encryptedFileName, directoryMetadata);
                            
                            var fileInfo = await _underlyingStorage.GetFileInfoAsync(encryptedFile, cancellationToken);
                            
                            var fileObject = new FileObject(Path.Combine(realPath, decryptedFileName).Replace('\\', '/'))
                            {
                                IsDirectory = false,
                                Filename = decryptedFileName,
                                RealPath = Path.Combine(realPath, decryptedFileName).Replace('\\', '/'),
                                VirtualPath = Path.Combine(realPath, decryptedFileName).Replace('\\', '/'),
                                Size = VaultHandler.CalculateExpectedDecryptedSize(fileInfo.Size), // Calculate decrypted size
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
                
                _logger?.LogDebug("Enumerated {Count} entries in UVF directory: {Path}", fileObjects.Count, realPath);
                return fileObjects.AsEnumerable();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error enumerating UVF directory: {Path}", realPath);
                // Return empty list if directory can't be read
                return new List<FileObject>();
            }
        }

        /// <summary>
        /// Gets directory metadata for a virtual path by navigating the UVF directory structure
        /// </summary>
        private async Task<DirectoryMetadata> GetDirectoryMetadataAsync(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/")
            {
                return _vault.GetRootDirectoryMetadata();
            }

            // Navigate through the directory hierarchy to get metadata
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            DirectoryMetadata currentMetadata = _vault.GetRootDirectoryMetadata();
            string currentContentDir = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());
            
            foreach (string pathPart in pathParts)
            {
                // Encrypt the directory name using current metadata
                string encryptedDirName = _vault.EncryptFilename(pathPart, currentMetadata);
                
                // Move to the reference directory in current content directory
                string referenceDirectory = Path.Combine(currentContentDir, encryptedDirName);
                
                // Load directory metadata from dir.uvf file
                currentMetadata = await LoadDirectoryMetadataFromDirUvfAsync(referenceDirectory, CancellationToken.None);
                
                // Update current content directory for next iteration
                currentContentDir = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(currentMetadata));
            }
            
            return currentMetadata;
        }

        /// <summary>
        /// Loads directory metadata from a dir.uvf file in the reference directory
        /// </summary>
        private async Task<DirectoryMetadata> LoadDirectoryMetadataFromDirUvfAsync(string referenceDirectory, CancellationToken cancellationToken)
        {
            string dirUvfPath = Path.Combine(referenceDirectory, _vault.GetDirectoryMetadataFilename());
            
            if (!await _underlyingStorage.FileExistsAsync(dirUvfPath, cancellationToken))
            {
                throw new FileNotFoundException($"Directory metadata file not found: {dirUvfPath}");
            }
            
            // Read and decrypt the dir.uvf file
            // First get file info to determine size
            var fileInfo = await _underlyingStorage.GetFileInfoAsync(dirUvfPath, cancellationToken);
            var buffer = new byte[fileInfo.Size];
            
            IntPtr fileHandle = await _underlyingStorage.OpenAsync(dirUvfPath, OpenFlags.ReadOnly, cancellationToken);
            try
            {
                await _underlyingStorage.ReadAsync(fileHandle, 0, fileInfo.Size, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0), cancellationToken);
                return _vault.DecryptDirectoryMetadata(buffer);
            }
            finally
            {
                await _underlyingStorage.CloseAsync(fileHandle, cancellationToken);
            }
        }

        #endregion
    }
} 