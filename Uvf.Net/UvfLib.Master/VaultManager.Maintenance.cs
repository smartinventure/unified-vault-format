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
                KeyDerivationParameters.Default().ToInternal()
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
