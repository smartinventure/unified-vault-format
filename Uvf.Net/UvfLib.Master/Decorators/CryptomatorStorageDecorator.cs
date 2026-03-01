using StorageLib.Abstractions;
using UvfLib.Master.PathTranslators;
using UvfLib.Master.Common;
using UvfLib.Vault;
using Microsoft.Extensions.Logging;
using System.IO;
using UvfLib.Core.CryptomatorV8;
using System.Text;
using UvfLib.Master.Abstractions;

namespace UvfLib.Master.Decorators
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

                    // For Cryptomator V8, process .c9r files/directories and .c9s shortened directories
                    if (!encryptedItem.Filename.EndsWith(".c9r") && 
                        !encryptedItem.Filename.EndsWith(".c9s"))
                    {
                        continue;
                    }

                    // Decrypt the filename based on filesystem type and extension
                    string decryptedName;
                    bool isDirectory = false;
                    
                    if (encryptedItem.IsDirectory && encryptedItem.Filename.EndsWith(".c9s"))
                    {
                        // This is a shortened item - need to determine if it's a file or directory
                        // by checking what's inside the .c9s directory
                        try
                        {
                            string originalEncryptedFilename = await ReadOriginalFilenameFromShortenedDirectoryAsync(
                                contentDirectoryPath, encryptedItem.Filename, cancellationToken);
                            
                            decryptedName = await DecryptShortenedFilenameAsync(
                                encryptedItem.Filename, originalEncryptedFilename, directoryMetadata, cancellationToken);
                            
                            // Determine if this shortened item is a file or directory
                            string shortenedDirPath = Path.Combine(contentDirectoryPath, encryptedItem.Filename);
                            string contentsFilePath = Path.Combine(shortenedDirPath, "contents.c9r");
                            string dirFilePath = Path.Combine(shortenedDirPath, "dir.c9r");
                            
                            if (await _underlyingStorage.FileExistsAsync(contentsFilePath, cancellationToken))
                            {
                                // Contains contents.c9r -> it's a shortened file
                                isDirectory = false;
                            }
                            else if (await _underlyingStorage.FileExistsAsync(dirFilePath, cancellationToken))
                            {
                                // Contains dir.c9r -> it's a shortened directory
                                isDirectory = true;
                            }
                            else
                            {
                                _logger?.LogWarning("Shortened directory {ShortenedName} contains neither contents.c9r nor dir.c9r", encryptedItem.Filename);
                                continue; // Skip malformed shortened item
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to decrypt shortened item name: {ShortenedName}", encryptedItem.Filename);
                            continue; // Skip this item if we can't decrypt it
                        }
                    }
                    else if (encryptedItem.Filename.EndsWith(".c9r"))
                    {
                        // This is a regular encrypted item - decrypt normally
                        decryptedName = _vault.DecryptFilename(encryptedItem.Filename, directoryMetadata);
                        isDirectory = encryptedItem.IsDirectory;
                    }
                    else
                    {
                        // Should not happen due to the filter above, but safety check
                        continue;
                    }
                    
                    string decryptedVirtualPath = PathNormalizer.JoinVirtualPath(virtualPath, decryptedName);

                    if (isDirectory)
                    {
                        // This is an encrypted subdirectory (reference directory or shortened directory)
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
                        // This is an encrypted file (regular or shortened)
                        long expectedDecryptedSize;
                        
                        if (encryptedItem.Filename.EndsWith(".c9s"))
                        {
                            // For shortened files, get size from contents.c9r
                            string shortenedDirPath = Path.Combine(contentDirectoryPath, encryptedItem.Filename);
                            string contentsFilePath = Path.Combine(shortenedDirPath, "contents.c9r");
                            var contentsFileInfo = await _underlyingStorage.GetFileInfoAsync(contentsFilePath, cancellationToken);
                            expectedDecryptedSize = VaultHandler.CalculateExpectedDecryptedSize(contentsFileInfo.Size);
                        }
                        else
                        {
                            // Regular file
                            expectedDecryptedSize = VaultHandler.CalculateExpectedDecryptedSize(encryptedItem.Size);
                        }
                        
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
                
                // Check if this is a shortened file (stored as directory with contents.c9r)
                if (encryptedFilePath.EndsWith(".c9s"))
                {
                    // For shortened files, check if the .c9s directory exists and contains contents.c9r
                    if (!await _underlyingStorage.DirectoryExistsAsync(encryptedFilePath, cancellationToken))
                    {
                        return false;
                    }
                    
                    string contentsFilePath = Path.Combine(encryptedFilePath, "contents.c9r");
                    return await _underlyingStorage.FileExistsAsync(contentsFilePath, cancellationToken);
                }
                else
                {
                    // Regular file - check if the encrypted file exists in underlying storage
                    return await _underlyingStorage.FileExistsAsync(encryptedFilePath, cancellationToken);
                }
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
                
                // Check if this is a shortened file
                if (encryptedFilePath.EndsWith(".c9s"))
                {
                    // For shortened files, delete the entire .c9s directory
                    if (!await _underlyingStorage.DirectoryExistsAsync(encryptedFilePath, cancellationToken))
                    {
                        throw new FileNotFoundException($"File not found: {filePath}");
                    }
                    
                    // Delete the entire shortened directory structure
                    await _underlyingStorage.DeleteDirectoryAsync(encryptedFilePath, cancellationToken);
                }
                else
                {
                    // Regular file - verify the file exists before attempting deletion
                    if (!await _underlyingStorage.FileExistsAsync(encryptedFilePath, cancellationToken))
                    {
                        throw new FileNotFoundException($"File not found: {filePath}");
                    }
                    
                    // Delete the encrypted .c9r file
                    await _underlyingStorage.DeleteAsync(encryptedFilePath, cancellationToken);
                }
                
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
                
                FileObject encryptedFileInfo;
                
                // Check if this is a shortened file
                if (encryptedFilePath.EndsWith(".c9s"))
                {
                    // For shortened files, get info from the contents.c9r file
                    string contentsFilePath = Path.Combine(encryptedFilePath, "contents.c9r");
                    encryptedFileInfo = await _underlyingStorage.GetFileInfoAsync(contentsFilePath, cancellationToken);
                }
                else
                {
                    // Regular file - get the encrypted file info from underlying storage
                    encryptedFileInfo = await _underlyingStorage.GetFileInfoAsync(encryptedFilePath, cancellationToken);
                }
                
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

        #region Stream Operations Override

        /// <summary>
        /// Opens a file for writing, handling Cryptomator's shortened filename structure
        /// </summary>
        public override async Task<Stream> OpenWriteAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptomatorStorageDecorator));
            
            try
            {
                // 1. Ensure parent directory exists for the virtual path
                string? parentDir = Path.GetDirectoryName(virtualPath);
                if (!string.IsNullOrEmpty(parentDir) && parentDir != "/" && !await DirectoryExistsAsync(parentDir, cancellationToken))
                {
                    await CreateDirectoryAsync(parentDir, cancellationToken);
                }
                
                // 2. Translate virtual path to physical path
                var pathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(virtualPath);
                string physicalPath = pathResult.StoragePath;
                
                // 3. Check if this is a shortened filename (path ends with .c9s and requires directory creation)
                if (physicalPath.EndsWith(".c9s") && pathResult.RequiresDirectoryCreation)
                {
                    // This is a shortened filename - create the proper directory structure
                    return await CreateShortenedFileStreamAsync(virtualPath, physicalPath, pathResult, cancellationToken);
                }
                else
                {
                    // Regular file - use base implementation
                    return await base.OpenWriteAsync(virtualPath, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error opening Cryptomator file for writing: {VirtualPath}", virtualPath);
                throw new IOException($"Failed to open file '{virtualPath}' for writing: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a file for reading, handling Cryptomator's shortened filename structure
        /// </summary>
        public override async Task<Stream> OpenReadAsync(string virtualPath, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CryptomatorStorageDecorator));
            
            try
            {
                // 1. Translate virtual path to physical path
                var pathResult = await _cryptomatorTranslator.TranslateToStoragePathAsync(virtualPath);
                string physicalPath = pathResult.StoragePath;
                
                // 2. Check if this is a shortened filename (path ends with .c9s and requires directory creation)
                if (physicalPath.EndsWith(".c9s") && pathResult.RequiresDirectoryCreation)
                {
                    // This is a shortened filename - read from the contents.c9r file
                    return await OpenShortenedFileStreamAsync(physicalPath, pathResult, cancellationToken);
                }
                else
                {
                    // Regular file - use base implementation
                    return await base.OpenReadAsync(virtualPath, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error opening Cryptomator file for reading: {VirtualPath}", virtualPath);
                throw new IOException($"Failed to open file '{virtualPath}' for reading: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a stream for reading from a shortened filename structure
        /// </summary>
        private async Task<Stream> OpenShortenedFileStreamAsync(
            string shortenedDirectoryPath, 
            VaultPathResult pathResult, 
            CancellationToken cancellationToken)
        {
            // The shortenedDirectoryPath already points to the .c9s directory
            // No need to remove extension - it's already the directory path
            
            // Open the contents.c9r file for reading
            string contentsFilePath = Path.Combine(shortenedDirectoryPath, "contents.c9r");
            
            if (!await _underlyingStorage.FileExistsAsync(contentsFilePath, cancellationToken))
            {
                throw new FileNotFoundException($"Shortened file contents not found: {contentsFilePath}");
            }
            
            var underlyingHandle = await _underlyingStorage.OpenAsync(contentsFilePath, OpenFlags.ReadOnly, cancellationToken);
            
            // Get underlying stream and wrap with decryption
            var underlyingStream = GetStreamFromUnderlyingHandle(underlyingHandle);
            return _vault.GetDecryptingStream(underlyingStream, leaveOpen: false);
        }

        /// <summary>
        /// Creates a stream for writing to a shortened filename structure
        /// </summary>
        private async Task<Stream> CreateShortenedFileStreamAsync(
            string virtualPath, 
            string shortenedDirectoryPath, 
            VaultPathResult pathResult, 
            CancellationToken cancellationToken)
        {
            // The shortenedDirectoryPath already points to the .c9s directory
            // No need to remove extension - it's already the directory path
            
            // 1. Create the shortened directory (.c9s directory)
            if (!await _underlyingStorage.DirectoryExistsAsync(shortenedDirectoryPath, cancellationToken))
            {
                await _underlyingStorage.CreateDirectoryAsync(shortenedDirectoryPath, cancellationToken);
            }
            
            // 2. Create the name.c9s file with the original encrypted filename
            string nameFilePath = Path.Combine(shortenedDirectoryPath, "name.c9s");
            if (!await _underlyingStorage.FileExistsAsync(nameFilePath, cancellationToken))
            {
                // Get the original encrypted filename from the path translator
                string originalEncryptedFilename = await GetOriginalEncryptedFilenameAsync(virtualPath, pathResult, cancellationToken);
                await WriteOriginalFilenameToNameFileAsync(nameFilePath, originalEncryptedFilename, cancellationToken);
            }
            
            // 3. Open the contents.c9r file for writing
            string contentsFilePath = Path.Combine(shortenedDirectoryPath, "contents.c9r");
            var underlyingHandle = await _underlyingStorage.OpenAsync(contentsFilePath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
            
            // 4. Get underlying stream and wrap with encryption
            var underlyingStream = GetStreamFromUnderlyingHandle(underlyingHandle);
            return _vault.GetEncryptingStream(underlyingStream, leaveOpen: false);
        }

        /// <summary>
        /// Gets the full encrypted filename for a virtual path (the complete encrypted filename that would be used without shortening)
        /// This is used for storing in name.c9s files
        /// </summary>
        private async Task<string> GetFullEncryptedFilenameAsync(
            string virtualPath, 
            VaultPathResult pathResult, 
            CancellationToken cancellationToken)
        {
            // Extract the filename from the virtual path
            string filename = Path.GetFileName(virtualPath);
            
            // Encrypt the filename using the parent directory metadata
            if (pathResult.ParentMetadata == null)
            {
                throw new InvalidOperationException($"Parent metadata not available for path: {virtualPath}");
            }
            
            // Get the encrypted filename from the vault
            string encryptedFilename = _vault.EncryptFilename(filename, pathResult.ParentMetadata);
            
            // The vault should return the encrypted filename with .c9r extension
            // If it returns .c9s, it means the vault is applying shortening, but we want the full name
            if (encryptedFilename.EndsWith(".c9s"))
            {
                // Convert back to what the .c9r filename would have been
                // This is a workaround for the vault applying shortening internally
                string baseEncrypted = encryptedFilename.Substring(0, encryptedFilename.Length - 4);
                return baseEncrypted + ".c9r";
            }
            
            // If it already ends with .c9r, return as-is
            if (encryptedFilename.EndsWith(".c9r"))
            {
                return encryptedFilename;
            }
            
            // If it doesn't have an extension, add .c9r
            return encryptedFilename + ".c9r";
        }

        /// <summary>
        /// Determines if a filename needs shortening according to Cryptomator specification
        /// </summary>
        private bool NeedsShortening(string encryptedFilenameWithExtension)
        {
            return encryptedFilenameWithExtension.Length > 220;
        }

        /// <summary>
        /// Creates a shortened directory name from a long encrypted filename
        /// </summary>
        private string CreateShortenedDirectoryName(string longEncryptedFilename)
        {
            // Use the NameShorteningHelper from the Core library
            return NameShorteningHelper.CreateShortenedDirectoryName(longEncryptedFilename);
        }

        /// <summary>
        /// Gets the shortened encrypted filename for a virtual path (for use as directory names)
        /// This is used for creating .c9s directory names
        /// </summary>
        private async Task<string> GetShortenedEncryptedFilenameAsync(
            string virtualPath, 
            VaultPathResult pathResult, 
            CancellationToken cancellationToken)
        {
            // For shortened filenames, we can use the vault's method which applies shortening
            string filename = Path.GetFileName(virtualPath);
            
            if (pathResult.ParentMetadata == null)
            {
                throw new InvalidOperationException($"Parent metadata not available for path: {virtualPath}");
            }
            
            string encryptedFilename = _vault.EncryptFilename(filename, pathResult.ParentMetadata);
            
            // Convert .c9r to .c9s for shortened directory names
            if (encryptedFilename.EndsWith(".c9r"))
            {
                encryptedFilename = encryptedFilename.Substring(0, encryptedFilename.Length - 4) + ".c9s";
            }
            
            return encryptedFilename;
        }

        /// <summary>
        /// Gets the original encrypted filename for a virtual path (legacy method)
        /// </summary>
        private async Task<string> GetOriginalEncryptedFilenameAsync(
            string virtualPath, 
            VaultPathResult pathResult, 
            CancellationToken cancellationToken)
        {
            // Use the full encrypted filename method
            return await GetFullEncryptedFilenameAsync(virtualPath, pathResult, cancellationToken);
        }

        /// <summary>
        /// Writes the original encrypted filename to the name.c9s file
        /// </summary>
        private async Task WriteOriginalFilenameToNameFileAsync(
            string nameFilePath, 
            string originalEncryptedFilename, 
            CancellationToken cancellationToken)
        {
            // The name.c9s file contains the original encrypted filename as PLAINTEXT
            // The originalEncryptedFilename is already encrypted, so we just store it as-is
            byte[] originalFilenameBytes = System.Text.Encoding.UTF8.GetBytes(originalEncryptedFilename);
            
            // Open the name.c9s file for writing and write as plaintext
            var nameFileHandle = await _underlyingStorage.OpenAsync(nameFilePath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
            
            try
            {
                // Write the encrypted filename as plaintext (no additional encryption)
                IntPtr dataPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(originalFilenameBytes.Length);
                try
                {
                    System.Runtime.InteropServices.Marshal.Copy(originalFilenameBytes, 0, dataPtr, originalFilenameBytes.Length);
                    await _underlyingStorage.WriteAsync(nameFilePath, nameFileHandle, 0, originalFilenameBytes.Length, dataPtr, cancellationToken);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(dataPtr);
                }
            }
            finally
            {
                await _underlyingStorage.CloseAsync(nameFilePath, nameFileHandle, cancellationToken);
            }
        }

        /// <summary>
        /// Helper method to get stream from underlying handle (copied from base class)
        /// </summary>
        private Stream GetStreamFromUnderlyingHandle(IntPtr underlyingHandle)
        {
            // Convert the underlying storage's handle back to a FileHandle to get the stream
            var fileHandle = FileHandle.FromContext(underlyingHandle);
            if (fileHandle?.Stream == null)
            {
                throw new InvalidOperationException("Could not retrieve stream from underlying storage handle");
            }
            
            return fileHandle.Stream;
        }

        #endregion

        #region Name Shortening Support

        /// <summary>
        /// Reads the original encrypted filename from a shortened directory's name.c9s file.
        /// </summary>
        /// <param name="contentDirectoryPath">The content directory path where the shortened directory is located</param>
        /// <param name="shortenedDirectoryName">The shortened directory name (e.g., "ABC123.c9s")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The original long encrypted filename</returns>
        private async Task<string> ReadOriginalFilenameFromShortenedDirectoryAsync(
            string contentDirectoryPath, 
            string shortenedDirectoryName, 
            CancellationToken cancellationToken)
        {
            string shortenedDirectoryPath = Path.Combine(contentDirectoryPath, shortenedDirectoryName);
            string nameFilePath = Path.Combine(shortenedDirectoryPath, "name.c9s");

            if (!await _underlyingStorage.FileExistsAsync(nameFilePath, cancellationToken))
            {
                throw new FileNotFoundException($"Name file not found in shortened directory: {nameFilePath}");
            }

            // Read the original filename from the name.c9s file as PLAINTEXT
            // According to Cryptomator spec, name.c9s contains the original encrypted filename as plaintext
            IntPtr fileHandle = await _underlyingStorage.OpenAsync(nameFilePath, OpenFlags.ReadOnly, cancellationToken);
            try
            {
                var fileInfo = await _underlyingStorage.GetFileInfoAsync(nameFilePath, cancellationToken);
                long fileSize = fileInfo.Size;

                if (fileSize <= 0)
                {
                    throw new InvalidDataException($"Name file is empty: {nameFilePath}");
                }

                // Read file content as plaintext (no decryption needed)
                IntPtr dataPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)fileSize);
                try
                {
                    await _underlyingStorage.ReadAsync(nameFilePath, fileHandle, 0, fileSize, dataPtr, cancellationToken);
                    
                    // Copy to managed array
                    byte[] fileBytes = new byte[fileSize];
                    System.Runtime.InteropServices.Marshal.Copy(dataPtr, fileBytes, 0, (int)fileSize);
                    
                    // Convert to string and trim
                    string originalFilename = Encoding.UTF8.GetString(fileBytes).Trim();
                    
                    if (string.IsNullOrEmpty(originalFilename))
                    {
                        throw new InvalidDataException($"Name file contains empty filename: {nameFilePath}");
                    }

                    return originalFilename;
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(dataPtr);
                }
            }
            finally
            {
                await _underlyingStorage.CloseAsync(nameFilePath, fileHandle, cancellationToken);
            }
        }

        /// <summary>
        /// Decrypts a shortened filename using the original encrypted filename.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name</param>
        /// <param name="originalEncryptedFilename">The original long encrypted filename</param>
        /// <param name="directoryMetadata">The directory metadata for decryption context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The decrypted filename</returns>
        private async Task<string> DecryptShortenedFilenameAsync(
            string shortenedDirectoryName,
            string originalEncryptedFilename,
            UvfLib.Core.Api.DirectoryMetadata directoryMetadata,
            CancellationToken cancellationToken)
        {
            // Note: We don't validate the shortened name against the original filename
            // because Cryptomator's shortening algorithm uses a hash/truncation method
            // that doesn't allow for simple reverse validation

            // The originalEncryptedFilename should include the .c9r extension
            if (!originalEncryptedFilename.EndsWith(".c9r"))
            {
                throw new InvalidDataException(
                    $"Original encrypted filename should end with .c9r extension: '{originalEncryptedFilename}'");
            }

            // Decrypt the original filename - the VaultHandler should properly handle the .c9r extension
            return _vault.DecryptFilename(originalEncryptedFilename, directoryMetadata);
        }

        /// <summary>
        /// Writes the original encrypted filename to a shortened directory's name.c9s file.
        /// </summary>
        /// <param name="contentDirectoryPath">The content directory path</param>
        /// <param name="shortenedDirectoryName">The shortened directory name</param>
        /// <param name="originalEncryptedFilename">The original long encrypted filename to store</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task WriteOriginalFilenameToShortenedDirectoryAsync(
            string contentDirectoryPath,
            string shortenedDirectoryName,
            string originalEncryptedFilename,
            CancellationToken cancellationToken)
        {
            string shortenedDirectoryPath = Path.Combine(contentDirectoryPath, shortenedDirectoryName);
            
            // Ensure the shortened directory exists
            if (!await _underlyingStorage.DirectoryExistsAsync(shortenedDirectoryPath, cancellationToken))
            {
                await _underlyingStorage.CreateDirectoryAsync(shortenedDirectoryPath, cancellationToken);
            }

            string nameFilePath = NameShorteningHelper.GetInflatedNameFilePath(shortenedDirectoryName);
            string fullNameFilePath = Path.Combine(contentDirectoryPath, nameFilePath);

            // Write the original filename to the name.c9s file
            byte[] filenameBytes = Encoding.UTF8.GetBytes(originalEncryptedFilename);
            
            IntPtr fileHandle = await _underlyingStorage.OpenAsync(fullNameFilePath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
            try
            {
                IntPtr dataPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(filenameBytes.Length);
                try
                {
                    System.Runtime.InteropServices.Marshal.Copy(filenameBytes, 0, dataPtr, filenameBytes.Length);
                    await _underlyingStorage.WriteAsync(nameFilePath, fileHandle, 0, filenameBytes.Length, dataPtr, cancellationToken);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(dataPtr);
                }
            }
            finally
            {
                await _underlyingStorage.CloseAsync(fullNameFilePath, fileHandle, cancellationToken);
            }
        }

        /// <summary>
        /// Extracts the directory ID from directory metadata for use with DirectoryContentCryptor methods.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>The Base64Url encoded directory ID</returns>
        private string GetDirectoryIdFromMetadata(UvfLib.Core.Api.DirectoryMetadata directoryMetadata)
        {
            // For Cryptomator V8, we need to extract the directory ID
            // This is implementation-specific and may need adjustment based on the actual metadata structure
            if (directoryMetadata is UvfLib.Core.CryptomatorV8.DirectoryMetadataImpl cryptomatorMetadata)
            {
                return Convert.ToBase64String(cryptomatorMetadata.DirIdBytes())
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .TrimEnd('=');
            }
            
            throw new NotSupportedException("Directory metadata type not supported for name shortening");
        }

        #endregion
    }
} 