using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using UvfLib.Master;

namespace ExampleVaultApp.Wrapper
{
    /// <summary>
    /// Managed wrapper that provides the same interface as native TitanVault methods
    /// but calls VaultManager directly instead of using P/Invoke or UnmanagedCallersOnly exports.
    /// </summary>
    public static class TitanVaultNativeMethods
    {
        #region Constants and Error Codes

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

        #region Stream Handle Management

        private static readonly ConcurrentDictionary<IntPtr, Stream> _streamHandles = new();
        private static long _nextStreamHandle = 1000; // Start at 1000 to differentiate from vault handles

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
            if (_streamHandles.TryRemove(handle, out var stream))
            {
                try
                {
                    stream?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
                return true;
            }
            return false;
        }

        #endregion

        #region Utility Functions

        /// <summary>
        /// Get TitanVault library version.
        /// </summary>
        public static unsafe IntPtr GetVersion()
        {
            try
            {
                var version = "TitanVault v1.0.0 - UVF.NET Managed Wrapper";
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
        /// </summary>
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
        /// </summary>
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
        /// </summary>
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
        public static unsafe int CreateUvfVault(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            int encryptFilenames,
            int kdfMethod,
            int kdfIterations)
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
                var adminPassword = GetUtf8String((IntPtr)adminPasswordBytes, adminPasswordLength);

                if (string.IsNullOrEmpty(vaultPath) || string.IsNullOrEmpty(adminPassword))
                {
                    SetLastError("Failed to parse parameters");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                // Convert parameters
                var adminPasswordChars = adminPassword.ToCharArray();
                var kdfParams = ConvertKdfParameters(kdfMethod, kdfIterations);

                try
                {
                    VaultManager.CreateUvfVaultAsync(vaultPath, adminPasswordChars, encryptFilenames != 0, kdfParams).Wait();
                    return TITAN_VAULT_SUCCESS;
                }
                finally
                {
                    Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                }
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to create UVF vault: {ex.InnerException.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to create UVF vault: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        /// <summary>
        /// Load existing UVF vault.
        /// </summary>
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
            SetLastError("AddUserToVault not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Remove user from UVF vault.
        /// </summary>
        public static unsafe int RemoveUserFromVault(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            byte* userIdToRemoveBytes,
            int userIdToRemoveLength)
        {
            SetLastError("RemoveUserFromVault not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Change UVF admin password.
        /// </summary>
        public static unsafe int ChangeUvfAdminPassword(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* oldPasswordBytes,
            int oldPasswordLength,
            byte* newPasswordBytes,
            int newPasswordLength)
        {
            SetLastError("ChangeUvfAdminPassword not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Change UVF user password.
        /// </summary>
        public static unsafe int ChangeUvfUserPassword(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            byte* userIdBytes,
            int userIdLength,
            byte* newUserPasswordBytes,
            int newUserPasswordLength)
        {
            SetLastError("ChangeUvfUserPassword not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Rotate vault keys.
        /// </summary>
        public static unsafe int RotateVaultKeys(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            int vaultFormat)
        {
            SetLastError("RotateVaultKeys not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Backup vault files.
        /// </summary>
        public static unsafe int BackupVaultFiles(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* backupPathBytes,
            int backupPathLength,
            int overwriteExisting)
        {
            SetLastError("BackupVaultFiles not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Read file from vault.
        /// </summary>
        public static unsafe int ReadFile(
            IntPtr vaultHandle,
            byte* filePathBytes,
            int filePathLength,
            byte* buffer,
            int* bufferSize)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == null || 
                    filePathLength <= 0 || bufferSize == null)
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

                var fileData = vault.ReadAllBytesAsync(filePath).Result;
                var requiredSize = fileData.Length;
                var availableSize = *bufferSize;

                // Write the required size back
                *bufferSize = requiredSize;

                if (buffer == null || availableSize < requiredSize)
                {
                    SetLastError($"Buffer too small. Required: {requiredSize}, Available: {availableSize}");
                    return TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER;
                }

                fixed (byte* fileDataPtr = fileData)
                {
                    Buffer.MemoryCopy(fileDataPtr, buffer, availableSize, fileData.Length);
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
        public static unsafe int WriteFile(
            IntPtr vaultHandle,
            byte* filePathBytes,
            int filePathLength,
            byte* buffer,
            int bufferSize)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == null || 
                    filePathLength <= 0 || buffer == null || bufferSize <= 0)
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

                // Copy buffer data to managed array
                var fileData = new byte[bufferSize];
                fixed (byte* fileDataPtr = fileData)
                {
                    Buffer.MemoryCopy(buffer, fileDataPtr, bufferSize, bufferSize);
                }

                // Write to vault using VaultManager
                vault.WriteAllBytesAsync(filePath, fileData).Wait();
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

        /// <summary>
        /// Delete file from vault.
        /// </summary>
        public static unsafe int DeleteFile(
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

        #region Directory Operations

        /// <summary>
        /// Create directory in vault.
        /// </summary>
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
        /// </summary>
        public static unsafe int DirectoryExists(
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

                bool exists = vault.DirectoryExistsAsync(directoryPath).Result;
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
        public static unsafe int DeleteDirectory(
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

        #endregion

        #region Stream Operations

        /// <summary>
        /// Open file for reading as stream.
        /// </summary>
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

                // Use VaultManager's read streaming capability
                var stream = vault.OpenReadAsync(filePath).Result;
                return CreateStreamHandle(stream);
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to open read stream: {ex.InnerException.Message}");
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
        /// </summary>
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

                // Use VaultManager's write streaming capability
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
        /// Read from stream.
        /// </summary>
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
                    return -1;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                // Create managed buffer and read from stream
                var managedBuffer = new byte[count];
                int bytesRead = stream.Read(managedBuffer, 0, count);

                // Copy to unmanaged buffer
                if (bytesRead > 0)
                {
                    Marshal.Copy(managedBuffer, 0, buffer, bytesRead);
                }

                return bytesRead;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to read from stream: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Write to stream.
        /// </summary>
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
                    return -1;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                // Copy from unmanaged buffer to managed buffer
                var managedBuffer = new byte[count];
                Marshal.Copy(buffer, managedBuffer, 0, count);

                // Write to stream
                stream.Write(managedBuffer, 0, count);
                
                return count; // Return number of bytes written
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to write to stream: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Seek in stream.
        /// </summary>
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
                    return -1;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                if (!stream.CanSeek)
                {
                    SetLastError("Stream does not support seeking");
                    return -1;
                }

                var seekOrigin = origin switch
                {
                    0 => SeekOrigin.Begin,
                    1 => SeekOrigin.Current,
                    2 => SeekOrigin.End,
                    _ => throw new ArgumentException($"Invalid seek origin: {origin}")
                };

                return stream.Seek(offset, seekOrigin);
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to seek in stream: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Get stream position.
        /// </summary>
        public static unsafe long StreamGetPosition(IntPtr streamHandle)
        {
            try
            {
                if (streamHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                return stream.Position;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to get stream position: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Get stream length.
        /// </summary>
        public static unsafe long StreamGetLength(IntPtr streamHandle)
        {
            try
            {
                if (streamHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                return stream.Length;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to get stream length: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Set stream length.
        /// </summary>
        public static unsafe int StreamSetLength(
            IntPtr streamHandle,
            long length)
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
        /// Flush stream.
        /// </summary>
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
        /// Close stream.
        /// </summary>
        public static unsafe int CloseStream(IntPtr streamHandle)
        {
            try
            {
                if (streamHandle == IntPtr.Zero)
                {
                    SetLastError("Invalid stream handle");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }

                if (RemoveStreamHandle(streamHandle))
                {
                    return TITAN_VAULT_SUCCESS;
                }
                else
                {
                    SetLastError("Stream handle not found");
                    return TITAN_VAULT_ERROR_INVALID_PARAMETER;
                }
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to close stream: {ex.Message}");
                return TITAN_VAULT_ERROR_INTERNAL;
            }
        }

        #endregion

        #region Advanced Operations

        /// <summary>
        /// List directory contents.
        /// </summary>
        public static unsafe int ListDirectory(
            IntPtr vaultHandle,
            IntPtr directoryPathBytes,
            int directoryPathLength,
            IntPtr entriesBuffer,
            IntPtr maxEntries)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || directoryPathBytes == IntPtr.Zero || 
                    directoryPathLength <= 0 || entriesBuffer == IntPtr.Zero || maxEntries == IntPtr.Zero)
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

                // Get directory entries
                var entries = vault.ListDirectoryAsync(directoryPath).Result.ToList();
                
                // DEBUG: Log what we found in this directory
                Console.WriteLine($"🔍 DEBUG ListDirectory '{directoryPath}': Found {entries.Count} entries");
                for (int i = 0; i < entries.Count; i++)
                {
                    Console.WriteLine($"   Entry {i}: '{entries[i].Filename}' (IsDirectory: {entries[i].IsDirectory})");
                }
                
                // Calculate required buffer size
                int requiredSize = 0;
                foreach (var entry in entries)
                {
                    var entryNameBytes = Encoding.UTF8.GetBytes(entry.Filename);
                    requiredSize += 4 + entryNameBytes.Length; // 4 bytes for length + string bytes
                    Console.WriteLine($"   Entry '{entry.Filename}': {entryNameBytes.Length} bytes + 4 = {4 + entryNameBytes.Length} bytes");
                }
                Console.WriteLine($"   Total required buffer size: {requiredSize} bytes");

                // Get the maximum number of entries we can return
                int maxEntriesCount = Marshal.ReadInt32(maxEntries);
                int actualCount = Math.Min(entries.Count, maxEntriesCount);
                
                // Convert entries to array of string pointers
                IntPtr[] entryPtrs = new IntPtr[actualCount];
                for (int i = 0; i < actualCount; i++)
                {
                    // Use the Filename property from StorageLib.Abstractions.FileObject
                    entryPtrs[i] = AllocateUtf8String(entries[i].Filename);
                }
                
                // Copy the pointers to the output buffer
                Marshal.Copy(entryPtrs, 0, entriesBuffer, actualCount);
                
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
        /// Get file information.
        /// </summary>
        public static unsafe int GetFileInfo(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength,
            IntPtr fileSizePtr,
            IntPtr lastModifiedPtr)
        {
            SetLastError("GetFileInfo not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Move/rename entry.
        /// </summary>
        public static unsafe int MoveEntry(
            IntPtr vaultHandle,
            IntPtr sourcePathBytes,
            int sourcePathLength,
            IntPtr destinationPathBytes,
            int destinationPathLength)
        {
            SetLastError("MoveEntry not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Get vault users.
        /// </summary>
        public static unsafe int GetVaultUsers(
            byte* vaultPathBytes,
            int vaultPathLength,
            byte* adminPasswordBytes,
            int adminPasswordLength,
            IntPtr usersBuffer,
            IntPtr maxUsers)
        {
            SetLastError("GetVaultUsers not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Read all text from file.
        /// </summary>
        public static unsafe IntPtr ReadAllText(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength)
        {
            SetLastError("ReadAllText not implemented in managed wrapper");
            return IntPtr.Zero;
        }

        /// <summary>
        /// Write all text to file.
        /// </summary>
        public static unsafe int WriteAllText(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength,
            IntPtr textBytes,
            int textLength)
        {
            SetLastError("WriteAllText not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        /// <summary>
        /// Append all text to file.
        /// </summary>
        public static unsafe int AppendAllText(
            IntPtr vaultHandle,
            IntPtr filePathBytes,
            int filePathLength,
            IntPtr textBytes,
            int textLength)
        {
            SetLastError("AppendAllText not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        #endregion

        #region Vault Handle Management

        /// <summary>
        /// Close vault and free resources.
        /// </summary>
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
        public static unsafe void FreeString(IntPtr stringPtr)
        {
            try
            {
                if (stringPtr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(stringPtr);
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

        #region Helper Methods

        private static unsafe IntPtr AllocateUtf8String(string str)
        {
            if (string.IsNullOrEmpty(str))
                return IntPtr.Zero;

            var bytes = Encoding.UTF8.GetBytes(str + '\0');
            var ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
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

        private static VaultManager.KeyDerivationParameters ConvertKdfParameters(int kdfMethod, int kdfIterations)
        {
            return kdfMethod switch
            {
                TITAN_VAULT_KDF_SCRYPT => VaultManager.KeyDerivationParameters.Scrypt(16384, 8, 1), // Standard scrypt parameters
                TITAN_VAULT_KDF_PBKDF2 => VaultManager.KeyDerivationParameters.Pbkdf2(kdfIterations > 0 ? kdfIterations : 64000),
                _ => VaultManager.KeyDerivationParameters.Pbkdf2(kdfIterations > 0 ? kdfIterations : 64000) // Default to PBKDF2
            };
        }

        #endregion

        #region Missing Methods for Compatibility

        // Add missing methods that other wrapper classes expect

        public static unsafe int AddUser(byte* vaultPathBytes, int vaultPathLength, byte* adminPasswordBytes, int adminPasswordLength, byte* newUserIdBytes, int newUserIdLength, byte* newUserPasswordBytes, int newUserPasswordLength)
        {
            SetLastError("AddUser not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        public static unsafe int RemoveUser(byte* vaultPathBytes, int vaultPathLength, byte* adminPasswordBytes, int adminPasswordLength, byte* userIdToRemoveBytes, int userIdToRemoveLength)
        {
            SetLastError("RemoveUser not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        // Stream operations with compatible signatures
        public static unsafe IntPtr OpenStream(IntPtr vaultHandle, byte* filePathBytes, int filePathLength, int accessMode)
        {
            try
            {
                if (vaultHandle == IntPtr.Zero || filePathBytes == null || filePathLength <= 0)
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

                var filePath = GetUtf8String((IntPtr)filePathBytes, filePathLength);
                if (string.IsNullOrEmpty(filePath))
                {
                    SetLastError("Failed to decode file path");
                    return IntPtr.Zero;
                }

                // Use VaultManager's streaming capabilities
                Stream stream;
                if (accessMode == (int)FileAccess.Read)
                {
                    stream = vault.OpenReadAsync(filePath).Result;
                }
                else if (accessMode == (int)FileAccess.Write)
                {
                    stream = vault.OpenWriteAsync(filePath).Result;
                }
                else
                {
                    SetLastError($"Unsupported access mode: {accessMode}");
                    return IntPtr.Zero;
                }

                return CreateStreamHandle(stream);
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                SetLastError($"Failed to open stream: {ex.InnerException.Message}");
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to open stream: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        public static unsafe int StreamRead(IntPtr streamHandle, byte* buffer, int count)
        {
            try
            {
                if (streamHandle == IntPtr.Zero || buffer == null || count <= 0)
                {
                    SetLastError("Invalid parameters");
                    return -1;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                // Create managed buffer and read from stream
                var managedBuffer = new byte[count];
                int bytesRead = stream.Read(managedBuffer, 0, count);

                // Copy to unmanaged buffer
                if (bytesRead > 0)
                {
                    Marshal.Copy(managedBuffer, 0, (IntPtr)buffer, bytesRead);
                }

                return bytesRead;
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to read from stream: {ex.Message}");
                return -1;
            }
        }

        public static unsafe int StreamWrite(IntPtr streamHandle, byte* buffer, int count)
        {
            try
            {
                if (streamHandle == IntPtr.Zero || buffer == null || count <= 0)
                {
                    SetLastError("Invalid parameters");
                    return -1;
                }

                var stream = GetStream(streamHandle);
                if (stream == null)
                {
                    SetLastError("Invalid stream handle");
                    return -1;
                }

                // Copy from unmanaged buffer to managed buffer
                var managedBuffer = new byte[count];
                Marshal.Copy((IntPtr)buffer, managedBuffer, 0, count);

                // Write to stream
                stream.Write(managedBuffer, 0, count);
                
                return count; // Return number of bytes written
            }
            catch (Exception ex)
            {
                SetLastError($"Failed to write to stream: {ex.Message}");
                return -1;
            }
        }

        // Directory listing with compatible signature
        public static unsafe int ListDirectory(IntPtr vaultHandle, byte* directoryPathBytes, int directoryPathLength, byte* buffer, int* bufferSize, int* entryCount)
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

                // Get directory entries
                var entries = vault.ListDirectoryAsync(directoryPath).Result.ToList();
                
                // Handle empty directories immediately
                if (entries.Count == 0)
                {
                    if (bufferSize != null)
                        *bufferSize = 0;
                    if (entryCount != null)
                        *entryCount = 0;
                    return TITAN_VAULT_SUCCESS; // ✅ Empty directory = SUCCESS
                }
                
                // Calculate required buffer size for non-empty directories
                int requiredSize = 0;
                foreach (var entry in entries)
                {
                    var entryNameBytes = Encoding.UTF8.GetBytes(entry.Filename);
                    requiredSize += 4 + entryNameBytes.Length; // 4 bytes for length + string bytes
                }

                // If buffer is null, return required size
                if (buffer == null || bufferSize == null)
                {
                    if (bufferSize != null)
                        *bufferSize = requiredSize;
                    if (entryCount != null)
                        *entryCount = entries.Count;
                    return TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER;
                }

                // Check if buffer is large enough  
                int providedBufferSize = *bufferSize;
                if (providedBufferSize < requiredSize)
                {
                    SetLastError($"Buffer too small. Required: {requiredSize}, Available: {providedBufferSize}");
                    *bufferSize = requiredSize;
                    *entryCount = entries.Count;
                    return TITAN_VAULT_ERROR_INSUFFICIENT_BUFFER;
                }
                
                // Don't modify the original buffer size - we have enough space

                // Fill buffer with entry names
                int offset = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entryNameBytes = Encoding.UTF8.GetBytes(entries[i].Filename);
                    
                    // Write length
                    *((int*)(buffer + offset)) = entryNameBytes.Length;
                    offset += 4;
                    
                    // Write string bytes
                    Marshal.Copy(entryNameBytes, 0, (IntPtr)(buffer + offset), entryNameBytes.Length);
                    offset += entryNameBytes.Length;
                }

                *entryCount = entries.Count;
                return TITAN_VAULT_SUCCESS;
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

        public static unsafe int MoveFile(IntPtr vaultHandle, byte* sourcePathBytes, int sourcePathLength, byte* destinationPathBytes, int destinationPathLength)
        {
            SetLastError("MoveFile not implemented in managed wrapper");
            return TITAN_VAULT_ERROR_UNSUPPORTED_FORMAT;
        }

        #endregion
    }
} 