using StorageLib.Abstractions;
using UvfLib.Master.Abstractions;
using UvfLib.Master.Common;
using UvfLib.Vault;

namespace UvfLib.Master.PathTranslators
{
    /// <summary>
    /// Path translator for UVF format.
    /// 
    /// UVF structure:
    /// - Simple encrypted filenames approach
    /// - Files stored with encrypted names + .uvf extension
    /// - Directory structure mirrors virtual structure but with encrypted names
    /// </summary>
    public class UvfPathTranslator : IVaultPathTranslator
    {
        private readonly VaultHandler _vault;
        private readonly IStorage _underlyingStorage;
        private readonly string _vaultBasePath;
        private readonly bool _encryptFilenames;
        private bool _disposed;

        public UvfPathTranslator(VaultHandler vault, IStorage underlyingStorage, string vaultBasePath, bool encryptFilenames = true)
        {
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _underlyingStorage = underlyingStorage ?? throw new ArgumentNullException(nameof(underlyingStorage));
            _vaultBasePath = vaultBasePath ?? throw new ArgumentNullException(nameof(vaultBasePath));
            _encryptFilenames = encryptFilenames;
            
            if (_vault.IsCryptomatorV8())
            {
                throw new ArgumentException("VaultHandler must be configured for UVF format, not Cryptomator V8", nameof(vault));
            }
        }

        #region IVaultPathTranslator Properties

        public UvfLib.Master.Abstractions.VaultFormat Format => UvfLib.Master.Abstractions.VaultFormat.UVF;
        public bool IsEncryptionEnabled => _encryptFilenames;
        public string BaseStoragePath => _vaultBasePath;

        #endregion

        #region Path Translation

        public async Task<VaultPathResult> TranslateToStoragePathAsync(string virtualPath)
        {
            // Normalize the virtual path first
            virtualPath = PathNormalizer.NormalizeVirtualPath(virtualPath);
            
            if (virtualPath == PathNormalizer.VirtualRoot)
            {
                // Root directory - return the vault base path as the root
                return new VaultPathResult
                {
                    StoragePath = _vaultBasePath,
                    ContentDirectoryPath = _vaultBasePath,
                    IsEncrypted = _encryptFilenames,
                    RequiresDirectoryCreation = false
                };
            }

            if (!_encryptFilenames)
            {
                // Simple mode: just use path directly with .uvf extension for files
                string physicalPath = PathNormalizer.VirtualToPhysicalPath(virtualPath);
                
                if (IsDirectory(virtualPath))
                {
                    string simpleDirPath = PathNormalizer.CombineWithMountPoint(_vaultBasePath, physicalPath);
                    return new VaultPathResult
                    {
                        StoragePath = simpleDirPath,
                        ContentDirectoryPath = simpleDirPath,
                        IsEncrypted = false,
                        RequiresDirectoryCreation = false
                    };
                }
                else
                {
                    string simpleFilePath = PathNormalizer.CombineWithMountPoint(_vaultBasePath, physicalPath + GetEncryptedFileExtension());
                    
                    // Debug output to track path translation
                    Console.WriteLine($"🔍 UvfPathTranslator: '{virtualPath}' → '{simpleFilePath}'");
                    
                    return new VaultPathResult
                    {
                        StoragePath = simpleFilePath,
                        ContentDirectoryPath = Path.GetDirectoryName(simpleFilePath) ?? _vaultBasePath,
                        EncryptedFilename = Path.GetFileName(simpleFilePath),
                        IsEncrypted = false,
                        RequiresDirectoryCreation = false
                    };
                }
            }

            // Encrypted mode: implement full UVF encrypted path translation
            return await TranslateEncryptedPathAsync(virtualPath);
        }

        public async Task<string?> TranslateToVirtualPathAsync(string storagePath)
        {
            // TODO: Implement reverse translation for UVF
            throw new NotImplementedException("UVF physical-to-virtual translation not yet implemented");
        }

        #endregion

        #region Vault Format Methods

        public string GetEncryptedFileExtension()
        {
            return ".uvf";
        }

        public string GetMetadataFileName()
        {
            return "dir.uvf";
        }

        #endregion

        #region Helper Methods

        private bool IsDirectory(string virtualPath)
        {
            // Simple heuristic - directories typically don't have extensions
            // This might need refinement based on your specific use case
            return !Path.HasExtension(virtualPath);
        }

        private string GetFileNameHash(string fileName)
        {
            // Create a short hash of the filename for truncation purposes
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            byte[] hashBytes = sha1.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fileName));
            
            // Take first 8 bytes and convert to hex (16 characters)
            return Convert.ToHexString(hashBytes).Substring(0, 16);
        }

        private async Task<VaultPathResult> TranslateEncryptedPathAsync(string virtualPath)
        {
            // Split the virtual path into directory parts and filename
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            if (pathParts.Length == 0)
            {
                // This should not happen as root is handled earlier
                throw new ArgumentException("Invalid virtual path", nameof(virtualPath));
            }

            // Start with root directory metadata
            var currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string currentContentPath = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(currentDirMetadata));

            // Navigate through directory hierarchy
            for (int i = 0; i < pathParts.Length - 1; i++) // All parts except the last (which is the file/final directory)
            {
                string dirName = pathParts[i];
                
                // Encrypt the directory name using current directory metadata
                string encryptedDirName = _vault.EncryptFilename(dirName, currentDirMetadata);
                
                // Create reference directory path (contains dir.uvf)
                string referenceDirectoryPath = Path.Combine(currentContentPath, encryptedDirName);
                
                // Load the subdirectory metadata from dir.uvf file
                currentDirMetadata = await LoadDirectoryMetadataAsync(referenceDirectoryPath);
                
                // Update current content path to the subdirectory's content location
                currentContentPath = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(currentDirMetadata));
            }

            // Handle the final path component (file or final directory)
            string finalComponent = pathParts[pathParts.Length - 1];
            
            if (IsDirectory(virtualPath))
            {
                // Final component is a directory
                string encryptedDirName = _vault.EncryptFilename(finalComponent, currentDirMetadata);
                string referenceDirectoryPath = Path.Combine(currentContentPath, encryptedDirName);
                
                return new VaultPathResult
                {
                    StoragePath = referenceDirectoryPath,
                    ContentDirectoryPath = currentContentPath,
                    EncryptedFilename = encryptedDirName,
                    IsEncrypted = true,
                    RequiresDirectoryCreation = true
                };
            }
            else
            {
                // Final component is a file
                string encryptedFileName = _vault.EncryptFilename(finalComponent, currentDirMetadata);
                
                // Check if the encrypted filename is too long for Windows filesystem
                // Windows has a 260 character path limit, but let's be conservative and use 200 for filename
                const int MAX_FILENAME_LENGTH = 200;
                if (encryptedFileName.Length > MAX_FILENAME_LENGTH)
                {
                    // Simple truncation approach - take first part + hash of full name + extension
                    string hashSuffix = GetFileNameHash(encryptedFileName);
                    string baseExtension = GetEncryptedFileExtension();
                    int availableLength = MAX_FILENAME_LENGTH - hashSuffix.Length - baseExtension.Length - 1; // -1 for underscore
                    
                    if (availableLength > 0)
                    {
                        string truncatedBase = encryptedFileName.Substring(0, Math.Min(availableLength, encryptedFileName.Length - baseExtension.Length));
                        encryptedFileName = truncatedBase + "_" + hashSuffix + baseExtension;
                    }
                    else
                    {
                        // Fallback: just use hash + extension
                        encryptedFileName = hashSuffix + baseExtension;
                    }
                }
                
                string encryptedFilePath = Path.Combine(currentContentPath, encryptedFileName);
                
                // Debug output to track path translation
                Console.WriteLine($"🔍 UvfPathTranslator: '{virtualPath}' → '{encryptedFilePath}'");
                
                return new VaultPathResult
                {
                    StoragePath = encryptedFilePath,
                    ContentDirectoryPath = currentContentPath,
                    EncryptedFilename = encryptedFileName,
                    IsEncrypted = true,
                    RequiresDirectoryCreation = false
                };
            }
        }

        private async Task<UvfLib.Core.Api.DirectoryMetadata> LoadDirectoryMetadataAsync(string referenceDirectoryPath)
        {
            string dirUvfPath = Path.Combine(referenceDirectoryPath, GetMetadataFileName());
            
            if (!await _underlyingStorage.FileExistsAsync(dirUvfPath))
            {
                throw new DirectoryNotFoundException($"Directory metadata file not found: {dirUvfPath}");
            }
            
            // Read the dir.uvf file
            var fileInfo = await _underlyingStorage.GetFileInfoAsync(dirUvfPath);
            var buffer = new byte[fileInfo.Size];
            
            IntPtr fileHandle = await _underlyingStorage.OpenAsync(dirUvfPath, OpenFlags.ReadOnly);
            try
            {
                await _underlyingStorage.ReadAsync(fileHandle, 0, fileInfo.Size, System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0));
                return _vault.DecryptDirectoryMetadata(buffer);
            }
            finally
            {
                await _underlyingStorage.CloseAsync(fileHandle);
            }
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