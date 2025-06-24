using System.Runtime.InteropServices;
using UvfLib.Master;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.IO;

namespace ExampleVaultApp.Wrapper
{
    /// <summary>
    /// Managed wrapper that mimics the native TitanVault API but calls VaultManager directly.
    /// This allows testing the same C-style wrapper logic without AOT compilation.
    /// </summary>
    public static class TitanVaultNativeMethods
    {
        private static string? _lastError;
        private static readonly Dictionary<IntPtr, VaultManager> _vaultHandles = new();
        private static long _nextHandle = 1;

        #region Helper Methods

        private static unsafe string GetUtf8String(byte* ptr, int length)
        {
            if (ptr == null || length <= 0) return string.Empty;
            return Encoding.UTF8.GetString(ptr, length);
        }

        private static unsafe char[] GetPasswordChars(byte* ptr, int length)
        {
            if (ptr == null || length <= 0) return Array.Empty<char>();
            byte[] bytes = new byte[length];
            Marshal.Copy((IntPtr)ptr, bytes, 0, length);
            return Encoding.UTF8.GetChars(bytes);
        }

        private static IntPtr CreateHandle(VaultManager vault)
        {
            var handle = new IntPtr(Interlocked.Increment(ref _nextHandle));
            _vaultHandles[handle] = vault;
            return handle;
        }

        private static void SetLastError(string error)
        {
            _lastError = error;
        }

        #endregion

        #region Library Information

        /// <summary>
        /// Get TitanVault library version
        /// </summary>
        public static unsafe IntPtr GetVersion()
        {
            string version = "1.0.0-managed";
            return Marshal.StringToHGlobalAnsi(version);
        }

        /// <summary>
        /// Get last error message
        /// </summary>
        public static unsafe IntPtr GetLastError()
        {
            return _lastError != null ? Marshal.StringToHGlobalAnsi(_lastError) : IntPtr.Zero;
        }

        /// <summary>
        /// Detect vault format at specified path
        /// </summary>
        public static unsafe int DetectVaultFormat(byte* vaultPathBytes, int vaultPathLength)
        {
            try
            {
                string vaultPath = GetUtf8String(vaultPathBytes, vaultPathLength);
                
                // Check if it's a UVF vault (has vault.uvf file)
                if (File.Exists(Path.Combine(vaultPath, "vault.uvf")))
                    return 1; // UVF
                
                // Check if it's a Cryptomator vault (has masterkey.cryptomator file)
                if (File.Exists(Path.Combine(vaultPath, "masterkey.cryptomator")))
                    return 0; // Cryptomator
                
                return -1; // Unknown
            }
            catch (Exception ex)
            {
                SetLastError(ex.Message);
                return -1;
            }
        }

        #endregion

        #region Cryptomator Vault Operations

        /// <summary>
        /// Create new Cryptomator V8 vault
        /// </summary>
        public static unsafe int CreateCryptomatorVault(
            byte* vaultPathBytes, int vaultPathLength,
            byte* passwordBytes, int passwordLength)
        {
            try
            {
                string vaultPath = GetUtf8String(vaultPathBytes, vaultPathLength);
                char[] password = GetPasswordChars(passwordBytes, passwordLength);
                
                try
                {
                    var vault = VaultManager.CreateCryptomatorVaultAsync(vaultPath, password).Result;
                    vault.CloseVaultAsync().Wait();
                    vault.Dispose();
                    return 0; // SUCCESS
                }
                finally
                {
                    Array.Clear(password, 0, password.Length);
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create Cryptomator vault: {ex.Message}");
                return -100; // INTERNAL_ERROR
            }
        }

        #endregion

        #region UVF Vault Operations

        /// <summary>
        /// Create new UVF vault
        /// </summary>
        public static unsafe int CreateUvfVault(
            byte* vaultPathBytes, int vaultPathLength,
            byte* adminPasswordBytes, int adminPasswordLength,
            int encryptFilenames, int kdfMethod, int kdfIterations)
        {
            try
            {
                string vaultPath = GetUtf8String(vaultPathBytes, vaultPathLength);
                char[] adminPassword = GetPasswordChars(adminPasswordBytes, adminPasswordLength);
                
                var kdfParams = kdfMethod == 1 // SCRYPT
                    ? VaultManager.KeyDerivationParameters.Scrypt(16384, 8, 1)
                    : VaultManager.KeyDerivationParameters.Pbkdf2(kdfIterations > 0 ? kdfIterations : 64000);
                
                try
                {
                    var vault = VaultManager.CreateUvfVaultAsync(vaultPath, adminPassword, encryptFilenames != 0, kdfParams).Result;
                    vault.CloseVaultAsync().Wait();
                    vault.Dispose();
                    return 0; // SUCCESS
                }
                finally
                {
                    Array.Clear(adminPassword, 0, adminPassword.Length);
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create UVF vault: {ex.Message}");
                return -100; // INTERNAL_ERROR
            }
        }

        /// <summary>
        /// Load existing UVF vault
        /// </summary>
        public static unsafe IntPtr LoadUvfVault(
            byte* vaultPathBytes, int vaultPathLength,
            byte* userPasswordBytes, int userPasswordLength,
            byte* userIdBytes, int userIdLength)
        {
            try
            {
                string vaultPath = GetUtf8String(vaultPathBytes, vaultPathLength);
                char[] userPassword = GetPasswordChars(userPasswordBytes, userPasswordLength);
                string? userId = userIdLength > 0 ? GetUtf8String(userIdBytes, userIdLength) : null;
                
                try
                {
                    var vault = VaultManager.LoadUvfVaultAsync(vaultPath, userPassword, userId).Result;
                    return CreateHandle(vault);
                }
                finally
                {
                    Array.Clear(userPassword, 0, userPassword.Length);
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to load UVF vault: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        #endregion

        #region Memory Management

        /// <summary>
        /// Free string allocated by native methods
        /// </summary>
        public static unsafe void FreeString(IntPtr stringPtr)
        {
            if (stringPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(stringPtr);
            }
        }

        #endregion
    }
} 