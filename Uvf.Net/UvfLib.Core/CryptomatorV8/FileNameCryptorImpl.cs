/*******************************************************************************
 * Copyright (c) 2015, 2016 Sebastian Stenzel and others.
 * This file is licensed under the terms of the MIT license.
 * See the LICENSE.txt file for more info.
 *
 * Contributors:
 *     Sebastian Stenzel - initial API and implementation
 *******************************************************************************/

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

using System;
using System.Security.Cryptography;
using System.Text;
using UvfLib.Core.Api;
using UvfLib.Core.Common;
using UvfLib.Core.V3; // For AesSivHelper

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// File name cryptor implementation for Cryptomator v2.
    /// Uses AES-SIV for authenticated encryption of filenames.
    /// </summary>
    internal class FileNameCryptorImpl : FileNameCryptor
    {
        private const string BASE32_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        private readonly PerpetualMasterkey _masterkey;

        /// <summary>
        /// Initializes a new instance of the FileNameCryptorImpl class.
        /// </summary>
        /// <param name="masterkey">The perpetual masterkey</param>
        internal FileNameCryptorImpl(PerpetualMasterkey masterkey)
        {
            _masterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
        }

        /// <summary>
        /// Hashes a directory ID using AES-SIV and SHA-1.
        /// </summary>
        /// <param name="cleartextDirectoryId">The cleartext directory ID</param>
        /// <returns>The hashed directory ID as a base32 string</returns>
        public string HashDirectoryId(byte[] cleartextDirectoryId)
        {
            if (cleartextDirectoryId == null)
                throw new ArgumentNullException(nameof(cleartextDirectoryId));

            using var encKey = _masterkey.GetEncKey();
            using var macKey = _masterkey.GetMacKey();
            
            // Java version: siv.get().encrypt(ek, mk, cleartextDirectoryId)
            // We need to modify our AES-SIV helper to accept separate keys like Java
            // For now, let's create a version that matches exactly how Java does it
            
            try
            {
                // CORRECT key order discovered: Java AES-SIV expects [macKey][encKey]
                byte[] combinedKey = new byte[64];
                Buffer.BlockCopy(macKey.GetEncoded(), 0, combinedKey, 0, 32);   // First 32 bytes: MAC key  
                Buffer.BlockCopy(encKey.GetEncoded(), 0, combinedKey, 32, 32); // Last 32 bytes: encryption key
                
                // Encrypt the directory ID using AES-SIV (no associated data per official docs)
                byte[] encryptedBytes = AesSivHelper.Encrypt(combinedKey, cleartextDirectoryId, null);
                
                // Hash the encrypted bytes using SHA-1
                using var sha1 = SHA1.Create();
                byte[] hashedBytes = sha1.ComputeHash(encryptedBytes);
                
                // Use Google Guava compatible Base32 encoding (no padding)
                string result = ToBase32GoogleGuava(hashedBytes);
                Console.WriteLine($"DEBUG: v2 algorithm (CORRECT key order) for dirId (length {cleartextDirectoryId.Length}): {result}");
                
                return result;
            }
            finally
            {
                // Keys are disposed automatically via using statements
            }
        }

        /// <summary>
        /// Google Guava compatible Base32 encoding (no padding, specific alphabet)
        /// </summary>
        private static string ToBase32GoogleGuava(byte[] input)
        {
            if (input == null || input.Length == 0)
                return string.Empty;

            // Google Guava uses the standard RFC 4648 Base32 alphabet without padding
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            
            var result = new StringBuilder((input.Length * 8 + 4) / 5);
            int buffer = 0;
            int bitsLeft = 0;

            foreach (byte b in input)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;

                while (bitsLeft >= 5)
                {
                    result.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                    bitsLeft -= 5;
                }
            }

            if (bitsLeft > 0)
            {
                result.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Encrypts a filename using AES-SIV with associated data.
        /// </summary>
        /// <param name="cleartextName">The cleartext filename</param>
        /// <returns>The encrypted filename</returns>
        public string EncryptFilename(string cleartextName)
        {
            throw new NotSupportedException("Use EncryptFilename with directory ID");
        }

        /// <summary>
        /// Encrypts a filename using AES-SIV with associated data.
        /// </summary>
        /// <param name="cleartextName">The cleartext filename</param>
        /// <param name="prefix">Not used in v2, kept for compatibility</param>
        /// <returns>The encrypted filename</returns>
        public string EncryptFilename(string cleartextName, string prefix)
        {
            throw new NotSupportedException("Use EncryptFilename with directory ID");
        }

        /// <summary>
        /// Encrypts a filename using base64url encoding and AES-SIV.
        /// </summary>
        /// <param name="cleartextName">The cleartext filename</param>
        /// <param name="associatedData">The associated data (typically directory ID)</param>
        /// <returns>The encrypted filename as base64url</returns>
        internal string EncryptFilename(string cleartextName, params byte[][] associatedData)
        {
            if (string.IsNullOrEmpty(cleartextName))
                throw new ArgumentException("Filename cannot be null or empty", nameof(cleartextName));

            using var encKey = _masterkey.GetEncKey();
            using var macKey = _masterkey.GetMacKey();
            
            // CORRECT key order: [macKey][encKey] (same as directory hashing)
            byte[] combinedKey = new byte[64];
            Buffer.BlockCopy(macKey.GetEncoded(), 0, combinedKey, 0, 32);   // First 32 bytes: MAC key  
            Buffer.BlockCopy(encKey.GetEncoded(), 0, combinedKey, 32, 32); // Last 32 bytes: encryption key
            
            try
            {
                byte[] cleartextBytes = Encoding.UTF8.GetBytes(cleartextName);
                // Always pass the directory ID as-is (including empty array for root directory)
                byte[] ad = associatedData?.Length > 0 ? associatedData[0] : Array.Empty<byte>();
                byte[] encryptedBytes = AesSivHelper.Encrypt(combinedKey, cleartextBytes, ad);
                
                return ToBase64Url(encryptedBytes);
            }
            finally
            {
                // Clear the combined key
                UvfLib.Core.Common.CryptographicOperations.ZeroMemory(combinedKey);
            }
        }

        /// <summary>
        /// Decrypts a filename.
        /// </summary>
        /// <param name="ciphertextName">The encrypted filename</param>
        /// <returns>The decrypted filename</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails</exception>
        public string DecryptFilename(string ciphertextName)
        {
            throw new NotSupportedException("Use DecryptFilename with directory ID");
        }

        /// <summary>
        /// Decrypts a filename using base64url decoding and AES-SIV.
        /// </summary>
        /// <param name="ciphertextName">The encrypted filename as base64url</param>
        /// <param name="associatedData">The associated data (typically directory ID)</param>
        /// <returns>The decrypted filename</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails</exception>
        internal string DecryptFilename(string ciphertextName, params byte[][] associatedData)
        {
            if (string.IsNullOrEmpty(ciphertextName))
                throw new ArgumentException("Ciphertext name cannot be null or empty", nameof(ciphertextName));

            using var encKey = _masterkey.GetEncKey();
            using var macKey = _masterkey.GetMacKey();
            
            // CORRECT key order: [macKey][encKey] (same as directory hashing)
            byte[] combinedKey = new byte[64];
            Buffer.BlockCopy(macKey.GetEncoded(), 0, combinedKey, 0, 32);   // First 32 bytes: MAC key  
            Buffer.BlockCopy(encKey.GetEncoded(), 0, combinedKey, 32, 32); // Last 32 bytes: encryption key
            
            try
            {
                byte[] encryptedBytes = FromBase64Url(ciphertextName);
                // Always pass the directory ID as-is (including empty array for root directory)
                byte[] ad = associatedData?.Length > 0 ? associatedData[0] : Array.Empty<byte>();
                byte[] cleartextBytes = AesSivHelper.Decrypt(combinedKey, encryptedBytes, ad);
                
                return Encoding.UTF8.GetString(cleartextBytes);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is CryptographicException)
            {
                throw new AuthenticationFailedException("Invalid ciphertext filename", ex);
            }
            finally
            {
                // Clear the combined key
                UvfLib.Core.Common.CryptographicOperations.ZeroMemory(combinedKey);
            }
        }

        /// <summary>
        /// Converts bytes to base32 encoding.
        /// </summary>
        /// <param name="input">The input bytes</param>
        /// <returns>The base32 encoded string</returns>
        private static string ToBase32(byte[] input)
        {
            if (input == null || input.Length == 0)
                return string.Empty;

            var result = new StringBuilder((input.Length * 8 + 4) / 5);
            int buffer = 0;
            int bitsLeft = 0;

            foreach (byte b in input)
            {
                buffer = (buffer << 8) | b;
                bitsLeft += 8;

                while (bitsLeft >= 5)
                {
                    result.Append(BASE32_ALPHABET[(buffer >> (bitsLeft - 5)) & 0x1F]);
                    bitsLeft -= 5;
                }
            }

            if (bitsLeft > 0)
            {
                result.Append(BASE32_ALPHABET[(buffer << (5 - bitsLeft)) & 0x1F]);
            }

            return result.ToString();
        }

        /// <summary>
        /// Converts bytes to base64url encoding.
        /// </summary>
        /// <param name="input">The input bytes</param>
        /// <returns>The base64url encoded string</returns>
        private static string ToBase64Url(byte[] input)
        {
            // FIXED: Match real Cryptomator's actual behavior (not their documentation).
            // Real Cryptomator uses standard Base64 with URL-safe characters but KEEPS the padding.
            // This explains the mixed padding pattern observed in real vaults.
            return Convert.ToBase64String(input)
                .Replace('+', '-')
                .Replace('/', '_');
            // NO .TrimEnd('=') - keep natural Base64 padding for Cryptomator compatibility
        }

        /// <summary>
        /// Converts base64url string to bytes.
        /// </summary>
        /// <param name="input">The base64url encoded string</param>
        /// <returns>The decoded bytes</returns>
        private static byte[] FromBase64Url(string input)
        {
            // Reverse URL-safe character encoding (padding should already be present)
            string base64 = input.Replace('-', '+').Replace('_', '/');
            
            // Real Cryptomator includes natural padding, so we don't need to add it back
            // But add safety check in case some edge cases need padding
            int padding = base64.Length % 4;
            switch (padding)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }
            
            return Convert.FromBase64String(base64);
        }
    }
} 
