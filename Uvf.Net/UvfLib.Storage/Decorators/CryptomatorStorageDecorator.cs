using StorageLib.Abstractions;
using UvfLib.Storage.PathTranslators;
using UvfLib.Vault;
using Microsoft.Extensions.Logging;
using System.IO;

namespace UvfLib.Storage.Decorators
{
    /// <summary>
    /// Cryptomator V8 Storage Decorator - implements IStorage for Cryptomator vault operations.
    /// 
    /// Key principle: This decorator handles the complex Cryptomator vault logic while delegating
    /// actual file system operations to the underlying storage connector (e.g., LocalStorage).
    /// 
    /// Cryptomator vault structure:
    /// - Reference directories: contain dir.c9r files with UUID references
    /// - Content directories: d/XX/XXXXXXXX/ structure based on directory UUID
    /// - Encrypted files: stored as .c9r files in content directories
    /// - Directory metadata: dirid.c9r files in content directories
    /// </summary>
    public class CryptomatorStorageDecorator : CryptorStorageDecoratorBase
    {
        private readonly CryptomatorPathTranslator _cryptomatorTranslator;
        private readonly Microsoft.Extensions.Logging.ILogger? _logger;

        public CryptomatorStorageDecorator(
            IStorage underlyingStorage,
            VaultHandler vault,
            string? vaultBasePath = null,
            ILogger? logger = null)
            : base(underlyingStorage, vault, 
                   new CryptomatorPathTranslator(vault, underlyingStorage, vaultBasePath ?? underlyingStorage.BaseFolderOrContainer),
                   vaultBasePath ?? underlyingStorage.BaseFolderOrContainer)
        {
            if (!_vault.IsCryptomatorV8())
            {
                throw new ArgumentException("VaultHandler must be configured for Cryptomator V8 format", nameof(vault));
            }
            
            _cryptomatorTranslator = (CryptomatorPathTranslator)_pathTranslator;
            _logger = logger;
        }

        // Path translation is now handled by CryptomatorPathTranslator

        #region Directory Operations

        /// <summary>
        /// Creates Cryptomator directory structure:
        /// 1. Reference directory with encrypted name in parent
        /// 2. dir.c9r file containing UUID  
        /// 3. Content directory in d/XX/XXXXXXXX/ structure
        /// 4. dirid.c9r file in content directory
        /// </summary>
        public override async Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await _cryptomatorTranslator.CreateDirectoryAsync(directoryPath, cancellationToken);
        }

        /// <summary>
        /// Deletes Cryptomator directory structure:
        /// 1. Delete all files in content directory
        /// 2. Delete content directory  
        /// 3. Delete dir.c9r file
        /// 4. Delete reference directory
        /// </summary>
        public override async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            await _cryptomatorTranslator.DeleteDirectoryAsync(path, cancellationToken);
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

                // For other directories, check if the reference directory exists and contains dir.c9r
                var pathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(directoryPath);
                string referenceDirectory = pathResult.StoragePath;
                
                // Check if reference directory exists
                if (!await _underlyingStorage.DirectoryExistsAsync(referenceDirectory, cancellationToken))
                {
                    return false;
                }
                
                // Check if dir.c9r file exists in the reference directory
                string dirC9rPath = Path.Combine(referenceDirectory, "dir.c9r");
                if (!await _underlyingStorage.FileExistsAsync(dirC9rPath, cancellationToken))
                {
                    return false;
                }
                
                // Optionally verify that the content directory also exists
                try
                {
                    var directoryMetadata = await _cryptomatorTranslator.LoadDirectoryMetadataFromDirC9rAsync(referenceDirectory, cancellationToken);
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

        public override Task<IEnumerable<string>> GetDirectoriesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("GetDirectoriesAsync - will enumerate reference directories and decrypt names");
        }

        public override Task<IEnumerable<string>> GetDirectoriesAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("GetDirectoriesAsync - will call overload with default parameters");
        }

        public override async Task<IEnumerable<StorageLib.Abstractions.FileObject>> ReadDirAsync(string directoryPath, bool readOnly = true, CancellationToken cancellationToken = default)
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
                var pathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(filePath);
                string encryptedFilePath = pathResult.StoragePath;
                
                // Check if the encrypted .c9r file exists in the content directory
                return await _underlyingStorage.FileExistsAsync(encryptedFilePath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking file existence for: {FilePath}", filePath);
                return false;
            }
        }

        public override Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("DeleteAsync - will translate to content directory and delete .c9r file");
        }

        public override Task<IEnumerable<string>> GetFilesAsync(string directoryPath, string searchPattern, SearchOption searchOption, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("GetFilesAsync - will enumerate .c9r files in content directory and decrypt names");
        }

        public override Task<IEnumerable<string>> GetFilesAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("GetFilesAsync - will call overload with default parameters");
        }

        public override Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("GetFileInfoAsync - will get info from .c9r file and calculate decrypted size");
        }

        public override Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("MoveAsync - will handle moving between encrypted locations");
        }

        #endregion

        #region Directory Enumeration

        private async Task<IEnumerable<StorageLib.Abstractions.FileObject>> ReadDirInternalAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            var fileObjects = new List<StorageLib.Abstractions.FileObject>();
            
            try
            {
                // Get directory metadata for the virtual path
                var directoryMetadata = await GetDirectoryMetadataAsync(realPath);
                
                // Get the paths for this directory
                var pathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(realPath);
                string referenceDirectory = pathResult.ContentDirectoryPath; // For Cryptomator, this is the reference directory for subdirs
                string contentDirectory = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(directoryMetadata)); // Content directory for files
                
                                 // Enumerate subdirectories (these are in the reference directory)
                 if (await _underlyingStorage.DirectoryExistsAsync(referenceDirectory, cancellationToken))
                 {
                     var allItems = await _underlyingStorage.ReadDirAsync(referenceDirectory, true, cancellationToken);
                     var encryptedDirs = allItems.Where(item => item.IsDirectory).Select(item => item.RealPath);
                    foreach (string encryptedDir in encryptedDirs)
                    {
                        try
                        {
                            string encryptedDirName = Path.GetFileName(encryptedDir);
                            
                            // Skip if this is not an encrypted directory (Cryptomator dirs don't have extensions)
                            if (Path.HasExtension(encryptedDirName))
                                continue;
                                
                            string decryptedDirName = _vault.DecryptFilename(encryptedDirName, directoryMetadata);
                            
                            var dirObject = new StorageLib.Abstractions.FileObject(Path.Combine(realPath, decryptedDirName).Replace('\\', '/'))
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
                }
                
                // Enumerate files (these are in the content directory as .c9r files)
                if (await _underlyingStorage.DirectoryExistsAsync(contentDirectory, cancellationToken))
                {
                                         var allContentItems = await _underlyingStorage.ReadDirAsync(contentDirectory, true, cancellationToken);
                     var encryptedFiles = allContentItems.Where(item => !item.IsDirectory && item.Filename.EndsWith(".c9r")).Select(item => item.RealPath);
                    foreach (string encryptedFile in encryptedFiles)
                    {
                        try
                        {
                            string encryptedFileName = Path.GetFileNameWithoutExtension(encryptedFile); // Remove .c9r extension
                            
                            // Skip metadata files (dirid.c9r)
                            if (encryptedFileName == "dirid")
                                continue;
                                
                            string decryptedFileName = _vault.DecryptFilename(encryptedFileName, directoryMetadata);
                            
                            var fileInfo = await _underlyingStorage.GetFileInfoAsync(encryptedFile, cancellationToken);
                            
                            var fileObject = new StorageLib.Abstractions.FileObject(Path.Combine(realPath, decryptedFileName).Replace('\\', '/'))
                            {
                                IsDirectory = false,
                                Filename = decryptedFileName,
                                RealPath = Path.Combine(realPath, decryptedFileName).Replace('\\', '/'),
                                VirtualPath = Path.Combine(realPath, decryptedFileName).Replace('\\', '/'),
                                Size = UvfLib.Vault.VaultHandler.CalculateExpectedDecryptedSize(fileInfo.Size), // Calculate decrypted size
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
                
                _logger?.LogDebug("Enumerated {Count} entries in Cryptomator directory: {Path}", fileObjects.Count, realPath);
                return fileObjects.AsEnumerable();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error enumerating Cryptomator directory: {Path}", realPath);
                // Return empty list if directory can't be read
                return new List<StorageLib.Abstractions.FileObject>();
            }
        }

        /// <summary>
        /// Gets directory metadata for a virtual path by navigating the Cryptomator directory structure
        /// </summary>
        private async Task<UvfLib.Core.Api.DirectoryMetadata> GetDirectoryMetadataAsync(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/")
            {
                return _vault.GetRootDirectoryMetadata();
            }

            // Navigate through the directory hierarchy to get metadata
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            UvfLib.Core.Api.DirectoryMetadata currentMetadata = _vault.GetRootDirectoryMetadata();
            string currentReferenceDir = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());
            
            foreach (string pathPart in pathParts)
            {
                // Encrypt the directory name using current metadata
                string encryptedDirName = _vault.EncryptFilename(pathPart, currentMetadata);
                
                // Move to the reference directory
                currentReferenceDir = Path.Combine(currentReferenceDir, encryptedDirName);
                
                // Load directory metadata from dir.c9r file
                currentMetadata = await _cryptomatorTranslator.LoadDirectoryMetadataFromDirC9rAsync(currentReferenceDir, CancellationToken.None);
            }
            
            return currentMetadata;
        }

        public override Task<IEnumerable<string>> EnumerateFileSystemEntriesAsync(string path, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("EnumerateFileSystemEntriesAsync - will enumerate all entries");
        }

        #endregion
    }
} 