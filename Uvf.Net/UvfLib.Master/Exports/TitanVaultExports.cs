// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UvfLib.Master;

namespace UvfLib.Master.Exports
{
    /// <summary>
    /// TitanVault - Native C-style exports for cross-language compatibility.
    /// Provides secure vault operations with UTF-8 byte array password handling.
    /// 
    /// All functions use explicit UTF-8 byte buffers to avoid encoding ambiguity.
    /// Memory management is handled through dedicated cleanup functions.
    /// </summary>
    public static class TitanVaultExports
    {
        #region Constants and Error Codes


        // Debug logging control
        private static readonly bool DEBUG_ENABLED = false;


        // Return codes for TitanVault operations
        public const int TITAN_VAULT_SUCCESS = 0;
        public const int TITAN_VAULT_ERROR_INVALID_PARAMETER = -1;
        public const int TITAN_VAULT_ERROR_VAULT_NOT_FOUND = -2;
        public const int TITAN_VAULT_ERROR_INVALID_PASSWORD = -3;
        public const int TITAN_VAULT_ERROR_ACCESS_DENIED = -4;
        public const int TITAN_VAULT_ERROR_VAULT_CORRUPTED = -5;
        public const int TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER = -6;
        public const int TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT = -7;
        public const int TITAN_VAULT_ERROR_INTERNAL = -100;

        // Vault format constants
        public const int TITAN_VAULT_FORMAT_CRYPTOMATOR = 0;
        public const int TITAN_VAULT_FORMAT_UVF = 1;

        // KDF method constants
        public const int TITAN_VAULT_KDF_PBKDF2 = 0;
        public const int TITAN_VAULT_KDF_SCRYPT = 1;

        // Open flags constants (matches StorageLib.Abstractions.OpenFlags)
        public const int TITAN_VAULT_O_RDONLY = 0x0000;      // Open for read-only access
        public const int TITAN_VAULT_O_WRONLY = 0x0001;      // Open for write-only access
        public const int TITAN_VAULT_O_RDWR = 0x0002;        // Open for both reading and writing
        public const int TITAN_VAULT_O_CREAT = 0x0040;       // Create file if it doesn't exist
        public const int TITAN_VAULT_O_EXCL = 0x0080;        // Used with Create, fail if file exists
        public const int TITAN_VAULT_O_TRUNC = 0x0200;       // Truncate file to zero length if it exists
        public const int TITAN_VAULT_O_APPEND = 0x0400;      // Open the file in append mode

        #endregion

        #region Handle Management

        private static readonly ConcurrentDictionary<IntPtr, VaultManager> _vaultHandles = new();
        private static long _nextHandle = 1;
        private static string? _lastError;

        private static IntPtr CreateHandle(VaultManager vault)
        {
            var handle = new IntPtr(Interlocked.Increment(ref _nextHandle));
            _vaultHandles[handle] = vault;
            return handle;
        }

        private static VaultManager? GetVault(IntPtr handle)
        {
            return _vaultHandles.TryGetValue(handle, out var vault) ? vault : null;
        }

        private static bool RemoveHandle(IntPtr handle)
        {
            return _vaultHandles.TryRemove(handle, out _);
        }

        private static void SetLastError(string error)
        {
            _lastError = error;
        }

        #endregion

        #region Utility Functions

        /// <summary>
        /// Get TitanVault library version.
        /// Returns pointer to null-terminated UTF-8 string.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_get_version")]
        public static unsafe IntPtr GetVersion()
        {
            try
            {
                var version = "TitanVault v1.0.0 - UVF.NET Native Library";
                return AllocateUtf8String(version);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to get version: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Get last error message.
        /// Returns pointer to null-terminated UTF-8 string.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_get_last_error")]
        public static unsafe IntPtr GetLastError()
        {
            try
            {
                var error = _lastError ?? "No error";
                return AllocateUtf8String(error);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Detect vault format at specified path.
        /// Returns format constant or negative error code.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_detect_vault_format")]
        public static unsafe int DetectVaultFormat(
            byte* vaultPathBytes,
            int vaultPathLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0)
                {
                    SetLastError("Invalid vault path parameter");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                if (string.IsNullOrEmpty(vaultPath))
                {
                    SetLastError("Failed to decode vault path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var format = VaultManager.DetectVaultFormat(vaultPath);
                return format == VaultManager.VaultFormat.CryptomatorV8 
                    ? TITAN_VAULT_FORMAT_CRYPTOMATOR 
                    : TITAN_VAULT_FORMAT_UVF;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to detect vault format: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region Cryptomator Vault Operations

        /// <summary>
        /// Create new Cryptomator V8 vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_create_cryptomator_vault")]
        public static unsafe int CreateCryptomatorVault(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* passwordBytes,
            int passwordLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    passwordBytes == null || passwordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var passwordChars = GetPasswordChars((IntPtr)passwordBytes, passwordLength);

                if (string.IsNullOrEmpty(vaultPath) || passwordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    var vault = VaultManager.CreateCryptomatorVaultAsync(vaultPath, passwordChars).Result;
                    vault.CloseVaultAsync().Wait();
                    vault.Dispose();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    if (passwordChars != null)
                        Array.Clear(passwordChars, 0, passwordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to create Cryptomator vault: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create Cryptomator vault: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Load existing Cryptomator V8 vault.
        /// Returns vault handle or zero on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_load_cryptomator_vault")]
        public static unsafe IntPtr LoadCryptomatorVault(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* passwordBytes,
            int passwordLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    passwordBytes == null || passwordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return IntPtr.Zero;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var passwordChars = GetPasswordChars((IntPtr)passwordBytes, passwordLength);

                if (string.IsNullOrEmpty(vaultPath) || passwordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return IntPtr.Zero;
                }

                try
                {
                    var vault = VaultManager.LoadCryptomatorVaultAsync(vaultPath, passwordChars).Result;
                    return CreateHandle(vault);
                }
                finally
                {
                    if (passwordChars != null)
                        Array.Clear(passwordChars, 0, passwordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid password");
                return IntPtr.Zero;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to load Cryptomator vault: {ex.InnerException.Message}");
                return IntPtr.Zero;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid password");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to load Cryptomator vault: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Change Cryptomator vault password.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_change_cryptomator_password")]
        public static unsafe int ChangeCryptomatorPassword(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* oldPasswordBytes,
            int oldPasswordLength,
            byte* newPasswordBytes,
            int newPasswordLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    oldPasswordBytes == null || oldPasswordLength <= 0 ||
                    newPasswordBytes == null || newPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var oldPasswordChars = GetPasswordChars((IntPtr)oldPasswordBytes, oldPasswordLength);
                var newPasswordChars = GetPasswordChars((IntPtr)newPasswordBytes, newPasswordLength);

                if (string.IsNullOrEmpty(vaultPath) || oldPasswordChars == null || newPasswordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.ChangeCryptomatorVaultPasswordAsync(vaultPath, oldPasswordChars, newPasswordChars).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    if (oldPasswordChars != null)
                        Array.Clear(oldPasswordChars, 0, oldPasswordChars.Length);
                    if (newPasswordChars != null)
                        Array.Clear(newPasswordChars, 0, newPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid old password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to change password: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid old password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to change password: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region UVF Vault Operations

        /// <summary>
        /// Create new UVF vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_create_uvf_vault")]
        public static unsafe int CreateUvfVault(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            int encryptFilenames,
            int kdfMethod,
            int kdfIterations)
        {
            DebugLog("=== CreateUvfVault START ===");
            
            try
            {
                DebugLog($"Parameters: vaultPathLength={vaultPathLength}, adminPasswordLength={adminPasswordLength}, encryptFilenames={encryptFilenames}, kdfMethod={kdfMethod}, kdfIterations={kdfIterations}");
                
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    adminPasswordBytes == null || adminPasswordLength <= 0)
                {
                    DebugLog("ERROR: Invalid parameters - null pointers or invalid lengths");
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var adminPassword = GetUtf8String((IntPtr)adminPasswordBytes, adminPasswordLength);
                
                DebugLog($"Parsed vaultPath: '{vaultPath}'");
                DebugLog($"Parsed adminPassword length: {adminPassword?.Length ?? 0}");

                if (string.IsNullOrEmpty(vaultPath) || string.IsNullOrEmpty(adminPassword))
                {
                    DebugLog("ERROR: Failed to parse vault path or admin password");
                    SetLastError("Failed to parse parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                // Convert parameters
                var adminPasswordChars = adminPassword.ToCharArray();
                var kdfParams = ConvertKdfParameters(kdfMethod, kdfIterations);
                
                DebugLog($"KDF Parameters: Method={kdfParams.Method}, Iterations={kdfParams.Pbkdf2Iterations}");
                DebugLog($"About to call VaultManager.CreateUvfVaultAsync...");

                try
                {
                    VaultManager.CreateUvfVaultAsync(vaultPath, adminPasswordChars, encryptFilenames != 0, kdfParams).Wait();
                    DebugLog("VaultManager.CreateUvfVaultAsync completed successfully");
                    
                    DebugLog("=== CreateUvfVault SUCCESS ===");
                    return TITAN_VAULT_SUCCESS;
                }
                catch (Exception ex)
                {
                    DebugLog($"ERROR in VaultManager.CreateUvfVaultAsync: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        DebugLog($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                    DebugLog($"Stack trace: {ex.StackTrace}");
                    
                    SetLastError($"Failed to create UVF vault: {ex.Message}");
                    return TITAN_VAULT_ERROR_INTERNAL;
                }
                finally
                {
                    Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                }
            }
            catch (Exception ex)
            {
                DebugLog($"ERROR in CreateUvfVault outer catch: {ex.GetType().Name}: {ex.Message}");
                DebugLog($"Stack trace: {ex.StackTrace}");
                
                SetLastError($"Internal error: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Load existing UVF vault.
        /// Returns vault handle or zero on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_load_uvf_vault")]
        public static unsafe IntPtr LoadUvfVault(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* userPasswordBytes,
            int userPasswordLength,
            byte* userIdBytes,
            int userIdLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    userPasswordBytes == null || userPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return IntPtr.Zero;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var userPasswordChars = GetPasswordChars((IntPtr)userPasswordBytes, userPasswordLength);
                var userId = userIdBytes != null && userIdLength > 0 
                    ? GetUtf8String((IntPtr)userIdBytes, userIdLength) 
                    : null;

                if (string.IsNullOrEmpty(vaultPath) || userPasswordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return IntPtr.Zero;
                }

                try
                {
                    var vault = VaultManager.LoadUvfVaultAsync(vaultPath, userPasswordChars, userId).Result;
                    return CreateHandle(vault);
                }
                finally
                {
                    if (userPasswordChars != null)
                        Array.Clear(userPasswordChars, 0, userPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid password");
                return IntPtr.Zero;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to load UVF vault: {ex.InnerException.Message}");
                return IntPtr.Zero;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid password");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to load UVF vault: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Add user to UVF vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_add_user")]
        public static unsafe int AddUserToVault(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            byte* newUserIdBytes,
            int newUserIdLength,
            byte* newUserPasswordBytes,
            int newUserPasswordLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    adminPasswordBytes == null || adminPasswordLength <= 0 ||
                    newUserIdBytes == null || newUserIdLength <= 0 ||
                    newUserPasswordBytes == null || newUserPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var adminPasswordChars = GetPasswordChars((IntPtr)adminPasswordBytes, adminPasswordLength);
                var newUserId = GetUtf8String((IntPtr)newUserIdBytes, newUserIdLength);
                var newUserPasswordChars = GetPasswordChars((IntPtr)newUserPasswordBytes, newUserPasswordLength);

                if (string.IsNullOrEmpty(vaultPath) || adminPasswordChars == null ||
                    string.IsNullOrEmpty(newUserId) || newUserPasswordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.AddUserToVaultAsync(vaultPath, adminPasswordChars, newUserId, newUserPasswordChars).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    if (adminPasswordChars != null)
                        Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                    if (newUserPasswordChars != null)
                        Array.Clear(newUserPasswordChars, 0, newUserPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to add user: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to add user: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Remove user from UVF vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_remove_user")]
        public static unsafe int RemoveUserFromVault(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            byte* userIdToRemoveBytes,
            int userIdToRemoveLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    adminPasswordBytes == null || adminPasswordLength <= 0 ||
                    userIdToRemoveBytes == null || userIdToRemoveLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var adminPasswordChars = GetPasswordChars((IntPtr)adminPasswordBytes, adminPasswordLength);
                var userIdToRemove = GetUtf8String((IntPtr)userIdToRemoveBytes, userIdToRemoveLength);

                if (string.IsNullOrEmpty(vaultPath) || adminPasswordChars == null || string.IsNullOrEmpty(userIdToRemove))
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.RemoveUserFromVaultAsync(vaultPath, adminPasswordChars, userIdToRemove).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    if (adminPasswordChars != null)
                        Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to remove user: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to remove user: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Generates a user key pair (P-384) for public-key vault membership. Writes the public key
        /// (SubjectPublicKeyInfo) and the password-encrypted PKCS#8 private key into the caller's buffers;
        /// the *BufferSize args are in/out (set to the required length). Pass null buffers to query sizes.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_generate_user_keypair")]
        public static unsafe int GenerateUserKeyPair(
            byte* passwordBytes,
            int passwordLength,
            IntPtr publicKeyBuffer,
            IntPtr publicKeyBufferSize,
            IntPtr privateKeyBuffer,
            IntPtr privateKeyBufferSize)
        {
            try
            {
                if (passwordBytes == null || passwordLength <= 0 ||
                    publicKeyBufferSize == IntPtr.Zero || privateKeyBufferSize == IntPtr.Zero)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var password = GetPasswordChars((IntPtr)passwordBytes, passwordLength);
                if (password == null)
                {
                    SetLastError("Failed to decode password");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    var (publicKey, encryptedPrivateKey) = VaultManager.GenerateUserKeyPair(password);

                    int publicCapacity = Marshal.ReadInt32(publicKeyBufferSize);
                    int privateCapacity = Marshal.ReadInt32(privateKeyBufferSize);
                    Marshal.WriteInt32(publicKeyBufferSize, publicKey.Length);
                    Marshal.WriteInt32(privateKeyBufferSize, encryptedPrivateKey.Length);

                    if (publicKeyBuffer == IntPtr.Zero || privateKeyBuffer == IntPtr.Zero ||
                        publicCapacity < publicKey.Length || privateCapacity < encryptedPrivateKey.Length)
                    {
                        SetLastError($"Buffer too small (public={publicKey.Length}, private={encryptedPrivateKey.Length})");
                        return TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER;
                    }

                    Marshal.Copy(publicKey, 0, publicKeyBuffer, publicKey.Length);
                    Marshal.Copy(encryptedPrivateKey, 0, privateKeyBuffer, encryptedPrivateKey.Length);
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    Array.Clear(password, 0, password.Length);
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to generate user key pair: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Grants a user access to a UVF vault by their public key (SubjectPublicKeyInfo). The admin
        /// password unwraps and re-wraps the vault key — the user's password is not required.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_add_user_by_public_key")]
        public static unsafe int AddUserByPublicKey(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            byte* userIdBytes,
            int userIdLength,
            byte* publicKeyBytes,
            int publicKeyLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    adminPasswordBytes == null || adminPasswordLength <= 0 ||
                    userIdBytes == null || userIdLength <= 0 ||
                    publicKeyBytes == null || publicKeyLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var adminPassword = GetPasswordChars((IntPtr)adminPasswordBytes, adminPasswordLength);
                var userId = GetUtf8String((IntPtr)userIdBytes, userIdLength);
                var publicKey = new byte[publicKeyLength];
                Marshal.Copy((IntPtr)publicKeyBytes, publicKey, 0, publicKeyLength);

                if (string.IsNullOrEmpty(vaultPath) || adminPassword == null || string.IsNullOrEmpty(userId))
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.AddPublicKeyUserAsync(vaultPath, adminPassword, userId, publicKey).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    Array.Clear(adminPassword, 0, adminPassword.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to add user by public key: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Loads a UVF vault using a user's password-encrypted (PKCS#8) private key. Returns a vault
        /// handle or zero on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_load_uvf_vault_with_key")]
        public static unsafe IntPtr LoadUvfVaultWithKey(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* encryptedPrivateKeyBytes,
            int encryptedPrivateKeyLength,
            byte* keyPasswordBytes,
            int keyPasswordLength,
            byte* userIdBytes,
            int userIdLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    encryptedPrivateKeyBytes == null || encryptedPrivateKeyLength <= 0 ||
                    keyPasswordBytes == null || keyPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return IntPtr.Zero;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var encryptedPrivateKey = new byte[encryptedPrivateKeyLength];
                Marshal.Copy((IntPtr)encryptedPrivateKeyBytes, encryptedPrivateKey, 0, encryptedPrivateKeyLength);
                var keyPassword = GetPasswordChars((IntPtr)keyPasswordBytes, keyPasswordLength);
                var userId = userIdBytes != null && userIdLength > 0
                    ? GetUtf8String((IntPtr)userIdBytes, userIdLength)
                    : null;

                if (string.IsNullOrEmpty(vaultPath) || keyPassword == null)
                {
                    SetLastError("Failed to decode parameters");
                    return IntPtr.Zero;
                }

                try
                {
                    var vault = VaultManager.LoadUvfVaultWithEncryptedKeyAsync(vaultPath, encryptedPrivateKey, keyPassword, userId).Result;
                    return CreateHandle(vault);
                }
                finally
                {
                    Array.Clear(keyPassword, 0, keyPassword.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid key or password");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to load vault with key: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Rotates the vault key for a public-key membership: adds a new seed and re-wraps the fresh key
        /// to admin and every public-key member, without any member's password. Fails if non-admin
        /// password recipients exist.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_rotate_keys_pubkey")]
        public static unsafe int RotateKeysForPublicKeyMembers(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    adminPasswordBytes == null || adminPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var adminPassword = GetPasswordChars((IntPtr)adminPasswordBytes, adminPasswordLength);
                if (string.IsNullOrEmpty(vaultPath) || adminPassword == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.RotateForPublicKeyMembersAsync(vaultPath, adminPassword).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    Array.Clear(adminPassword, 0, adminPassword.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to rotate keys: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to rotate keys: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Read file from vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_read_file")]
        public static unsafe int ReadFile(
            IntPtr vaultHandle,
            byte* filePathBytes,
            int filePathLength,
            IntPtr buffer,
            IntPtr bufferSize)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == null || 
                    filePathLength <= 0 || bufferSize == IntPtr.Zero)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var filePath = GetUtf8String((IntPtr)filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                // Get file size first to determine buffer requirements
                var availableSize = Marshal.ReadInt32(bufferSize);
                
                // Try to get file size without reading content
                long fileSize;
                try
                {
                    // Use FileExists to check if file exists, then get its size
                    if (!vault.FileExistsAsync(filePath).Result)
                    {
                        SetLastError($"File not found: {filePath}");
                        return TITAN_VAULT_ERROR_VAULT_NOT_FOUND;
                    }
                    
                    // For the first call (buffer == IntPtr.Zero), we need to determine the file size
                    // without actually reading the file content
                    if (buffer == IntPtr.Zero)
                    {
                        // Try to read the file to get its size
                        var fileData = vault.ReadAllBytesAsync(filePath).Result;
                        fileSize = fileData.Length;

                // Write the required size back
                        Marshal.WriteInt32(bufferSize, (int)fileSize);
                        
                        SetLastError($"Buffer too small. Required: {fileSize}, Available: {availableSize}");
                        return TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER;
                    }
                    else
                    {
                        // Second call - read the actual file content
                        var fileData = vault.ReadAllBytesAsync(filePath).Result;
                        fileSize = fileData.Length;
                        
                        // Write the actual size back
                        Marshal.WriteInt32(bufferSize, (int)fileSize);
                        
                        if (availableSize < fileSize)
                {
                            SetLastError($"Buffer too small. Required: {fileSize}, Available: {availableSize}");
                    return TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER;
                }

                Marshal.Copy(fileData, 0, buffer, fileData.Length);
                    }
                }
                catch (Exception readEx)
                {
                    // If we can't read the file, we still need to set bufferSize to 0 for consistency
                    Marshal.WriteInt32(bufferSize, 0);
                    throw; // Re-throw to be handled by outer catch
                }
                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to read file: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to read file: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Write file to vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_write_file")]
        public static unsafe int WriteFile(
            IntPtr vaultHandle,
            byte* filePathBytes,
            int filePathLength,
            IntPtr buffer,
            int bufferSize)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == null || 
                    filePathLength <= 0 || buffer == IntPtr.Zero || bufferSize <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var filePath = GetUtf8String((IntPtr)filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                byte[] data = new byte[bufferSize];
                Marshal.Copy(buffer, data, 0, bufferSize);

                vault.WriteAllBytesAsync(filePath, data).Wait();
                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to write file: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to write file: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Check if file exists in vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_file_exists")]
        public static unsafe int FileExists(
            IntPtr vaultHandle,
            byte* filePathBytes,
            int filePathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == null || filePathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var filePath = GetUtf8String((IntPtr)filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                bool exists = vault.FileExistsAsync(filePath).Result;
                return exists ? 1 : 0;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to check file existence: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to check file existence: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region Vault Handle Management

        /// <summary>
        /// Close vault and free resources.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_close_vault")]
        public static unsafe int CloseVault(IntPtr vaultHandle)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Vault handle not found");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                vault.CloseVaultAsync().Wait();
                vault.Dispose();
                RemoveHandle(vaultHandle);

                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to close vault: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to close vault: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region Memory Management

        /// <summary>
        /// Free string allocated by TitanVault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_free_string")]
        public static unsafe void FreeString(IntPtr stringPtr)
        {
            try
            {
                if (stringPtr != IntPtr.Zero)
                {
                    NativeMemory.Free(stringPtr.ToPointer());
                }
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }

        /// <summary>
        /// Securely zero memory buffer.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_secure_zero_memory")]
        public static unsafe void SecureZeroMemory(IntPtr buffer, int size)
        {
            try
            {
                if (buffer != IntPtr.Zero && size > 0)
                {
                    var span = new Span<byte>(buffer.ToPointer(), size);
                    CryptographicOperations.ZeroMemory(span);
                }
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }

        #endregion

        #region Directory Operations

        /// <summary>
        /// Create directory in vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_create_directory")]
        public static unsafe int CreateDirectory(
            IntPtr vaultHandle,
            byte* directoryPathBytes,
            int directoryPathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || directoryPathBytes == null || directoryPathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var directoryPath = GetUtf8String((IntPtr)directoryPathBytes, directoryPathLength);
                if (string.IsNullOrEmpty(directoryPath))
                {
                    SetLastError("Failed to decode directory path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                vault.CreateDirectoryAsync(directoryPath).Wait();
                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to create directory: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create directory: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Check if directory exists in vault.
        /// Returns 1 if exists, 0 if not, negative on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_directory_exists")]
        public static unsafe int DirectoryExists(
            IntPtr vaultHandle,
            IntPtr directoryPathBytes,
            int directoryPathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || directoryPathBytes == IntPtr.Zero || directoryPathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var directoryPath = GetUtf8String(directoryPathBytes, directoryPathLength);
                if (string.IsNullOrEmpty(directoryPath))
                {
                    SetLastError("Failed to decode directory path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var exists = vault.DirectoryExistsAsync(directoryPath).Result;
                return exists ? 1 : 0;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to check directory existence: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to check directory existence: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Delete directory from vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_delete_directory")]
        public static unsafe int DeleteDirectory(
            IntPtr vaultHandle,
            IntPtr directoryPathBytes,
            int directoryPathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || directoryPathBytes == IntPtr.Zero || directoryPathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var directoryPath = GetUtf8String(directoryPathBytes, directoryPathLength);
                if (string.IsNullOrEmpty(directoryPath))
                {
                    SetLastError("Failed to decode directory path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                vault.DeleteDirectoryAsync(directoryPath).Wait();
                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to delete directory: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to delete directory: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Delete file from vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_delete_file")]
        public static unsafe int DeleteFile(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == IntPtr.Zero || filePathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var filePath = GetUtf8String(filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                vault.DeleteFileAsync(filePath).Wait();
                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to delete file: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to delete file: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region Stream Operations

        private static readonly ConcurrentDictionary<IntPtr, Stream> _streamHandles = new();
        private static long _nextStreamHandle = 1000;

        private static IntPtr CreateStreamHandle(Stream stream)
        {
            var handle = new IntPtr(Interlocked.Increment(ref _nextStreamHandle));
            _streamHandles[handle] = stream;
            return handle;
        }

        private static Stream? GetStream(IntPtr handle)
        {
            return _streamHandles.TryGetValue(handle, out var stream) ? stream : null;
        }

        private static bool RemoveStreamHandle(IntPtr handle)
        {
            return _streamHandles.TryRemove(handle, out _);
        }

        /// <summary>
        /// Open file for reading as stream.
        /// Returns stream handle or zero on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_open_read_stream")]
        public static unsafe IntPtr OpenReadStream(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == IntPtr.Zero || filePathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return IntPtr.Zero;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return IntPtr.Zero;
                }

                var filePath = GetUtf8String(filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return IntPtr.Zero;
                }

                var stream = vault.OpenReadAsync(filePath).Result;
                return CreateStreamHandle(stream);
            }
            catch (AggregateException ex) when (ex.InnerException is FileNotFoundException)
            {
                SetLastError("File not found");
                return IntPtr.Zero;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to open read stream: {ex.InnerException.Message}");
                return IntPtr.Zero;
            }
            catch (FileNotFoundException)
            {
                SetLastError("File not found");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to open read stream: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Open file for writing as stream.
        /// Returns stream handle or zero on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_open_write_stream")]
        public static unsafe IntPtr OpenWriteStream(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == IntPtr.Zero || filePathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return IntPtr.Zero;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return IntPtr.Zero;
                }

                var filePath = GetUtf8String(filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return IntPtr.Zero;
                }

                var stream = vault.OpenWriteAsync(filePath).Result;
                return CreateStreamHandle(stream);
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to open write stream: {ex.InnerException.Message}");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to open write stream: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Open file with specific flags as stream.
        /// Returns stream handle or zero on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_open_stream_with_flags")]
        public static unsafe IntPtr OpenStreamWithFlags(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength,
            int openFlags)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == IntPtr.Zero || filePathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return IntPtr.Zero;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return IntPtr.Zero;
                }

                var filePath = GetUtf8String(filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return IntPtr.Zero;
                }

                // Convert integer flags to OpenFlags enum
                var flags = (StorageLib.Abstractions.OpenFlags)openFlags;
                
                var stream = vault.OpenAsync(filePath, flags).Result;
                return CreateStreamHandle(stream);
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to open stream with flags: {ex.InnerException.Message}");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to open stream with flags: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Read from stream.
        /// Returns number of bytes read, or negative on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_stream_read")]
        public static unsafe int StreamRead(
            IntPtr streamHandle,
            IntPtr buffer,
            int count)
        {
            try
            {
                if (streamHandle == IntPtr.Zero || buffer == IntPtr.Zero || count <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var managedBuffer = new byte[count];
                var bytesRead = stream.Read(managedBuffer, 0, count);
                
                if (bytesRead > 0)
                {
                    Marshal.Copy(managedBuffer, 0, buffer, bytesRead);
                }

                return bytesRead;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to read from stream: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Write to stream.
        /// Returns number of bytes written, or negative on error.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_stream_write")]
        public static unsafe int StreamWrite(
            IntPtr streamHandle,
            IntPtr buffer,
            int count)
        {
            try
            {
                if (streamHandle == IntPtr.Zero || buffer == IntPtr.Zero || count <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var managedBuffer = new byte[count];
                Marshal.Copy(buffer, managedBuffer, 0, count);
                
                stream.Write(managedBuffer, 0, count);
                return count;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to write to stream: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Seek to a specific position in the stream.
        /// Returns the new position or negative error code.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_stream_seek")]
        public static unsafe long StreamSeek(
            IntPtr streamHandle,
            long offset,
            int origin)
        {
            try
            {
                if (streamHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                if (!stream.CanSeek)
                {
                    SetLastError("Stream does not support seeking");
                    return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
                }

                SeekOrigin seekOrigin = origin switch
                {
                    0 => SeekOrigin.Begin,
                    1 => SeekOrigin.Current,
                    2 => SeekOrigin.End,
                    _ => throw new ArgumentException("Invalid seek origin")
                };

                return stream.Seek(offset, seekOrigin);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to seek stream: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Get the current position in the stream.
        /// Returns position or negative error code.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_stream_get_position")]
        public static unsafe long StreamGetPosition(IntPtr streamHandle)
        {
            try
            {
                if (streamHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                return stream.Position;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to get stream position: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Get the length of the stream.
        /// Returns length or negative error code.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_stream_get_length")]
        public static unsafe long StreamGetLength(IntPtr streamHandle)
        {
            try
            {
                if (streamHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                return stream.Length;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to get stream length: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Set the length of a writable stream.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_stream_set_length")]
        public static unsafe int StreamSetLength(
            IntPtr streamHandle,
            long length)
        {
            try
            {
                if (streamHandle == IntPtr.Zero || length < 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                if (!stream.CanWrite)
                {
                    SetLastError("Stream is not writable");
                    return TITAN_VAULT_ERROR_ACCESS_DENIED;
                }

                stream.SetLength(length);
                return TITAN_VAULT_SUCCESS;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to set stream length: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Flush any pending writes to the stream.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_stream_flush")]
        public static unsafe int StreamFlush(IntPtr streamHandle)
        {
            try
            {
                if (streamHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                stream.Flush();
                return TITAN_VAULT_SUCCESS;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to flush stream: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Close stream and free resources.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_close_stream")]
        public static unsafe int CloseStream(IntPtr streamHandle)
        {
            try
            {
                if (streamHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Stream handle not found");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                stream.Dispose();
                RemoveStreamHandle(streamHandle);

                return TITAN_VAULT_SUCCESS;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to close stream: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region Advanced File Operations

        /// <summary>
        /// List directory contents in vault.
        /// Returns number of entries found, or negative error code.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_list_directory")]
        public static unsafe int ListDirectory(
            IntPtr vaultHandle,
            IntPtr directoryPathBytes,
            int directoryPathLength,
            IntPtr entriesBuffer,
            IntPtr maxEntries)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || directoryPathBytes == IntPtr.Zero || directoryPathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var directoryPath = GetUtf8String(directoryPathBytes, directoryPathLength);
                if (string.IsNullOrEmpty(directoryPath))
                {
                    SetLastError("Failed to decode directory path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var entries = vault.ListDirectoryAsync(directoryPath).Result;
                var entryList = entries.ToList();
                int maxEntriesCount = Marshal.ReadInt32(maxEntries);
                int actualCount = Math.Min(entryList.Count, maxEntriesCount);

                // Write entry names to buffer as null-terminated UTF-8 strings
                IntPtr* entryPtrs = (IntPtr*)entriesBuffer;
                for (int i = 0; i < actualCount; i++)
                {
                    entryPtrs[i] = AllocateUtf8String(entryList[i].Filename);
                }

                return actualCount;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to list directory: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to list directory: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Get file information (size, modified time, etc.).
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_get_file_info")]
        public static unsafe int GetFileInfo(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength,
            IntPtr fileSizePtr,
            IntPtr lastModifiedPtr)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == IntPtr.Zero || filePathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var filePath = GetUtf8String(filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var fileInfo = vault.GetFileInfoAsync(filePath).Result;
                if (fileInfo == null)
                {
                    SetLastError("File not found");
                    return TITAN_VAULT_ERROR_VAULT_NOT_FOUND;
                }

                if (fileSizePtr != IntPtr.Zero)
                {
                    Marshal.WriteInt64(fileSizePtr, fileInfo.Size);
                }

                if (lastModifiedPtr != IntPtr.Zero)
                {
                    // Write as Unix timestamp (seconds since epoch)
                    var unixTime = ((DateTimeOffset)fileInfo.LastModified).ToUnixTimeSeconds();
                    Marshal.WriteInt64(lastModifiedPtr, unixTime);
                }

                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to get file info: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to get file info: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Move/rename file or directory in vault.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_move")]
        public static unsafe int MoveEntry(
            IntPtr vaultHandle,
            IntPtr sourcePathBytes,
            int sourcePathLength,
            IntPtr destinationPathBytes,
            int destinationPathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || sourcePathBytes == IntPtr.Zero || sourcePathLength <= 0 ||
                    destinationPathBytes == IntPtr.Zero || destinationPathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var sourcePath = GetUtf8String(sourcePathBytes, sourcePathLength);
                var destinationPath = GetUtf8String(destinationPathBytes, destinationPathLength);

                if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath))
                {
                    SetLastError("Failed to decode paths");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                vault.MoveAsync(sourcePath, destinationPath, true).Wait(); // overwrite = true
                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to move entry: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to move entry: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Get list of users in UVF vault.
        /// Returns number of users found, or negative error code.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_get_vault_users")]
        public static unsafe int GetVaultUsers(
            IntPtr vaultPathBytes,
            int vaultPathLength,
            IntPtr adminPasswordBytes,
            int adminPasswordLength,
            IntPtr usersBuffer,
            IntPtr maxUsers)
        {
            try
            {
                if (vaultPathBytes == IntPtr.Zero || vaultPathLength <= 0 ||
                    adminPasswordBytes == IntPtr.Zero || adminPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String(vaultPathBytes, vaultPathLength);
                var adminPasswordChars = GetPasswordChars(adminPasswordBytes, adminPasswordLength);

                if (string.IsNullOrEmpty(vaultPath) || adminPasswordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    var users = VaultManager.GetVaultUsersAsync(vaultPath, adminPasswordChars).Result;
                    int maxUsersCount = Marshal.ReadInt32(maxUsers);
                    int actualCount = Math.Min(users.Count, maxUsersCount);

                    // Write user IDs to buffer as null-terminated UTF-8 strings
                    IntPtr* userPtrs = (IntPtr*)usersBuffer;
                    for (int i = 0; i < actualCount; i++)
                    {
                        userPtrs[i] = AllocateUtf8String(users[i].UserId);
                    }

                    return actualCount;
                }
                finally
                {
                    if (adminPasswordChars != null)
                        Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to get vault users: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to get vault users: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Read text file from vault as UTF-8 string.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_read_all_text")]
        public static unsafe IntPtr ReadAllText(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == IntPtr.Zero || filePathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return IntPtr.Zero;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return IntPtr.Zero;
                }

                var filePath = GetUtf8String(filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return IntPtr.Zero;
                }

                var text = vault.ReadAllTextAsync(filePath).Result;
                return AllocateUtf8String(text);
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to read text file: {ex.InnerException.Message}");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to read text file: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Write text to file in vault as UTF-8.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_write_all_text")]
        public static unsafe int WriteAllText(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength,
            IntPtr textBytes,
            int textLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == IntPtr.Zero || filePathLength <= 0 ||
                    textBytes == IntPtr.Zero || textLength < 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var filePath = GetUtf8String(filePathBytes, filePathLength);
                var text = GetUtf8String(textBytes, textLength);

                if (string.IsNullOrEmpty(filePath) || text == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                vault.WriteAllTextAsync(filePath, text).Wait();
                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to write text file: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to write text file: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Append text to file in vault as UTF-8.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_append_all_text")]
        public static unsafe int AppendAllText(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength,
            IntPtr textBytes,
            int textLength)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == IntPtr.Zero || filePathLength <= 0 ||
                    textBytes == IntPtr.Zero || textLength < 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vault = GetVault(vaultHandle);
                if (vault == null)
                {
                    SetLastError("Invalid vault handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var filePath = GetUtf8String(filePathBytes, filePathLength);
                var text = GetUtf8String(textBytes, textLength);

                if (string.IsNullOrEmpty(filePath) || text == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                vault.AppendAllTextAsync(filePath, text).Wait();
                return TITAN_VAULT_SUCCESS;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to append text file: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to append text file: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region Advanced Vault Operations

        /// <summary>
        /// Change UVF admin password.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_change_uvf_admin_password")]
        public static unsafe int ChangeUvfAdminPassword(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* oldPasswordBytes,
            int oldPasswordLength,
            byte* newPasswordBytes,
            int newPasswordLength)
        {
            try
            {
                if (vaultPathBytes == null || vaultPathLength <= 0 ||
                    oldPasswordBytes == null || oldPasswordLength <= 0 ||
                    newPasswordBytes == null || newPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String((IntPtr)vaultPathBytes, vaultPathLength);
                var oldPasswordChars = GetPasswordChars((IntPtr)oldPasswordBytes, oldPasswordLength);
                var newPasswordChars = GetPasswordChars((IntPtr)newPasswordBytes, newPasswordLength);

                if (string.IsNullOrEmpty(vaultPath) || oldPasswordChars == null || newPasswordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.ChangeUvfAdminPasswordAsync(vaultPath, oldPasswordChars, newPasswordChars).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    if (oldPasswordChars != null)
                        Array.Clear(oldPasswordChars, 0, oldPasswordChars.Length);
                    if (newPasswordChars != null)
                        Array.Clear(newPasswordChars, 0, newPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid old password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to change admin password: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid old password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to change admin password: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Change UVF user password.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_change_uvf_user_password")]
        public static unsafe int ChangeUvfUserPassword(
            IntPtr vaultPathBytes,
            int vaultPathLength,
            IntPtr adminPasswordBytes,
            int adminPasswordLength,
            IntPtr userIdBytes,
            int userIdLength,
            IntPtr newUserPasswordBytes,
            int newUserPasswordLength)
        {
            try
            {
                if (vaultPathBytes == IntPtr.Zero || vaultPathLength <= 0 ||
                    adminPasswordBytes == IntPtr.Zero || adminPasswordLength <= 0 ||
                    userIdBytes == IntPtr.Zero || userIdLength <= 0 ||
                    newUserPasswordBytes == IntPtr.Zero || newUserPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String(vaultPathBytes, vaultPathLength);
                var adminPasswordChars = GetPasswordChars(adminPasswordBytes, adminPasswordLength);
                var userId = GetUtf8String(userIdBytes, userIdLength);
                var newUserPasswordChars = GetPasswordChars(newUserPasswordBytes, newUserPasswordLength);

                if (string.IsNullOrEmpty(vaultPath) || adminPasswordChars == null ||
                    string.IsNullOrEmpty(userId) || newUserPasswordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.ChangeUvfUserPasswordAsync(vaultPath, adminPasswordChars, userId, newUserPasswordChars).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    if (adminPasswordChars != null)
                        Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                    if (newUserPasswordChars != null)
                        Array.Clear(newUserPasswordChars, 0, newUserPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to change user password: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to change user password: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Rotate vault keys (regenerate encryption keys).
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_rotate_keys")]
        public static unsafe int RotateVaultKeys(
            IntPtr vaultPathBytes,
            int vaultPathLength,
            IntPtr adminPasswordBytes,
            int adminPasswordLength,
            int vaultFormat)
        {
            try
            {
                if (vaultPathBytes == IntPtr.Zero || vaultPathLength <= 0 ||
                    adminPasswordBytes == IntPtr.Zero || adminPasswordLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String(vaultPathBytes, vaultPathLength);
                var adminPasswordChars = GetPasswordChars(adminPasswordBytes, adminPasswordLength);

                if (string.IsNullOrEmpty(vaultPath) || adminPasswordChars == null)
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.RotateVaultKeysAsync(vaultPath, adminPasswordChars).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    if (adminPasswordChars != null)
                        Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (AggregateException ex) when (ex.InnerException is NotImplementedException)
            {
                SetLastError("Key rotation not implemented for this vault type");
                return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to rotate keys: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (UnauthorizedAccessException)
            {
                SetLastError("Invalid admin password");
                return TITAN_VAULT_ERROR_INVALID_PASSWORD;
            }
            catch (NotImplementedException)
            {
                SetLastError("Key rotation not implemented for this vault type");
                return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to rotate keys: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Backup vault files to specified directory.
        /// </summary>
        [UnmanagedCallersOnly(EntryPoint = "titan_vault_backup_files")]
        public static unsafe int BackupVaultFiles(
            IntPtr vaultPathBytes,
            int vaultPathLength,
            IntPtr backupPathBytes,
            int backupPathLength,
            int overwriteExisting)
        {
            try
            {
                if (vaultPathBytes == IntPtr.Zero || vaultPathLength <= 0 ||
                    backupPathBytes == IntPtr.Zero || backupPathLength <= 0)
                {
                    SetLastError("Invalid parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                var vaultPath = GetUtf8String(vaultPathBytes, vaultPathLength);
                var backupPath = GetUtf8String(backupPathBytes, backupPathLength);

                if (string.IsNullOrEmpty(vaultPath) || string.IsNullOrEmpty(backupPath))
                {
                    SetLastError("Failed to decode parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                try
                {
                    VaultManager.BackupVaultFilesAsync(vaultPath, backupPath, overwriteExisting != 0).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                catch (AggregateException ex) when (ex.InnerException is DirectoryNotFoundException)
                {
                    SetLastError("Vault directory not found");
                    return TITAN_VAULT_ERROR_VAULT_NOT_FOUND;
                }
                catch (AggregateException ex) when (ex.InnerException is FileNotFoundException)
                {
                    SetLastError("Required vault files not found");
                    return TITAN_VAULT_ERROR_VAULT_NOT_FOUND;
                }
                catch (AggregateException ex) when (ex.InnerException is IOException)
                {
                    SetLastError($"I/O error during backup: {ex.InnerException.Message}");
                    return TITAN_VAULT_ERROR_ACCESS_DENIED;
                }
                catch (AggregateException ex) when (ex.InnerException != null)
                {
                    SetLastError($"Failed to backup vault files: {ex.InnerException.Message}");
                    return TITAN_VAULT_ERROR_INTERNAL;
                }
            }
            catch (DirectoryNotFoundException)
            {
                SetLastError("Vault directory not found");
                return TITAN_VAULT_ERROR_VAULT_NOT_FOUND;
            }
            catch (FileNotFoundException)
            {
                SetLastError("Required vault files not found");
                return TITAN_VAULT_ERROR_VAULT_NOT_FOUND;
            }
            catch (IOException ex)
            {
                SetLastError($"I/O error during backup: {ex.Message}");
                return TITAN_VAULT_ERROR_ACCESS_DENIED;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to backup vault files: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region Helper Methods

        private static unsafe IntPtr AllocateUtf8String(string str)
        {
            if (string.IsNullOrEmpty(str))
                return IntPtr.Zero;

            var bytes = Encoding.UTF8.GetBytes(str + '\0');
            var ptr = NativeMemory.Alloc((nuint)bytes.Length);
            fixed (byte* src = bytes)
            {
                Buffer.MemoryCopy(src, ptr, bytes.Length, bytes.Length);
            }
            return new IntPtr(ptr);
        }

        private static unsafe string? GetUtf8String(IntPtr ptr, int length)
        {
            if (ptr == IntPtr.Zero || length <= 0)
                return null;

            var bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static unsafe char[]? GetPasswordChars(IntPtr ptr, int length)
        {
            if (ptr == IntPtr.Zero || length <= 0)
                return null;

            var bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            
            try
            {
                return Encoding.UTF8.GetChars(bytes);
            }
            finally
            {
                // Clear the temporary byte array
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        #endregion

        #region Debug Logging


        private static void DebugLog(string message)
        {
            if (DEBUG_ENABLED)
            {
                Console.WriteLine($"[TitanVault DEBUG] {message}");
            }
        }

        #endregion

        #region KDF Parameters

        private static VaultManager.KeyDerivationParameters ConvertKdfParameters(int kdfMethod, int kdfIterations)
        {
            return kdfMethod switch
            {
                TITAN_VAULT_KDF_SCRYPT => VaultManager.KeyDerivationParameters.Scrypt(16384, 8, 1), // Standard scrypt parameters
                TITAN_VAULT_KDF_PBKDF2 => VaultManager.KeyDerivationParameters.Pbkdf2(kdfIterations > 0 ? kdfIterations : 210000),
                _ => VaultManager.KeyDerivationParameters.Pbkdf2(kdfIterations > 0 ? kdfIterations : 210000) // Default to PBKDF2 (OWASP 2023)
            };
        }

        #endregion
    }
} 