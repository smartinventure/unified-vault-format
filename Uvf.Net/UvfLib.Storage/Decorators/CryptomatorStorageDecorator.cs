using StorageLib.Abstractions;
using UvfLib.Storage.PathTranslators;
using UvfLib.Storage.Common;
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
            // Normalize virtual path
            directoryPath = PathNormalizer.NormalizeVirtualPath(directoryPath);
            
            try
            {
                await _cryptomatorTranslator.CreateDirectoryAsync(directoryPath, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to create directory '{directoryPath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Deletes Cryptomator directory structure:
        /// 1. Delete all files in content directory
        /// 2. Delete content directory  
        /// 3. Delete dir.c9r file
        /// 4. Delete reference directory
        /// </summary>
        public override async Task DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            // Normalize virtual path
            directoryPath = PathNormalizer.NormalizeVirtualPath(directoryPath);
            
            try
            {
                await _cryptomatorTranslator.DeleteDirectoryAsync(directoryPath, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error deleting Cryptomator directory: {DirectoryPath}", directoryPath);
                throw new IOException($"Failed to delete directory '{directoryPath}': {ex.Message}", ex);
            }
        }

        public override async Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            // Normalize virtual path
            directoryPath = PathNormalizer.NormalizeVirtualPath(directoryPath);
            
            try
            {
                // Handle root directory - it should always exist if vault is initialized
                if (directoryPath == PathNormalizer.VirtualRoot)
                {
                    string rootDirPath = PathNormalizer.NormalizeVaultDirectoryPath(_vault.GetRootDirectoryPath());
                    string rootDirectory = PathNormalizer.CombineWithMountPoint(_vaultBasePath, rootDirPath);
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
                    string vaultDirPath = _vault.GetDirectoryPath(directoryMetadata);
                    string normalizedVaultDirPath = PathNormalizer.NormalizeVaultDirectoryPath(vaultDirPath);
                    string contentDirectory = PathNormalizer.CombineWithMountPoint(_vaultBasePath, normalizedVaultDirPath);
                    
                    return await _underlyingStorage.DirectoryExistsAsync(contentDirectory, cancellationToken);
                }
                catch
                {
                    // If we can't load metadata, assume directory doesn't exist properly
                    return false;
                }
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                _logger?.LogError(ex, "Error checking Cryptomator directory existence: {DirectoryPath}", directoryPath);
                return false;
            }
        }

        /// <summary>
        /// Reads directory contents and returns decrypted file/directory names.
        /// This method translates virtual vault paths to storage paths and decrypts the contents.
        /// </summary>
        public override async Task<IEnumerable<FileObject>> ReadDirAsync(string virtualPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            // Normalize the virtual path
            virtualPath = PathNormalizer.NormalizeVirtualPath(virtualPath);
            
            try
            {
                // Translate virtual path to storage path
                var pathResult = await _pathTranslator.TranslateToStoragePathAsync(virtualPath);
                
                // For directories, we need to read from the content directory, not the reference directory
                // The pathResult.StoragePath for directories points to the reference directory
                // But we need to read the actual content from the content directory
                string contentDirectoryPath;
                UvfLib.Core.Api.DirectoryMetadata directoryMetadata;
                
                if (virtualPath == PathNormalizer.VirtualRoot)
                {
                    // Root directory - use the root content directory directly
                    contentDirectoryPath = pathResult.ContentDirectoryPath;
                    directoryMetadata = _vault.GetRootDirectoryMetadata();
                }
                else
                {
                    // For subdirectories, we need to load the directory metadata from dir.c9r
                    // and then get the actual content directory
                    string referenceDirectory = pathResult.StoragePath;
                    directoryMetadata = await _cryptomatorTranslator.LoadDirectoryMetadataFromDirC9rAsync(referenceDirectory, cancellationToken);
                    
                    // Get the actual content directory where files are stored
                    string vaultDirPath = _vault.GetDirectoryPath(directoryMetadata);
                    string normalizedVaultDirPath = PathNormalizer.NormalizeVaultDirectoryPath(vaultDirPath);
                    contentDirectoryPath = PathNormalizer.CombineWithMountPoint(_vaultBasePath, normalizedVaultDirPath);
                }

                // Check if the content directory exists
                if (!await _underlyingStorage.DirectoryExistsAsync(contentDirectoryPath, cancellationToken))
                {
                    throw new DirectoryNotFoundException($"Content directory not found: {contentDirectoryPath}");
                }

                // Read the encrypted directory contents using IStorage
                var encryptedItems = await _underlyingStorage.ReadDirAsync(contentDirectoryPath, readOnly, cancellationToken);
                
                var fileObjects = new List<FileObject>();

                // Process each encrypted item
                foreach (var encryptedItem in encryptedItems)
                {

                        // Skip metadata files
                        if (IsMetadataFile(encryptedItem.Filename))
                        {
                            continue;
                        }

                        // For Cryptomator V8, process .c9r files/directories and .c9r.c9r encrypted files
                        if (!encryptedItem.Filename.EndsWith(".c9r") && !encryptedItem.Filename.EndsWith(".c9r.c9r"))
                        {
                            continue;
                        }

                        // Decrypt the filename - pass the full encrypted filename with extension
                        // The DecryptFilename method expects and handles the .c9r extension internally
                        string decryptedName;
                        if (encryptedItem.Filename.EndsWith(".c9r.c9r"))
                        {
                            // This is an encrypted file - remove only the outer .c9r extension
                            string encryptedFilenameForDecryption = encryptedItem.Filename.Substring(0, encryptedItem.Filename.Length - 4);
                            decryptedName = _vault.DecryptFilename(encryptedFilenameForDecryption, directoryMetadata);
                        }
                        else if (encryptedItem.Filename.EndsWith(".c9r"))
                        {
                            // This is an encrypted directory - pass the full filename
                            decryptedName = _vault.DecryptFilename(encryptedItem.Filename, directoryMetadata);
                        }
                        else
                        {
                            // Should not happen due to the filter above, but safety check
                            continue;
                        }
                        
                        string decryptedVirtualPath = PathNormalizer.JoinVirtualPath(virtualPath, decryptedName);

                        if (encryptedItem.IsDirectory)
                        {
                            // This is an encrypted subdirectory (reference directory)
                            var dirObject = new FileObject(decryptedVirtualPath)
                            {
                                IsDirectory = true,
                                Filename = decryptedName,
                                RealPath = decryptedVirtualPath,
                                VirtualPath = decryptedVirtualPath,
                                Size = 0,
                                CreationTime = encryptedItem.CreationTime,
                                LastModified = encryptedItem.LastModified,
                                LastAccessTime = encryptedItem.LastAccessTime,
                                SC = this
                            };
                            
                            fileObjects.Add(dirObject);
                            
                        }
                        else
                        {
                            // This is an encrypted file
                            // Calculate expected decrypted size
                            long expectedDecryptedSize = VaultHandler.CalculateExpectedDecryptedSize(encryptedItem.Size);
                            
                            var fileObject = new FileObject(decryptedVirtualPath)
                            {
                                IsDirectory = false,
                                Filename = decryptedName,
                                RealPath = decryptedVirtualPath,
                                VirtualPath = decryptedVirtualPath,
                                Size = expectedDecryptedSize, // Show decrypted size to user
                                CreationTime = encryptedItem.CreationTime,
                                LastModified = encryptedItem.LastModified,
                                LastAccessTime = encryptedItem.LastAccessTime,
                                SC = this
                            };
                            
                            fileObjects.Add(fileObject);
                            
                        }

                }


                return fileObjects;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to read directory: {VirtualPath}", virtualPath);
                throw;
            }
        }

        /// <summary>
        /// Checks if a filename is a metadata file that should be skipped during directory enumeration.
        /// </summary>
        private bool IsMetadataFile(string filename)
        {
            return filename == "dirid.c9r" || filename == "dir.c9r" || filename == _vault.GetDirectoryMetadataFilename();
        }

        #endregion

        #region File Operations

        public override async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            // Normalize virtual path
            filePath = PathNormalizer.NormalizeVirtualPath(filePath);
            
            try
            {
                // Translate the virtual file path to the physical encrypted file path
                var pathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(filePath);
                string encryptedFilePath = pathResult.StoragePath;
                
                // Check if the encrypted file exists in underlying storage
                return await _underlyingStorage.FileExistsAsync(encryptedFilePath, cancellationToken);
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                _logger?.LogError(ex, "Error checking Cryptomator file existence: {FilePath}", filePath);
                return false;
            }
        }

        public override async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
        {
            // Normalize virtual path
            filePath = PathNormalizer.NormalizeVirtualPath(filePath);
            
            try
            {
                // Translate the virtual file path to the physical encrypted file path
                var pathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(filePath);
                string encryptedFilePath = pathResult.StoragePath;
                
                // Verify the file exists before attempting deletion
                if (!await _underlyingStorage.FileExistsAsync(encryptedFilePath, cancellationToken))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }
                
                // Delete the encrypted .c9r file
                await _underlyingStorage.DeleteAsync(encryptedFilePath, cancellationToken);
                
            }
            catch (Exception ex) when (!(ex is FileNotFoundException || ex is ArgumentException))
            {
                _logger?.LogError(ex, "Error deleting Cryptomator file: {FilePath}", filePath);
                throw new IOException($"Failed to delete file '{filePath}': {ex.Message}", ex);
            }
        }

        public override async Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            // Normalize virtual path
            filePath = PathNormalizer.NormalizeVirtualPath(filePath);
            
            try
            {
                // Translate the virtual file path to the physical encrypted file path
                var pathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(filePath);
                string encryptedFilePath = pathResult.StoragePath;
                
                // Get the encrypted file info from underlying storage
                var encryptedFileInfo = await _underlyingStorage.GetFileInfoAsync(encryptedFilePath, cancellationToken);
                
                // Calculate the decrypted size using VaultHandler
                long decryptedSize = VaultHandler.CalculateExpectedDecryptedSize(encryptedFileInfo.Size);
                
                // Create virtual file object with decrypted information
                var virtualFileInfo = new FileObject(filePath)
                {
                    IsDirectory = false,
                    Filename = Path.GetFileName(filePath),
                    RealPath = filePath,
                    VirtualPath = filePath,
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
                _logger?.LogError(ex, "Error getting Cryptomator file info: {FilePath}", filePath);
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
                    // Moving a directory - this is complex for Cryptomator due to the 4-component structure
                    await MoveDirectoryAsync(sourceFilePath, destinationFilePath, overwrite, cancellationToken);
                }
                
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is FileNotFoundException || ex is IOException))
            {
                _logger?.LogError(ex, "Error moving Cryptomator item: {SourcePath} -> {DestinationPath}", sourceFilePath, destinationFilePath);
                throw new IOException($"Failed to move '{sourceFilePath}' to '{destinationFilePath}': {ex.Message}", ex);
            }
        }

        private async Task MoveFileAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken)
        {
            // Translate both paths to encrypted storage paths
            var sourcePathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(sourceFilePath);
            var destPathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(destinationFilePath);
            
            string sourceEncryptedPath = sourcePathResult.StoragePath;
            string destEncryptedPath = destPathResult.StoragePath;
            
            // Use underlying storage to move the encrypted file
            await _underlyingStorage.MoveAsync(sourceEncryptedPath, destEncryptedPath, overwrite, cancellationToken);
        }

        private async Task MoveDirectoryAsync(string sourceDirectoryPath, string destinationDirectoryPath, bool overwrite, CancellationToken cancellationToken)
        {
            // Moving directories in Cryptomator is complex because of the 4-component structure:
            // 1. Reference directory (with encrypted name)
            // 2. dir.c9r file (contains UUID)
            // 3. Content directory (d/XX/XXXXXXXX/)
            // 4. dirid.c9r file (in content directory)
            
            // For now, throw NotImplementedException as this requires careful handling
            // to avoid breaking the vault structure
            throw new NotImplementedException("Directory moving in Cryptomator vaults requires careful implementation to maintain vault integrity. Use file-level operations instead.");
        }

        #endregion
    }
} 