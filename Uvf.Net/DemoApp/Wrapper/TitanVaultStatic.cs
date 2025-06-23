using System.Text;
using System.Runtime.InteropServices;

namespace DemoApp.Wrapper
{
    /// <summary>
    /// Static operations for TitanVault that don't require an open vault handle
    /// </summary>
    public static class TitanVaultStatic
    {
        /// <summary>
        /// Detect the format of a vault at the specified path
        /// </summary>
        public static TitanVaultUtils.VaultFormat DetectVaultFormat(string vaultPath)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));

            unsafe
            {
                var pathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                fixed (byte* pathPtr = pathBytes)
                {
                    var result = TitanVaultNativeMethods.DetectVaultFormat(pathPtr, pathBytes.Length);
                    return result switch
                    {
                        1 => TitanVaultUtils.VaultFormat.CryptomatorV8,
                        2 => TitanVaultUtils.VaultFormat.UVF,
                        _ => TitanVaultUtils.VaultFormat.Unknown
                    };
                }
            }
        }

        /// <summary>
        /// Add a user to an existing UVF vault
        /// </summary>
        public static void AddUserToVault(string vaultPath, char[] adminPassword, string newUserId, char[] newUserPassword)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (adminPassword == null)
                throw new ArgumentNullException(nameof(adminPassword));
            if (string.IsNullOrEmpty(newUserId))
                throw new ArgumentNullException(nameof(newUserId));
            if (newUserPassword == null)
                throw new ArgumentNullException(nameof(newUserPassword));

            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var adminPasswordBytes = Encoding.UTF8.GetBytes(adminPassword);
                var newUserIdBytes = TitanVaultUtils.StringToUtf8Bytes(newUserId);
                var newUserPasswordBytes = Encoding.UTF8.GetBytes(newUserPassword);

                try
                {
                    fixed (byte* vaultPathPtr = vaultPathBytes)
                    fixed (byte* adminPasswordPtr = adminPasswordBytes)
                    fixed (byte* newUserIdPtr = newUserIdBytes)
                    fixed (byte* newUserPasswordPtr = newUserPasswordBytes)
                    {
                        var result = TitanVaultNativeMethods.AddUser(
                            vaultPathPtr, vaultPathBytes.Length,
                            adminPasswordPtr, adminPasswordBytes.Length,
                            newUserIdPtr, newUserIdBytes.Length,
                            newUserPasswordPtr, newUserPasswordBytes.Length);

                        if (result != TitanVaultUtils.ReturnCodes.Success)
                        {
                            throw new InvalidOperationException($"Failed to add user: {TitanVaultUtils.GetLastErrorString()}");
                        }
                    }
                }
                finally
                {
                    // Clear sensitive data
                    Array.Clear(adminPasswordBytes, 0, adminPasswordBytes.Length);
                    Array.Clear(newUserPasswordBytes, 0, newUserPasswordBytes.Length);
                }
            }
        }

        /// <summary>
        /// Remove a user from an existing UVF vault
        /// </summary>
        public static void RemoveUserFromVault(string vaultPath, char[] adminPassword, string userIdToRemove)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (adminPassword == null)
                throw new ArgumentNullException(nameof(adminPassword));
            if (string.IsNullOrEmpty(userIdToRemove))
                throw new ArgumentNullException(nameof(userIdToRemove));

            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var adminPasswordBytes = Encoding.UTF8.GetBytes(adminPassword);
                var userIdToRemoveBytes = TitanVaultUtils.StringToUtf8Bytes(userIdToRemove);

                try
                {
                    fixed (byte* vaultPathPtr = vaultPathBytes)
                    fixed (byte* adminPasswordPtr = adminPasswordBytes)
                    fixed (byte* userIdToRemovePtr = userIdToRemoveBytes)
                    {
                        var result = TitanVaultNativeMethods.RemoveUser(
                            vaultPathPtr, vaultPathBytes.Length,
                            adminPasswordPtr, adminPasswordBytes.Length,
                            userIdToRemovePtr, userIdToRemoveBytes.Length);

                        if (result != TitanVaultUtils.ReturnCodes.Success)
                        {
                            throw new InvalidOperationException($"Failed to remove user: {TitanVaultUtils.GetLastErrorString()}");
                        }
                    }
                }
                finally
                {
                    // Clear sensitive data
                    Array.Clear(adminPasswordBytes, 0, adminPasswordBytes.Length);
                }
            }
        }

        /// <summary>
        /// Change the password of a Cryptomator vault
        /// </summary>
        public static void ChangeCryptomatorPassword(string vaultPath, char[] oldPassword, char[] newPassword)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (oldPassword == null)
                throw new ArgumentNullException(nameof(oldPassword));
            if (newPassword == null)
                throw new ArgumentNullException(nameof(newPassword));

            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var oldPasswordBytes = Encoding.UTF8.GetBytes(oldPassword);
                var newPasswordBytes = Encoding.UTF8.GetBytes(newPassword);

                try
                {
                    fixed (byte* vaultPathPtr = vaultPathBytes)
                    fixed (byte* oldPasswordPtr = oldPasswordBytes)
                    fixed (byte* newPasswordPtr = newPasswordBytes)
                    {
                        var result = TitanVaultNativeMethods.ChangeCryptomatorPassword(
                            vaultPathPtr, vaultPathBytes.Length,
                            oldPasswordPtr, oldPasswordBytes.Length,
                            newPasswordPtr, newPasswordBytes.Length);

                        if (result != TitanVaultUtils.ReturnCodes.Success)
                        {
                            throw new InvalidOperationException($"Failed to change password: {TitanVaultUtils.GetLastErrorString()}");
                        }
                    }
                }
                finally
                {
                    // Clear sensitive data
                    Array.Clear(oldPasswordBytes, 0, oldPasswordBytes.Length);
                    Array.Clear(newPasswordBytes, 0, newPasswordBytes.Length);
                }
            }
        }

        /// <summary>
        /// Get list of users in UVF vault
        /// </summary>
        public static string[] GetVaultUsers(string vaultPath, char[] adminPassword)
        {
            if (string.IsNullOrEmpty(vaultPath))
                throw new ArgumentNullException(nameof(vaultPath));
            if (adminPassword == null)
                throw new ArgumentNullException(nameof(adminPassword));

            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var adminPasswordBytes = Encoding.UTF8.GetBytes(adminPassword);

                try
                {
                    // Allocate buffer for user pointers (max 50 users)
                    const int maxUsers = 50;
                    IntPtr[] userPtrs = new IntPtr[maxUsers];
                    int maxUsersCount = maxUsers;

                    fixed (byte* vaultPathPtr = vaultPathBytes)
                    fixed (byte* adminPasswordPtr = adminPasswordBytes)
                    fixed (IntPtr* userBuffer = userPtrs)
                    {
                        var result = TitanVaultNativeMethods.GetVaultUsers(
                            vaultPathPtr, vaultPathBytes.Length,
                            adminPasswordPtr, adminPasswordBytes.Length,
                            (IntPtr)userBuffer, (IntPtr)(&maxUsersCount));

                        if (result < 0)
                        {
                            throw new InvalidOperationException($"Failed to get vault users: {TitanVaultUtils.GetLastErrorString()}");
                        }

                        // Convert the returned user strings
                        var users = new string[result];
                        for (int i = 0; i < result; i++)
                        {
                            if (userPtrs[i] != IntPtr.Zero)
                            {
                                users[i] = Marshal.PtrToStringUTF8(userPtrs[i]) ?? "";
                                TitanVaultNativeMethods.FreeString(userPtrs[i]);
                            }
                        }

                        return users;
                    }
                }
                finally
                {
                    // Clear sensitive data
                    Array.Clear(adminPasswordBytes, 0, adminPasswordBytes.Length);
                }
            }
        }
    }
} 