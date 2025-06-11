using StorageLib.Abstractions;
using UvfLib.Storage.Abstractions;
using UvfLib.Core.Api;

namespace UvfLib.Storage.Translators
{
    /// <summary>
    /// UVF-specific path translator for encrypted and unencrypted modes.
    /// </summary>
    public class UvfPathTranslator : IVaultPathTranslator
    {
        private readonly IStorage _underlyingStorage;
        private readonly bool _isEncryptionEnabled;
        private readonly string _baseStoragePath;
        private bool _disposed;

        public UvfLib.Storage.Abstractions.VaultFormat Format => UvfLib.Storage.Abstractions.VaultFormat.UVF;
        public bool IsEncryptionEnabled => _isEncryptionEnabled;
        public string BaseStoragePath => _baseStoragePath;

        public UvfPathTranslator(IStorage underlyingStorage, bool isEncryptionEnabled, string baseStoragePath)
        {
            _underlyingStorage = underlyingStorage ?? throw new ArgumentNullException(nameof(underlyingStorage));
            _isEncryptionEnabled = isEncryptionEnabled;
            _baseStoragePath = baseStoragePath ?? throw new ArgumentNullException(nameof(baseStoragePath));
        }

        public async Task<VaultPathResult> TranslateToStoragePathAsync(string virtualPath)
        {
            ThrowIfDisposed();

            if (!_isEncryptionEnabled)
            {
                // Unencrypted mode: ReadMe.txt -> ReadMe.txt.uvf
                string storagePath = Path.Combine(_baseStoragePath, virtualPath.TrimStart('/') + ".uvf");
                return new VaultPathResult
                {
                    StoragePath = storagePath,
                    ContentDirectoryPath = Path.GetDirectoryName(storagePath) ?? _baseStoragePath,
                    EncryptedFilename = Path.GetFileName(storagePath),
                    IsEncrypted = false,
                    RequiresDirectoryCreation = false
                };
            }

            // Encrypted mode - placeholder for now
            throw new NotImplementedException("Encrypted mode path translation needs vault integration");
        }

        public Task<string?> TranslateToVirtualPathAsync(string storagePath)
        {
            throw new NotImplementedException();
        }

        // Removed storage operation methods - these belong in the storage decorator, not translator

        public string GetEncryptedFileExtension() => ".uvf";

        public string GetMetadataFileName() => "dir.uvf";

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UvfPathTranslator));
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// Interface for providing directory IDs and paths (abstraction for vault-specific logic)
    /// </summary>
    public interface IDirectoryIdProvider
    {
        DirectoryMetadata GetRootDirectoryMetadata();
        DirectoryMetadata CreateNewDirectoryMetadata();
        string GetDirectoryPath(DirectoryMetadata metadata);
    }

    /// <summary>
    /// Interface for filename encryption/decryption (abstraction for vault-specific logic)
    /// </summary>
    public interface IFileNameCryptor
    {
        string EncryptFilename(string plaintextName, DirectoryMetadata directoryMetadata);
        string DecryptFilename(string encryptedName, DirectoryMetadata directoryMetadata);
    }
} 