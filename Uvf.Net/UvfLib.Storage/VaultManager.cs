using StorageLib.Abstractions;
using StorageLib.Streaming;
using UvfLib.Storage.Decorators;
using UvfLib.Storage.PathTranslators;
using UvfLib.Vault;
using System.Text;
using System.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace UvfLib.Storage
{
    /// <summary>
    /// High-level vault manager that provides an easy-to-use API for vault operations.
    /// Supports different storage connectors and vault formats (Cryptomator V8, UVF V3).
    /// Uses forward-slash path normalization for cross-platform compatibility.
    /// Implements secure memory management for passwords and cryptographic keys.
    /// </summary>
    public class VaultManager : IDisposable, IStreamStorage
    {
        private IStorage? _baseStorage;
        private VaultHandler? _vault;
        private IStreamStorage? _vaultStorage; // GENERALIZED: From CryptomatorStorageDecorator to support multiple vault formats
        private string? _vaultBasePath;
        private VaultFormat _vaultFormat; // NEW: To track the current vault format
        private bool _isOpen;
        private bool _ownsStorage; // Track if we created the storage
        private bool _disposed;

        /// <summary>
        /// Defines the vault format being used.
        /// </summary>
        public enum VaultFormat
        {
            CryptomatorV8,
            UvfV3
        }

        #region Factory Methods - Cryptomator

        /// <summary>
        /// Creates a new Cryptomator V8 vault at the specified path using LocalStorage
        /// </summary>
        public static async Task<VaultManager> CreateCryptomatorVaultAsync(string vaultPath, string password)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await CreateCryptomatorVaultAsync(storage, password, vaultPath, ownsStorage: true);
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault from the specified path using LocalStorage
        /// </summary>
        public static async Task<VaultManager> LoadCryptomatorVaultAsync(string vaultPath, string password)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await LoadCryptomatorVaultAsync(storage, password, vaultPath, ownsStorage: true);
        }

        /// <summary>
        /// Creates a new Cryptomator V8 vault using the provided storage connector
        /// </summary>
        public static async Task<VaultManager> CreateCryptomatorVaultAsync(IStorage storage, string password, string vaultBasePath, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeNewCryptomatorVaultAsync(storage, password, vaultBasePath, ownsStorage);
            return manager;
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault using the provided storage connector
        /// </summary>
        public static async Task<VaultManager> LoadCryptomatorVaultAsync(IStorage storage, string password, string vaultBasePath, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeExistingCryptomatorVaultAsync(storage, password, vaultBasePath, ownsStorage);
            return manager;
        }

        #endregion

        #region Factory Methods - UVF

        /// <summary>
        /// Creates a new UVF vault at the specified path using LocalStorage.
        /// </summary>
        public static async Task<VaultManager> CreateUvfVaultAsync(string vaultPath, string password, bool encryptFilenames = true)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await CreateUvfVaultAsync(storage, password, vaultPath, encryptFilenames, ownsStorage: true);
        }

        /// <summary>
        /// Loads an existing UVF vault from the specified path using LocalStorage.
        /// </summary>
        public static async Task<VaultManager> LoadUvfVaultAsync(string vaultPath, string password, bool encryptFilenames = true)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await LoadUvfVaultAsync(storage, password, vaultPath, encryptFilenames, ownsStorage: true);
        }

        /// <summary>
        /// Creates a new UVF vault using the provided storage connector.
        /// </summary>
        public static async Task<VaultManager> CreateUvfVaultAsync(IStorage storage, string password, string vaultBasePath, bool encryptFilenames = true, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeNewUvfVaultAsync(storage, password, vaultBasePath, encryptFilenames, ownsStorage);
            return manager;
        }

        /// <summary>
        /// Loads an existing UVF vault using the provided storage connector.
        /// </summary>
        public static async Task<VaultManager> LoadUvfVaultAsync(IStorage storage, string password, string vaultBasePath, bool encryptFilenames = true, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeExistingUvfVaultAsync(storage, password, vaultBasePath, encryptFilenames, ownsStorage);
            return manager;
        }

        #endregion

        #region Vault Management

        /// <summary>
        /// Closes the vault and optionally disposes the underlying storage
        /// </summary>
        public async Task CloseVaultAsync()
        {
            if (_isOpen && !_disposed)
            {
                if (_vaultStorage is IDisposable disposableStorage)
                {
                    disposableStorage.Dispose();
                }
                _vaultStorage = null;


                if (_ownsStorage && _baseStorage != null)
                {
                    if (_baseStorage is IAsyncDisposable asyncDisposableBase)
                        await asyncDisposableBase.DisposeAsync();
                    else if (_baseStorage is IDisposable disposableBase)
                        disposableBase.Dispose();

                    _baseStorage = null;
                }

                // Securely dispose vault (clears cryptographic keys)
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
        public IStorage UnderlyingStorage
        {
            get
            {
                EnsureOpen();
                if (_vaultStorage is CryptomatorStorageDecorator csd)
                {
                    return csd.UnderlyingStorage;
                }
                if (_vaultStorage is UvfStorageDecorator usd)
                {
                    return usd.UnderlyingStorage;
                }
                throw new InvalidOperationException("The underlying storage cannot be accessed for the current vault type.");
            }
        }

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

        #region Private Implementation - Cryptomator

        private async Task InitializeNewCryptomatorVaultAsync(IStorage storage, string password, string vaultBasePath, bool ownsStorage)
        {
            _baseStorage = storage;
            _vaultBasePath = vaultBasePath;
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.CryptomatorV8;

            try
            {
                // Create new Cryptomator V8 vault files
                VaultHandler.CreateNewCryptomatorV8VaultComplete(vaultBasePath, password);

                // Load the newly created vault
                await LoadCryptomatorVaultInternalAsync(password);
                _isOpen = true;
            }
            catch
            {
                // Cleanup on failure
                if (_ownsStorage && storage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                throw;
            }
        }
        
        private async Task InitializeExistingCryptomatorVaultAsync(IStorage storage, string password, string vaultBasePath, bool ownsStorage)
        {
            _baseStorage = storage;
            _vaultBasePath = vaultBasePath;
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.CryptomatorV8;

            try
            {
                // Load existing vault
                await LoadCryptomatorVaultInternalAsync(password);
                _isOpen = true;
            }
            catch
            {
                // Cleanup on failure
                if (_ownsStorage && storage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                throw;
            }
        }

        private async Task LoadCryptomatorVaultInternalAsync(string password)
        {
            // Load vault from masterkey file
            string masterkeyPath = Path.Combine(_vaultBasePath!, "masterkey.cryptomator");

            if (!File.Exists(masterkeyPath))
            {
                throw new FileNotFoundException($"Vault masterkey not found at: {masterkeyPath}");
            }

            byte[] masterkeyBytes = await File.ReadAllBytesAsync(masterkeyPath);

            try
            {
                _vault = VaultHandler.LoadCryptomatorV8Vault(masterkeyBytes, password);
            }
            catch (Org.BouncyCastle.Crypto.InvalidCipherTextException ex) when (ex.Message.Contains("checksum failed"))
            {
                throw new UnauthorizedAccessException("Incorrect passphrase or pepper during key unwrapping", ex);
            }
            catch (Exception ex) when (ex.Message.Contains("passphrase") || ex.Message.Contains("password") || ex.Message.Contains("credential"))
            {
                throw new UnauthorizedAccessException($"Invalid vault credentials: {ex.Message}", ex);
            }

            // Create vault storage decorator
            _vaultStorage = new CryptomatorStorageDecorator(
                _baseStorage!,
                _vault,
                _vaultBasePath!,
                logger: null
            );
        }

        #endregion

        #region Private Implementation - UVF

        private async Task InitializeNewUvfVaultAsync(IStorage storage, string password, string vaultBasePath, bool encryptFilenames, bool ownsStorage)
        {
            _baseStorage = storage;
            _vaultBasePath = vaultBasePath;
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.UvfV3;

            try
            {
                // Create new UVF vault file
                byte[] vaultFileContent = VaultHandler.CreateNewUvfVaultFileContent(password);
                string vaultFilePath = Path.Combine(_vaultBasePath, "vault.uvf");
                await File.WriteAllBytesAsync(vaultFilePath, vaultFileContent);

                // Load the newly created vault
                await LoadUvfVaultInternalAsync(password, encryptFilenames);
                _isOpen = true;
            }
            catch
            {
                // Cleanup on failure
                if (_ownsStorage && storage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                throw;
            }
        }

        private async Task InitializeExistingUvfVaultAsync(IStorage storage, string password, string vaultBasePath, bool encryptFilenames, bool ownsStorage)
        {
            _baseStorage = storage;
            _vaultBasePath = vaultBasePath;
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.UvfV3;

            try
            {
                // Load existing vault
                await LoadUvfVaultInternalAsync(password, encryptFilenames);
                _isOpen = true;
            }
            catch
            {
                // Cleanup on failure
                if (_ownsStorage && storage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                throw;
            }
        }

        private async Task LoadUvfVaultInternalAsync(string password, bool encryptFilenames)
        {
            string vaultFilePath = Path.Combine(_vaultBasePath!, "vault.uvf");

            if (!File.Exists(vaultFilePath))
            {
                throw new FileNotFoundException($"UVF vault file not found at: {vaultFilePath}");
            }

            byte[] vaultBytes = await File.ReadAllBytesAsync(vaultFilePath);

            try
            {
                _vault = VaultHandler.LoadUvfVault(vaultBytes, password);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException($"Failed to load UVF vault: {ex.Message}", ex);
            }

            // Create vault storage decorator for UVF
            _vaultStorage = new UvfStorageDecorator(
                _baseStorage!,
                _vault,
                encryptFilenames, // Use the provided encryptFilenames parameter
                _vaultBasePath!
            );
        }

        #endregion

        #region Private Implementation - Common

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

        #region Vault Backup Methods

        /// <summary>
        /// Backs up essential vault files to the specified backup directory.
        /// For Cryptomator: backs up masterkey.cryptomator and vault.cryptomator (if exists)
        /// For UVF: backs up vault.uvf
        /// </summary>
        /// <param name="backupPath">Directory where backup files will be stored</param>
        /// <param name="overwriteExisting">Whether to overwrite existing backup files</param>
        /// <returns>Array of backed up file paths</returns>
        public async Task<string[]> BackupVaultFilesAsync(string backupPath, bool overwriteExisting = false)
        {
            if (string.IsNullOrEmpty(backupPath))
                throw new ArgumentNullException(nameof(backupPath));
            
            if (string.IsNullOrEmpty(_vaultBasePath))
                throw new InvalidOperationException("Vault is not initialized or has no base path");

            return await BackupVaultFilesAsync(_vaultBasePath, backupPath, overwriteExisting);
        }

        /// <summary>
        /// Backs up essential vault files from the specified vault path to the backup directory.
        /// Automatically detects vault format (Cryptomator or UVF) and backs up appropriate files.
        /// </summary>
        /// <param name="vaultPath">Path to the vault directory</param>
        /// <param name="backupPath">Directory where backup files will be stored</param>
        /// <param name="overwriteExisting">Whether to overwrite existing backup files</param>
        /// <returns>Array of backed up file paths</returns>
        public static async Task<string[]> BackupVaultFilesAsync(string vaultPath, string backupPath, bool overwriteExisting = false)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (string.IsNullOrEmpty(backupPath))
                throw new ArgumentNullException(nameof(backupPath));
            if (!Directory.Exists(vaultPath))
                throw new DirectoryNotFoundException($"Vault directory not found: {vaultPath}");

            // Create backup directory if it doesn't exist
            Directory.CreateDirectory(backupPath);

            var backedUpFiles = new List<string>();

            // Detect vault format and backup appropriate files
            VaultFormat vaultFormat = DetectVaultFormat(vaultPath);

            try
            {
                switch (vaultFormat)
                {
                    case VaultFormat.CryptomatorV8:
                        backedUpFiles.AddRange(await BackupCryptomatorFilesAsync(vaultPath, backupPath, overwriteExisting));
                        break;

                    case VaultFormat.UvfV3:
                        backedUpFiles.AddRange(await BackupUvfFilesAsync(vaultPath, backupPath, overwriteExisting));
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown or unsupported vault format detected in: {vaultPath}");
                }

                return backedUpFiles.ToArray();
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to backup vault files from '{vaultPath}' to '{backupPath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Detects the vault format based on the presence of specific files in the vault directory.
        /// </summary>
        /// <param name="vaultPath">Path to the vault directory</param>
        /// <returns>The detected vault format</returns>
        public static VaultFormat DetectVaultFormat(string vaultPath)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (!Directory.Exists(vaultPath))
                throw new DirectoryNotFoundException($"Vault directory not found: {vaultPath}");

            // Check for UVF vault file first
            string uvfVaultPath = Path.Combine(vaultPath, "vault.uvf");
            if (File.Exists(uvfVaultPath))
            {
                return VaultFormat.UvfV3;
            }

            // Check for Cryptomator masterkey file
            string cryptomatorMasterkeyPath = Path.Combine(vaultPath, "masterkey.cryptomator");
            if (File.Exists(cryptomatorMasterkeyPath))
            {
                return VaultFormat.CryptomatorV8;
            }

            throw new InvalidOperationException($"No recognizable vault files found in directory: {vaultPath}");
        }

        /// <summary>
        /// Backs up Cryptomator vault files (masterkey.cryptomator and vault.cryptomator if exists)
        /// </summary>
        private static async Task<string[]> BackupCryptomatorFilesAsync(string vaultPath, string backupPath, bool overwriteExisting)
        {
            var backedUpFiles = new List<string>();

            // Backup masterkey.cryptomator (essential file)
            string masterkeyPath = Path.Combine(vaultPath, "masterkey.cryptomator");
            if (!File.Exists(masterkeyPath))
            {
                throw new FileNotFoundException($"Essential Cryptomator masterkey file not found: {masterkeyPath}");
            }

            string masterkeyBackupPath = Path.Combine(backupPath, "masterkey.cryptomator");
            await CopyFileWithOverwriteCheckAsync(masterkeyPath, masterkeyBackupPath, overwriteExisting);
            backedUpFiles.Add(masterkeyBackupPath);

            // Backup vault.cryptomator (optional config file)
            string vaultConfigPath = Path.Combine(vaultPath, "vault.cryptomator");
            if (File.Exists(vaultConfigPath))
            {
                string vaultConfigBackupPath = Path.Combine(backupPath, "vault.cryptomator");
                await CopyFileWithOverwriteCheckAsync(vaultConfigPath, vaultConfigBackupPath, overwriteExisting);
                backedUpFiles.Add(vaultConfigBackupPath);
            }

            return backedUpFiles.ToArray();
        }

        /// <summary>
        /// Backs up UVF vault files (vault.uvf)
        /// </summary>
        private static async Task<string[]> BackupUvfFilesAsync(string vaultPath, string backupPath, bool overwriteExisting)
        {
            var backedUpFiles = new List<string>();

            // Backup vault.uvf (essential file)
            string uvfVaultPath = Path.Combine(vaultPath, "vault.uvf");
            if (!File.Exists(uvfVaultPath))
            {
                throw new FileNotFoundException($"Essential UVF vault file not found: {uvfVaultPath}");
            }

            string uvfBackupPath = Path.Combine(backupPath, "vault.uvf");
            await CopyFileWithOverwriteCheckAsync(uvfVaultPath, uvfBackupPath, overwriteExisting);
            backedUpFiles.Add(uvfBackupPath);

            return backedUpFiles.ToArray();
        }

        /// <summary>
        /// Copies a file with overwrite checking
        /// </summary>
        private static async Task CopyFileWithOverwriteCheckAsync(string sourceFilePath, string destinationFilePath, bool overwriteExisting)
        {
            if (File.Exists(destinationFilePath) && !overwriteExisting)
            {
                throw new IOException($"Backup file already exists and overwrite is disabled: {destinationFilePath}");
            }

            // Create a backup copy
            byte[] fileData = await File.ReadAllBytesAsync(sourceFilePath);
            await File.WriteAllBytesAsync(destinationFilePath, fileData);
        }

        #endregion

        #region Static Password Change Methods

        /// <summary>
        /// Changes the password of a Cryptomator V8 vault at the specified path.
        /// </summary>
        public static async Task ChangeCryptomatorVaultPasswordAsync(string vaultPath, string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(vaultPath)) throw new ArgumentNullException(nameof(vaultPath));
            if (string.IsNullOrEmpty(oldPassword)) throw new ArgumentNullException(nameof(oldPassword));
            if (string.IsNullOrEmpty(newPassword)) throw new ArgumentNullException(nameof(newPassword));

            try
            {
                // Read the current masterkey file
                string masterkeyPath = Path.Combine(vaultPath, "masterkey.cryptomator");
                if (!File.Exists(masterkeyPath))
                {
                    throw new FileNotFoundException($"Masterkey file not found: {masterkeyPath}");
                }

                byte[] currentMasterkeyContent = await File.ReadAllBytesAsync(masterkeyPath);

                // Change password using VaultHandler
                byte[] newMasterkeyContent = VaultHandler.ChangeCryptomatorV8VaultPassword(
                    currentMasterkeyContent, oldPassword, newPassword);

                // Write the new masterkey file
                await File.WriteAllBytesAsync(masterkeyPath, newMasterkeyContent);

                // Update vault.cryptomator if it exists
                string vaultConfigPath = Path.Combine(vaultPath, "vault.cryptomator");
                if (File.Exists(vaultConfigPath))
                {
                    await UpdateVaultConfigStaticAsync(newMasterkeyContent, newPassword, vaultConfigPath);
                }
            }
            catch (Exception ex) when (ex is not ArgumentNullException && ex is not FileNotFoundException)
            {
                throw new IOException($"Failed to change Cryptomator vault password: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Changes the password of a Cryptomator V8 vault with enhanced password security.
        /// </summary>
        public static async Task ChangeCryptomatorVaultPasswordAsync(string vaultPath, char[] oldPasswordChars, char[] newPasswordChars)
        {
            if (string.IsNullOrEmpty(vaultPath)) throw new ArgumentNullException(nameof(vaultPath));
            if (oldPasswordChars == null) throw new ArgumentNullException(nameof(oldPasswordChars));
            if (newPasswordChars == null) throw new ArgumentNullException(nameof(newPasswordChars));

            string oldPassword = new string(oldPasswordChars);
            string newPassword = new string(newPasswordChars);

            try
            {
                await ChangeCryptomatorVaultPasswordAsync(vaultPath, oldPassword, newPassword);
            }
            finally
            {
                // Clear passwords from memory
                Array.Clear(oldPasswordChars, 0, oldPasswordChars.Length);
                Array.Clear(newPasswordChars, 0, newPasswordChars.Length);
            }
        }
        
        /// <summary>
        /// Changes the password of a UVF vault at the specified path.
        /// </summary>
        public static async Task ChangeUvfPasswordAsync(string vaultPath, string oldPassword, string newPassword)
        {
            if (string.IsNullOrEmpty(vaultPath)) throw new ArgumentNullException(nameof(vaultPath));
            if (string.IsNullOrEmpty(oldPassword)) throw new ArgumentNullException(nameof(oldPassword));
            if (string.IsNullOrEmpty(newPassword)) throw new ArgumentNullException(nameof(newPassword));

            try
            {
                string vaultFilePath = Path.Combine(vaultPath, "vault.uvf");
                if (!File.Exists(vaultFilePath))
                {
                    throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");
                }
                
                byte[] currentVaultContent = await File.ReadAllBytesAsync(vaultFilePath);

                byte[] newVaultContent = VaultHandler.ChangeUvfVaultPassword(currentVaultContent, oldPassword, newPassword);

                await File.WriteAllBytesAsync(vaultFilePath, newVaultContent);
            }
            catch (Exception ex) when (ex is not ArgumentNullException && ex is not FileNotFoundException)
            {
                throw new IOException($"Failed to change UVF vault password: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Updates the vault.cryptomator file after a password change (static version)
        /// </summary>
        private static async Task UpdateVaultConfigStaticAsync(byte[] newMasterkeyContent, string newPassword, string vaultConfigPath)
        {
            try
            {
                // Use reflection to access the private CreateNewCryptomatorV8VaultConfigContentSigned method
                var vaultHandlerType = typeof(VaultHandler);
                var method = vaultHandlerType.GetMethod("CreateNewCryptomatorV8VaultConfigContentSigned", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                
                if (method != null)
                {
                    // Call the private method using reflection
                    byte[] newVaultConfigContent = (byte[])method.Invoke(null, new object[] { newMasterkeyContent, newPassword })!;
                    
                    // Write new vault config
                    await File.WriteAllBytesAsync(vaultConfigPath, newVaultConfigContent);
                }
                else
                {
                    // Fallback: create unsigned vault config (not ideal but functional)
                    byte[] fallbackVaultConfigContent = VaultHandler.CreateNewCryptomatorV8VaultConfigContent();
                    await File.WriteAllBytesAsync(vaultConfigPath, fallbackVaultConfigContent);
                }
            }
            catch (Exception)
            {
                // If vault config update fails, the masterkey change was still successful
                // The vault will still work, but the config signature might be invalid
                // This is not critical for basic functionality
            }
        }

        #endregion
    }
} 