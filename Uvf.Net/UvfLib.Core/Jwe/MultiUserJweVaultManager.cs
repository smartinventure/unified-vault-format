using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jose;
using UvfLib.Core.Api;
using Org.BouncyCastle.Crypto.Generators;

namespace UvfLib.Core.Jwe
{
    /// <summary>
    /// Manages multi-user JWE-formatted vault files (vault.uvf) with secure password handling.
    /// Uses char[] for passwords internally for better memory security.
    /// </summary>
    public static class MultiUserJweVaultManager
    {
        private const JweAlgorithm KeyManagementAlgorithm = JweAlgorithm.PBES2_HS512_A256KW;
        private const JweEncryption ContentEncryptionAlgorithm = JweEncryption.A256GCM;
        private const int DefaultPbkdf2Iterations = 64000;
        private const string AdminKeyId = "uvflib.net.admin";
        private const string UserKeyIdPrefix = "uvflib.net.user.";

        /// <summary>
        /// Creates a multi-user JWE vault with admin and user access.
        /// </summary>
        /// <param name="payload">The UVF masterkey payload to encrypt</param>
        /// <param name="userCredentials">Dictionary of userId -> password (char[])</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="pbkdf2Iterations">PBKDF2 iteration count</param>
        /// <returns>JWE JSON serialization string</returns>
        public static string CreateMultiUserVault(UvfMasterkeyPayload payload, 
            Dictionary<string, char[]> userCredentials, 
            char[] adminPassword, 
            int? pbkdf2Iterations = null)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (userCredentials == null) throw new ArgumentNullException(nameof(userCredentials));
            if (adminPassword == null) throw new ArgumentNullException(nameof(adminPassword));

            int iterationsToUse = pbkdf2Iterations ?? DefaultPbkdf2Iterations;
            string payloadJson = JsonSerializer.Serialize(payload);

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
                recipients.Add(CreateRecipient(AdminKeyId, adminPassword, cek, iterationsToUse));

                // Add user recipients
                foreach (var (userId, userPassword) in userCredentials)
                {
                    string keyId = UserKeyIdPrefix + userId;
                    recipients.Add(CreateRecipient(keyId, userPassword, cek, iterationsToUse));
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

                // Create JWE JSON structure
                var jweJson = new
                {
                    @protected = CreateProtectedHeader(),
                    recipients = recipients,
                    iv = Jose.Base64Url.Encode(iv),
                    ciphertext = Jose.Base64Url.Encode(ciphertext),
                    tag = Jose.Base64Url.Encode(tag)
                };

                return JsonSerializer.Serialize(jweJson);
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
        /// Creates a multi-user JWE vault with configurable key derivation method.
        /// </summary>
        /// <param name="payload">The UVF masterkey payload to encrypt</param>
        /// <param name="userCredentials">Dictionary of userId -> password (char[])</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="kdfParams">Key derivation parameters (optional, defaults to PBKDF2)</param>
        /// <returns>JWE JSON serialization string</returns>
        public static string CreateMultiUserVault(UvfMasterkeyPayload payload, 
            Dictionary<string, char[]> userCredentials, 
            char[] adminPassword, 
            KeyDerivationParameters kdfParams = null)
        {
            // Use default PBKDF2 if no parameters provided (backward compatibility)
            kdfParams ??= KeyDerivationParameters.Default();
            kdfParams.Validate();

            return kdfParams.Method switch
            {
                KeyDerivationMethod.PBKDF2_HMAC_SHA512 => CreateMultiUserVault(payload, userCredentials, adminPassword, kdfParams.Pbkdf2Iterations),
                KeyDerivationMethod.Scrypt => CreateMultiUserVaultWithScrypt(payload, userCredentials, adminPassword, kdfParams),
                _ => throw new ArgumentException($"Unsupported key derivation method: {kdfParams.Method}")
            };
        }

        /// <summary>
        /// Loads a multi-user vault by trying to decrypt with the provided password.
        /// </summary>
        /// <param name="jweJsonString">JWE JSON serialization string</param>
        /// <param name="userPassword">User password (char[])</param>
        /// <param name="userId">Optional user ID hint for faster lookup</param>
        /// <returns>Decrypted UVF masterkey payload</returns>
        public static UvfMasterkeyPayload LoadMultiUserVault(string jweJsonString, char[] userPassword, string? userId = null)
        {
            if (string.IsNullOrEmpty(jweJsonString)) throw new ArgumentNullException(nameof(jweJsonString));
            if (userPassword == null) throw new ArgumentNullException(nameof(userPassword));

            using var doc = JsonDocument.Parse(jweJsonString);
            var root = doc.RootElement;

            // Extract common elements
            byte[] iv = Jose.Base64Url.Decode(root.GetProperty("iv").GetString()!);
            byte[] ciphertext = Jose.Base64Url.Decode(root.GetProperty("ciphertext").GetString()!);
            byte[] tag = Jose.Base64Url.Decode(root.GetProperty("tag").GetString()!);

            // Try to decrypt with user password
            var recipients = root.GetProperty("recipients").EnumerateArray();
            
            foreach (var recipient in recipients)
            {
                try
                {
                    // Check if this recipient matches the user hint
                    if (userId != null && recipient.TryGetProperty("header", out var headerElement))
                    {
                        if (headerElement.TryGetProperty("kid", out var kidElement))
                        {
                            string kid = kidElement.GetString()!;
                            string expectedKid = userId == "admin" ? AdminKeyId : UserKeyIdPrefix + userId;
                            if (kid != expectedKid)
                            {
                                continue; // Skip this recipient
                            }
                        }
                    }

                    // Try to decrypt CEK with this recipient
                    byte[]? cek = TryDecryptCek(recipient, userPassword);
                    if (cek != null)
                    {
                        try
                        {
                            // Decrypt payload with CEK
                            byte[] decryptedPayload = new byte[ciphertext.Length];
                            using (var aesGcm = new System.Security.Cryptography.AesGcm(cek))
                            {
                                aesGcm.Decrypt(iv, ciphertext, tag, decryptedPayload);
                            }

                            string payloadJson = Encoding.UTF8.GetString(decryptedPayload);
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            return JsonSerializer.Deserialize<UvfMasterkeyPayload>(payloadJson, options)!;
                        }
                        finally
                        {
                            Array.Clear(cek, 0, cek.Length);
                        }
                    }
                }
                catch
                {
                    // Continue to next recipient
                    continue;
                }
            }

            throw new UnauthorizedAccessException("Unable to decrypt vault with provided password");
        }

        /// <summary>
        /// Adds a new user to an existing multi-user vault.
        /// </summary>
        /// <param name="jweJsonString">Current JWE JSON string</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="newUserId">New user ID</param>
        /// <param name="newUserPassword">New user password (char[])</param>
        /// <returns>Updated JWE JSON string</returns>
        public static string AddUserToVault(string jweJsonString, char[] adminPassword, string newUserId, char[] newUserPassword)
        {
            if (string.IsNullOrEmpty(jweJsonString)) throw new ArgumentNullException(nameof(jweJsonString));
            if (adminPassword == null) throw new ArgumentNullException(nameof(adminPassword));
            if (string.IsNullOrEmpty(newUserId)) throw new ArgumentNullException(nameof(newUserId));
            if (newUserPassword == null) throw new ArgumentNullException(nameof(newUserPassword));

            // First, decrypt the vault to get the CEK (Content Encryption Key)
            byte[] cek = null;
            using var doc = JsonDocument.Parse(jweJsonString);
            var root = doc.RootElement;
            
            var recipients = root.GetProperty("recipients").EnumerateArray();
            foreach (var recipient in recipients)
            {
                cek = TryDecryptCek(recipient, adminPassword);
                if (cek != null) break;
            }
            
            if (cek == null)
            {
                throw new UnauthorizedAccessException("Unable to decrypt vault with admin password");
            }

            try
            {
                // Create new recipient for the new user
                string newUserKeyId = UserKeyIdPrefix + newUserId;
                var newRecipient = CreateRecipient(newUserKeyId, newUserPassword, cek, DefaultPbkdf2Iterations);

                // Add new recipient to existing recipients list
                var existingRecipients = new List<object>();
                foreach (var recipient in recipients)
                {
                    existingRecipients.Add(JsonSerializer.Deserialize<object>(recipient.GetRawText())!);
                }
                existingRecipients.Add(newRecipient);

                // Create updated JWE with new recipient
                var updatedJwe = new
                {
                    @protected = root.GetProperty("protected").GetString(),
                    recipients = existingRecipients,
                    iv = root.GetProperty("iv").GetString(),
                    ciphertext = root.GetProperty("ciphertext").GetString(),
                    tag = root.GetProperty("tag").GetString()
                };

                return JsonSerializer.Serialize(updatedJwe);
            }
            finally
            {
                if (cek != null)
                {
                    Array.Clear(cek, 0, cek.Length);
                }
            }
        }

        /// <summary>
        /// Removes a user from a multi-user vault.
        /// </summary>
        /// <param name="jweJsonString">Current JWE JSON string</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="userIdToRemove">User ID to remove</param>
        /// <returns>Updated JWE JSON string</returns>
        public static string RemoveUserFromVault(string jweJsonString, char[] adminPassword, string userIdToRemove)
        {
            if (string.IsNullOrEmpty(jweJsonString)) throw new ArgumentNullException(nameof(jweJsonString));
            if (adminPassword == null) throw new ArgumentNullException(nameof(adminPassword));
            if (string.IsNullOrEmpty(userIdToRemove)) throw new ArgumentNullException(nameof(userIdToRemove));

            using var doc = JsonDocument.Parse(jweJsonString);
            var root = doc.RootElement;

            // Create new recipients list without the removed user
            var newRecipients = new List<object>();
            var recipients = root.GetProperty("recipients").EnumerateArray();

            foreach (var recipient in recipients)
            {
                if (recipient.TryGetProperty("header", out var headerElement))
                {
                    if (headerElement.TryGetProperty("kid", out var kidElement))
                    {
                        string kid = kidElement.GetString()!;
                        string userKid = UserKeyIdPrefix + userIdToRemove;
                        
                        // Skip the user to be removed
                        if (kid == userKid)
                        {
                            continue;
                        }
                    }
                }
                
                // Keep this recipient
                newRecipients.Add(JsonSerializer.Deserialize<object>(recipient.GetRawText())!);
            }

            // Recreate JWE with updated recipients
            var updatedJwe = new
            {
                @protected = root.GetProperty("protected").GetString(),
                recipients = newRecipients,
                iv = root.GetProperty("iv").GetString(),
                ciphertext = root.GetProperty("ciphertext").GetString(),
                tag = root.GetProperty("tag").GetString()
            };

            return JsonSerializer.Serialize(updatedJwe);
        }

        /// <summary>
        /// Gets list of user IDs from a multi-user vault.
        /// </summary>
        /// <param name="jweJsonString">JWE JSON string</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <returns>List of user IDs</returns>
        public static List<string> GetVaultUsers(string jweJsonString, char[] adminPassword)
        {
            if (string.IsNullOrEmpty(jweJsonString)) throw new ArgumentNullException(nameof(jweJsonString));
            if (adminPassword == null) throw new ArgumentNullException(nameof(adminPassword));

            using var doc = JsonDocument.Parse(jweJsonString);
            var root = doc.RootElement;
            var users = new List<string>();

            var recipients = root.GetProperty("recipients").EnumerateArray();
            foreach (var recipient in recipients)
            {
                if (recipient.TryGetProperty("header", out var headerElement))
                {
                    if (headerElement.TryGetProperty("kid", out var kidElement))
                    {
                        string kid = kidElement.GetString()!;
                        
                        if (kid == AdminKeyId)
                        {
                            users.Add("admin");
                        }
                        else if (kid.StartsWith(UserKeyIdPrefix))
                        {
                            string userId = kid.Substring(UserKeyIdPrefix.Length);
                            users.Add(userId);
                        }
                    }
                }
            }

            return users;
        }

        #region Private Helper Methods

        private static string CreateProtectedHeader()
        {
            var header = new
            {
                enc = "A256GCM",
                cty = "json",
                crit = new[] { "uvf.spec.version" },
                uvf_spec_version = 1
            };
            
            string headerJson = JsonSerializer.Serialize(header);
            return Jose.Base64Url.Encode(Encoding.UTF8.GetBytes(headerJson));
        }

        private static object CreateRecipient(string keyId, char[] password, byte[] cek, int iterations)
        {
            // Generate salt for this recipient
            byte[] salt = new byte[12]; // 96-bit salt
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Derive KEK using PBKDF2
            byte[] kek = new byte[32]; // 256-bit KEK for A256KW
            byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            using (var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, salt, iterations, HashAlgorithmName.SHA512))
            {
                kek = pbkdf2.GetBytes(32);
            }

            try
            {
                // Encrypt CEK with KEK using AES Key Wrap
                byte[] encryptedKey = AesKeyWrap.WrapKey(kek, cek);

                return new
                {
                    header = new
                    {
                        alg = "PBES2-HS512+A256KW",
                        kid = keyId,
                        p2s = Jose.Base64Url.Encode(salt),
                        p2c = iterations
                    },
                    encrypted_key = Jose.Base64Url.Encode(encryptedKey)
                };
            }
            finally
            {
                Array.Clear(kek, 0, kek.Length);
            }
        }

        private static byte[]? TryDecryptCek(JsonElement recipient, char[] password)
        {
            try
            {
                if (!recipient.TryGetProperty("header", out var headerElement) ||
                    !recipient.TryGetProperty("encrypted_key", out var encKeyElement))
                {
                    return null;
                }

                // Extract parameters
                string alg = headerElement.GetProperty("alg").GetString()!;
                if (alg != "PBES2-HS512+A256KW") return null;

                byte[] salt = Jose.Base64Url.Decode(headerElement.GetProperty("p2s").GetString()!);
                int iterations = headerElement.GetProperty("p2c").GetInt32();
                byte[] encryptedKey = Jose.Base64Url.Decode(encKeyElement.GetString()!);

                // Derive KEK
                byte[] kek = new byte[32];
                byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
                using (var pbkdf2 = new Rfc2898DeriveBytes(passwordBytes, salt, iterations, HashAlgorithmName.SHA512))
                {
                    kek = pbkdf2.GetBytes(32);
                }

                try
                {
                    // Decrypt CEK
                    return AesKeyWrap.UnwrapKey(kek, encryptedKey);
                }
                finally
                {
                    Array.Clear(kek, 0, kek.Length);
                }
            }
            catch
            {
                return null;
            }
        }

        #endregion

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

            string payloadJson = JsonSerializer.Serialize(payload);

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
                var jweJson = new
                {
                    @protected = CreateScryptProtectedHeader(payload.UvfSpecVersion),
                    recipients = recipients,
                    iv = Jose.Base64Url.Encode(iv),
                    ciphertext = Jose.Base64Url.Encode(ciphertext),
                    tag = Jose.Base64Url.Encode(tag)
                };

                return JsonSerializer.Serialize(jweJson);
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
        private static object CreateScryptRecipient(string keyId, char[] password, byte[] cek, KeyDerivationParameters kdfParams)
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

                return new
                {
                    header = new
                    {
                        alg = "uvf.scrypt+A256KW", // Custom algorithm identifier
                        kid = keyId,
                        uvf_kdf_scrypt_n = kdfParams.ScryptN,
                        uvf_kdf_scrypt_r = kdfParams.ScryptR,
                        uvf_kdf_scrypt_p = kdfParams.ScryptP,
                        uvf_kdf_scrypt_salt = Jose.Base64Url.Encode(salt)
                    },
                    encrypted_key = Jose.Base64Url.Encode(encryptedKey)
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
            var header = new
            {
                enc = "A256GCM",
                cty = "json",
                crit = new[] { "uvf.spec.version", "uvf.kdf.method" },
                uvf_spec_version = uvfSpecVersion,
                uvf_kdf_method = "scrypt"
            };
            
            string headerJson = JsonSerializer.Serialize(header);
            return Jose.Base64Url.Encode(Encoding.UTF8.GetBytes(headerJson));
        }

        #endregion
    }

    /// <summary>
    /// Helper class for AES Key Wrap operations
    /// </summary>
    internal static class AesKeyWrap
    {
        public static byte[] WrapKey(byte[] kek, byte[] keyToWrap)
        {
            // Simplified AES Key Wrap implementation
            // In production, use a proper AES Key Wrap implementation
            using var aes = Aes.Create();
            aes.Key = kek;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();
            return encryptor.TransformFinalBlock(keyToWrap, 0, keyToWrap.Length);
        }

        public static byte[] UnwrapKey(byte[] kek, byte[] wrappedKey)
        {
            // Simplified AES Key Wrap implementation
            // In production, use a proper AES Key Wrap implementation
            using var aes = Aes.Create();
            aes.Key = kek;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(wrappedKey, 0, wrappedKey.Length);
        }
    }
}