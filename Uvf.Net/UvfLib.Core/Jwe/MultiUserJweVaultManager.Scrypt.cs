using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Jose;
using UvfLib.Core.Api;
using UvfLib.Core.Common;
using Org.BouncyCastle.Crypto.Generators;

namespace UvfLib.Core.Jwe
{
    public static partial class MultiUserJweVaultManager
    {
        #region Scrypt Implementation

        /// <summary>
        /// Creates a multi-user JWE vault using Scrypt key derivation.
        /// Uses BouncyCastle Scrypt implementation for consistency with Cryptomator.
        /// </summary>
        private static string CreateMultiUserVaultWithScrypt(UvfMasterkeyPayload payload, 
            Dictionary<string, char[]> userCredentials, 
            char[] adminPassword, 
            KeyDerivationParameters kdfParams)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (userCredentials == null) throw new ArgumentNullException(nameof(userCredentials));
            if (adminPassword == null) throw new ArgumentNullException(nameof(adminPassword));
            if (kdfParams?.Method != KeyDerivationMethod.Scrypt) throw new ArgumentException("KDF parameters must be for Scrypt method");

            string payloadJson = JsonSerializer.Serialize(payload, UvfJsonContext.Default.UvfMasterkeyPayload);

            // Generate a random CEK (Content Encryption Key)
            byte[] cek = new byte[32]; // 256-bit key for A256GCM
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(cek);
            }

            try
            {
                // Create recipients list
                var recipients = new List<object>();

                // Add admin recipient (always first)
                recipients.Add(CreateScryptRecipient(AdminKeyId, adminPassword, cek, kdfParams));

                // Add user recipients
                foreach (var (userId, userPassword) in userCredentials)
                {
                    string keyId = UserKeyIdPrefix + userId;
                    recipients.Add(CreateScryptRecipient(keyId, userPassword, cek, kdfParams));
                }

                // Encrypt payload with CEK
                byte[] iv = new byte[12]; // 96-bit IV for GCM
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(iv);
                }

                byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
                byte[] ciphertext;
                byte[] tag;

                using (var aesGcm = new System.Security.Cryptography.AesGcm(cek))
                {
                    ciphertext = new byte[payloadBytes.Length];
                    tag = new byte[16]; // 128-bit authentication tag
                    aesGcm.Encrypt(iv, payloadBytes, ciphertext, tag);
                }

                // Create JWE JSON structure with Scrypt marker
                var jweJson = new JweJson
                {
                    Protected = CreateScryptProtectedHeader(payload.UvfSpecVersion),
                    Recipients = recipients,
                    Iv = Jose.Base64Url.Encode(iv),
                    Ciphertext = Jose.Base64Url.Encode(ciphertext),
                    Tag = Jose.Base64Url.Encode(tag)
                };

                return JsonSerializer.Serialize(jweJson, UvfJsonContext.Default.JweJson);
            }
            finally
            {
                // Clear CEK from memory
                if (cek != null)
                {
                    Array.Clear(cek, 0, cek.Length);
                }
            }
        }

        /// <summary>
        /// Creates a Scrypt-based recipient for multi-user vaults.
        /// </summary>
        private static JweScryptRecipient CreateScryptRecipient(string keyId, char[] password, byte[] cek, KeyDerivationParameters kdfParams)
        {
            // Generate salt for this recipient
            byte[] salt = new byte[12]; // 96-bit salt
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Derive KEK using BouncyCastle Scrypt
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] kek;
            try
            {
                kek = SCrypt.Generate(
                    passwordBytes,
                    salt,
                    kdfParams.ScryptN,
                    kdfParams.ScryptR,
                    kdfParams.ScryptP,
                    32 // 256-bit KEK for AES Key Wrap
                );
            }
            finally
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }

            try
            {
                // Encrypt CEK with KEK using AES Key Wrap
                byte[] encryptedKey = AesKeyWrap.WrapKey(kek, cek);

                return new JweScryptRecipient
                {
                    Header = new JweScryptRecipientHeader
                    {
                        Algorithm = "uvf.scrypt+A256KW", // Custom algorithm identifier
                        KeyId = keyId,
                        ScryptN = kdfParams.ScryptN,
                        ScryptR = kdfParams.ScryptR,
                        ScryptP = kdfParams.ScryptP,
                        ScryptSalt = Jose.Base64Url.Encode(salt)
                    },
                    EncryptedKey = Jose.Base64Url.Encode(encryptedKey)
                };
            }
            finally
            {
                Array.Clear(kek, 0, kek.Length);
            }
        }

        /// <summary>
        /// Creates protected header for Scrypt-based multi-user vaults.
        /// </summary>
        private static string CreateScryptProtectedHeader(int uvfSpecVersion)
        {
            var header = new Dictionary<string, object>
            {
                ["enc"] = "A256GCM",
                ["cty"] = "json",
                ["crit"] = new[] { "uvf.spec.version", "uvf.kdf.method" },
                ["uvf.spec.version"] = uvfSpecVersion,
                ["uvf.kdf.method"] = "scrypt"
            };
            
            string headerJson = JsonSerializer.Serialize(header, UvfJsonContext.Default.DictionaryStringObject);
            return Jose.Base64Url.Encode(Encoding.UTF8.GetBytes(headerJson));
        }

        #endregion
    }
}
