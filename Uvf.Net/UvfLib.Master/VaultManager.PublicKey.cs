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
    public partial class VaultManager
    {
        #region Public-Key (Asymmetric) Multi-User

        /// <summary>
        /// Generates a fresh user key pair (P-384). Returns the public key (SubjectPublicKeyInfo) to hand
        /// to an admin for granting access, and the private key as password-encrypted PKCS#8 for the user
        /// to store. The admin never needs the private key.
        /// </summary>
        public static (byte[] PublicKey, byte[] EncryptedPrivateKey) GenerateUserKeyPair(char[] privateKeyPassword)
        {
            if (privateKeyPassword == null) throw new ArgumentNullException(nameof(privateKeyPassword));
            using var keyPair = UvfLib.Core.Common.EcdhKeyMaterial.GenerateKeyPair();
            return (UvfLib.Core.Common.EcdhKeyMaterial.ExportPublicKey(keyPair),
                    UvfLib.Core.Common.EcdhKeyMaterial.ExportEncryptedPrivateKey(keyPair, privateKeyPassword));
        }

        /// <summary>
        /// Grants vault access to a user's public key (ECDH-ES+A256KW). The admin password unwraps the
        /// current key and re-wraps it to the public key — the user's password is never needed.
        /// </summary>
        public static async Task AddPublicKeyUserAsync(string vaultPath, char[] adminPassword, string userId, byte[] userPublicKeySpki)
        {
            string vaultFilePath = Path.Combine(vaultPath, "vault.uvf");
            if (!System.IO.File.Exists(vaultFilePath))
                throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");

            string jwe = await System.IO.File.ReadAllTextAsync(vaultFilePath);
            string updated = UvfLib.Core.Jwe.MultiUserJweVaultManager.AddPublicKeyUserToVault(jwe, adminPassword, userId, userPublicKeySpki);
            await System.IO.File.WriteAllTextAsync(vaultFilePath, updated);
        }

        /// <summary>
        /// Loads a UVF vault using a user's EC private key (a public-key recipient added via
        /// <see cref="AddPublicKeyUserAsync"/>). No shared password required.
        /// </summary>
        public static async Task<VaultManager> LoadUvfVaultWithKeyAsync(string vaultPath, System.Security.Cryptography.ECDiffieHellman privateKey, string? userId = null)
        {
            var storage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            var manager = new VaultManager();
            await manager.InitializeExistingUvfVaultWithKeyAsync(storage, privateKey, vaultPath, userId, ownsStorage: true);
            return manager;
        }

        /// <summary>
        /// Convenience overload: imports a password-encrypted PKCS#8 private key, then loads the vault.
        /// </summary>
        public static async Task<VaultManager> LoadUvfVaultWithEncryptedKeyAsync(string vaultPath, byte[] encryptedPrivateKey, char[] keyPassword, string? userId = null)
        {
            using var key = UvfLib.Core.Common.EcdhKeyMaterial.ImportEncryptedPrivateKey(encryptedPrivateKey, keyPassword);
            return await LoadUvfVaultWithKeyAsync(vaultPath, key, userId);
        }

        /// <summary>
        /// Rotates the vault key for a public-key membership: adds a new seed and re-wraps the (fresh) key
        /// to admin and to every public-key member — without any member's password. Blocked if non-admin
        /// password recipients exist (those would need their passwords to re-wrap).
        /// </summary>
        public static async Task RotateForPublicKeyMembersAsync(string vaultPath, char[] adminPassword)
        {
            string vaultFilePath = Path.Combine(vaultPath, "vault.uvf");
            if (!System.IO.File.Exists(vaultFilePath))
                throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");

            byte[] vaultFileContent = await System.IO.File.ReadAllBytesAsync(vaultFilePath);
            string jwe = System.Text.Encoding.UTF8.GetString(vaultFileContent);

            var pubKeyMembers = UvfLib.Core.Jwe.MultiUserJweVaultManager.GetPublicKeyMembers(jwe);
            var pubKeyIds = new HashSet<string>(pubKeyMembers.Select(m => m.UserId));
            var passwordNonAdmin = UvfLib.Core.Jwe.MultiUserJweVaultManager.GetVaultUsers(jwe, adminPassword)
                .Where(u => u != "admin" && !pubKeyIds.Contains(u)).ToList();
            if (passwordNonAdmin.Any())
            {
                throw new InvalidOperationException(
                    $"Cannot rotate for public-key members while password-based users exist: {string.Join(", ", passwordNonAdmin)}. " +
                    "Remove them first, or rotate with all user passwords.");
            }

            // New seed + fresh key, admin-only; then re-wrap to each public-key member.
            byte[] adminPasswordBytes = System.Text.Encoding.UTF8.GetBytes(adminPassword);
            string rotated;
            try
            {
                byte[] rotatedAdminOnly = UvfLib.Vault.VaultHandler.RotateUvfVaultKey(vaultFileContent, adminPasswordBytes);
                rotated = System.Text.Encoding.UTF8.GetString(rotatedAdminOnly);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(adminPasswordBytes);
            }

            foreach (var (memberId, memberPublicKey) in pubKeyMembers)
            {
                rotated = UvfLib.Core.Jwe.MultiUserJweVaultManager.AddPublicKeyUserToVault(rotated, adminPassword, memberId, memberPublicKey);
            }
            await System.IO.File.WriteAllTextAsync(vaultFilePath, rotated);
        }

        private async Task InitializeExistingUvfVaultWithKeyAsync(IStorage storage, System.Security.Cryptography.ECDiffieHellman privateKey, string vaultBasePath, string? userId, bool ownsStorage)
        {
            _baseStorage = storage ?? throw new ArgumentNullException(nameof(storage));
            _vaultBasePath = vaultBasePath ?? throw new ArgumentNullException(nameof(vaultBasePath));
            _ownsStorage = ownsStorage;
            _vaultFormat = VaultFormat.UvfV3;

            try
            {
                string vaultFilePath = Path.Combine(_vaultBasePath, "vault.uvf");
                if (!System.IO.File.Exists(vaultFilePath))
                    throw new FileNotFoundException($"UVF vault file not found: {vaultFilePath}");

                string jwe = await System.IO.File.ReadAllTextAsync(vaultFilePath);
                var payload = UvfLib.Core.Jwe.MultiUserJweVaultManager.LoadMultiUserVaultWithKey(jwe, privateKey, userId);
                bool encryptFilenames = payload.Config?.EncryptFilenames ?? true;

                // Re-wrap the decrypted payload under a random ephemeral password so the existing
                // single-user loader can build the vault. The ephemeral password never leaves memory.
                char[] ephemeral = Guid.NewGuid().ToString("N").ToCharArray();
                byte[] ephemeralBytes = System.Text.Encoding.UTF8.GetBytes(ephemeral);
                try
                {
                    string singleUserJwe = UvfLib.Core.Jwe.MultiUserJweVaultManager.CreateSingleUserVault(payload, ephemeralBytes);
                    _vault = UvfLib.Vault.VaultHandler.LoadUvfVault(System.Text.Encoding.UTF8.GetBytes(singleUserJwe), ephemeralBytes);
                }
                finally
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(ephemeralBytes);
                    Array.Clear(ephemeral, 0, ephemeral.Length);
                }

                _vaultStorage = new UvfStorageDecorator(_baseStorage, _vault, encryptFilenames, _vaultBasePath);
                _isOpen = true;
            }
            catch (Exception)
            {
                if (_ownsStorage && _baseStorage is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                throw;
            }
        }

        #endregion
    }
}
