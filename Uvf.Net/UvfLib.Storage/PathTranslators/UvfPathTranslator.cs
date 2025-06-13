using StorageLib.Abstractions;
using UvfLib.Storage.Abstractions;
using UvfLib.Storage.Common;
using UvfLib.Vault;

namespace UvfLib.Storage.PathTranslators
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

        public UvfLib.Storage.Abstractions.VaultFormat Format => UvfLib.Storage.Abstractions.VaultFormat.UVF;
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
                throw new ArgumentException("Invalid virtual path", nameof(virtualPath));
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

            // TODO: Implement full UVF encrypted path translation
            // This requires:
            // 1. Split path into directory parts and filename
            // 2. Encrypt each directory name using appropriate DirectoryMetadata
            // 3. Encrypt filename and add .uvf extension
            // 4. Return: /vault/encrypted_dir1/encrypted_dir2/encrypted_file.txt.uvf
            throw new NotImplementedException("Full UVF encrypted path translation not yet implemented");
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