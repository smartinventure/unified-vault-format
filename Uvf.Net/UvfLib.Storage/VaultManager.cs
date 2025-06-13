using StorageLib.Abstractions;
using StorageLib.Streaming;
using UvfLib.Storage.Decorators;
using UvfLib.Storage.PathTranslators;
using UvfLib.Vault;
using System.Text;

namespace UvfLib.Storage
{
    /// <summary>
    /// High-level vault manager that provides an easy-to-use API for vault operations.
    /// Supports different storage connectors (LocalStorage, MemoryStorage, S3Storage, etc.)
    /// and uses forward-slash path normalization for cross-platform compatibility.
    /// </summary>
    public class VaultManager : IDisposable, IStreamStorage
    {
        private IStorage? _baseStorage;
        private VaultHandler? _vault;
        private CryptomatorStorageDecorator? _vaultStorage;
        private string? _vaultBasePath;
        private bool _isOpen;
        private bool _ownsStorage; // Track if we created the storage
        private bool _disposed;

        #region Factory Methods - Path-based (Convenience)

        /// <summary>
        /// Creates a new Cryptomator V8 vault at the specified path using LocalStorage
        /// </summary>
        /// <param name="vaultPath">Path where the vault should be created</param>
        /// <param name="password">Vault password</param>
        /// <returns>A VaultManager instance for the new vault</returns>
        public static async Task<VaultManager> CreateVaultAsync(string vaultPath, string password)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await CreateVaultAsync(storage, password, vaultPath, ownsStorage: true);
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault from the specified path using LocalStorage
        /// </summary>
        /// <param name="vaultPath">Path where the vault is located</param>
        /// <param name="password">Vault password</param>
        /// <returns>A VaultManager instance for the existing vault</returns>
        public static async Task<VaultManager> LoadVaultAsync(string vaultPath, string password)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await LoadVaultAsync(storage, password, vaultPath, ownsStorage: true);
        }

        #endregion

        #region Factory Methods - Storage-based (Flexible)

        /// <summary>
        /// Creates a new Cryptomator V8 vault using the provided storage connector
        /// </summary>
        /// <param name="storage">Storage connector (LocalStorage, MemoryStorage, S3Storage, etc.)</param>
        /// <param name="password">Vault password</param>
        /// <param name="vaultBasePath">Base path for vault operations (used for path translation)</param>
        /// <param name="ownsStorage">Whether VaultManager should dispose the storage when closed</param>
        /// <returns>A VaultManager instance for the new vault</returns>
        public static async Task<VaultManager> CreateVaultAsync(IStorage storage, string password, string vaultBasePath, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeNewVaultAsync(storage, password, vaultBasePath, ownsStorage);
            return manager;
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault using the provided storage connector
        /// </summary>
        /// <param name="storage">Storage connector (LocalStorage, MemoryStorage, S3Storage, etc.)</param>
        /// <param name="password">Vault password</param>
        /// <param name="vaultBasePath">Base path for vault operations (used for path translation)</param>
        /// <param name="ownsStorage">Whether VaultManager should dispose the storage when closed</param>
        /// <returns>A VaultManager instance for the existing vault</returns>
        public static async Task<VaultManager> LoadVaultAsync(IStorage storage, string password, string vaultBasePath, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeExistingVaultAsync(storage, password, vaultBasePath, ownsStorage);
            return manager;
        }

        #endregion

        #region Vault Management

        /// <summary>
        /// Changes the vault password
        /// </summary>
        /// <param name="newPassword">New vault password</param>
        public async Task ChangePasswordAsync(string newPassword)
        {
            EnsureOpen();
            
            // TODO: Implement password change functionality
            // This requires VaultHandler to support password changes
            // May need to re-encrypt the masterkey file with new password
            throw new NotImplementedException("Password change functionality requires VaultHandler enhancement");
        }

        /// <summary>
        /// Closes the vault and optionally disposes the underlying storage
        /// </summary>
        public async Task CloseVaultAsync()
        {
            if (_isOpen && !_disposed)
            {
                if (_vaultStorage != null)
                {
                    await _vaultStorage.ShutdownAsync();
                    _vaultStorage.Dispose();
                    _vaultStorage = null;
                }

                if (_ownsStorage && _baseStorage != null)
                {
                    await _baseStorage.ShutdownAsync();
                    _baseStorage.Dispose();
                    _baseStorage = null;
                }

                _vault?.Dispose();
                _vault = null;
                _isOpen = false;
            }
        }

        #endregion

        #region File Operations (Forward Slash Paths)

        /// <summary>
        /// Opens a file for reading. Path uses forward slashes (e.g., "/documents/file.txt")
        /// </summary>
        public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.OpenReadAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Opens a file for writing. Path uses forward slashes (e.g., "/documents/file.txt")
        /// </summary>
        public async Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.OpenWriteAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Opens a file for both reading and writing. Path uses forward slashes.
        /// </summary>
        public async Task<Stream> OpenReadWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.OpenReadWriteAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Opens a file with specific flags. Path uses forward slashes.
        /// </summary>
        public async Task<Stream> OpenAsync(string path, OpenFlags flags, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await ((IStreamStorage)_vaultStorage!).OpenAsync(NormalizePath(path), flags, cancellationToken);
        }

        /// <summary>
        /// Reads all bytes from a file. Path uses forward slashes.
        /// </summary>
        public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.ReadAllBytesAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Writes all bytes to a file. Path uses forward slashes.
        /// </summary>
        public async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.WriteAllBytesAsync(NormalizePath(path), data, cancellationToken);
        }

        /// <summary>
        /// Reads all text from a file. Path uses forward slashes.
        /// </summary>
        public async Task<string> ReadAllTextAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.ReadAllTextAsync(NormalizePath(path), encoding, cancellationToken);
        }

        /// <summary>
        /// Writes all text to a file. Path uses forward slashes.
        /// </summary>
        public async Task WriteAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.WriteAllTextAsync(NormalizePath(path), content, encoding, cancellationToken);
        }

        /// <summary>
        /// Reads all lines from a text file. Path uses forward slashes.
        /// </summary>
        public async Task<string[]> ReadAllLinesAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.ReadAllLinesAsync(NormalizePath(path), encoding, cancellationToken);
        }

        /// <summary>
        /// Writes all lines to a text file. Path uses forward slashes.
        /// </summary>
        public async Task WriteAllLinesAsync(string path, IEnumerable<string> lines, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.WriteAllLinesAsync(NormalizePath(path), lines, encoding, cancellationToken);
        }

        /// <summary>
        /// Appends text to a file. Path uses forward slashes.
        /// </summary>
        public async Task AppendAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.AppendAllTextAsync(NormalizePath(path), content, encoding, cancellationToken);
        }

        /// <summary>
        /// Copies data from a stream to a file. Path uses forward slashes.
        /// </summary>
        public async Task CopyFromStreamAsync(string destinationPath, Stream sourceStream, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.CopyFromStreamAsync(NormalizePath(destinationPath), sourceStream, cancellationToken);
        }

        /// <summary>
        /// Copies a file to a stream. Path uses forward slashes.
        /// </summary>
        public async Task CopyToStreamAsync(string sourcePath, Stream destinationStream, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.CopyToStreamAsync(NormalizePath(sourcePath), destinationStream, cancellationToken);
        }

        #endregion

        #region Directory Operations

        /// <summary>
        /// Lists directory contents. Path uses forward slashes (e.g., "/documents" or "/" for root)
        /// </summary>
        public async Task<IEnumerable<FileObject>> ListDirectoryAsync(string path = "/", CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.ReadDirAsync(NormalizePath(path), true, cancellationToken);
        }

        /// <summary>
        /// Creates a directory. Path uses forward slashes.
        /// </summary>
        public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.CreateDirectoryAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Deletes a file. Path uses forward slashes.
        /// </summary>
        public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.DeleteAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Deletes a directory. Path uses forward slashes.
        /// </summary>
        public async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.DeleteDirectoryAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Checks if a file exists. Path uses forward slashes.
        /// </summary>
        public async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.FileExistsAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Checks if a directory exists. Path uses forward slashes.
        /// </summary>
        public async Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.DirectoryExistsAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Gets file information. Path uses forward slashes.
        /// </summary>
        public async Task<FileObject> GetFileInfoAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.GetFileInfoAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Moves a file. Paths use forward slashes.
        /// </summary>
        public async Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.MoveAsync(NormalizePath(sourceFilePath), NormalizePath(destinationFilePath), overwrite, cancellationToken);
        }

        #endregion

        #region IStreamStorage Implementation (Delegate to vault storage)

        /// <summary>
        /// Gets the underlying IStorage instance that this stream storage wraps
        /// </summary>
        public IStorage UnderlyingStorage => _vaultStorage?.UnderlyingStorage ?? throw new InvalidOperationException("Vault is not open");

        // Delegate remaining IStreamStorage methods to _vaultStorage
        Task<IEnumerable<FileObject>> IStreamStorage.ReadDirAsync(string directoryPath, bool readOnly, CancellationToken cancellationToken)
            => ListDirectoryAsync(directoryPath, cancellationToken);

        Task IStreamStorage.DeleteAsync(string filePath, CancellationToken cancellationToken)
            => DeleteFileAsync(filePath, cancellationToken);

        Task IStreamStorage.ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken)
        {
            EnsureOpen();
            return _vaultStorage!.ChownAsync(NormalizePath(path), uid, gid, cancellationToken);
        }

        Task IStreamStorage.SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken)
        {
            EnsureOpen();
            return _vaultStorage!.SetTimesAsync(NormalizePath(path), accessTime, modifiedTime, cancellationToken);
        }

        Task<bool> IStreamStorage.TestReadAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return ((IStreamStorage)_vaultStorage!).TestReadAsync(cancellationToken);
        }

        Task<bool> IStreamStorage.TestWriteAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return ((IStreamStorage)_vaultStorage!).TestWriteAsync(cancellationToken);
        }

        Task<long> IStreamStorage.GetTotalCapacityAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return ((IStreamStorage)_vaultStorage!).GetTotalCapacityAsync(cancellationToken);
        }

        Task<long> IStreamStorage.GetAvailableFreeSpaceAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return ((IStreamStorage)_vaultStorage!).GetAvailableFreeSpaceAsync(cancellationToken);
        }

        #endregion

        #region Private Implementation

        private async Task InitializeNewVaultAsync(IStorage storage, string password, string vaultBasePath, bool ownsStorage)
        {
            _baseStorage = storage;
            _vaultBasePath = vaultBasePath;
            _ownsStorage = ownsStorage;

            try
            {
                // Create new Cryptomator V8 vault
                VaultHandler.CreateNewCryptomatorV8VaultComplete(vaultBasePath, password);

                // Load the newly created vault
                await LoadVaultInternalAsync(password);
                _isOpen = true;
            }
            catch
            {
                // Cleanup on failure
                if (_ownsStorage)
                {
                    await storage.ShutdownAsync();
                    storage.Dispose();
                }
                throw;
            }
        }

        private async Task InitializeExistingVaultAsync(IStorage storage, string password, string vaultBasePath, bool ownsStorage)
        {
            _baseStorage = storage;
            _vaultBasePath = vaultBasePath;
            _ownsStorage = ownsStorage;

            try
            {
                // Load existing vault
                await LoadVaultInternalAsync(password);
                _isOpen = true;
            }
            catch
            {
                // Cleanup on failure
                if (_ownsStorage)
                {
                    await storage.ShutdownAsync();
                    storage.Dispose();
                }
                throw;
            }
        }

        private async Task LoadVaultInternalAsync(string password)
        {
            // Load vault from masterkey file
            string masterkeyPath = Path.Combine(_vaultBasePath!, "masterkey.cryptomator");
            
            if (!File.Exists(masterkeyPath))
            {
                throw new FileNotFoundException($"Vault masterkey not found at: {masterkeyPath}");
            }

            byte[] masterkeyBytes = await File.ReadAllBytesAsync(masterkeyPath);
            _vault = VaultHandler.LoadCryptomatorV8Vault(masterkeyBytes, password);

            // Create vault storage decorator
            _vaultStorage = new CryptomatorStorageDecorator(
                _baseStorage!,
                _vault,
                _vaultBasePath!,
                logger: null
            );
        }

        private void EnsureOpen()
        {
            if (!_isOpen || _disposed)
                throw new InvalidOperationException("Vault is not open");
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";

            // Replace backslashes with forward slashes
            path = path.Replace("\\", "/");

            // Remove duplicate slashes (including leading ones)
            while (path.Contains("//"))
                path = path.Replace("//", "/");

            // Ensure path starts with exactly one forward slash
            path = path.TrimStart('/');
            if (!string.IsNullOrEmpty(path))
                path = "/" + path;
            else
                path = "/";

            return path;
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (!_disposed)
            {
                CloseVaultAsync().Wait();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        #endregion
    }
} 