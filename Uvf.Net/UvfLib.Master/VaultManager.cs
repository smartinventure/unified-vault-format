// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

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
    public partial class VaultManager : IDisposable, IStreamStorage
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

        /// <summary>
        /// Key derivation methods supported by UVF vaults.
        /// </summary>
        public enum KeyDerivationMethod
        {
            /// <summary>
            /// PBKDF2 with HMAC-SHA512 (default, backward compatible)
            /// </summary>
            PBKDF2_HMAC_SHA512,
            
            /// <summary>
            /// Scrypt key derivation (enhanced security, slower)
            /// </summary>
            Scrypt
        }

        /// <summary>
        /// Parameters for key derivation functions used in UVF vaults.
        /// </summary>
        public class KeyDerivationParameters
        {
            /// <summary>
            /// Key derivation method to use
            /// </summary>
            public KeyDerivationMethod Method { get; set; }

            /// <summary>
            /// PBKDF2 iteration count (used when Method is PBKDF2_HMAC_SHA512)
            /// </summary>
            public int Pbkdf2Iterations { get; set; }

            /// <summary>
            /// Scrypt CPU/memory cost parameter (used when Method is Scrypt)
            /// </summary>
            public int ScryptN { get; set; }

            /// <summary>
            /// Scrypt block size parameter (used when Method is Scrypt)
            /// </summary>
            public int ScryptR { get; set; }

            /// <summary>
            /// Scrypt parallelization parameter (used when Method is Scrypt)
            /// </summary>
            public int ScryptP { get; set; }

            /// <summary>
            /// Creates default PBKDF2 parameters for backward compatibility
            /// </summary>
            public static KeyDerivationParameters Default()
            {
                return new KeyDerivationParameters
                {
                    Method = KeyDerivationMethod.PBKDF2_HMAC_SHA512,
                    Pbkdf2Iterations = 210000 // OWASP 2023 guidance for PBKDF2-HMAC-SHA512
                };
            }

            /// <summary>
            /// Creates Scrypt parameters with recommended security settings
            /// </summary>
            public static KeyDerivationParameters Scrypt()
            {
                return new KeyDerivationParameters
                {
                    Method = KeyDerivationMethod.Scrypt,
                    ScryptN = 32768,  // 2^15
                    ScryptR = 8,
                    ScryptP = 1
                };
            }

            /// <summary>
            /// Creates Scrypt parameters with custom settings
            /// </summary>
            public static KeyDerivationParameters Scrypt(int n, int r, int p)
            {
                return new KeyDerivationParameters
                {
                    Method = KeyDerivationMethod.Scrypt,
                    ScryptN = n,
                    ScryptR = r,
                    ScryptP = p
                };
            }

            /// <summary>
            /// Creates PBKDF2 parameters with custom iteration count
            /// </summary>
            public static KeyDerivationParameters Pbkdf2(int iterations)
            {
                return new KeyDerivationParameters
                {
                    Method = KeyDerivationMethod.PBKDF2_HMAC_SHA512,
                    Pbkdf2Iterations = iterations
                };
            }

            /// <summary>
            /// Validates the parameters for the selected method
            /// </summary>
            public void Validate()
            {
                switch (Method)
                {
                    case KeyDerivationMethod.PBKDF2_HMAC_SHA512:
                        if (Pbkdf2Iterations <= 0)
                            throw new ArgumentException("PBKDF2 iterations must be positive");
                        break;
                    case KeyDerivationMethod.Scrypt:
                        if (ScryptN <= 0 || (ScryptN & (ScryptN - 1)) != 0)
                            throw new ArgumentException("Scrypt N must be a positive power of 2");
                        if (ScryptR <= 0)
                            throw new ArgumentException("Scrypt r must be positive");
                        if (ScryptP <= 0)
                            throw new ArgumentException("Scrypt p must be positive");
                        break;
                    default:
                        throw new ArgumentException($"Unknown key derivation method: {Method}");
                }
            }

            /// <summary>
            /// Converts to internal UvfLib.Core.Api.KeyDerivationParameters
            /// </summary>
            internal UvfLib.Core.Api.KeyDerivationParameters ToInternal()
            {
                return new UvfLib.Core.Api.KeyDerivationParameters
                {
                    Method = Method switch
                    {
                        KeyDerivationMethod.PBKDF2_HMAC_SHA512 => UvfLib.Core.Api.KeyDerivationMethod.PBKDF2_HMAC_SHA512,
                        KeyDerivationMethod.Scrypt => UvfLib.Core.Api.KeyDerivationMethod.Scrypt,
                        _ => throw new ArgumentException($"Unknown method: {Method}")
                    },
                    Pbkdf2Iterations = Pbkdf2Iterations,
                    ScryptN = ScryptN,
                    ScryptR = ScryptR,
                    ScryptP = ScryptP
                };
            }

            /// <summary>
            /// Creates from internal UvfLib.Core.Api.KeyDerivationParameters
            /// </summary>
            internal static KeyDerivationParameters FromInternal(UvfLib.Core.Api.KeyDerivationParameters internal_params)
            {
                return new KeyDerivationParameters
                {
                    Method = internal_params.Method switch
                    {
                        UvfLib.Core.Api.KeyDerivationMethod.PBKDF2_HMAC_SHA512 => KeyDerivationMethod.PBKDF2_HMAC_SHA512,
                        UvfLib.Core.Api.KeyDerivationMethod.Scrypt => KeyDerivationMethod.Scrypt,
                        _ => throw new ArgumentException($"Unknown internal method: {internal_params.Method}")
                    },
                    Pbkdf2Iterations = internal_params.Pbkdf2Iterations,
                    ScryptN = internal_params.ScryptN,
                    ScryptR = internal_params.ScryptR,
                    ScryptP = internal_params.ScryptP
                };
            }
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

                // CreateNewCryptomatorV8VaultComplete writes the masterkey + vault config but does not
                // materialize the root CONTENT directory (d/XX/...). Create it through the storage now so
                // the vault is immediately mountable (ReadDir("/") would otherwise fail on a fresh vault).
                await EnsureRootContentDirectoryAsync().ConfigureAwait(false);

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

                // Self-heal: ensure the root content directory exists (older/partial vaults may lack it).
                await EnsureRootContentDirectoryAsync().ConfigureAwait(false);

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

        /// <summary>
        /// Ensures the vault's ROOT content directory exists on the backend. A freshly created vault has
        /// its masterkey/config written but not the root content directory (d/XX/...); without it the
        /// first ReadDir("/") fails and the vault is not mountable. Idempotent — a no-op once the
        /// directory exists (e.g. on subsequent loads).
        /// </summary>
        private async Task EnsureRootContentDirectoryAsync()
        {
            if (_vault == null || _baseStorage == null || string.IsNullOrEmpty(_vaultBasePath))
            {
                return;
            }

            string rootDirPath = Common.PathNormalizer.NormalizeVaultDirectoryPath(_vault.GetRootDirectoryPath());
            string rootContentDir = Common.PathNormalizer.CombineWithMountPoint(_vaultBasePath, rootDirPath);
            if (!await _baseStorage.DirectoryExistsAsync(rootContentDir).ConfigureAwait(false))
            {
                await _baseStorage.CreateDirectoryAsync(rootContentDir).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private Implementation - UVF (Multi-User Only)

        private async Task InitializeNewMultiUserUvfVaultAsync(IStorage storage, char[] adminPassword, string vaultBasePath, bool encryptFilenames, KeyDerivationParameters kdfParams, bool ownsStorage)
        {
            _baseStorage = storage ?? throw new ArgumentNullException(nameof(storage));
            _vaultBasePath = vaultBasePath ?? throw new ArgumentNullException(nameof(vaultBasePath));
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.UvfV3;

            try
            {
               
                // Validate KDF parameters
                kdfParams.Validate();
               
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
                string jweString = UvfLib.Core.Jwe.MultiUserJweVaultManager.CreateMultiUserVault(payload, userCredentials, adminPassword, kdfParams.ToInternal());
                
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
                await LoadMultiUserUvfVaultInternalAsync(adminPassword, encryptFilenames, null);
            }
            catch (Exception ex)
            {
                if (UvfLib.Core.Common.DebugLog.IsEnabled)
                {
                    Console.WriteLine($"[VaultManager DEBUG] ERROR in InitializeNewMultiUserUvfVaultAsync: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"[VaultManager DEBUG] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                }
                
                // Clean up on failure
                if (_ownsStorage && _baseStorage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                throw;
            }
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
                    // Pass userId so a non-admin user's password is matched against their own recipient.
                    encryptFilenames = VaultHandler.DetectFilenameEncryption(vaultFileContent, userPasswordBytes, userId);
                }
                finally
                {
                    // Clear the temporary password bytes from memory
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(userPasswordBytes);
                }

                // Load the vault for use
                await LoadMultiUserUvfVaultInternalAsync(userPassword, encryptFilenames, userId);
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

        private async Task LoadMultiUserUvfVaultInternalAsync(char[] userPassword, bool encryptFilenames, string? userId)
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
                var payload = UvfLib.Core.Jwe.MultiUserJweVaultManager.LoadMultiUserVault(jweString, userPassword, userId);
                
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
            catch (Exception ex)
            {
                if (UvfLib.Core.Common.DebugLog.IsEnabled)
                {
                    Console.WriteLine($"[VaultManager DEBUG] ERROR in LoadMultiUserUvfVaultInternalAsync: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"[VaultManager DEBUG] Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                    Console.WriteLine($"[VaultManager DEBUG] Stack trace: {ex.StackTrace}");
                }
                
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

            // Only the admin user exists -> rotate by adding a new seed and re-encrypting the vault file.
            // Existing files stay readable via the retained older seeds; new files use the latest seed
            // (forward secrecy). Reuses the implementation in VaultHandler.RotateUvfVaultKey.
            byte[] adminPasswordBytes = System.Text.Encoding.UTF8.GetBytes(adminPassword);
            try
            {
                byte[] rotatedVaultContent = VaultHandler.RotateUvfVaultKey(vaultFileContent, adminPasswordBytes);
                await System.IO.File.WriteAllBytesAsync(vaultFilePath, rotatedVaultContent);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(adminPasswordBytes);
            }
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


        #region Utility Methods

        /// <summary>
        /// Detects whether filename encryption is enabled in a UVF vault.
        /// </summary>
        /// <param name="vaultFileContent">Content of the vault.uvf file</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="userId">Optional user id; required for non-admin users (null tries all recipients).</param>
        /// <returns>True if filename encryption is enabled, false otherwise</returns>
        public static bool DetectFilenameEncryption(byte[] vaultFileContent, byte[] passwordBytes, string? userId = null)
        {
            return VaultHandler.DetectFilenameEncryption(vaultFileContent, passwordBytes, userId);
        }

        #endregion
    }
} 