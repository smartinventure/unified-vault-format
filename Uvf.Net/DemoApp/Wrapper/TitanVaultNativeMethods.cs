using System.Runtime.InteropServices;

namespace DemoApp.Wrapper
{
    /// <summary>
    /// P/Invoke declarations for the native TitanVault.dll (AOT compiled)
    /// This contains all the native function signatures for calling the TitanVault library
    /// </summary>
    public static class TitanVaultNativeMethods
    {
        private const string LibraryName = "TitanVault.dll";

        #region Library Information

        /// <summary>
        /// Get TitanVault library version
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_get_version")]
        public static extern IntPtr GetVersion();

        /// <summary>
        /// Get last error message
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_get_last_error")]
        public static extern IntPtr GetLastError();

        /// <summary>
        /// Detect vault format at specified path
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_detect_vault_format")]
        public static extern unsafe int DetectVaultFormat(byte* vaultPathBytes, int vaultPathLength);

        #endregion

        #region Cryptomator Vault Operations

        /// <summary>
        /// Create new Cryptomator V8 vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_create_cryptomator_vault")]
        public static extern unsafe int CreateCryptomatorVault(
            byte* vaultPathBytes, int vaultPathLength,
            byte* passwordBytes, int passwordLength);

        /// <summary>
        /// Load existing Cryptomator V8 vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_load_cryptomator_vault")]
        public static extern unsafe IntPtr LoadCryptomatorVault(
            byte* vaultPathBytes, int vaultPathLength,
            byte* passwordBytes, int passwordLength);

        /// <summary>
        /// Change Cryptomator vault password
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_change_cryptomator_password")]
        public static extern unsafe int ChangeCryptomatorPassword(
            byte* vaultPathBytes, int vaultPathLength,
            byte* oldPasswordBytes, int oldPasswordLength,
            byte* newPasswordBytes, int newPasswordLength);

        #endregion

        #region UVF Vault Operations

        /// <summary>
        /// Create new UVF vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_create_uvf_vault")]
        public static extern unsafe int CreateUvfVault(
            byte* vaultPathBytes, int vaultPathLength,
            byte* adminPasswordBytes, int adminPasswordLength,
            int encryptFilenames, int kdfMethod, int kdfIterations);

        /// <summary>
        /// Load existing UVF vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_load_uvf_vault")]
        public static extern unsafe IntPtr LoadUvfVault(
            byte* vaultPathBytes, int vaultPathLength,
            byte* userPasswordBytes, int userPasswordLength,
            byte* userIdBytes, int userIdLength);

        /// <summary>
        /// Add user to UVF vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_add_user")]
        public static extern unsafe int AddUser(
            byte* vaultPathBytes, int vaultPathLength,
            byte* adminPasswordBytes, int adminPasswordLength,
            byte* newUserIdBytes, int newUserIdLength,
            byte* newUserPasswordBytes, int newUserPasswordLength);

        /// <summary>
        /// Remove user from UVF vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_remove_user")]
        public static extern unsafe int RemoveUser(
            byte* vaultPathBytes, int vaultPathLength,
            byte* adminPasswordBytes, int adminPasswordLength,
            byte* userIdToRemoveBytes, int userIdToRemoveLength);

        /// <summary>
        /// Change UVF admin password
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_change_uvf_admin_password")]
        public static extern unsafe int ChangeUvfAdminPassword(
            byte* vaultPathBytes, int vaultPathLength,
            byte* oldPasswordBytes, int oldPasswordLength,
            byte* newPasswordBytes, int newPasswordLength);

        /// <summary>
        /// Change UVF user password
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_change_uvf_user_password")]
        public static extern unsafe int ChangeUvfUserPassword(
            byte* vaultPathBytes, int vaultPathLength,
            byte* adminPasswordBytes, int adminPasswordLength,
            byte* userIdBytes, int userIdLength,
            byte* newUserPasswordBytes, int newUserPasswordLength);

        /// <summary>
        /// Rotate vault keys (regenerate encryption keys)
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_rotate_keys")]
        public static extern unsafe int RotateVaultKeys(
            byte* vaultPathBytes, int vaultPathLength,
            byte* adminPasswordBytes, int adminPasswordLength,
            int vaultFormat); // 1 = Cryptomator, 2 = UVF

        /// <summary>
        /// Backup vault files to specified directory
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_backup_files")]
        public static extern unsafe int BackupVaultFiles(
            byte* vaultPathBytes, int vaultPathLength,
            byte* backupPathBytes, int backupPathLength,
            int overwriteExisting); // 1 = overwrite, 0 = don't overwrite

        #endregion

        #region File Operations

        /// <summary>
        /// Read file from vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_read_file")]
        public static extern unsafe int ReadFile(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength,
            byte* buffer, int* bufferSize);

        /// <summary>
        /// Write file to vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_write_file")]
        public static extern unsafe int WriteFile(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength,
            byte* buffer, int bufferSize);

        /// <summary>
        /// Check if file exists in vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_file_exists")]
        public static extern unsafe int FileExists(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength);

        /// <summary>
        /// Delete file from vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_delete_file")]
        public static extern unsafe int DeleteFile(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength);

        #endregion

        #region Directory Operations

        /// <summary>
        /// Create directory in vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_create_directory")]
        public static extern unsafe int CreateDirectory(
            IntPtr vaultHandle,
            byte* directoryPathBytes, int directoryPathLength);

        /// <summary>
        /// Check if directory exists in vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_directory_exists")]
        public static extern unsafe int DirectoryExists(
            IntPtr vaultHandle,
            byte* directoryPathBytes, int directoryPathLength);

        /// <summary>
        /// Delete directory from vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_delete_directory")]
        public static extern unsafe int DeleteDirectory(
            IntPtr vaultHandle,
            byte* directoryPathBytes, int directoryPathLength);

        #endregion

        #region Stream Operations

        /// <summary>
        /// Open file for reading as stream
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_open_read_stream")]
        public static extern unsafe IntPtr OpenReadStream(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength);

        /// <summary>
        /// Open file for writing as stream
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_open_write_stream")]
        public static extern unsafe IntPtr OpenWriteStream(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength);

        /// <summary>
        /// Read from stream
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_stream_read")]
        public static extern unsafe int StreamRead(
            IntPtr streamHandle,
            byte* buffer, int count);

        /// <summary>
        /// Write to stream
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_stream_write")]
        public static extern unsafe int StreamWrite(
            IntPtr streamHandle,
            byte* buffer, int count);

        /// <summary>
        /// Close stream and free resources
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_close_stream")]
        public static extern int CloseStream(IntPtr streamHandle);

        #endregion

        #region Vault Handle Management

        /// <summary>
        /// Close vault and free resources
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_close_vault")]
        public static extern int CloseVault(IntPtr vaultHandle);

        #endregion

        #region Memory Management

        /// <summary>
        /// Free string allocated by TitanVault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_free_string")]
        public static extern void FreeString(IntPtr stringPtr);

        /// <summary>
        /// Securely zero memory buffer
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_secure_zero_memory")]
        public static extern unsafe void SecureZeroMemory(byte* buffer, int size);

        #endregion

        #region Advanced File Operations

        /// <summary>
        /// List directory contents in vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_list_directory")]
        public static extern unsafe int ListDirectory(
            IntPtr vaultHandle,
            byte* directoryPathBytes, int directoryPathLength,
            IntPtr entriesBuffer, IntPtr maxEntries);

        /// <summary>
        /// Get file information (size, modified time, etc.)
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_get_file_info")]
        public static extern unsafe int GetFileInfo(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength,
            IntPtr fileSizePtr, IntPtr lastModifiedPtr);

        /// <summary>
        /// Move/rename file or directory in vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_move")]
        public static extern unsafe int MoveEntry(
            IntPtr vaultHandle,
            byte* sourcePathBytes, int sourcePathLength,
            byte* destinationPathBytes, int destinationPathLength);

        /// <summary>
        /// Get list of users in UVF vault
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_get_vault_users")]
        public static extern unsafe int GetVaultUsers(
            byte* vaultPathBytes, int vaultPathLength,
            byte* adminPasswordBytes, int adminPasswordLength,
            IntPtr usersBuffer, IntPtr maxUsers);

        /// <summary>
        /// Read text file from vault as UTF-8 string
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_read_all_text")]
        public static extern unsafe IntPtr ReadAllText(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength);

        /// <summary>
        /// Write text to file in vault as UTF-8
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_write_all_text")]
        public static extern unsafe int WriteAllText(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength,
            byte* textBytes, int textLength);

        /// <summary>
        /// Append text to file in vault as UTF-8
        /// </summary>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "titan_vault_append_all_text")]
        public static extern unsafe int AppendAllText(
            IntPtr vaultHandle,
            byte* filePathBytes, int filePathLength,
            byte* textBytes, int textLength);

        #endregion
    }
} 