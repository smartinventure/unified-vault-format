using ExampleVaultApp.Wrapper;

namespace ExampleVaultApp
{
    /// <summary>
    /// Compatibility wrapper that exposes the separated TitanVault classes
    /// This maintains backward compatibility while using the new organized structure
    /// </summary>
    public static class TitanVaultWrapper
    {
        /// <summary>
        /// Test if the native library can be loaded and basic functions work
        /// </summary>
        public static bool TestNativeLibrary() => TitanVaultLibraryLoader.TestNativeLibrary();

        /// <summary>
        /// Print detailed information about the loaded library
        /// </summary>
        public static void PrintLibraryInfo() => TitanVaultLibraryLoader.PrintLibraryInfo();

        /// <summary>
        /// Convert a string to UTF-8 byte array for native interop
        /// </summary>
        public static byte[] StringToUtf8Bytes(string str) => TitanVaultUtils.StringToUtf8Bytes(str);

        /// <summary>
        /// Convert UTF-8 byte array back to string
        /// </summary>
        public static string Utf8BytesToString(byte[] bytes) => TitanVaultUtils.Utf8BytesToString(bytes);

        /// <summary>
        /// Get the last error message from the native library
        /// </summary>
        private static string GetLastErrorString() => TitanVaultUtils.GetLastErrorString();

        /// <summary>
        /// High-level C# wrapper for TitanVault operations
        /// </summary>
        public class TitanVault : IDisposable
        {
            private readonly Wrapper.TitanVault _innerVault;

            private TitanVault(Wrapper.TitanVault innerVault)
            {
                _innerVault = innerVault;
            }

            /// <summary>
            /// Create a new UVF vault
            /// </summary>
            public static TitanVault CreateUvfVault(string vaultPath, char[] adminPassword, bool encryptFilenames = true)
            {
                var innerVault = Wrapper.TitanVault.CreateUvfVault(vaultPath, adminPassword, encryptFilenames);
                return new TitanVault(innerVault);
            }

            /// <summary>
            /// Load an existing UVF vault
            /// </summary>
            public static TitanVault LoadUvfVault(string vaultPath, char[] userPassword, string? userId = null)
            {
                var innerVault = Wrapper.TitanVault.LoadUvfVault(vaultPath, userPassword, userId);
                return new TitanVault(innerVault);
            }

            /// <summary>
            /// Create a new Cryptomator vault
            /// </summary>
            public static TitanVault CreateCryptomatorVault(string vaultPath, char[] password)
            {
                var innerVault = Wrapper.TitanVault.CreateCryptomatorVault(vaultPath, password);
                return new TitanVault(innerVault);
            }

            /// <summary>
            /// Load an existing Cryptomator vault
            /// </summary>
            public static TitanVault LoadCryptomatorVault(string vaultPath, char[] password)
            {
                var innerVault = Wrapper.TitanVault.LoadCryptomatorVault(vaultPath, password);
                return new TitanVault(innerVault);
            }

            // Delegate all methods to the inner vault
            public void WriteAllBytes(string filePath, byte[] data) => _innerVault.WriteAllBytes(filePath, data);
            public byte[] ReadAllBytes(string filePath) => _innerVault.ReadAllBytes(filePath);
            public bool FileExists(string filePath) => _innerVault.FileExists(filePath);
            public void CreateDirectory(string directoryPath) => _innerVault.CreateDirectory(directoryPath);
            public bool DirectoryExists(string directoryPath) => _innerVault.DirectoryExists(directoryPath);
            public void DeleteDirectory(string directoryPath) => _innerVault.DeleteDirectory(directoryPath);
            public void DeleteFile(string filePath) => _innerVault.DeleteFile(filePath);
            public Stream OpenReadStream(string filePath) => _innerVault.OpenReadStream(filePath);
            public Stream OpenWriteStream(string filePath) => _innerVault.OpenWriteStream(filePath);
            public string[] ListDirectory(string directoryPath) => _innerVault.ListDirectory(directoryPath);
            internal void EnsureOpen() => _innerVault.EnsureOpen();
            public void Close() => _innerVault.Close();
            public void Dispose() => _innerVault.Dispose();
        }

        /// <summary>
        /// Static operations for TitanVault that don't require an open vault handle
        /// </summary>
        public static class TitanVaultStatic
        {
            /// <summary>
            /// Detect the format of a vault at the specified path
            /// </summary>
            public static VaultFormat DetectVaultFormat(string vaultPath)
            {
                var format = Wrapper.TitanVaultStatic.DetectVaultFormat(vaultPath);
                return format switch
                {
                    TitanVaultUtils.VaultFormat.CryptomatorV8 => VaultFormat.CryptomatorV8,
                    TitanVaultUtils.VaultFormat.UVF => VaultFormat.UVF,
                    _ => VaultFormat.Unknown
                };
            }

            /// <summary>
            /// Add a user to an existing UVF vault
            /// </summary>
            public static void AddUserToVault(string vaultPath, char[] adminPassword, string newUserId, char[] newUserPassword)
                => Wrapper.TitanVaultStatic.AddUserToVault(vaultPath, adminPassword, newUserId, newUserPassword);

            /// <summary>
            /// Remove a user from an existing UVF vault
            /// </summary>
            public static void RemoveUserFromVault(string vaultPath, char[] adminPassword, string userIdToRemove)
                => Wrapper.TitanVaultStatic.RemoveUserFromVault(vaultPath, adminPassword, userIdToRemove);

            /// <summary>
            /// Change the password of a Cryptomator vault
            /// </summary>
            public static void ChangeCryptomatorPassword(string vaultPath, char[] oldPassword, char[] newPassword)
                => Wrapper.TitanVaultStatic.ChangeCryptomatorVaultPassword(vaultPath, oldPassword, newPassword);
        }

        /// <summary>
        /// Vault format enumeration
        /// </summary>
        public enum VaultFormat
        {
            Unknown = 0,
            CryptomatorV8 = 1,
            UVF = 2
        }

        /// <summary>
        /// Ensure the vault is open (static version)
        /// </summary>
        internal static void EnsureOpen(TitanVault vault)
        {
            vault.EnsureOpen();
        }
    }
} 