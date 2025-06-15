using StorageLib.Abstractions;
using UvfLib.Vault;
using System.Text;
using System.Security.Cryptography;

namespace UvfLib.Storage
{
    /// <summary>
    /// High-level vault manager that provides a simple, developer-friendly API for vault operations.
    /// Supports both path-based and storage-based vault creation/loading with cross-platform path normalization.
    /// 
    /// SECURITY NOTE: Passwords are handled as strings and will remain in memory until garbage collection.
    /// The library attempts to clear passwords from memory after key derivation, but this is best-effort.
    /// For maximum security in sensitive applications, consider using char[] and clearing manually.
    /// </summary>
    public class VaultManager : IDisposable, IStreamStorage
    {
        #region Private Fields

        private IStorage? _baseStorage;
        private VaultHandler? _vault;
        private CryptomatorStorageDecorator? _vaultStorage;
        private string? _vaultBasePath;
        private bool _isOpen;
        private bool _ownsStorage; // Track if we created the storage
        private bool _disposed;

        #endregion

        #region Factory Methods - Path-based (Simple)

        /// <summary>
        /// Creates a new Cryptomator V8 vault at the specified path using LocalStorage.
        /// 
        /// SECURITY NOTE: The password will remain in memory until garbage collection.
        /// The password is cleared from memory after key derivation where possible.
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
        /// Creates a new Cryptomator V8 vault at the specified path with enhanced password security.
        /// The char array will be cleared after key derivation.
        /// </summary>
        /// <param name="vaultPath">Path where the vault should be created</param>
        /// <param name="passwordChars">Password as char array (will be cleared after key derivation)</param>
        /// <returns>A VaultManager instance for the new vault</returns>
        public static async Task<VaultManager> CreateVaultAsync(string vaultPath, char[] passwordChars)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await CreateVaultAsync(storage, passwordChars, vaultPath, ownsStorage: true);
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault from the specified path using LocalStorage.
        /// 
        /// SECURITY NOTE: The password will remain in memory until garbage collection.
        /// The password is cleared from memory after key derivation where possible.
        /// </summary>
        /// <param name="vaultPath">Path where the vault is located</param>
        /// <param name="password">Vault password</param>
        /// <returns>A VaultManager instance for the existing vault</returns>
        public static async Task<VaultManager> LoadVaultAsync(string vaultPath, string password)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await LoadVaultAsync(storage, password, vaultPath, ownsStorage: true);
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault from the specified path with enhanced password security.
        /// The char array will be cleared after key derivation.
        /// </summary>
        /// <param name="vaultPath">Path where the vault is located</param>
        /// <param name="passwordChars">Password as char array (will be cleared after key derivation)</param>
        /// <returns>A VaultManager instance for the existing vault</returns>
        public static async Task<VaultManager> LoadVaultAsync(string vaultPath, char[] passwordChars)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            return await LoadVaultAsync(storage, passwordChars, vaultPath, ownsStorage: true);
        }

        #endregion

        #region Factory Methods - Storage-based (Flexible)

        /// <summary>
        /// Creates a new Cryptomator V8 vault using the provided storage connector.
        /// 
        /// SECURITY NOTE: The password will remain in memory until garbage collection.
        /// The password is cleared from memory after key derivation where possible.
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
        /// Creates a new Cryptomator V8 vault using the provided storage connector with enhanced password security.
        /// The char array will be cleared after key derivation.
        /// </summary>
        /// <param name="storage">Storage connector (LocalStorage, MemoryStorage, S3Storage, etc.)</param>
        /// <param name="passwordChars">Password as char array (will be cleared after key derivation)</param>
        /// <param name="vaultBasePath">Base path for vault operations (used for path translation)</param>
        /// <param name="ownsStorage">Whether VaultManager should dispose the storage when closed</param>
        /// <returns>A VaultManager instance for the new vault</returns>
        public static async Task<VaultManager> CreateVaultAsync(IStorage storage, char[] passwordChars, string vaultBasePath, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeNewVaultAsync(storage, passwordChars, vaultBasePath, ownsStorage);
            return manager;
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault using the provided storage connector.
        /// 
        /// SECURITY NOTE: The password will remain in memory until garbage collection.
        /// The password is cleared from memory after key derivation where possible.
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

        /// <summary>
        /// Loads an existing Cryptomator V8 vault using the provided storage connector with enhanced password security.
        /// The char array will be cleared after key derivation.
        /// </summary>
        /// <param name="storage">Storage connector (LocalStorage, MemoryStorage, S3Storage, etc.)</param>
        /// <param name="passwordChars">Password as char array (will be cleared after key derivation)</param>
        /// <param name="vaultBasePath">Base path for vault operations (used for path translation)</param>
        /// <param name="ownsStorage">Whether VaultManager should dispose the storage when closed</param>
        /// <returns>A VaultManager instance for the existing vault</returns>
        public static async Task<VaultManager> LoadVaultAsync(IStorage storage, char[] passwordChars, string vaultBasePath, bool ownsStorage = false)
        {
            var manager = new VaultManager();
            await manager.InitializeExistingVaultAsync(storage, passwordChars, vaultBasePath, ownsStorage);
            return manager;
        }

        #endregion

        #region Vault Management

        /// <summary>
        /// Changes the vault password.
        /// 
        /// SECURITY NOTE: The password will remain in memory until garbage collection.
        /// The password is cleared from memory after key derivation where possible.
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
        /// Changes the vault password with enhanced password security.
        /// The char array will be cleared after key derivation.
        /// </summary>
        /// <param name="newPasswordChars">New password as char array (will be cleared after key derivation)</param>
        public async Task ChangePasswordAsync(char[] newPasswordChars)
        {
            EnsureOpen();
            
            // TODO: Implement password change functionality with char[]
            throw new NotImplementedException("Password change functionality requires VaultHandler enhancement");
        }

        /// <summary>
        /// Closes the vault and securely disposes all cryptographic material
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

                // Securely dispose vault (clears cryptographic keys)
                _vault?.Dispose();
                _vault = null;
                _isOpen = false;
            }
        }

        #endregion
    }
} 