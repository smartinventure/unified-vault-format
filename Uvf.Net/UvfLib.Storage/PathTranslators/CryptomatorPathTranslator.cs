using StorageLib.Abstractions;
using UvfLib.Core.Api;
using UvfLib.Storage.Abstractions;
using UvfLib.Vault;
using DirectoryMetadata = UvfLib.Core.Api.DirectoryMetadata;

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

        public CryptomatorPathTranslator(VaultHandler vault, IStorage underlyingStorage, string vaultBasePath)
        {
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
            _underlyingStorage = underlyingStorage ?? throw new ArgumentNullException(nameof(underlyingStorage));
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
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == "/")
            {
                // Root directory - return the root directory path
                return new VaultPathResult
                {
                    StoragePath = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath()),
                    ContentDirectoryPath = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath()),
                    IsEncrypted = true,
                    RequiresDirectoryCreation = false
                };
            }

            // Split path into components
            string[] pathParts = virtualPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            // Navigate through the directory hierarchy
            DirectoryMetadata currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string currentReferenceDir = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());

            // Process each directory part except the last (which might be a file)
            for (int i = 0; i < pathParts.Length - 1; i++)
            {
                string dirName = pathParts[i];
                
                // Encrypt directory name using current directory's metadata
                string encryptedDirName = _vault.EncryptFilename(dirName, currentDirMetadata);
                
                // Move to the reference directory
                currentReferenceDir = Path.Combine(currentReferenceDir, encryptedDirName);
                
                // Load the subdirectory metadata from dir.c9r
                currentDirMetadata = await LoadDirectoryMetadataFromDirC9rAsync(currentReferenceDir, CancellationToken.None);
            }

            // Handle the final path component (could be file or directory)
            string finalName = pathParts[pathParts.Length - 1];
            string encryptedFinalName = _vault.EncryptFilename(finalName, currentDirMetadata);
            
            if (IsDirectory(virtualPath))
            {
                // It's a directory - return the reference directory path
                return new VaultPathResult
                {
                    StoragePath = Path.Combine(currentReferenceDir, encryptedFinalName),
                    ContentDirectoryPath = Path.Combine(currentReferenceDir, encryptedFinalName),
                    EncryptedFilename = encryptedFinalName,
                    ParentMetadata = currentDirMetadata,
                    IsEncrypted = true,
                    RequiresDirectoryCreation = true
                };
            }
            else
            {
                // It's a file - return the path in the content directory
                string contentDir = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(currentDirMetadata));
                return new VaultPathResult
                {
                    StoragePath = Path.Combine(contentDir, encryptedFinalName + GetEncryptedFileExtension()),
                    ContentDirectoryPath = contentDir,
                    EncryptedFilename = encryptedFinalName + GetEncryptedFileExtension(),
                    ParentMetadata = currentDirMetadata,
                    IsEncrypted = true,
                    RequiresDirectoryCreation = false
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
        private async Task<DirectoryMetadata> LoadDirectoryMetadataFromDirC9rAsync(string referenceDir, CancellationToken cancellationToken)
        {
            string dirC9rPath = Path.Combine(referenceDir, "dir.c9r");
            
            if (!await _underlyingStorage.FileExistsAsync(dirC9rPath, cancellationToken))
            {
                throw new DirectoryNotFoundException($"dir.c9r file not found at {dirC9rPath}");
            }

            // Read the UUID from dir.c9r (plaintext)
            string uuidString = await File.ReadAllTextAsync(dirC9rPath, cancellationToken);
            uuidString = uuidString.Trim();

            // Create directory metadata from the UUID
            return _vault.CreateCryptomatorV8DirectoryMetadataFromUuid(uuidString);
        }

        /// <summary>
        /// Creates a new directory in the Cryptomator V8 structure.
        /// This follows the exact Program.cs CreateDirectory pattern.
        /// </summary>
        public async Task CreateDirectoryAsync(string virtualDirPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(virtualDirPath) || virtualDirPath == "/")
            {
                // Root directory should already exist
                return;
            }

            string[] pathParts = virtualDirPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            DirectoryMetadata currentDirMetadata = _vault.GetRootDirectoryMetadata();
            string currentReferenceDir = Path.Combine(_vaultBasePath, _vault.GetRootDirectoryPath());

            // Navigate through the path, creating directories as needed
            foreach (string dirName in pathParts)
            {
                // 1. Create new directory metadata (UUID)
                DirectoryMetadata subDirMetadata = _vault.CreateNewDirectoryMetadata();
                
                // 2. Encrypt directory name
                string encryptedSubDirName = _vault.EncryptFilename(dirName, currentDirMetadata);
                
                // 3. Create reference directory
                string subDirReferenceDir = Path.Combine(currentReferenceDir, encryptedSubDirName);
                await _underlyingStorage.CreateDirectoryAsync(subDirReferenceDir, cancellationToken);
                
                // 4. Create dir.c9r file (plaintext UUID)
                byte[] decodedDirIdBytes = Convert.FromBase64String(subDirMetadata.DirId);
                string rawUuidString = System.Text.Encoding.ASCII.GetString(decodedDirIdBytes);
                string dirC9rPath = Path.Combine(subDirReferenceDir, "dir.c9r");
                await File.WriteAllTextAsync(dirC9rPath, rawUuidString, cancellationToken);
                
                // 5. Create actual content directory
                string actualContentPath = Path.Combine(_vaultBasePath, _vault.GetDirectoryPath(subDirMetadata));
                await _underlyingStorage.CreateDirectoryAsync(actualContentPath, cancellationToken);
                
                // 6. Create dirid.c9r in content directory (encrypted own UUID)
                string diridFilePath = Path.Combine(actualContentPath, "dirid.c9r");
                using (FileStream diridStream = File.Create(diridFilePath))
                using (Stream encryptingStream = _vault.GetEncryptingStream(diridStream))
                {
                    byte[] dirIdBytes = System.Text.Encoding.ASCII.GetBytes(rawUuidString);
                    await encryptingStream.WriteAsync(dirIdBytes, 0, dirIdBytes.Length, cancellationToken);
                }
                
                // Move to the next level
                currentDirMetadata = subDirMetadata;
                currentReferenceDir = subDirReferenceDir;
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