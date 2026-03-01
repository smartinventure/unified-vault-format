using StorageLib.Abstractions;
using UvfLib.Core.Api;
using UvfLib.Master.PathTranslators;
using UvfLib.Vault;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.InteropServices;

namespace UvfLib.Master.Decorators
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
        private readonly bool _encryptFilenames;

        public UvfStorageDecorator(
            IStorage underlyingStorage, 
            VaultHandler vault, 
            bool encryptFilenames = true,  // UVF encrypts filenames by default for security
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
            _encryptFilenames = encryptFilenames;
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

                // Additional safety check - if Path.GetFileName returns empty, treat as root
                string dirName = Path.GetFileName(directoryPath);
                if (string.IsNullOrEmpty(dirName))
                {
                    // This is effectively the root directory
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
                
                // Encrypt the directory name using parent metadata (dirName already extracted above)
                string encryptedDirName;
                if (_encryptFilenames)
                {
                    encryptedDirName = _vault.EncryptFilename(dirName, parentMetadata);
                }
                else
                {
                    // Simple mode: directory names are not encrypted
                    encryptedDirName = dirName;
                }
                
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
                    await _underlyingStorage.WriteAsync(dirUvfPath, fileHandle, 0, encryptedMetadata.Length, Marshal.UnsafeAddrOfPinnedArrayElement(encryptedMetadata, 0), cancellationToken);
                }
                finally
                {
                    await _underlyingStorage.CloseAsync(dirUvfPath, fileHandle, cancellationToken);
                }
                
                // Create content directory where files will be stored
                string contentDirectory = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(newDirMetadata));
                await _underlyingStorage.CreateDirectoryAsync(contentDirectory, cancellationToken);
                
                // CRITICAL: Create second dir.uvf file in content directory for disaster recovery
                // This allows recovery even if the parent directory is lost
                string contentDirUvfPath = Path.Combine(contentDirectory, _vault.GetDirectoryMetadataFilename());
                IntPtr contentFileHandle = await _underlyingStorage.OpenAsync(contentDirUvfPath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
                try
                {
                    // IMPORTANT: Both dir.uvf files must be encrypted independently (different ciphertexts)
                    // Even though they contain the same dirId, they must be encrypted separately
                    byte[] contentEncryptedMetadata = _vault.EncryptDirectoryMetadata(newDirMetadata);
                    await _underlyingStorage.WriteAsync(contentDirUvfPath, contentFileHandle, 0, contentEncryptedMetadata.Length, Marshal.UnsafeAddrOfPinnedArrayElement(contentEncryptedMetadata, 0), cancellationToken);
                }
                finally
                {
                    await _underlyingStorage.CloseAsync(contentDirUvfPath, contentFileHandle, cancellationToken);
                }
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

                if (_encryptFilenames)
                {
                    // Encrypted mode: use path translator and check reference directory structure
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
                else
                {
                    // Simple mode: directories exist directly in the vault content structure
                    try
                    {
                        var directoryMetadata = await GetDirectoryMetadataAsync(directoryPath);
                        string contentDirectory = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(directoryMetadata));
                        return await _underlyingStorage.DirectoryExistsAsync(contentDirectory, cancellationToken);
                    }
                    catch
                    {
                        // If we can't get directory metadata, the directory doesn't exist
                        return false;
                    }
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
                if (_encryptFilenames)
                {
                    // Complex encrypted mode: use directory metadata system
                    var directoryMetadata = await GetDirectoryMetadataAsync(realPath);
                    string contentDirectory = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(directoryMetadata));
                    
                    if (await _underlyingStorage.DirectoryExistsAsync(contentDirectory, cancellationToken))
                    {
                        var allContentItems = await _underlyingStorage.ReadDirAsync(contentDirectory, true, cancellationToken);
                        
                        // Process subdirectories and files using encrypted approach
                        foreach (var item in allContentItems)
                        {
                            // Skip the dir.uvf metadata file itself
                            if (item.Filename.Equals(_vault.GetDirectoryMetadataFilename(), StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            
                            if (item.IsDirectory)
                            {
                                // This is a subdirectory reference - decrypt the directory name
                                try
                                {
                                    string decryptedDirName = _vault.DecryptFilename(item.Filename, directoryMetadata);
                                    string virtualDirPath = string.IsNullOrEmpty(realPath) || realPath == "/" 
                                        ? $"/{decryptedDirName}" 
                                        : $"{realPath}/{decryptedDirName}";
                                    
                                    // Check if this directory contains symlink.uvf (making it a symlink)
                                    string symlinkUvfPath = Path.Combine(item.RealPath ?? item.Filename, _vault.GetSymlinkMetadataFilename());
                                    bool isSymlink = await _underlyingStorage.FileExistsAsync(symlinkUvfPath, cancellationToken);
                                    
                                    var dirObject = new FileObject(virtualDirPath)
                                    {
                                        IsDirectory = !isSymlink, // If it's a symlink, it's not a directory
                                        Filename = decryptedDirName,
                                        RealPath = virtualDirPath,
                                        VirtualPath = virtualDirPath,
                                        Size = 0,
                                        CreationTime = item.CreationTime,
                                        LastModified = item.LastModified,
                                        LastAccessTime = item.LastAccessTime,
                                        SC = this
                                    };
                                    
                                    // Note: FileObject doesn't have Properties collection, so symlink metadata
                                    // needs to be determined by calling IsSymlinkAsync() when needed
                                    if (isSymlink)
                                    {
                                        _logger?.LogDebug("Detected symlink in directory listing: {Path}", virtualDirPath);
                                    }
                                    
                                    fileObjects.Add(dirObject);
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning(ex, "Could not decrypt directory name: {EncryptedName}", item.Filename);
                                }
                            }
                            else
                            {
                                // This is an encrypted file - decrypt the filename
                                // Files in UVF have .uvf extension, but directories can also have .uvf extensions
                                // We need to check if this is actually a file or a directory reference
                                try
                                {
                                    string decryptedFileName = _vault.DecryptFilename(item.Filename, directoryMetadata);
                                    string virtualFilePath = string.IsNullOrEmpty(realPath) || realPath == "/" 
                                        ? $"/{decryptedFileName}" 
                                        : $"{realPath}/{decryptedFileName}";
                                    
                                    var fileObject = new FileObject(virtualFilePath)
                                    {
                                        IsDirectory = false,
                                        Filename = decryptedFileName,
                                        RealPath = virtualFilePath,
                                        VirtualPath = virtualFilePath,
                                        Size = VaultHandler.CalculateExpectedDecryptedSize(item.Size),
                                        CreationTime = item.CreationTime,
                                        LastModified = item.LastModified,
                                        LastAccessTime = item.LastAccessTime,
                                        SC = this
                                    };
                                    
                                    fileObjects.Add(fileObject);
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning(ex, "Could not decrypt file name: {EncryptedName}", item.Filename);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Simple mode: files are stored directly with .uvf extensions
                    string physicalPath = string.IsNullOrEmpty(realPath) || realPath == "/" 
                        ? _vaultBasePath 
                        : Path.Combine(_vaultBasePath, realPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    
                    if (await _underlyingStorage.DirectoryExistsAsync(physicalPath, cancellationToken))
                    {
                        var allItems = await _underlyingStorage.ReadDirAsync(physicalPath, true, cancellationToken);
                        
                        // Process subdirectories (these are normal directories)
                        var dirs = allItems.Where(item => item.IsDirectory && 
                            !item.Filename.Equals("d", StringComparison.OrdinalIgnoreCase) && // Skip UVF internal directory
                            !item.Filename.EndsWith(".uvf", StringComparison.OrdinalIgnoreCase)); // Skip .uvf files
                        
                        foreach (var dir in dirs)
                        {
                            string dirName = dir.Filename;
                            string virtualPath = string.IsNullOrEmpty(realPath) || realPath == "/" 
                                ? $"/{dirName}" 
                                : $"{realPath}/{dirName}";
                            
                            // Check if this directory contains symlink.uvf (making it a symlink)
                            string symlinkUvfPath = Path.Combine(dir.RealPath ?? Path.Combine(physicalPath, dir.Filename), _vault.GetSymlinkMetadataFilename());
                            bool isSymlink = await _underlyingStorage.FileExistsAsync(symlinkUvfPath, cancellationToken);
                            
                            var dirObject = new FileObject(virtualPath)
                            {
                                IsDirectory = !isSymlink, // If it's a symlink, it's not a directory
                                Filename = dirName,
                                RealPath = virtualPath,
                                VirtualPath = virtualPath,
                                Size = 0,
                                CreationTime = dir.CreationTime,
                                LastModified = dir.LastModified,
                                LastAccessTime = dir.LastAccessTime,
                                SC = this
                            };
                            
                            if (isSymlink)
                            {
                                _logger?.LogDebug("Detected symlink in simple mode directory listing: {Path}", virtualPath);
                            }
                            
                            fileObjects.Add(dirObject);
                        }
                        
                        // Process files (these have .uvf extensions)
                        var files = allItems.Where(item => !item.IsDirectory && 
                            item.Filename.EndsWith(".uvf", StringComparison.OrdinalIgnoreCase) &&
                            !item.Filename.Equals("vault.uvf", StringComparison.OrdinalIgnoreCase)); // Skip vault file
                        
                        foreach (var file in files)
                        {
                            string encryptedFileName = file.Filename;
                            string decryptedFileName = encryptedFileName.Substring(0, encryptedFileName.Length - 4); // Remove .uvf extension
                            string virtualPath = string.IsNullOrEmpty(realPath) || realPath == "/" 
                                ? $"/{decryptedFileName}" 
                                : $"{realPath}/{decryptedFileName}";
                            
                            var fileObject = new FileObject(virtualPath)
                            {
                                IsDirectory = false,
                                Filename = decryptedFileName,
                                RealPath = virtualPath,
                                VirtualPath = virtualPath,
                                Size = VaultHandler.CalculateExpectedDecryptedSize(file.Size), // Calculate decrypted size
                                CreationTime = file.CreationTime,
                                LastModified = file.LastModified,
                                LastAccessTime = file.LastAccessTime,
                                SC = this
                            };
                            
                            fileObjects.Add(fileObject);
                        }
                    }
                }
                
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
                string encryptedDirName;
                if (_encryptFilenames)
                {
                    // Encrypt the directory name using current metadata
                    encryptedDirName = _vault.EncryptFilename(pathPart, currentMetadata);
                }
                else
                {
                    // Simple mode: directory names are not encrypted
                    encryptedDirName = pathPart;
                }
                
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
                await _underlyingStorage.ReadAsync(dirUvfPath, fileHandle, 0, fileInfo.Size, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0), cancellationToken);
                return _vault.DecryptDirectoryMetadata(buffer);
            }
            finally
            {
                await _underlyingStorage.CloseAsync(dirUvfPath, fileHandle, cancellationToken);
            }
        }

        #endregion

        #region Symlink Operations (UVF only)

        /// <summary>
        /// Creates a symlink at the specified path pointing to the given target.
        /// UVF symlinks are stored as directories containing symlink.uvf files.
        /// </summary>
        /// <param name="symlinkPath">Virtual path where the symlink should be created</param>
        /// <param name="targetPath">The target path the symlink should point to</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <exception cref="InvalidOperationException">If this is not a UVF vault</exception>
        /// <exception cref="ArgumentException">If paths are invalid</exception>
        /// <exception cref="IOException">If the symlink cannot be created</exception>
        public async Task CreateSymlinkAsync(string symlinkPath, string targetPath, CancellationToken cancellationToken = default)
        {
            if (_vault.IsCryptomatorV8())
            {
                throw new InvalidOperationException("Symlinks are only supported in UVF format");
            }

            if (string.IsNullOrEmpty(symlinkPath)) throw new ArgumentException("Symlink path cannot be null or empty", nameof(symlinkPath));
            if (string.IsNullOrEmpty(targetPath)) throw new ArgumentException("Target path cannot be null or empty", nameof(targetPath));

            try
            {
                // Get parent directory metadata to encrypt the symlink name
                string parentPath = Path.GetDirectoryName(symlinkPath)?.Replace('\\', '/') ?? "/";
                string symlinkName = Path.GetFileName(symlinkPath);
                
                var parentMetadata = await GetDirectoryMetadataAsync(parentPath);
                
                // Encrypt the symlink name using parent metadata
                string encryptedSymlinkName;
                if (_encryptFilenames)
                {
                    encryptedSymlinkName = _vault.EncryptFilename(symlinkName, parentMetadata);
                }
                else
                {
                    // Simple mode: symlink names are not encrypted
                    encryptedSymlinkName = symlinkName;
                }
                
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
                
                // Create symlink directory in parent content directory
                string symlinkDirectory = Path.Combine(parentContentDir, encryptedSymlinkName);
                await _underlyingStorage.CreateDirectoryAsync(symlinkDirectory, cancellationToken);
                
                // Encrypt the symlink target
                byte[] encryptedTarget = _vault.EncryptSymlinkTarget(targetPath);
                
                // Create symlink.uvf file in the symlink directory
                string symlinkUvfPath = Path.Combine(symlinkDirectory, _vault.GetSymlinkMetadataFilename());
                IntPtr fileHandle = await _underlyingStorage.OpenAsync(symlinkUvfPath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
                try
                {
                    await _underlyingStorage.WriteAsync(symlinkUvfPath, fileHandle, 0, encryptedTarget.Length, Marshal.UnsafeAddrOfPinnedArrayElement(encryptedTarget, 0), cancellationToken);
                }
                finally
                {
                    await _underlyingStorage.CloseAsync(symlinkUvfPath, fileHandle, cancellationToken);
                }
                
                _logger?.LogDebug("Created UVF symlink: {SymlinkPath} -> {TargetPath}", symlinkPath, targetPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error creating UVF symlink: {SymlinkPath} -> {TargetPath}", symlinkPath, targetPath);
                throw new IOException($"Failed to create symlink '{symlinkPath}' -> '{targetPath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Reads the target of a symlink at the specified path.
        /// </summary>
        /// <param name="symlinkPath">Virtual path of the symlink</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The target path the symlink points to</returns>
        /// <exception cref="InvalidOperationException">If this is not a UVF vault</exception>
        /// <exception cref="ArgumentException">If the path is invalid</exception>
        /// <exception cref="FileNotFoundException">If the symlink does not exist</exception>
        /// <exception cref="IOException">If the symlink cannot be read</exception>
        public async Task<string> ReadSymlinkAsync(string symlinkPath, CancellationToken cancellationToken = default)
        {
            if (_vault.IsCryptomatorV8())
            {
                throw new InvalidOperationException("Symlinks are only supported in UVF format");
            }

            if (string.IsNullOrEmpty(symlinkPath)) throw new ArgumentException("Symlink path cannot be null or empty", nameof(symlinkPath));

            try
            {
                // Get parent directory metadata to encrypt the symlink name
                string parentPath = Path.GetDirectoryName(symlinkPath)?.Replace('\\', '/') ?? "/";
                string symlinkName = Path.GetFileName(symlinkPath);
                
                var parentMetadata = await GetDirectoryMetadataAsync(parentPath);
                
                // Encrypt the symlink name using parent metadata
                string encryptedSymlinkName;
                if (_encryptFilenames)
                {
                    encryptedSymlinkName = _vault.EncryptFilename(symlinkName, parentMetadata);
                }
                else
                {
                    // Simple mode: symlink names are not encrypted
                    encryptedSymlinkName = symlinkName;
                }
                
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
                
                // Get symlink directory path
                string symlinkDirectory = Path.Combine(parentContentDir, encryptedSymlinkName);
                string symlinkUvfPath = Path.Combine(symlinkDirectory, _vault.GetSymlinkMetadataFilename());
                
                if (!await _underlyingStorage.FileExistsAsync(symlinkUvfPath, cancellationToken))
                {
                    throw new FileNotFoundException($"Symlink not found: {symlinkPath}");
                }
                
                // Read and decrypt the symlink.uvf file
                var fileInfo = await _underlyingStorage.GetFileInfoAsync(symlinkUvfPath, cancellationToken);
                var buffer = new byte[fileInfo.Size];
                
                IntPtr fileHandle = await _underlyingStorage.OpenAsync(symlinkUvfPath, OpenFlags.ReadOnly, cancellationToken);
                try
                {
                    await _underlyingStorage.ReadAsync(symlinkUvfPath, fileHandle, 0, fileInfo.Size, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0), cancellationToken);
                    return _vault.DecryptSymlinkTarget(buffer);
                }
                finally
                {
                    await _underlyingStorage.CloseAsync(symlinkUvfPath, fileHandle, cancellationToken);
                }
            }
            catch (Exception ex) when (!(ex is FileNotFoundException || ex is InvalidOperationException || ex is ArgumentException))
            {
                _logger?.LogError(ex, "Error reading UVF symlink: {SymlinkPath}", symlinkPath);
                throw new IOException($"Failed to read symlink '{symlinkPath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Checks if the specified path is a symlink.
        /// </summary>
        /// <param name="path">Virtual path to check</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the path is a symlink, false otherwise</returns>
        /// <exception cref="InvalidOperationException">If this is not a UVF vault</exception>
        public async Task<bool> IsSymlinkAsync(string path, CancellationToken cancellationToken = default)
        {
            if (_vault.IsCryptomatorV8())
            {
                throw new InvalidOperationException("Symlinks are only supported in UVF format");
            }

            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                // Get parent directory metadata to encrypt the path name
                string parentPath = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "/";
                string pathName = Path.GetFileName(path);
                
                var parentMetadata = await GetDirectoryMetadataAsync(parentPath);
                
                // Encrypt the path name using parent metadata
                string encryptedPathName;
                if (_encryptFilenames)
                {
                    encryptedPathName = _vault.EncryptFilename(pathName, parentMetadata);
                }
                else
                {
                    // Simple mode: path names are not encrypted
                    encryptedPathName = pathName;
                }
                
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
                
                // Check if symlink directory exists and contains symlink.uvf
                string symlinkDirectory = Path.Combine(parentContentDir, encryptedPathName);
                string symlinkUvfPath = Path.Combine(symlinkDirectory, _vault.GetSymlinkMetadataFilename());
                
                return await _underlyingStorage.DirectoryExistsAsync(symlinkDirectory, cancellationToken) &&
                       await _underlyingStorage.FileExistsAsync(symlinkUvfPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking if path is symlink: {Path}", path);
                return false;
            }
        }

        #endregion
    }
} 