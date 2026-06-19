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
    /// <summary>
    /// Manages multi-user JWE-formatted vault files (vault.uvf) with secure password handling.
    /// Uses char[] for passwords internally for better memory security.
    /// </summary>
    public static partial class MultiUserJweVaultManager
    {
        private const JweAlgorithm KeyManagementAlgorithm = JweAlgorithm.PBES2_HS512_A256KW;
        private const JweEncryption ContentEncryptionAlgorithm = JweEncryption.A256GCM;
        private const int DefaultPbkdf2Iterations = 210000; // OWASP 2023 guidance for PBKDF2-HMAC-SHA512
        private const string AdminKeyId = "uvflib.net.admin";
        private const string UserKeyIdPrefix = "uvflib.net.user.";
        private const string PublicKeyKeyIdPrefix = "uvflib.net.pubkey.";
        private const string EcdhAlgorithm = "ECDH-ES+A256KW";

        /// <summary>
        /// Creates a single-user JWE vault (UVF-compliant with admin recipient).
        /// </summary>
        /// <param name="payload">The UVF masterkey payload to encrypt</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="kdfParams">Key derivation parameters (optional, defaults to PBKDF2)</param>
        /// <returns>JWE JSON serialization string</returns>
        public static string CreateSingleUserVault(UvfMasterkeyPayload payload, byte[] passwordBytes, KeyDerivationParameters? kdfParams = null)
        {
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));
            
            char[] passwordChars = System.Text.Encoding.UTF8.GetChars(passwordBytes);
            try
            {
                var emptyUsers = new Dictionary<string, char[]>();
                return CreateMultiUserVault(payload, emptyUsers, passwordChars, kdfParams);
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }

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
                var jweJson = new JweJson
                {
                    Protected = CreateProtectedHeader(),
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
        /// Loads a single-user vault (admin recipient).
        /// </summary>
        /// <param name="jweJsonString">JWE JSON serialization string</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <returns>Decrypted UVF masterkey payload</returns>
        public static UvfMasterkeyPayload LoadSingleUserVault(string jweJsonString, byte[] passwordBytes)
        {
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));
            
            char[] passwordChars = System.Text.Encoding.UTF8.GetChars(passwordBytes);
            try
            {
                return LoadMultiUserVault(jweJsonString, passwordChars, "admin");
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
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
                            return JsonSerializer.Deserialize<UvfMasterkeyPayload>(payloadJson, UvfJsonContext.Default.UvfMasterkeyPayload)!;
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
                    existingRecipients.Add(JsonSerializer.Deserialize<object>(recipient.GetRawText(), UvfJsonContext.Default.Object)!);
                }
                existingRecipients.Add(newRecipient);

                // Create updated JWE with new recipient using the proper JweJson class
                var updatedJwe = new JweJson
                {
                    Protected = root.GetProperty("protected").GetString()!,
                    Recipients = existingRecipients,
                    Iv = root.GetProperty("iv").GetString()!,
                    Ciphertext = root.GetProperty("ciphertext").GetString()!,
                    Tag = root.GetProperty("tag").GetString()!
                };

                return JsonSerializer.Serialize(updatedJwe, UvfJsonContext.Default.JweJson);
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
                        string pubKeyKid = PublicKeyKeyIdPrefix + userIdToRemove;

                        // Skip the user to be removed (password or public-key recipient)
                        if (kid == userKid || kid == pubKeyKid)
                        {
                            continue;
                        }
                    }
                }
                
                // Keep this recipient
                newRecipients.Add(JsonSerializer.Deserialize<object>(recipient.GetRawText(), UvfJsonContext.Default.Object)!);
            }

            // Recreate JWE with updated recipients using the proper JweJson class
            var updatedJwe = new JweJson
            {
                Protected = root.GetProperty("protected").GetString()!,
                Recipients = newRecipients,
                Iv = root.GetProperty("iv").GetString()!,
                Ciphertext = root.GetProperty("ciphertext").GetString()!,
                Tag = root.GetProperty("tag").GetString()!
            };

            return JsonSerializer.Serialize(updatedJwe, UvfJsonContext.Default.JweJson);
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
                            users.Add(kid.Substring(UserKeyIdPrefix.Length));
                        }
                        else if (kid.StartsWith(PublicKeyKeyIdPrefix))
                        {
                            users.Add(kid.Substring(PublicKeyKeyIdPrefix.Length));
                        }
                    }
                }
            }

            return users;
        }

        /// <summary>
        /// Adds a public-key recipient (ECDH-ES+A256KW) to an existing vault. The admin password unwraps
        /// the current CEK, which is re-wrapped to the user's public key. The user's password is not
        /// required (or known) — only their public key. The static public key is stored in the recipient
        /// so the CEK can be re-wrapped to it on rotation without the user being present.
        /// </summary>
        /// <param name="jweJsonString">Current JWE JSON string</param>
        /// <param name="adminPassword">Admin password (char[])</param>
        /// <param name="userId">New user id</param>
        /// <param name="userPublicKeySpki">User's public key as SubjectPublicKeyInfo (DER)</param>
        /// <returns>Updated JWE JSON string</returns>
        public static string AddPublicKeyUserToVault(string jweJsonString, char[] adminPassword, string userId, byte[] userPublicKeySpki)
        {
            if (string.IsNullOrEmpty(jweJsonString)) throw new ArgumentNullException(nameof(jweJsonString));
            if (adminPassword == null) throw new ArgumentNullException(nameof(adminPassword));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (userPublicKeySpki == null || userPublicKeySpki.Length == 0) throw new ArgumentNullException(nameof(userPublicKeySpki));

            using var doc = JsonDocument.Parse(jweJsonString);
            var root = doc.RootElement;

            byte[]? cek = null;
            foreach (var recipient in root.GetProperty("recipients").EnumerateArray())
            {
                cek = TryDecryptCek(recipient, adminPassword);
                if (cek != null) break;
            }
            if (cek == null) throw new UnauthorizedAccessException("Unable to decrypt vault with admin password");

            try
            {
                var newRecipient = CreateEcdhRecipient(PublicKeyKeyIdPrefix + userId, userPublicKeySpki, cek);

                var existingRecipients = new List<object>();
                foreach (var recipient in root.GetProperty("recipients").EnumerateArray())
                {
                    existingRecipients.Add(JsonSerializer.Deserialize<object>(recipient.GetRawText(), UvfJsonContext.Default.Object)!);
                }
                existingRecipients.Add(newRecipient);

                var updatedJwe = new JweJson
                {
                    Protected = root.GetProperty("protected").GetString()!,
                    Recipients = existingRecipients,
                    Iv = root.GetProperty("iv").GetString()!,
                    Ciphertext = root.GetProperty("ciphertext").GetString()!,
                    Tag = root.GetProperty("tag").GetString()!
                };
                return JsonSerializer.Serialize(updatedJwe, UvfJsonContext.Default.JweJson);
            }
            finally
            {
                Array.Clear(cek, 0, cek.Length);
            }
        }

        /// <summary>
        /// Loads a vault using a user's EC private key (public-key recipient, ECDH-ES+A256KW).
        /// </summary>
        /// <param name="jweJsonString">JWE JSON string</param>
        /// <param name="privateKey">The user's EC private key (P-384)</param>
        /// <param name="userId">Optional user id hint (matches the uvflib.net.pubkey.&lt;id&gt; recipient)</param>
        /// <returns>Decrypted UVF masterkey payload</returns>
        public static UvfMasterkeyPayload LoadMultiUserVaultWithKey(string jweJsonString, ECDiffieHellman privateKey, string? userId = null)
        {
            if (string.IsNullOrEmpty(jweJsonString)) throw new ArgumentNullException(nameof(jweJsonString));
            if (privateKey == null) throw new ArgumentNullException(nameof(privateKey));

            using var doc = JsonDocument.Parse(jweJsonString);
            var root = doc.RootElement;
            byte[] iv = Jose.Base64Url.Decode(root.GetProperty("iv").GetString()!);
            byte[] ciphertext = Jose.Base64Url.Decode(root.GetProperty("ciphertext").GetString()!);
            byte[] tag = Jose.Base64Url.Decode(root.GetProperty("tag").GetString()!);

            foreach (var recipient in root.GetProperty("recipients").EnumerateArray())
            {
                try
                {
                    if (userId != null && recipient.TryGetProperty("header", out var headerEl) &&
                        headerEl.TryGetProperty("kid", out var kidEl) &&
                        kidEl.GetString() != PublicKeyKeyIdPrefix + userId)
                    {
                        continue;
                    }

                    byte[]? cek = TryDecryptCekWithKey(recipient, privateKey);
                    if (cek == null) continue;
                    try
                    {
                        byte[] decrypted = new byte[ciphertext.Length];
                        using (var aesGcm = new System.Security.Cryptography.AesGcm(cek))
                        {
                            aesGcm.Decrypt(iv, ciphertext, tag, decrypted);
                        }
                        return JsonSerializer.Deserialize<UvfMasterkeyPayload>(Encoding.UTF8.GetString(decrypted), UvfJsonContext.Default.UvfMasterkeyPayload)!;
                    }
                    finally
                    {
                        Array.Clear(cek, 0, cek.Length);
                    }
                }
                catch
                {
                    continue;
                }
            }
            throw new UnauthorizedAccessException("Unable to decrypt vault with the provided private key");
        }

        /// <summary>
        /// Returns the public-key members of a vault as (userId, public-key SubjectPublicKeyInfo) pairs,
        /// so the CEK can be re-wrapped to each on key rotation (no member passwords required).
        /// </summary>
        public static List<(string UserId, byte[] PublicKey)> GetPublicKeyMembers(string jweJsonString)
        {
            if (string.IsNullOrEmpty(jweJsonString)) throw new ArgumentNullException(nameof(jweJsonString));

            using var doc = JsonDocument.Parse(jweJsonString);
            var members = new List<(string, byte[])>();
            foreach (var recipient in doc.RootElement.GetProperty("recipients").EnumerateArray())
            {
                if (recipient.TryGetProperty("header", out var header) &&
                    header.TryGetProperty("kid", out var kidEl))
                {
                    string kid = kidEl.GetString()!;
                    if (kid.StartsWith(PublicKeyKeyIdPrefix) &&
                        header.TryGetProperty("uvf_user_pubkey", out var pubEl))
                    {
                        members.Add((kid.Substring(PublicKeyKeyIdPrefix.Length), Jose.Base64Url.Decode(pubEl.GetString()!)));
                    }
                }
            }
            return members;
        }

        #region Private Helper Methods

        private static string CreateProtectedHeader()
        {
            // Create the exact protected header required by UVF spec
            var header = new Dictionary<string, object>
            {
                ["enc"] = "A256GCM",
                ["cty"] = "json",
                ["crit"] = new[] { "uvf.spec.version" },
                ["uvf.spec.version"] = 1
            };
            
            string headerJson = JsonSerializer.Serialize(header, UvfJsonContext.Default.DictionaryStringObject);
            return Jose.Base64Url.Encode(Encoding.UTF8.GetBytes(headerJson));
        }

        private static JweRecipient CreateRecipient(string keyId, char[] password, byte[] cek, int iterations)
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

                return new JweRecipient
                {
                    Header = new JweRecipientHeader
                    {
                        Algorithm = "PBES2-HS512+A256KW",
                        KeyId = keyId,
                        Salt = Jose.Base64Url.Encode(salt),
                        Iterations = iterations
                    },
                    EncryptedKey = Jose.Base64Url.Encode(encryptedKey)
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
                byte[] encryptedKey = Jose.Base64Url.Decode(encKeyElement.GetString()!);

                if (alg == "PBES2-HS512+A256KW")
                {
                    // PBKDF2 recipient
                    byte[] salt = Jose.Base64Url.Decode(headerElement.GetProperty("p2s").GetString()!);
                    int iterations = headerElement.GetProperty("p2c").GetInt32();

                    // Derive KEK using PBKDF2
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
                else if (alg == "uvf.scrypt+A256KW")
                {
                    // Scrypt recipient
                    byte[] salt = Jose.Base64Url.Decode(headerElement.GetProperty("uvf_kdf_scrypt_salt").GetString()!);
                    int scryptN = headerElement.GetProperty("uvf_kdf_scrypt_n").GetInt32();
                    int scryptR = headerElement.GetProperty("uvf_kdf_scrypt_r").GetInt32();
                    int scryptP = headerElement.GetProperty("uvf_kdf_scrypt_p").GetInt32();

                    // Derive KEK using Scrypt
                    byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                    byte[] kek;
                    try
                    {
                        kek = SCrypt.Generate(
                            passwordBytes,
                            salt,
                            scryptN,
                            scryptR,
                            scryptP,
                            32 // 256-bit KEK for AES Key Wrap
                        );
                    }
                    finally
                    {
                        Array.Clear(passwordBytes, 0, passwordBytes.Length);
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
                else
                {
                    // Unsupported algorithm
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates an ECDH-ES+A256KW recipient that wraps the CEK to the user's public key. A fresh
        /// ephemeral key is used per wrap; the user's static public key is retained for rotation.
        /// </summary>
        private static JweEcdhRecipient CreateEcdhRecipient(string keyId, byte[] userPublicKeySpki, byte[] cek)
        {
            using var userPublic = EcdhKeyMaterial.ImportPublicKey(userPublicKeySpki);
            using var ephemeral = EcdhKeyMaterial.GenerateKeyPair();
            byte[] kek = EcdhKeyMaterial.DeriveKek(ephemeral, userPublic.PublicKey);
            try
            {
                byte[] encryptedKey = AesKeyWrap.WrapKey(kek, cek);
                return new JweEcdhRecipient
                {
                    Header = new JweEcdhRecipientHeader
                    {
                        Algorithm = EcdhAlgorithm,
                        KeyId = keyId,
                        EphemeralPublicKey = Jose.Base64Url.Encode(EcdhKeyMaterial.ExportPublicKey(ephemeral)),
                        UserPublicKey = Jose.Base64Url.Encode(userPublicKeySpki)
                    },
                    EncryptedKey = Jose.Base64Url.Encode(encryptedKey)
                };
            }
            finally
            {
                Array.Clear(kek, 0, kek.Length);
            }
        }

        private static byte[]? TryDecryptCekWithKey(JsonElement recipient, ECDiffieHellman privateKey)
        {
            try
            {
                if (!recipient.TryGetProperty("header", out var headerElement) ||
                    !recipient.TryGetProperty("encrypted_key", out var encKeyElement))
                {
                    return null;
                }
                if (headerElement.GetProperty("alg").GetString() != EcdhAlgorithm) return null;

                byte[] epk = Jose.Base64Url.Decode(headerElement.GetProperty("epk").GetString()!);
                byte[] encryptedKey = Jose.Base64Url.Decode(encKeyElement.GetString()!);

                using var ephemeralPublic = EcdhKeyMaterial.ImportPublicKey(epk);
                byte[] kek = EcdhKeyMaterial.DeriveKek(privateKey, ephemeralPublic.PublicKey);
                try
                {
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

    }

    /// <summary>
    /// Helper class for RFC 3394 AES Key Wrap operations
    /// </summary>
    internal static class AesKeyWrap
    {
        private static readonly byte[] DefaultIV = { 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6, 0xA6 };

        public static byte[] WrapKey(byte[] kek, byte[] keyToWrap)
        {
            if (kek == null) throw new ArgumentNullException(nameof(kek));
            if (keyToWrap == null) throw new ArgumentNullException(nameof(keyToWrap));
            if (keyToWrap.Length % 8 != 0) throw new ArgumentException("Key to wrap must be a multiple of 8 bytes");

            int n = keyToWrap.Length / 8;
            byte[] a = new byte[8];
            Array.Copy(DefaultIV, a, 8);
            
            byte[] r = new byte[keyToWrap.Length];
            Array.Copy(keyToWrap, r, keyToWrap.Length);

            using var aes = Aes.Create();
            aes.Key = kek;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            using var encryptor = aes.CreateEncryptor();

            for (int j = 0; j <= 5; j++)
            {
                for (int i = 1; i <= n; i++)
                {
                    byte[] b = new byte[16];
                    Array.Copy(a, 0, b, 0, 8);
                    Array.Copy(r, (i - 1) * 8, b, 8, 8);
                    
                    byte[] encrypted = encryptor.TransformFinalBlock(b, 0, 16);
                    
                    // A = MSB(64, B) ^ t where t = (n*j)+i
                    long t = (long)n * j + i;
                    Array.Copy(encrypted, 0, a, 0, 8);
                    for (int k = 7; k >= 0; k--)
                    {
                        a[k] ^= (byte)(t & 0xFF);
                        t >>= 8;
                    }
                    
                    // R[i] = LSB(64, B)
                    Array.Copy(encrypted, 8, r, (i - 1) * 8, 8);
                }
            }

            byte[] result = new byte[8 + r.Length];
            Array.Copy(a, 0, result, 0, 8);
            Array.Copy(r, 0, result, 8, r.Length);
            return result;
        }

        public static byte[] UnwrapKey(byte[] kek, byte[] wrappedKey)
        {
            if (kek == null) throw new ArgumentNullException(nameof(kek));
            if (wrappedKey == null) throw new ArgumentNullException(nameof(wrappedKey));
            if (wrappedKey.Length < 24 || (wrappedKey.Length - 8) % 8 != 0) 
                throw new ArgumentException("Wrapped key must be at least 24 bytes and (length - 8) must be a multiple of 8");

            int n = (wrappedKey.Length - 8) / 8;
            byte[] a = new byte[8];
            Array.Copy(wrappedKey, 0, a, 0, 8);
            
            byte[] r = new byte[wrappedKey.Length - 8];
            Array.Copy(wrappedKey, 8, r, 0, r.Length);

            using var aes = Aes.Create();
            aes.Key = kek;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            using var decryptor = aes.CreateDecryptor();

            for (int j = 5; j >= 0; j--)
            {
                for (int i = n; i >= 1; i--)
                {
                    // A = A ^ t where t = n*j+i
                    long t = (long)n * j + i;
                    for (int k = 7; k >= 0; k--)
                    {
                        a[k] ^= (byte)(t & 0xFF);
                        t >>= 8;
                    }
                    
                    byte[] b = new byte[16];
                    Array.Copy(a, 0, b, 0, 8);
                    Array.Copy(r, (i - 1) * 8, b, 8, 8);
                    
                    byte[] decrypted = decryptor.TransformFinalBlock(b, 0, 16);
                    
                    Array.Copy(decrypted, 0, a, 0, 8);
                    Array.Copy(decrypted, 8, r, (i - 1) * 8, 8);
                }
            }

            // Verify the IV
            for (int i = 0; i < 8; i++)
            {
                if (a[i] != DefaultIV[i])
                    throw new System.Security.Cryptography.CryptographicException("AES Key Wrap integrity check failed");
            }

            return r;
        }
    }
}