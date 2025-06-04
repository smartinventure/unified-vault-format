using System;
using System.Security.Cryptography;
using System.Text;
using UvfLib.Api;
using UvfLib.Common;
using System.Diagnostics;

namespace UvfLib.V3
{
    /// <summary>
    /// Implementation of the FileNameCryptor interface for v3 format.
    /// </summary>
    public sealed class FileNameCryptorImpl : FileNameCryptor
    {
        private static readonly byte[] SIV_KDF_CONTEXT = Encoding.ASCII.GetBytes("siv");
        private static readonly byte[] HMAC_KDF_CONTEXT = Encoding.ASCII.GetBytes("hmac");
        private static readonly byte[] DIR_HASH_KDF_CONTEXT = Encoding.ASCII.GetBytes("dirHash");

        private readonly RevolvingMasterkey _masterkey;
        private readonly RandomNumberGenerator _random;
        private readonly int _seedId;
        private readonly DestroyableSecretKey _sivKey;
        private readonly DestroyableSecretKey _hmacKey;

        /// <summary>
        /// Creates a new file name cryptor.
        /// </summary>
        /// <param name="masterkey">The revolving masterkey</param>
        /// <param name="random">The random number generator</param>
        /// <param name="seedId">The seed ID to use</param>
        public FileNameCryptorImpl(RevolvingMasterkey masterkey, RandomNumberGenerator random, int seedId)
        {
            _masterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _seedId = seedId;

            // Derive keys from masterkey
            _sivKey = masterkey.SubKey(seedId, 64, SIV_KDF_CONTEXT, "AES");
            _hmacKey = masterkey.SubKey(seedId, 64, HMAC_KDF_CONTEXT, "HMAC");
        }

        /// <summary>
        /// Creates a new file name cryptor using the current seed ID.
        /// </summary>
        /// <param name="masterkey">The revolving masterkey</param>
        /// <param name="random">The random number generator</param>
        public FileNameCryptorImpl(RevolvingMasterkey masterkey, RandomNumberGenerator random)
            : this(masterkey, random, masterkey.GetCurrentRevision())
        {
        }

        /// <summary>
        /// Encrypts a directory ID.
        /// </summary>
        /// <param name="cleartextDirectoryId">The cleartext directory ID</param>
        /// <returns>The encrypted directory ID</returns>
        public string EncryptDirectoryId(string cleartextDirectoryId)
        {
            if (string.IsNullOrEmpty(cleartextDirectoryId))
            {
                throw new ArgumentException("Directory ID must not be empty", nameof(cleartextDirectoryId));
            }

            return EncryptFilename(cleartextDirectoryId);
        }

        /// <summary>
        /// Decrypts a directory ID.
        /// </summary>
        /// <param name="ciphertextDirectoryId">The encrypted directory ID</param>
        /// <returns>The cleartext directory ID</returns>
        public string DecryptDirectoryId(string ciphertextDirectoryId)
        {
            if (string.IsNullOrEmpty(ciphertextDirectoryId))
            {
                throw new ArgumentException("Directory ID must not be empty", nameof(ciphertextDirectoryId));
            }

            return DecryptFilename(ciphertextDirectoryId);
        }

        /// <summary>
        /// Hashes a directory ID for use in path construction.
        /// </summary>
        /// <param name="dirId">The directory ID to hash</param>
        /// <returns>The hashed directory ID as a Base32 string</returns>
        public string HashDirectoryId(byte[] dirId)
        {
            if (dirId == null)
                throw new ArgumentNullException(nameof(dirId));
            if (dirId.Length == 0)
                throw new ArgumentException("Directory ID must not be empty", nameof(dirId));

            byte[] hmacKeyBytes = _hmacKey.GetEncoded();
           
            // Use HMAC-SHA256 for hashing with the HMAC key derived from the master key
            using var hmac = new HMACSHA256(hmacKeyBytes);
            byte[] hash = hmac.ComputeHash(dirId);

            // Only use the first 20 bytes (160 bits) for compatibility with Java implementation
            byte[] truncatedHash = new byte[20];
            Array.Copy(hash, truncatedHash, 20);

            // Convert to uppercase Base32 string
            string base32Hash = Base32Encoding.ToString(truncatedHash);
            string finalResult = base32Hash.ToUpperInvariant();
            return finalResult;
        }

        /// <summary>
        /// Encrypts a file name.
        /// </summary>
        /// <param name="cleartextName">The cleartext file name</param>
        /// <returns>The encrypted file name</returns>
        public string EncryptFilename(string cleartextName)
        {
            if (cleartextName == null)
            {
                throw new ArgumentNullException(nameof(cleartextName));
            }
            if (cleartextName == string.Empty)
            {
                throw new ArgumentException("File name must not be empty", nameof(cleartextName));
            }

            byte[] cleartextBytes = Encoding.UTF8.GetBytes(cleartextName);
            byte[] associatedData = Array.Empty<byte>();

            // For AES-SIV encryption
            byte[] encryptedBytes = AesSivHelper.Encrypt(_sivKey.GetEncoded(), cleartextBytes, associatedData);

            // Use the existing Base64Url.Encode method
            return UvfLib.Common.Base64Url.Encode(encryptedBytes);
        }

        /// <summary>
        /// Encrypts a file name for a specific directory.
        /// </summary>
        /// <param name="cleartextName">The cleartext file name</param>
        /// <param name="dirId">The directory ID</param>
        /// <returns>The encrypted file name</returns>
        public string EncryptFilename(string cleartextName, byte[] dirId)
        {
            if (string.IsNullOrEmpty(cleartextName))
            {
                throw new ArgumentException("File name must not be empty", nameof(cleartextName));
            }
            if (dirId == null)
            {
                throw new ArgumentNullException(nameof(dirId));
            }

            byte[] cleartextBytes = Encoding.UTF8.GetBytes(cleartextName);

            // Use dirId as associated authenticated data
            byte[] encryptedBytes = AesSivHelper.Encrypt(_sivKey.GetEncoded(), cleartextBytes, dirId);

            // Use the existing Base64Url.Encode method and append .uvf extension
            string base64UrlEncoded = UvfLib.Common.Base64Url.Encode(encryptedBytes);
            string finalCiphertext = base64UrlEncoded + Constants.UVF_FILE_EXT;
            return finalCiphertext;
        }

        /// <summary>
        /// Decrypts a file name from a specific directory.
        /// </summary>
        /// <param name="ciphertextName">The encrypted file name</param>
        /// <param name="dirId">The directory ID</param>
        /// <returns>The cleartext file name</returns>
        public string DecryptFilename(string ciphertextName, byte[] dirId)
        {
            if (string.IsNullOrEmpty(ciphertextName))
            {
                throw new ArgumentException("File name must not be empty", nameof(ciphertextName));
            }
            if (dirId == null)
            {
                throw new ArgumentNullException(nameof(dirId));
            }

            // Remove the .uvf extension
            if (!ciphertextName.EndsWith(Constants.UVF_FILE_EXT))
            {
                throw new ArgumentException($"Not a {Constants.UVF_FILE_EXT} file: {ciphertextName}", nameof(ciphertextName));
            }

            string ciphertextWithoutExt = ciphertextName.Substring(0, ciphertextName.Length - Constants.UVF_FILE_EXT.Length);

            try
            {
                // Use the existing Base64Url.Decode method
                byte[] encryptedBytes = UvfLib.Common.Base64Url.Decode(ciphertextWithoutExt);

                // Decrypt using AES-SIV with dirId as associated data
                byte[] decryptedBytes = AesSivHelper.Decrypt(_sivKey.GetEncoded(), encryptedBytes, dirId);
                string plaintext = Encoding.UTF8.GetString(decryptedBytes);
                return plaintext;
            }
            catch (FormatException ex)
            {
                throw new InvalidCiphertextException("Invalid Base64 encoding", ex);
            }
            catch (CryptographicException ex)
            {
                throw new AuthenticationFailedException("Failed to authenticate file name", ex);
            }
        }

        /// <summary>
        /// Decrypts a file name.
        /// </summary>
        /// <param name="ciphertextName">The encrypted file name</param>
        /// <returns>The cleartext file name</returns>
        public string DecryptFilename(string ciphertextName)
        {
            if (ciphertextName == null)
            {
                throw new ArgumentNullException(nameof(ciphertextName));
            }
            if (ciphertextName == string.Empty)
            {
                throw new AuthenticationFailedException("Ciphertext must not be empty", new ArgumentException("Input string was empty", nameof(ciphertextName)));
            }

            try
            {
                // Use the existing Base64Url.Decode method
                byte[] encryptedBytes = UvfLib.Common.Base64Url.Decode(ciphertextName);

                // Decrypt using AES-SIV with no associated data
                byte[] decryptedBytes = AesSivHelper.Decrypt(_sivKey.GetEncoded(), encryptedBytes, Array.Empty<byte>());
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (FormatException ex)
            {
                throw new AuthenticationFailedException("Invalid Base64 encoding", ex);
            }
            catch (ArgumentException ex) when (ex.ParamName == "ciphertext" || ex.Message.Contains("ciphertext", StringComparison.OrdinalIgnoreCase))
            {
                throw new AuthenticationFailedException("Invalid ciphertext format", ex);
            }
            catch (CryptographicException ex)
            {
                throw new AuthenticationFailedException("Failed to authenticate file name", ex);
            }
        }

        /// <summary>
        /// Encrypts a file name with a specific prefix.
        /// </summary>
        /// <param name="cleartextName">The cleartext file name</param>
        /// <param name="prefix">Custom prefix for the resulting encrypted file name</param>
        /// <returns>The encrypted file name</returns>
        public string EncryptFilename(string cleartextName, string prefix)
        {
            if (string.IsNullOrEmpty(cleartextName))
            {
                throw new ArgumentException("File name must not be empty", nameof(cleartextName));
            }
            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            // For UVF format, we don't use prefix in the same way as in older versions
            // Just concatenate it with the base encryption
            return prefix + EncryptFilename(cleartextName);
        }
    }

    /// <summary>
    /// Base32 Encoding Implementation (RFC 4648)
    /// </summary>
    internal static class Base32Encoding
    {
        private static readonly char[] DIGITS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".ToCharArray();

        /// <summary>
        /// Converts a byte array to a Base32 string
        /// </summary>
        /// <param name="data">The data to encode</param>
        /// <returns>The Base32 encoded string</returns>
        public static string ToString(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            StringBuilder result = new StringBuilder((data.Length * 8 + 4) / 5);

            int buffer = 0;
            int next = 0;
            int bitsLeft = 0;

            foreach (byte b in data)
            {
                buffer <<= 8;
                buffer |= b & 0xFF;
                bitsLeft += 8;

                while (bitsLeft >= 5)
                {
                    bitsLeft -= 5;
                    result.Append(DIGITS[(buffer >> bitsLeft) & 0x1F]);
                }
            }

            if (bitsLeft > 0)
            {
                buffer <<= (5 - bitsLeft);
                result.Append(DIGITS[buffer & 0x1F]);
            }

            return result.ToString();
        }
    }
}