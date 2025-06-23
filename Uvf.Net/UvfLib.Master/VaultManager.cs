using StorageLib.Abstractions;
using StorageLib.Streaming;
using UvfLib.Master.Decorators;
using UvfLib.Master.PathTranslators;
using UvfLib.Vault;
using System.Text;
using System.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Buffers.Binary;
using UvfLib.Core.Jwe;
using UvfLib.Core.Api;

namespace UvfLib.Master
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
        public static async Task<VaultManager> CreateCryptomatorVaultAsync(string vaultPath, char[] password)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await CreateCryptomatorVaultAsync(storage, password, vaultPath, ownsStorage: true);
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault from the specified path using LocalStorage
        /// </summary>
        public static async Task<VaultManager> LoadCryptomatorVaultAsync(string vaultPath, char[] password)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await LoadCryptomatorVaultAsync(storage, password, vaultPath, ownsStorage: true);
        }

        /// <summary>
        /// Creates a new Cryptomator V8 vault using the provided storage connector
        /// </summary>
        public static async Task<VaultManager> CreateCryptomatorVaultAsync(IStorage storage, char[] password, string vaultBasePath, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeNewCryptomatorVaultAsync(storage, password, vaultBasePath, ownsStorage);
            return manager;
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault using the provided storage connector
        /// </summary>
        public static async Task<VaultManager> LoadCryptomatorVaultAsync(IStorage storage, char[] password, string vaultBasePath, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeExistingCryptomatorVaultAsync(storage, password, vaultBasePath, ownsStorage);
            return manager;
        }

        #endregion

        #region Factory Methods - UVF (Multi-User Only)

        /// <summary>
        /// Creates a new UVF vault with admin user at the specified path using LocalStorage.
        /// UVF vaults are always multi-user capable, even with a single admin user.
        /// </summary>
        /// <param name="vaultPath">Path where the vault will be created</param>
        /// <param name="adminPassword">Admin password</param>
        /// <param name="encryptFilenames">Whether to encrypt filenames</param>
        /// <param name="kdfParams">Key derivation parameters (optional, defaults to PBKDF2)</param>
        /// <returns>VaultManager instance</returns>
        public static async Task<VaultManager> CreateUvfVaultAsync(string vaultPath, char[] adminPassword, bool encryptFilenames = true, KeyDerivationParameters? kdfParams = null)
        {
            return await CreateMultiUserUvfVaultAsync(vaultPath, adminPassword, encryptFilenames, kdfParams);
        }

        /// <summary>
        /// Creates a new UVF vault with configurable key derivation method.
        /// UVF vaults are always multi-user capable, even with a single admin user.
        /// </summary>
        /// <param name="vaultPath">Path to create the vault</param>
        /// <param name="adminPassword">Vault admin password</param>
        /// <param name="encryptFilenames">Whether to encrypt filenames</param>
        /// <param name="kdfParams">Key derivation parameters (optional, defaults to PBKDF2)</param>
        /// <returns>Initialized vault manager</returns>
        public static async Task<VaultManager> CreateUvfVaultWithKdfAsync(string vaultPath, char[] adminPassword, bool encryptFilenames = true, KeyDerivationParameters? kdfParams = null)
        {
            return await CreateMultiUserUvfVaultAsync(vaultPath, adminPassword, encryptFilenames, kdfParams);
        }

        /// <summary>
        /// Loads an existing UVF vault from the specified path using LocalStorage.
        /// Automatically detects the filename encryption mode from the vault metadata.
        /// Works with both single-admin and multi-user UVF vaults.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="userPassword">User password</param>
        /// <param name="userId">Optional user ID hint (use "admin" for admin user)</param>
        /// <returns>VaultManager instance</returns>
        public static async Task<VaultManager> LoadUvfVaultAsync(string vaultPath, char[] userPassword, string? userId = null)
        {
            return await LoadMultiUserUvfVaultAsync(vaultPath, userPassword, userId);
        }

        /// <summary>
        /// Creates a new multi-user UVF vault with admin user at the specified path using LocalStorage.
        /// </summary>
        /// <param name="vaultPath">Path where the vault will be created</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="encryptFilenames">Whether to encrypt filenames</param>
        /// <param name="kdfParams">Key derivation parameters (optional, defaults to PBKDF2)</param>
        /// <returns>VaultManager instance</returns>
        public static async Task<VaultManager> CreateMultiUserUvfVaultAsync(string vaultPath, char[] adminPassword, bool encryptFilenames = true, KeyDerivationParameters? kdfParams = null)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await CreateMultiUserUvfVaultAsync(storage, adminPassword, vaultPath, encryptFilenames, kdfParams, ownsStorage: true);
        }

        /// <summary>
        /// Creates a new multi-user UVF vault using the provided storage connector.
        /// </summary>
        /// <param name="storage">Storage connector</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="vaultBasePath">Base path for the vault</param>
        /// <param name="encryptFilenames">Whether to encrypt filenames</param>
        /// <param name="kdfParams">Key derivation parameters (optional, defaults to PBKDF2)</param>
        /// <param name="ownsStorage">Whether this manager owns the storage</param>
        /// <returns>VaultManager instance</returns>
        public static async Task<VaultManager> CreateMultiUserUvfVaultAsync(IStorage storage, char[] adminPassword, string vaultBasePath, bool encryptFilenames = true, KeyDerivationParameters? kdfParams = null, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeNewMultiUserUvfVaultAsync(storage, adminPassword, vaultBasePath, encryptFilenames, kdfParams ?? KeyDerivationParameters.Default(), ownsStorage);
            return manager;
        }

        /// <summary>
        /// Loads an existing multi-user UVF vault from the specified path using LocalStorage.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="userPassword">User password (char[])</param>
        /// <param name="userId">Optional user ID hint</param>
        /// <returns>VaultManager instance</returns>
        public static async Task<VaultManager> LoadMultiUserUvfVaultAsync(string vaultPath, char[] userPassword, string? userId = null)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await LoadMultiUserUvfVaultAsync(storage, userPassword, vaultPath, userId, ownsStorage: true);
        }

        /// <summary>
        /// Loads an existing multi-user UVF vault using the provided storage connector.
        /// </summary>
        /// <param name="storage">Storage connector</param>
        /// <param name="userPassword">User password (char[])</param>
        /// <param name="vaultBasePath">Base path for the vault</param>
        /// <param name="userId">Optional user ID hint</param>
        /// <param name="ownsStorage">Whether this manager owns the storage</param>
        /// <returns>VaultManager instance</returns>
        public static async Task<VaultManager> LoadMultiUserUvfVaultAsync(IStorage storage, char[] userPassword, string vaultBasePath, string? userId = null, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeExistingMultiUserUvfVaultAsync(storage, userPassword, vaultBasePath, userId, ownsStorage);
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

        private async Task InitializeNewCryptomatorVaultAsync(IStorage storage, char[] password, string vaultBasePath, bool ownsStorage)
        {
            _baseStorage = storage ?? throw new ArgumentNullException(nameof(storage));
            _vaultBasePath = vaultBasePath ?? throw new ArgumentNullException(nameof(vaultBasePath));
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.CryptomatorV8;

            try
            {
                // Create the vault directory if it doesn't exist
                if (!Directory.Exists(_vaultBasePath))
                {
                    Directory.CreateDirectory(_vaultBasePath);
                }

                // TODO: Update VaultHandler to use char[] passwords
                byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
                try
                {
                    VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultBasePath, passwordBytes);
                }
                finally
                {
                    // Clear the temporary password bytes from memory
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytes);
                }

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
        
        private async Task InitializeExistingCryptomatorVaultAsync(IStorage storage, char[] password, string vaultBasePath, bool ownsStorage)
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

        private async Task LoadCryptomatorVaultInternalAsync(char[] password)
        {
            // Load vault from masterkey file
            string masterkeyFilePath = Path.Combine(_vaultBasePath!, "masterkey.cryptomator");

            if (!File.Exists(masterkeyFilePath))
            {
                throw new FileNotFoundException($"Cryptomator masterkey file not found at: {masterkeyFilePath}");
            }

            byte[] masterkeyBytes = await File.ReadAllBytesAsync(masterkeyFilePath);

            try
            {
                // TODO: Update VaultHandler to use char[] passwords
                byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
                try
                {
                    _vault = VaultHandler.LoadCryptomatorV8Vault(masterkeyBytes, passwordBytes);
                }
                finally
                {
                    // Clear the temporary password bytes from memory
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytes);
                }
            }
            catch (Exception ex)
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

        #region Private Implementation - UVF (Multi-User Only)

        private async Task InitializeNewMultiUserUvfVaultAsync(IStorage storage, char[] adminPassword, string vaultBasePath, bool encryptFilenames, KeyDerivationParameters kdfParams, bool ownsStorage)
        {
            _baseStorage = storage ?? throw new ArgumentNullException(nameof(storage));
            _vaultBasePath = vaultBasePath ?? throw new ArgumentNullException(nameof(vaultBasePath));
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.UvfV3;

            // Create UVF masterkey payload exactly like the single-user system does
            using var rng = RandomNumberGenerator.Create();

            byte[] primaryEncryptionKey = new byte[32];
            rng.GetBytes(primaryEncryptionKey);
            byte[] primaryHmacKey = new byte[32];
            rng.GetBytes(primaryHmacKey);
            byte[] seedValue = new byte[32];
            rng.GetBytes(seedValue);
            int initialSeedId = 1;
            byte[] kdfSaltForSeeds = new byte[32];
            rng.GetBytes(kdfSaltForSeeds);
            byte[] rootDirIdContext = Encoding.ASCII.GetBytes("rootDirId");
            byte[] rootDirId = HKDF.DeriveKey(HashAlgorithmName.SHA512, seedValue, UvfLib.Core.V3.Constants.DIR_ID_SIZE, kdfSaltForSeeds, rootDirIdContext);

            byte[] initialSeedIdBytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(initialSeedIdBytes, initialSeedId);

            var payload = new UvfMasterkeyPayload
            {
                UvfSpecVersion = 1,
                Keys = new List<PayloadKey>
                {
                    new PayloadKey 
                    {
                        Id = "1", 
                        Purpose = "org.cryptomator.masterkey", 
                        Alg = "AES-256-RAW", 
                        Value = Jose.Base64Url.Encode(primaryEncryptionKey)
                    },
                    new PayloadKey 
                    {
                        Id = "2", 
                        Purpose = "org.cryptomator.hmacMasterkey", 
                        Alg = "HMAC-SHA256-RAW", 
                        Value = Jose.Base64Url.Encode(primaryHmacKey)
                    }
                },
                Kdf = new PayloadKdf
                {
                    Type = "HKDF-SHA512",
                    Salt = Jose.Base64Url.Encode(kdfSaltForSeeds)
                },
                Seeds = new List<PayloadSeed>
                {
                    new PayloadSeed
                    {
                        Id = Jose.Base64Url.Encode(initialSeedIdBytes),
                        Value = Jose.Base64Url.Encode(seedValue),
                        Created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }
                },
                RootDirId = Jose.Base64Url.Encode(rootDirId),
                Config = new UvfLibNetConfig
                {
                    EncryptFilenames = encryptFilenames,
                    CreatedByVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                }
            };

            // Create multi-user JWE with admin user only, using specified KDF
            var userCredentials = new Dictionary<string, char[]>();
            string jweString = UvfLib.Core.Jwe.MultiUserJweVaultManager.CreateMultiUserVault(payload, userCredentials, adminPassword, kdfParams);
            byte[] vaultFileContent = System.Text.Encoding.UTF8.GetBytes(jweString);
            
            // Ensure vault directory exists
            string vaultDirectoryPath = Path.GetDirectoryName(Path.Combine(_vaultBasePath, "vault.uvf"))!;
            if (!string.IsNullOrEmpty(vaultDirectoryPath) && !Directory.Exists(vaultDirectoryPath))
            {
                Directory.CreateDirectory(vaultDirectoryPath);
            }

            // Write vault file
            string vaultFilePath = Path.Combine(_vaultBasePath, "vault.uvf");
            await System.IO.File.WriteAllBytesAsync(vaultFilePath, vaultFileContent);

            // Load the vault for use
            await LoadMultiUserUvfVaultInternalAsync(adminPassword, encryptFilenames);
        }

        private async Task InitializeExistingMultiUserUvfVaultAsync(IStorage storage, char[] userPassword, string vaultBasePath, string? userId, bool ownsStorage)
        {
            _baseStorage = storage ?? throw new ArgumentNullException(nameof(storage));
            _vaultBasePath = vaultBasePath ?? throw new ArgumentNullException(nameof(vaultBasePath));
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.UvfV3;

            try
            {
                // Auto-detect filename encryption mode from vault file
            string vaultFilePath = Path.Combine(_vaultBasePath, "vault.uvf");
            if (!System.IO.File.Exists(vaultFilePath))
                throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");
                
            byte[] vaultFileContent = await System.IO.File.ReadAllBytesAsync(vaultFilePath);
                byte[] userPasswordBytes = System.Text.Encoding.UTF8.GetBytes(userPassword);
                bool encryptFilenames;
                try
                {
                    encryptFilenames = VaultHandler.DetectFilenameEncryption(vaultFileContent, userPasswordBytes);
                }
                finally
                {
                    // Clear the temporary password bytes from memory
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(userPasswordBytes);
                }

                // Load the vault for use
            await LoadMultiUserUvfVaultInternalAsync(userPassword, encryptFilenames);
                _isOpen = true;
            }
            catch (Exception)
            {
                // Clean up on failure
                if (_ownsStorage && _baseStorage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                throw;
            }
        }

        private async Task LoadMultiUserUvfVaultInternalAsync(char[] userPassword, bool encryptFilenames)
        {
            try
            {
                // Load vault file content
                string vaultFilePath = Path.Combine(_vaultBasePath, "vault.uvf");
                if (!System.IO.File.Exists(vaultFilePath))
                    throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");

                byte[] vaultFileContent = await System.IO.File.ReadAllBytesAsync(vaultFilePath);
                string jweString = System.Text.Encoding.UTF8.GetString(vaultFileContent);
                
                // Load the multi-user vault payload first
                var payload = UvfLib.Core.Jwe.MultiUserJweVaultManager.LoadMultiUserVault(jweString, userPassword, null);
                
                // Convert the payload back to a single-user JWE format that VaultHandler can understand
                // TODO: Update MultiUserJweVaultManager.CreateSingleUserVault to use char[] passwords
                byte[] passwordBytesForJwe = System.Text.Encoding.UTF8.GetBytes(userPassword);
                string singleUserJweString;
                try
                {
                    singleUserJweString = UvfLib.Core.Jwe.MultiUserJweVaultManager.CreateSingleUserVault(payload, passwordBytesForJwe);
                }
                finally
                {
                    // Clear temporary password bytes from memory
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytesForJwe);
                }
                byte[] singleUserJweBytes = System.Text.Encoding.UTF8.GetBytes(singleUserJweString);
                
                // Now load using the regular UVF loader
                byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(userPassword);
                try
                {
                    _vault = UvfLib.Vault.VaultHandler.LoadUvfVault(singleUserJweBytes, passwordBytes);
                }
                finally
                {
                    // Clear temporary password bytes from memory
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytes);
                }

                // Create storage decorator
                _vaultStorage = new UvfStorageDecorator(_baseStorage, _vault, encryptFilenames, _vaultBasePath);

                _isOpen = true;
            }
            catch (Exception)
            {
                // Clean up on failure
                _vault?.Dispose();
                _vault = null;
                throw;
            }
        }

        #endregion

        #region Multi-User Management Methods

        /// <summary>
        /// Adds a user to an existing multi-user UVF vault.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="newUserId">New user ID</param>
        /// <param name="newUserPassword">New user password (char[])</param>
        public static async Task AddUserToVaultAsync(string vaultPath, char[] adminPassword, string newUserId, char[] newUserPassword)
        {
            string vaultFilePath = Path.Combine(vaultPath, "vault.uvf");
            if (!System.IO.File.Exists(vaultFilePath))
                throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");

            byte[] vaultFileContent = await System.IO.File.ReadAllBytesAsync(vaultFilePath);
            string jweString = System.Text.Encoding.UTF8.GetString(vaultFileContent);
            string updatedJweString = UvfLib.Core.Jwe.MultiUserJweVaultManager.AddUserToVault(jweString, adminPassword, newUserId, newUserPassword);
            byte[] updatedVaultContent = System.Text.Encoding.UTF8.GetBytes(updatedJweString);
            await System.IO.File.WriteAllBytesAsync(vaultFilePath, updatedVaultContent);
        }

        /// <summary>
        /// Removes a user from an existing multi-user UVF vault.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="userIdToRemove">User ID to remove</param>
        public static async Task RemoveUserFromVaultAsync(string vaultPath, char[] adminPassword, string userIdToRemove)
        {
            string vaultFilePath = Path.Combine(vaultPath, "vault.uvf");
            if (!System.IO.File.Exists(vaultFilePath))
                throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");

            byte[] vaultFileContent = await System.IO.File.ReadAllBytesAsync(vaultFilePath);
            string jweString = System.Text.Encoding.UTF8.GetString(vaultFileContent);
            string updatedJweString = UvfLib.Core.Jwe.MultiUserJweVaultManager.RemoveUserFromVault(jweString, adminPassword, userIdToRemove);
            byte[] updatedVaultContent = System.Text.Encoding.UTF8.GetBytes(updatedJweString);
            await System.IO.File.WriteAllBytesAsync(vaultFilePath, updatedVaultContent);
        }

        /// <summary>
        /// Rotates keys for a multi-user UVF vault.
        /// Note: Multi-user key rotation is complex and requires all user passwords to be available.
        /// This is a limitation of the current implementation.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        public static async Task RotateVaultKeysAsync(string vaultPath, char[] adminPassword)
        {
            string vaultFilePath = Path.Combine(vaultPath, "vault.uvf");
            if (!System.IO.File.Exists(vaultFilePath))
                throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");

            byte[] vaultFileContent = await System.IO.File.ReadAllBytesAsync(vaultFilePath);
            string jweString = System.Text.Encoding.UTF8.GetString(vaultFileContent);
            
            // Get current users to check if rotation is possible
            var currentUsers = UvfLib.Core.Jwe.MultiUserJweVaultManager.GetVaultUsers(jweString, adminPassword);
            
            // Check if there are other users besides admin
            var nonAdminUsers = currentUsers.Where(u => u != "admin").ToList();
            if (nonAdminUsers.Any())
            {
                throw new InvalidOperationException($"Cannot rotate keys for multi-user vault with users: {string.Join(", ", nonAdminUsers)}. " +
                    "Multi-user key rotation requires all user passwords to re-encrypt the vault for all users. " +
                    "Consider removing all users except admin before key rotation, or implement a more sophisticated key management system.");
            }

            // If only admin user exists, we can proceed with rotation
            // Load the vault to get current payload
            var payload = UvfLib.Core.Jwe.MultiUserJweVaultManager.LoadMultiUserVault(jweString, adminPassword, "admin");

            // For now, key rotation for multi-user vaults is not fully implemented
            // This would require generating new seeds and re-encrypting the vault
            throw new NotImplementedException("Multi-user vault key rotation is not yet fully implemented. " +
                "As a workaround, you can create a new vault and migrate data, or remove all users except admin before rotation.");
        }

        /// <summary>
        /// Gets list of users from a multi-user UVF vault.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <returns>List of vault users</returns>
        public static async Task<List<UvfLib.Core.Api.VaultUser>> GetVaultUsersAsync(string vaultPath, char[] adminPassword)
        {
            string vaultFilePath = Path.Combine(vaultPath, "vault.uvf");
            if (!System.IO.File.Exists(vaultFilePath))
                throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");

            byte[] vaultFileContent = await System.IO.File.ReadAllBytesAsync(vaultFilePath);
            string jweString = System.Text.Encoding.UTF8.GetString(vaultFileContent);
            var userIds = UvfLib.Core.Jwe.MultiUserJweVaultManager.GetVaultUsers(jweString, adminPassword);

            var metadata = new UvfLib.Core.Api.MultiUserVaultMetadata("admin");
            
            foreach (var userId in userIds)
            {
                var role = userId == "admin" ? UvfLib.Core.Api.VaultUserRole.Admin : UvfLib.Core.Api.VaultUserRole.User;
                var user = new UvfLib.Core.Api.VaultUser(userId, role);
                metadata.AddUser(user);
            }

            return metadata.Users;
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
        /// Changes the password for a Cryptomator V8 vault.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="oldPassword">Current password (char[])</param>
        /// <param name="newPassword">New password (char[])</param>
        public static async Task ChangeCryptomatorVaultPasswordAsync(string vaultPath, char[] oldPassword, char[] newPassword)
        {
            if (string.IsNullOrEmpty(vaultPath)) throw new ArgumentNullException(nameof(vaultPath));
            if (oldPassword == null) throw new ArgumentNullException(nameof(oldPassword));
            if (newPassword == null) throw new ArgumentNullException(nameof(newPassword));

                string masterkeyPath = Path.Combine(vaultPath, "masterkey.cryptomator");

                if (!File.Exists(masterkeyPath))
                {
                throw new FileNotFoundException($"Masterkey file not found at: {masterkeyPath}");
                }

            // Read existing masterkey content
            byte[] existingMasterkeyContent = await File.ReadAllBytesAsync(masterkeyPath);

            // TODO: Update VaultHandler to use char[] passwords
            byte[] oldPasswordBytes = System.Text.Encoding.UTF8.GetBytes(oldPassword);
            byte[] newPasswordBytes = System.Text.Encoding.UTF8.GetBytes(newPassword);
            try
            {
                // Change password using VaultHandler
                byte[] newMasterkeyContent = VaultHandler.ChangeCryptomatorV8VaultPassword(
                    existingMasterkeyContent, 
                    oldPasswordBytes, 
                    newPasswordBytes
                );

                // Write updated masterkey content
                await File.WriteAllBytesAsync(masterkeyPath, newMasterkeyContent);
            }
            finally
            {
                // Clear temporary password bytes from memory
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(oldPasswordBytes);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(newPasswordBytes);
            }
        }
        
        /// <summary>
        /// Changes the admin password for a UVF vault.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="oldAdminPassword">Current admin password (char[])</param>
        /// <param name="newAdminPassword">New admin password (char[])</param>
        public static async Task ChangeUvfAdminPasswordAsync(string vaultPath, char[] oldAdminPassword, char[] newAdminPassword)
        {
            if (string.IsNullOrEmpty(vaultPath)) throw new ArgumentNullException(nameof(vaultPath));
            if (oldAdminPassword == null) throw new ArgumentNullException(nameof(oldAdminPassword));
            if (newAdminPassword == null) throw new ArgumentNullException(nameof(newAdminPassword));

                string vaultFilePath = Path.Combine(vaultPath, "vault.uvf");

                if (!File.Exists(vaultFilePath))
                {
                throw new FileNotFoundException($"UVF vault file not found at: {vaultFilePath}");
            }

            // Read existing vault content
            byte[] existingVaultContent = await File.ReadAllBytesAsync(vaultFilePath);
            string jweString = System.Text.Encoding.UTF8.GetString(existingVaultContent);

            // Load current payload with old admin password
            var payload = UvfLib.Core.Jwe.MultiUserJweVaultManager.LoadMultiUserVault(jweString, oldAdminPassword, "admin");

            // Get current users (excluding admin)
            var currentUsers = UvfLib.Core.Jwe.MultiUserJweVaultManager.GetVaultUsers(jweString, oldAdminPassword);
            var userCredentials = new Dictionary<string, char[]>();

            // Note: We can only change admin password, not other user passwords
            // Other users will need to change their own passwords separately

            // Create new vault with updated admin password
            string updatedJweString = UvfLib.Core.Jwe.MultiUserJweVaultManager.CreateMultiUserVault(
                payload, 
                userCredentials, 
                newAdminPassword,
                KeyDerivationParameters.Default()
            );

            // Write updated vault content
            byte[] updatedVaultContent = System.Text.Encoding.UTF8.GetBytes(updatedJweString);
            await File.WriteAllBytesAsync(vaultFilePath, updatedVaultContent);
        }

        /// <summary>
        /// Changes the password for a specific user in a UVF vault.
        /// </summary>
        /// <param name="vaultPath">Path to the vault</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="userId">User ID whose password to change</param>
        /// <param name="newUserPassword">New password for the user (char[])</param>
        public static async Task ChangeUvfUserPasswordAsync(string vaultPath, char[] adminPassword, string userId, char[] newUserPassword)
        {
            if (string.IsNullOrEmpty(vaultPath)) throw new ArgumentNullException(nameof(vaultPath));
            if (adminPassword == null) throw new ArgumentNullException(nameof(adminPassword));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (newUserPassword == null) throw new ArgumentNullException(nameof(newUserPassword));

            // Remove user and re-add with new password
            await RemoveUserFromVaultAsync(vaultPath, adminPassword, userId);
            await AddUserToVaultAsync(vaultPath, adminPassword, userId, newUserPassword);
        }

        #endregion
    }
} 