using System.Runtime.InteropServices;
using System.Text;

namespace ExampleVaultApp.Wrapper
{
    /// <summary>
    /// Utility functions for working with TitanVault
    /// </summary>
    public static class TitanVaultUtils
    {
        /// <summary>
        /// Convert a string to UTF-8 byte array for native interop
        /// </summary>
        public static byte[] StringToUtf8Bytes(string str)
        {
            return string.IsNullOrEmpty(str) ? new byte[0] : Encoding.UTF8.GetBytes(str);
        }

        /// <summary>
        /// Convert UTF-8 byte array back to string
        /// </summary>
        public static string Utf8BytesToString(byte[] bytes)
        {
            return bytes == null || bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Get the last error message from the native library
        /// </summary>
        public static string GetLastErrorString()
        {
            try
            {
                var errorPtr = TitanVaultNativeMethods.GetLastError();
                if (errorPtr == IntPtr.Zero)
                    return "Unknown error";
                
                var error = Marshal.PtrToStringUTF8(errorPtr);
                TitanVaultNativeMethods.FreeString(errorPtr);
                return error ?? "Unknown error";
            }
            catch
            {
                return "Failed to get error message";
            }
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
        /// KDF method constants
        /// </summary>
        public static class KdfMethod
        {
            public const int PBKDF2 = 0;
            public const int Scrypt = 1;
        }

        /// <summary>
        /// Return code constants
        /// </summary>
        public static class ReturnCodes
        {
            public const int Success = 0;
            public const int InvalidParameter = -1;
            public const int VaultNotFound = -2;
            public const int InvalidPassword = -3;
            public const int AccessDenied = -4;
            public const int VaultCorrupted = -5;
            public const int InsufficientBuffer = -6;
            public const int UnsupportedFormat = -7;
            public const int InternalError = -100;
        }
    }
} 