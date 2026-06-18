using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UvfLib.Core.Api;
using UvfLib.Core.Common;
using UvfLib.Core.Jwe;
using UvfLib.Core.V3;
using UvfLib.Core.CryptomatorV8;
using UvfLib.Vault.VaultHelpers; // Added for VaultKeyHelper
using System.Buffers.Binary; // Added for BinaryPrimitives
using CryptoOps = UvfLib.Core.Common.CryptographicOperations;
using JwtBase64Url = Jose.Base64Url; // Alias for JWT Base64Url operations

namespace UvfLib.Vault
{
    public sealed partial class VaultHandler
    {
        // Static utility for key file creation (doesn't require a Vault instance)
        /// <summary>
        /// Creates the encrypted master key file content for a new LEGACY Cryptomator vault.
        /// </summary>
        /// <param name="password">The password for the new vault.</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        /// <returns>A byte array containing the encrypted master key file data.</returns>
        /// <exception cref="ArgumentNullException">If password is null.</exception>
        /// <exception cref="CryptoException">If key generation or encryption fails.</exception>
        [Obsolete("Use CreateNewCryptomatorV8VaultFileContent instead")]
        public static byte[] CreateNewLegacyVaultKeyFileContent(string password, byte[]? pepper = null)
        {
            // Delegate to the new method for consistency
            return CreateNewCryptomatorV8VaultFileContent(Encoding.UTF8.GetBytes(password), pepper);
        }

        /// <summary>
        /// Creates the encrypted JWE content (UTF-8 bytes) for a new UVF vault file.
        /// This is the content for "masterkey.cryptomator" or "vault.uvf" in the new format.
        /// </summary>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="encryptFilenames">Whether to encrypt filenames</param>
        /// <returns>A byte array containing the encrypted UVF vault file data (JWE string as UTF-8 bytes).</returns>
        public static byte[] CreateNewUvfVaultFileContent(byte[] passwordBytes, bool encryptFilenames = true)
        {
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));

            // Generate a new random master key
            byte[] encKey = new byte[32]; // 256-bit encryption key
            byte[] macKey = new byte[32]; // 256-bit MAC key
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(encKey);
                rng.GetBytes(macKey);
            }

            // Generate root directory ID
            byte[] rootDirId = new byte[16]; // 128-bit directory ID
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(rootDirId);
            }

            // Generate KDF salt
            byte[] kdfSalt = new byte[16]; // 128-bit salt
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(kdfSalt);
            }

            // Create payload with all necessary keys and metadata
            var payload = new UvfMasterkeyPayload
            {
                UvfSpecVersion = 1,
                Keys = new List<PayloadKey>
                {
                    new PayloadKey { Id = "enc", Purpose = "fileContentEncryption", Alg = "AES-256-GCM", Value = Convert.ToBase64String(encKey) },
                    new PayloadKey { Id = "mac", Purpose = "fileContentAuthentication", Alg = "HMAC-SHA256", Value = Convert.ToBase64String(macKey) }
                },
                Kdf = new PayloadKdf
                {
                    Type = "PBKDF2-HMAC-SHA512",
                    Salt = Convert.ToBase64String(kdfSalt)
                },
                Seeds = new List<PayloadSeed>
                {
                    new PayloadSeed
                    {
                        Id = JwtBase64Url.Encode(Enumerable.Reverse(BitConverter.GetBytes(1)).ToArray()), // Encode integer 1 as Base64URL (big-endian)
                        Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                        Value = Convert.ToBase64String(new byte[32]) // Initial seed (32 random bytes)
                    }
                },
                RootDirId = Convert.ToBase64String(rootDirId)
            };

            // Fill the initial seed with random data
            byte[] seedValue = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(seedValue);
            }
            payload.Seeds[0].Value = Convert.ToBase64String(seedValue);

            // Create JWE with single user (admin)
            string jweString = MultiUserJweVaultManager.CreateSingleUserVault(payload, passwordBytes);
            
            return Encoding.UTF8.GetBytes(jweString);
        }

        /// <summary>
        /// Creates a new UVF vault file at the specified path.
        /// </summary>
        /// <param name="filePath">Path where the vault file will be created</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="encryptFilenames">Whether to encrypt filenames</param>
        public static void CreateNewUvfVault(string filePath, byte[] passwordBytes, bool encryptFilenames = true)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));

            byte[] vaultContent = CreateNewUvfVaultFileContent(passwordBytes, encryptFilenames);
            File.WriteAllBytes(filePath, vaultContent);
        }

        /// <summary>
        /// Detects whether filename encryption is enabled in a UVF vault without loading the full vault.
        /// </summary>
        /// <param name="uvfFileContent">The byte content of the UVF vault file (JWE string).</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="userId">Optional user id whose recipient/password should be matched. Null tries all
        /// recipients (admin-compatible). Required for a non-admin user, whose password only unwraps their own recipient.</param>
        /// <returns>True if filename encryption is enabled, false if disabled. Defaults to true if not specified.</returns>
        /// <exception cref="ArgumentNullException">If file content or password is null.</exception>
        /// <exception cref="InvalidPassphraseException">If the password is incorrect.</exception>
        /// <exception cref="MasterkeyLoadingFailedException">If the vault file cannot be decrypted or parsed.</exception>
        public static bool DetectFilenameEncryption(byte[] uvfFileContent, byte[] passwordBytes, string? userId = null)
        {
            if (uvfFileContent == null || uvfFileContent.Length == 0) throw new ArgumentNullException(nameof(uvfFileContent));
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));

            try
            {
                string jweString = Encoding.UTF8.GetString(uvfFileContent);
                // KDF-aware + user-aware loading: pass userId so a non-admin user's password is matched
                // against the correct recipient (LoadSingleUserVault hardcodes "admin", which fails for others).
                char[] passwordChars = Encoding.UTF8.GetChars(passwordBytes);
                UvfMasterkeyPayload payload;
                try
                {
                    payload = MultiUserJweVaultManager.LoadMultiUserVault(jweString, passwordChars, userId);
                }
                finally
                {
                    Array.Clear(passwordChars, 0, passwordChars.Length);
                }

                // Check for the custom config field
                if (payload.Config?.EncryptFilenames.HasValue == true)
                {
                    return payload.Config.EncryptFilenames.Value;
                }

                // Default to true (encrypted filenames) for compatibility with vaults created before this feature
                return true;
            }
            catch (Exception ex) when (ex is Jose.JoseException || ex is JsonException || ex is InvalidOperationException || ex is ArgumentException)
            {
                throw new MasterkeyLoadingFailedException("Failed to detect filename encryption mode. Check password or file integrity.", ex);
            }
        }

        /// <summary>
        /// Loads a UVF vault using its JWE-formatted key file content and password.
        /// </summary>
        /// <param name="uvfFileContent">The byte content of the UVF vault file (JWE string).</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <returns>An initialized Vault instance ready for operations.</returns>
        public static VaultHandler LoadUvfVault(byte[] uvfFileContent, byte[] passwordBytes)
        {
            if (uvfFileContent == null || uvfFileContent.Length == 0) throw new ArgumentNullException(nameof(uvfFileContent));
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));

            string jweString = Encoding.UTF8.GetString(uvfFileContent);
            UvfLib.Core.Api.UVFMasterkey? uvfMasterkey = null;
            UvfLib.Core.Api.Cryptor? cryptor = null;
            try
            {
                UvfMasterkeyPayload payload = MultiUserJweVaultManager.LoadSingleUserVault(jweString, passwordBytes);
                
                // Instead of re-serializing, pass the payload object directly if UVFMasterkeyImpl can accept it.
                // For now, assuming FromDecryptedPayload expects JSON string as per current V3.UVFMasterkeyImpl.
                string jsonPayloadString = JsonSerializer.Serialize(payload, UvfLib.Core.Common.UvfJsonContext.Default.UvfMasterkeyPayload);
                
                // UvfLib.Core.Api.UVFMasterkey.FromDecryptedPayload is the entry point
                // This will internally create a V3.UVFMasterkeyImpl instance.
                uvfMasterkey = (UvfLib.Core.Api.UVFMasterkey)UvfLib.Core.Api.UVFMasterkey.FromDecryptedPayload(jsonPayloadString);

                CryptorProvider provider = CryptorProvider.ForScheme(CryptorProvider.Scheme.UVF_DRAFT);
                using var csprng = RandomNumberGenerator.Create();
                cryptor = provider.Provide(uvfMasterkey, csprng);

                return new VaultHandler(cryptor, uvfMasterkey);
            }
            catch (Exception ex) when (ex is Jose.JoseException || ex is JsonException || ex is InvalidOperationException || ex is ArgumentException)
            {
                (uvfMasterkey as IDisposable)?.Dispose();
                (cryptor as IDisposable)?.Dispose();
                throw new MasterkeyLoadingFailedException("Failed to load UVF vault. Check password or file integrity.", ex);
            }
            catch
            {
                (uvfMasterkey as IDisposable)?.Dispose();
                (cryptor as IDisposable)?.Dispose();
                throw; 
            }
        }

        /// <summary>
        /// Loads a Legacy Cryptomator V8 vault's Cryptor instance using the master key file content and password.
        /// </summary>
        /// <param name="encryptedKeyFileContent">The byte content of the master key file.</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        /// <returns>An initialized Vault instance ready for operations.</returns>
        /// <exception cref="ArgumentNullException">If key content or password is null.</exception>
        /// <exception cref="InvalidPassphraseException">If the password is incorrect.</exception>
        /// <exception cref="AuthenticationFailedException">If the master key file MAC is invalid.</exception>
        /// <exception cref="UnsupportedVaultFormatException">If the vault format is not supported.</exception>
        /// <exception cref="MasterkeyLoadingFailedException">For other key loading errors.</exception>
        /// <exception cref="CryptoException">For general cryptographic errors.</exception>
        public static VaultHandler LoadCryptomatorV8Vault(byte[] encryptedKeyFileContent, byte[] passwordBytes, byte[]? pepper = null)
        {
            if (encryptedKeyFileContent == null) throw new ArgumentNullException(nameof(encryptedKeyFileContent));
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));
            byte[] effectivePepper = pepper ?? Array.Empty<byte>();

            // This path uses MasterkeyFileAccess for the old format.
            MasterkeyFile masterkeyFile = MasterkeyFile.FromJson(encryptedKeyFileContent); 
            var keyAccessor = new MasterkeyFileAccess(effectivePepper, RandomNumberGenerator.Create());
            
            // TODO: Update MasterkeyFileAccess to use byte[] passwords
            string passwordString = System.Text.Encoding.UTF8.GetString(passwordBytes);
            UvfLib.Core.Common.PerpetualMasterkey perpetualMasterkey;
            try
            {
                perpetualMasterkey = keyAccessor.Unlock(masterkeyFile, passwordString);
            }
            finally
            {
                // Clear temporary string from memory (best effort)
                passwordString = null;
            }
            
            // For Cryptomator V8, we use the new CryptomatorV8 provider
            var provider = new UvfLib.Core.CryptomatorV8.CryptorProviderImpl();
            var cryptor = provider.Provide(perpetualMasterkey, RandomNumberGenerator.Create());
            
            return new VaultHandler(cryptor, perpetualMasterkey);
        }

        /// <summary>
        /// Changes the password for an existing JWE UVF vault file's content.
        /// </summary>
        /// <param name="encryptedUvfFileContent">The current byte content of the vault.uvf file.</param>
        /// <param name="oldPasswordBytes">The current vault password.</param>
        /// <param name="newPasswordBytes">The desired new vault password.</param>
        /// <returns>A byte array containing the newly encrypted vault.uvf file data.</returns>
        /// <exception cref="ArgumentNullException">If file content or passwords are null.</exception>
        /// <exception cref="InvalidPassphraseException">If the oldPassword is incorrect.</exception>
        public static byte[] ChangeUvfVaultPassword(byte[] encryptedUvfFileContent, byte[] oldPasswordBytes, byte[] newPasswordBytes)
        {
            // This reuses JweVaultManager which is correct.
            string oldJwe = Encoding.UTF8.GetString(encryptedUvfFileContent);
            UvfMasterkeyPayload payload = MultiUserJweVaultManager.LoadSingleUserVault(oldJwe, oldPasswordBytes); // Decrypt with old
            string newJwe = MultiUserJweVaultManager.CreateSingleUserVault(payload, newPasswordBytes); // Re-encrypt with new
            return Encoding.UTF8.GetBytes(newJwe);
        }

        /// <summary>
        /// Changes the password for an existing Cryptomator V8 vault file's content.
        /// </summary>
        /// <param name="encryptedKeyFileContent">The current byte content of the masterkey.cryptomator file.</param>
        /// <param name="oldPasswordBytes">The current vault password.</param>
        /// <param name="newPasswordBytes">The desired new vault password.</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        /// <returns>A byte array containing the newly encrypted masterkey.cryptomator file data.</returns>
        /// <exception cref="ArgumentNullException">If file content or passwords are null.</exception>
        /// <exception cref="InvalidPassphraseException">If the oldPassword is incorrect.</exception>
        /// <exception cref="AuthenticationFailedException">If the master key file MAC is invalid.</exception>
        /// <exception cref="CryptoException">If key operations fail.</exception>
        public static byte[] ChangeCryptomatorV8VaultPassword(byte[] encryptedKeyFileContent, byte[] oldPasswordBytes, byte[] newPasswordBytes, byte[]? pepper = null)
        {
            if (encryptedKeyFileContent == null) throw new ArgumentNullException(nameof(encryptedKeyFileContent));
            if (oldPasswordBytes == null) throw new ArgumentNullException(nameof(oldPasswordBytes));
            if (newPasswordBytes == null) throw new ArgumentNullException(nameof(newPasswordBytes));
            byte[] effectivePepper = pepper ?? Array.Empty<byte>();

            // Use MasterkeyFileAccess.ChangePassphrase method which handles the encryption/decryption
            var keyAccessor = new MasterkeyFileAccess(effectivePepper, RandomNumberGenerator.Create());
            
            // TODO: Update MasterkeyFileAccess to use byte[] passwords
            string oldPasswordString = System.Text.Encoding.UTF8.GetString(oldPasswordBytes);
            string newPasswordString = System.Text.Encoding.UTF8.GetString(newPasswordBytes);
            byte[] result;
            try
            {
                result = keyAccessor.ChangePassphrase(encryptedKeyFileContent, oldPasswordString, newPasswordString);
            }
            finally
            {
                // Clear temporary strings from memory (best effort)
                oldPasswordString = null;
                newPasswordString = null;
            }
            return result;
        }

        /// <summary>
        /// Creates the encrypted master key file content for a new Cryptomator V8 vault.
        /// </summary>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        /// <returns>A byte array containing the encrypted master key file data.</returns>
        /// <exception cref="ArgumentNullException">If password is null.</exception>
        /// <exception cref="CryptoException">If key generation or encryption fails.</exception>
        public static byte[] CreateNewCryptomatorV8VaultFileContent(byte[] passwordBytes, byte[]? pepper = null)
        {
            // TODO: Update VaultKeyHelper to use byte[] passwords
            return VaultKeyHelper.CreateNewVaultKeyFileContentInternal(passwordBytes, pepper);
        }

        /// <summary>
        /// Creates the vault.cryptomator JWT configuration file content with proper HMAC-SHA256 signature.
        /// </summary>
        /// <param name="masterkeyContent">The masterkey.cryptomator file content to extract the keys from.</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <returns>A byte array containing the properly signed vault.cryptomator JWT file data.</returns>
        /// <exception cref="CryptoException">If JWT creation fails.</exception>
        private static byte[] CreateNewCryptomatorV8VaultConfigContentSigned(byte[] masterkeyContent, byte[] passwordBytes)
        {
            try
            {
                // Create JWT payload with vault configuration
                var payload = CryptomatorVaultConfig.CreateDefault();

                // Create JWT header using Base64URL encoding
                string header = JwtBase64Url.Encode(Encoding.UTF8.GetBytes(
                    """{"kid":"masterkeyfile:masterkey.cryptomator","alg":"HS256","typ":"JWT"}"""));

                // Serialize payload using compact JSON format (no spaces) to match Cryptomator
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = false  // Ensures compact format without spaces
                };
                string payloadJson = JsonSerializer.Serialize(payload, UvfLib.Core.Common.UvfJsonContext.Default.CryptomatorVaultConfig);
                string payloadBase64 = JwtBase64Url.Encode(Encoding.UTF8.GetBytes(payloadJson));

                // Create the signing input (header.payload)
                string signingInput = $"{header}.{payloadBase64}";
                byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);

                // According to Cryptomator documentation: "Verify the JWT signature using the concatenation of encryption masterkey and MAC masterkey"
                // We need to unwrap both keys and concatenate them for signing
                byte[] concatenatedSigningKey;
                string masterkeyJson = Encoding.UTF8.GetString(masterkeyContent);
                using (JsonDocument doc = JsonDocument.Parse(masterkeyJson))
                {
                    // Extract wrapped keys and scrypt parameters
                    if (!doc.RootElement.TryGetProperty("primaryMasterKey", out JsonElement encKeyElement) ||
                        !doc.RootElement.TryGetProperty("hmacMasterKey", out JsonElement macKeyElement) ||
                        !doc.RootElement.TryGetProperty("scryptSalt", out JsonElement saltElement) ||
                        !doc.RootElement.TryGetProperty("scryptCostParam", out JsonElement costElement) ||
                        !doc.RootElement.TryGetProperty("scryptBlockSize", out JsonElement blockSizeElement))
                    {
                        throw new InvalidOperationException("Required masterkey fields not found in JSON");
                    }

                    byte[] wrappedEncKey = Convert.FromBase64String(encKeyElement.GetString()!);
                    byte[] wrappedMacKey = Convert.FromBase64String(macKeyElement.GetString()!);
                    byte[] salt = Convert.FromBase64String(saltElement.GetString()!);
                    int costParam = costElement.GetInt32();
                    int blockSize = blockSizeElement.GetInt32();

                    // Derive KEK using the same password that was used to create the masterkey
                    // Note: This requires the password to be available during JWT creation
                    // For now, we'll use the global Password constant - this should be passed as parameter in production
                    byte[] kek = Org.BouncyCastle.Crypto.Generators.SCrypt.Generate(
                        passwordBytes, 
                        salt, 
                        costParam, 
                        blockSize, 
                        1, // parallelism = 1 for Cryptomator
                        32 // KEK length = 32 bytes
                    );

                    try
                    {
                        // Unwrap both keys using AES Key Wrap
                        byte[] rawEncKey = AesKeyWrap.Unwrap(kek, wrappedEncKey);
                        byte[] rawMacKey = AesKeyWrap.Unwrap(kek, wrappedMacKey);

                        // Concatenate as per Cryptomator specification: encryption key + MAC key
                        concatenatedSigningKey = new byte[rawEncKey.Length + rawMacKey.Length];
                        Buffer.BlockCopy(rawEncKey, 0, concatenatedSigningKey, 0, rawEncKey.Length);
                        Buffer.BlockCopy(rawMacKey, 0, concatenatedSigningKey, rawEncKey.Length, rawMacKey.Length);

                        // Clear sensitive data
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(rawEncKey);
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(rawMacKey);
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(kek);
                    }
                }

                // Create HMAC-SHA256 signature using concatenated key as per Cryptomator specification
                byte[] signatureBytes;
                using (var hmac = new HMACSHA256(concatenatedSigningKey))
                {
                    signatureBytes = hmac.ComputeHash(signingInputBytes);
                }

                // Clear the signing key
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(concatenatedSigningKey);

                // Convert signature to Base64URL
                string signature = JwtBase64Url.Encode(signatureBytes);

                string jwt = $"{header}.{payloadBase64}.{signature}";
                return Encoding.UTF8.GetBytes(jwt);
            }
            catch (Exception ex)
            {
                throw new CryptoException("Failed to create signed Cryptomator V8 vault config content", ex);
            }
        }

        /// <summary>
        /// Creates a new Cryptomator V8 vault configuration file content (vault.cryptomator).
        /// Returns a JWT-formatted content suitable for writing to vault.cryptomator.
        /// </summary>
        /// <returns>A byte array containing the vault.cryptomator JWT file data.</returns>
        /// <exception cref="CryptoException">If JWT creation fails.</exception>
        public static byte[] CreateNewCryptomatorV8VaultConfigContent()
        {
            try
            {
                // Create JWT payload with vault configuration
                var payload = CryptomatorVaultConfig.CreateDefault();

                // Create JWT header using Base64URL encoding
                string header = JwtBase64Url.Encode(Encoding.UTF8.GetBytes(
                    """{"kid":"masterkeyfile:masterkey.cryptomator","alg":"HS256","typ":"JWT"}"""));

                // Serialize payload using compact JSON format (no spaces) to match Cryptomator
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = false  // Ensures compact format without spaces
                };
                string payloadJson = JsonSerializer.Serialize(payload, UvfLib.Core.Common.UvfJsonContext.Default.CryptomatorVaultConfig);
                string payloadBase64 = JwtBase64Url.Encode(Encoding.UTF8.GetBytes(payloadJson));

                // Create a dummy signature for now (real implementation would use HMAC-SHA256)
                string signature = Convert.ToBase64String(new byte[32]) // 32 bytes = 256 bits
                    .TrimEnd('='); // Remove padding

                string jwt = $"{header}.{payloadBase64}.{signature}";
                return Encoding.UTF8.GetBytes(jwt);
            }
            catch (Exception ex)
            {
                throw new CryptoException("Failed to create Cryptomator V8 vault config content", ex);
            }
        }

        /// <summary>
        /// Creates a new Cryptomator V8 vault file (masterkey.cryptomator) at the specified path.
        /// </summary>
        /// <param name="filePath">The path where the vault file will be created.</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        public static void CreateNewCryptomatorV8Vault(string filePath, byte[] passwordBytes, byte[]? pepper = null)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            byte[] vaultFileContent = CreateNewCryptomatorV8VaultFileContent(passwordBytes, pepper);
            File.WriteAllBytes(filePath, vaultFileContent);
        }

        /// <summary>
        /// Creates both masterkey.cryptomator and vault.cryptomator files for a complete Cryptomator V8 vault.
        /// </summary>
        /// <param name="vaultDirectory">The directory where the vault files will be created.</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        public static void CreateNewCryptomatorV8VaultComplete(string vaultDirectory, byte[] passwordBytes, byte[]? pepper = null)
        {
            if (string.IsNullOrEmpty(vaultDirectory)) throw new ArgumentNullException(nameof(vaultDirectory));
            
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(vaultDirectory);
            
            // Create masterkey.cryptomator first
            string masterkeyPath = Path.Combine(vaultDirectory, "masterkey.cryptomator");
            byte[] masterkeyContent = CreateNewCryptomatorV8VaultFileContent(passwordBytes, pepper);
            File.WriteAllBytes(masterkeyPath, masterkeyContent);
            
            // Create vault.cryptomator with proper HMAC-SHA256 signature using the hmacMasterKey from the JSON file
            string vaultConfigPath = Path.Combine(vaultDirectory, "vault.cryptomator");
            byte[] vaultConfigContent = CreateNewCryptomatorV8VaultConfigContentSigned(masterkeyContent, passwordBytes);
            File.WriteAllBytes(vaultConfigPath, vaultConfigContent);
        }

        // --- Instance Methods for Operations ---

        /// <summary>
        /// Encrypts a filename for storage within the vault's root directory.
        /// </summary>
        /// <param name="plaintextFilename">The original filename.</param>
        /// <returns>The encrypted filename (Base64URL encoded + .uvf extension).</returns>
        /// <exception cref="ArgumentNullException">If plaintextFilename is null.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        /// <exception cref="CryptoException">If encryption fails.</exception>
        public string EncryptFilenameForRoot(string plaintextFilename)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (plaintextFilename == null) throw new ArgumentNullException(nameof(plaintextFilename));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");

            UvfLib.Core.Api.DirectoryMetadata rootMetadata = dirCryptor.RootDirectoryMetadata(); // Use Core type internally
            UvfLib.Core.Api.IDirectoryContentCryptor.Encrypting nameEncryptor = dirCryptor.FileNameEncryptor(rootMetadata);
            return nameEncryptor.Encrypt(plaintextFilename);
        }

        /// <summary>
        /// Decrypts a filename from the vault's root directory.
        /// </summary>
        /// <param name="encryptedFilename">The encrypted filename (including .uvf extension).</param>
        /// <returns>The original plaintext filename.</returns>
        /// <exception cref="ArgumentNullException">If encryptedFilename is null.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        /// <exception cref="ArgumentException">If the encryptedFilename format is invalid.</exception>
        /// <exception cref="AuthenticationFailedException">If the filename authentication fails.</exception>
        /// <exception cref="CryptoException">If decryption fails.</exception>
        public string DecryptFilenameFromRoot(string encryptedFilename)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (encryptedFilename == null) throw new ArgumentNullException(nameof(encryptedFilename));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");

            UvfLib.Core.Api.DirectoryMetadata rootMetadata = dirCryptor.RootDirectoryMetadata(); // Use Core type internally
            UvfLib.Core.Api.IDirectoryContentCryptor.Decrypting nameDecryptor = dirCryptor.FileNameDecryptor(rootMetadata);
            return nameDecryptor.Decrypt(encryptedFilename);
        }

        /// <summary>
        /// Gets the encrypted directory path for the vault's root directory.
        /// </summary>
        /// <returns>The encrypted path (e.g., "d/XX/YYYY...").</returns>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public string GetRootDirectoryPath()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");

            UvfLib.Core.Api.DirectoryMetadata rootMetadata = dirCryptor.RootDirectoryMetadata(); // Use Core type internally
            return dirCryptor.DirPath(rootMetadata);
        }

        /// <summary>
        /// Gets the DirectoryMetadata for the vault's root directory.
        /// </summary>
        /// <returns>The DirectoryMetadata for the root.</returns>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public UvfLib.Core.Api.DirectoryMetadata GetRootDirectoryMetadata()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");
            return dirCryptor.RootDirectoryMetadata();
        }
    }
}
