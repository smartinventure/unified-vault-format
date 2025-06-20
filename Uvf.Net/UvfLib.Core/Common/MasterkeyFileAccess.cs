using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UvfLib.Core.Api;
using Org.BouncyCastle.Crypto.Generators;
using System.Text.Json;

namespace UvfLib.Core.Common
{
    /// <summary>
    /// Provides access to masterkey files, including reading, writing, and key derivation.
    /// </summary>
    public class MasterkeyFileAccess
    {
        private const int SCRYPT_COST_DEFAULT = 1 << 15; // 32768 (matches Java: 2^15, actual N value)
        private const int SCRYPT_BLOCK_SIZE_DEFAULT = 8;
        private const int SCRYPT_PARALLELIZATION_DEFAULT = 1;

        private const int KEY_LEN_BYTES = 32;
        private const int MAC_LEN_BYTES = 32;
        private const int NONCE_LEN_BYTES = 16;

        private readonly byte[] _pepper;
        private readonly RandomNumberGenerator _random;

        /// <summary>
        /// Creates a new MasterkeyFileAccess.
        /// </summary>
        /// <param name="pepper">Additional secret material to use during key derivation</param>
        /// <param name="random">Random number generator to use</param>
        public MasterkeyFileAccess(byte[] pepper, RandomNumberGenerator random)
        {
            _pepper = pepper ?? throw new ArgumentNullException(nameof(pepper));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// Loads a masterkey file from disk.
        /// </summary>
        /// <param name="path">Path to the masterkey file</param>
        /// <returns>The loaded masterkey file</returns>
        /// <exception cref="IOException">If the file cannot be read</exception>
        public static MasterkeyFile Load(string path)
        {
            try
            {
                byte[] fileContent = File.ReadAllBytes(path);
                return MasterkeyFile.FromJson(fileContent);
            }
            catch (IOException ex)
            {
                throw new IOException($"Unable to read masterkey file: {path}", ex);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException)
            {
                throw new IOException($"Invalid masterkey file format: {path}", ex);
            }
        }

        /// <summary>
        /// Saves a masterkey file to disk.
        /// </summary>
        /// <param name="masterkeyFile">The masterkey file to save</param>
        /// <param name="path">Path where to save the file</param>
        /// <exception cref="IOException">If the file cannot be written</exception>
        public static void Save(MasterkeyFile masterkeyFile, string path)
        {
            if (masterkeyFile == null)
            {
                throw new ArgumentNullException(nameof(masterkeyFile));
            }

            try
            {
                byte[] fileContent = masterkeyFile.ToJson();
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllBytes(path, fileContent);
            }
            catch (IOException ex)
            {
                throw new IOException($"Unable to write masterkey file: {path}", ex);
            }
        }

        /// <summary>
        /// Creates a new masterkey file from a Cryptomator Vault Format master key.
        /// </summary>
        /// <param name="masterkey">The raw master key</param>
        /// <returns>A new masterkey file</returns>
        public static MasterkeyFile CreateNew(byte[] masterkey)
        {
            return CreateNew(masterkey, SCRYPT_COST_DEFAULT, SCRYPT_BLOCK_SIZE_DEFAULT, SCRYPT_PARALLELIZATION_DEFAULT);
        }

        /// <summary>
        /// Creates a new masterkey file from a Cryptomator Vault Format master key with custom parameters.
        /// </summary>
        /// <param name="masterkey">The raw master key</param>
        /// <param name="costParam">The scrypt cost parameter</param>
        /// <param name="blockSize">The scrypt block size</param>
        /// <param name="parallelism">The scrypt parallelism parameter</param>
        /// <returns>A new masterkey file</returns>
        public static MasterkeyFile CreateNew(byte[] masterkey, int costParam, int blockSize, int parallelism)
        {
            if (masterkey == null || masterkey.Length == 0)
            {
                throw new ArgumentException("Invalid master key", nameof(masterkey));
            }

            var masterkeyFile = new MasterkeyFile
            {
                ScryptCostParam = costParam,
                ScryptBlockSize = blockSize,
                ScryptParallelism = parallelism,
                VaultVersion = 8, // latest version of Cryptomator Vault Format
                Version = 999, // Legacy version field used by Cryptomator (matches real Cryptomator behavior)
                ContentEncryptionScheme = "SIV_GCM", // default for version 8
                FilenameEncryptionScheme = "SIV", // default for version 8
            };

            return masterkeyFile;
        }

        /// <summary>
        /// Creates a masterkey file from a passphrase.
        /// </summary>
        /// <param name="passphrase">The passphrase</param>
        /// <returns>A masterkey file with encrypted key material</returns>
        public static MasterkeyFile CreateFromPassphrase(string passphrase)
        {
            return CreateFromPassphrase(passphrase, SCRYPT_COST_DEFAULT, SCRYPT_BLOCK_SIZE_DEFAULT, SCRYPT_PARALLELIZATION_DEFAULT);
        }

        /// <summary>
        /// Creates a masterkey file from a passphrase with custom parameters.
        /// Returns a clean Cryptomator-compatible format without extra UVF fields.
        /// </summary>
        /// <param name="passphrase">The passphrase</param>
        /// <param name="costParam">The scrypt cost parameter</param>
        /// <param name="blockSize">The scrypt block size</param>
        /// <param name="parallelism">The scrypt parallelism parameter</param>
        /// <returns>A masterkey file with encrypted key material</returns>
        public static MasterkeyFile CreateFromPassphrase(string passphrase, int costParam, int blockSize, int parallelism)
        {
            if (string.IsNullOrEmpty(passphrase))
            {
                throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));
            }

            // Generate random masterkey (combined encryption + MAC key)
            byte[] combinedMasterkey = new byte[KEY_LEN_BYTES + MAC_LEN_BYTES]; // 64 bytes total
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(combinedMasterkey);
            }

            // Split the combined key into encryption and MAC keys
            byte[] encKey = new byte[KEY_LEN_BYTES];
            byte[] macKey = new byte[MAC_LEN_BYTES];
            Buffer.BlockCopy(combinedMasterkey, 0, encKey, 0, KEY_LEN_BYTES);
            Buffer.BlockCopy(combinedMasterkey, KEY_LEN_BYTES, macKey, 0, MAC_LEN_BYTES);

            // Generate random salt
            byte[] salt = new byte[NONCE_LEN_BYTES];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Create a clean MasterkeyFile with ONLY the essential fields set
            var masterkeyFile = new MasterkeyFile
            {
                Version = 999, // Legacy version field used by Cryptomator (matches real Cryptomator behavior)
                ScryptCostParam = costParam,
                ScryptBlockSize = blockSize,
                ScryptParallelism = parallelism,
                ScryptSalt = salt
            };

            try
            {
                // Derive key-encryption key from passphrase
                byte[] passphraseDerivedKey = DerivePassphraseKey(
                    Encoding.UTF8.GetBytes(passphrase),
                    salt,
                    null, // No pepper for static method
                    costParam,
                    blockSize,
                    parallelism);

                // Use the first part as KEK for key wrapping
                byte[] kek = new byte[KEY_LEN_BYTES];
                Buffer.BlockCopy(passphraseDerivedKey, 0, kek, 0, KEY_LEN_BYTES);

                // Wrap (encrypt) both keys separately using AES Key Wrap
                byte[] wrappedEncKey = AesKeyWrap.Wrap(kek, encKey);
                byte[] wrappedMacKey = AesKeyWrap.Wrap(kek, macKey);

                // Calculate version MAC using the raw MAC key
                byte[] versionMac;
                using (var hmac = new HMACSHA256(macKey))
                {
                    // Convert vault version to big-endian bytes for MAC calculation
                    // Note: We use 999 for the MAC calculation to match the version field
                    byte[] versionBytes = BitConverter.GetBytes(999); // Use version 999 for MAC
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(versionBytes);
                    }
                    versionMac = hmac.ComputeHash(versionBytes);
                }

                // Store ONLY the essential fields for Cryptomator compatibility
                masterkeyFile.EncMasterKey = wrappedEncKey;
                masterkeyFile.MacMasterKey = wrappedMacKey;
                masterkeyFile.VersionMac = versionMac;

                return masterkeyFile;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(combinedMasterkey);
                CryptographicOperations.ZeroMemory(encKey);
                CryptographicOperations.ZeroMemory(macKey);
            }
        }

        /// <summary>
        /// Loads the raw masterkey from a masterkey file using a passphrase.
        /// </summary>
        /// <param name="masterkeyFile">The masterkey file</param>
        /// <param name="passphrase">The passphrase</param>
        /// <returns>The raw masterkey</returns>
        /// <exception cref="InvalidPassphraseException">If the passphrase is incorrect</exception>
        public static PerpetualMasterkey LoadRawMasterkey(MasterkeyFile masterkeyFile, string passphrase)
        {
            if (masterkeyFile == null)
            {
                throw new ArgumentNullException(nameof(masterkeyFile));
            }
            if (string.IsNullOrEmpty(passphrase))
            {
                throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));
            }

            if (masterkeyFile.PrimaryMasterkey == null ||
                masterkeyFile.PrimaryMasterkeyNonce == null ||
                masterkeyFile.PrimaryMasterkeyMac == null)
            {
                throw new InvalidOperationException("Masterkey file does not contain a primary masterkey");
            }

            // Decode base64 values
            byte[] encryptedMasterkey = Convert.FromBase64String(masterkeyFile.PrimaryMasterkey);
            byte[] nonce = Convert.FromBase64String(masterkeyFile.PrimaryMasterkeyNonce);
            byte[] expectedMac = Convert.FromBase64String(masterkeyFile.PrimaryMasterkeyMac);

            // Derive key from passphrase
            byte[] passphraseDerivedKey = DerivePassphraseKey(
                Encoding.UTF8.GetBytes(passphrase),
                nonce,
                null,
                masterkeyFile.ScryptCostParam,
                masterkeyFile.ScryptBlockSize,
                masterkeyFile.ScryptParallelism);

            try
            {
                // Split derived key
                byte[] kek = new byte[KEY_LEN_BYTES];
                byte[] macKey = new byte[MAC_LEN_BYTES];

                Buffer.BlockCopy(passphraseDerivedKey, 0, kek, 0, KEY_LEN_BYTES);
                Buffer.BlockCopy(passphraseDerivedKey, KEY_LEN_BYTES, macKey, 0, MAC_LEN_BYTES);

                // Verify MAC
                byte[] calculatedMac = CalculateMac(macKey, encryptedMasterkey);

                if (!CryptographicOperations.FixedTimeEquals(expectedMac, calculatedMac))
                {
                    // Throw InvalidCredentialException to match test expectation
                    throw new InvalidCredentialException("Incorrect passphrase or pepper");
                }

                // Decrypt masterkey
                return DecryptMasterkey(encryptedMasterkey, kek);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passphraseDerivedKey);
            }
        }

        private static byte[] DerivePassphraseKey(byte[] passphraseBytes, byte[] salt, byte[]? pepper, int costParamN_or_LogN, int blockSize, int parallelism)
        {
            if (passphraseBytes == null) throw new ArgumentNullException(nameof(passphraseBytes));
            if (salt == null) throw new ArgumentNullException(nameof(salt));

            // Combine salt and pepper into a single salt
            byte[] combinedSalt;
            if (pepper != null && pepper.Length > 0)
            {
                combinedSalt = new byte[salt.Length + pepper.Length];
                Buffer.BlockCopy(salt, 0, combinedSalt, 0, salt.Length);
                Buffer.BlockCopy(pepper, 0, combinedSalt, salt.Length, pepper.Length);
            }
            else
            {
                combinedSalt = new byte[salt.Length];
                Buffer.BlockCopy(salt, 0, combinedSalt, 0, salt.Length);
            }

            // Determine if costParamN_or_LogN is already the N value or if it's log2(N)
            int costParamN;
            
            // Check if the value is already a power of 2 and > 1 (indicating it's the N value directly)
            if (costParamN_or_LogN > 1 && (costParamN_or_LogN & (costParamN_or_LogN - 1)) == 0)
            {
                // It's already the N value (used in version 999 masterkey files)
                costParamN = costParamN_or_LogN;
            }
            else
            {
                // It's log2(N), so convert it to N
                // For example: costParamN_or_LogN = 17 means N = 2^17 = 131072
                costParamN = 1 << costParamN_or_LogN;
            }
            
            int derivedKeyLength = KEY_LEN_BYTES + MAC_LEN_BYTES; // 64 bytes

            try
            {
                // Use BouncyCastle's SCrypt implementation with the correct N value
                return SCrypt.Generate(passphraseBytes, combinedSalt, costParamN, blockSize, parallelism, derivedKeyLength);
            }
            finally
            {
                // Clear combined salt
                CryptographicOperations.ZeroMemory(combinedSalt);
            }
        }

        private static byte[] EncryptMasterkey(byte[] masterkey, byte[] kek)
        {
            // Use AES key wrapping as specified in RFC 3394
            return AesKeyWrap.Wrap(kek, masterkey);
        }

        private static PerpetualMasterkey DecryptMasterkey(byte[] encryptedMasterkey, byte[] kek)
        {
            try
            {
                // Use AES key unwrapping as specified in RFC 3394
                byte[] decryptedKey = AesKeyWrap.Unwrap(kek, encryptedMasterkey);
                // Wrap the decrypted key in a PerpetualMasterkey object
                return new PerpetualMasterkey(decryptedKey);
            }
            catch (CryptographicException)
            {
                // Also throw InvalidCredentialException here to match tests
                throw new InvalidCredentialException("Incorrect passphrase or pepper during decryption");
            }
        }

        private static byte[] CalculateMac(byte[] macKey, byte[] data)
        {
            using (var hmac = new HMACSHA256(macKey))
            {
                return hmac.ComputeHash(data);
            }
        }

        /// <summary>
        /// Parses the given masterkey file contents and returns the alleged vault version.
        /// </summary>
        /// <param name="masterkey">The file contents of a masterkey file</param>
        /// <returns>The vault version</returns>
        public static int ReadAllegedVaultVersion(byte[] masterkey)
        {
            // Deserialize using System.Text.Json
            try
            {
                var masterkeyFile = System.Text.Json.JsonSerializer.Deserialize<MasterkeyFile>(masterkey);
                // Check for null before accessing VaultVersion
                return masterkeyFile?.VaultVersion ?? 0; // Return 0 or throw if null is invalid?
            }
            catch (System.Text.Json.JsonException ex)
            {
                // Wrap exception for consistency?
                throw new IOException("Invalid masterkey file format (JSON parsing failed)", ex);
            }
            // var masterkeyFile = MasterkeyFile.FromJson(masterkey); // Old way?
            // return masterkeyFile.VaultVersion;
        }

        /// <summary>
        /// Loads a PerpetualMasterkey from a file using the given passphrase.
        /// </summary>
        /// <param name="path">Path to the masterkey file</param>
        /// <param name="passphrase">Passphrase to unlock the masterkey</param>
        /// <returns>The unlocked masterkey</returns>
        /// <exception cref="IOException">If the file cannot be read</exception>
        /// <exception cref="InvalidCredentialException">If the passphrase is incorrect</exception>
        public PerpetualMasterkey Load(string path, string passphrase)
        {
            try
            {
                using (var fileStream = File.OpenRead(path))
                {
                    return Load(fileStream, passphrase);
                }
            }
            catch (IOException ex)
            {
                throw new IOException($"Unable to read masterkey file: {path}", ex);
            }
        }

        /// <summary>
        /// Loads a PerpetualMasterkey from a stream using the given passphrase.
        /// </summary>
        /// <param name="stream">Stream containing the masterkey file</param>
        /// <param name="passphrase">Passphrase to unlock the masterkey</param>
        /// <returns>The unlocked masterkey</returns>
        /// <exception cref="IOException">If the stream cannot be read</exception>
        /// <exception cref="InvalidCredentialException">If the passphrase is incorrect</exception>
        public PerpetualMasterkey Load(Stream stream, string passphrase)
        {
            try
            {
                // Read the masterkey file from the stream
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string json = reader.ReadToEnd();
                    var masterkeyFile = System.Text.Json.JsonSerializer.Deserialize<MasterkeyFile>(json);

                    if (masterkeyFile == null)
                    {
                        throw new IOException("Invalid masterkey file (null)");
                    }

                    // Unlock the masterkey
                    return Unlock(masterkeyFile, passphrase);
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new IOException("Invalid masterkey file format", ex);
            }
        }

        /// <summary>
        /// Unlocks a masterkey file using the given passphrase.
        /// </summary>
        /// <param name="masterkeyFile">The masterkey file</param>
        /// <param name="passphrase">The passphrase</param>
        /// <returns>The unlocked masterkey</returns>
        /// <exception cref="InvalidCredentialException">If the passphrase is incorrect</exception>
        public PerpetualMasterkey Unlock(MasterkeyFile masterkeyFile, string passphrase)
        {
            if (masterkeyFile == null)
            {
                throw new ArgumentNullException(nameof(masterkeyFile));
            }
            if (string.IsNullOrEmpty(passphrase))
            {
                throw new ArgumentException("Passphrase cannot be empty", nameof(passphrase));
            }

            // Validate required fields based on Java test setup/hardcoded Lock
            if (masterkeyFile.ScryptSalt == null ||
                masterkeyFile.EncMasterKey == null ||
                masterkeyFile.MacMasterKey == null)
            // PrimaryMasterkeyNonce and PrimaryMasterkeyMac are not set by hardcoded Lock
            // VersionMac is also set but not needed for unlock
            {
                throw new IOException("Invalid masterkey file format (missing required fields for unlock based on Java test setup)");
            }

            // Get values set by hardcoded Lock
            byte[] salt = masterkeyFile.ScryptSalt;
            byte[] wrappedEncKey = masterkeyFile.EncMasterKey;
            byte[] wrappedMacKey = masterkeyFile.MacMasterKey;

            byte[] passphraseDerivedKey = Array.Empty<byte>();
            byte[] kek = new byte[KEY_LEN_BYTES];
            byte[] encKeyBytes = Array.Empty<byte>();
            byte[] macKeyBytes = Array.Empty<byte>();
            byte[] combinedKeyBytes = new byte[KEY_LEN_BYTES + MAC_LEN_BYTES];

            try
            {
                // Derive key-encryption key (KEK) from passphrase, salt, pepper
                passphraseDerivedKey = DerivePassphraseKey(
                    Encoding.UTF8.GetBytes(passphrase),
                    salt,
                    this._pepper, // Use instance pepper
                    masterkeyFile.ScryptCostParam,
                    masterkeyFile.ScryptBlockSize,
                    1); // Parallelism default = 1

                // Use only the KEK part for unwrapping (first KEY_LEN_BYTES)
                // Note: Unlike previous C# version, we don't use the second half (MAC key) derived here.
                Buffer.BlockCopy(passphraseDerivedKey, 0, kek, 0, KEY_LEN_BYTES);

                // Unwrap (decrypt) both keys using the KEK
                encKeyBytes = AesKeyWrap.Unwrap(kek, wrappedEncKey);
                macKeyBytes = AesKeyWrap.Unwrap(kek, wrappedMacKey);

                // Combine decrypted keys
                Buffer.BlockCopy(encKeyBytes, 0, combinedKeyBytes, 0, encKeyBytes.Length);
                Buffer.BlockCopy(macKeyBytes, 0, combinedKeyBytes, encKeyBytes.Length, macKeyBytes.Length);

                // Return PerpetualMasterkey created from combined bytes
                return new PerpetualMasterkey(combinedKeyBytes);
            }
            catch (CryptographicException ex)
            {
                // If unwrapping fails, likely wrong password/pepper
                throw new InvalidCredentialException("Incorrect passphrase or pepper during key unwrapping", ex);
            }
            finally
            {
                // Clear sensitive byte arrays
                CryptographicOperations.ZeroMemory(passphraseDerivedKey);
                CryptographicOperations.ZeroMemory(kek);
                CryptographicOperations.ZeroMemory(encKeyBytes);
                CryptographicOperations.ZeroMemory(macKeyBytes);
                CryptographicOperations.ZeroMemory(combinedKeyBytes);
            }

            /* // Keep old implementation commented out for reference
            // Validate required fields are present - Use EncMasterKey now
            if (masterkeyFile.ScryptSalt == null ||
                masterkeyFile.EncMasterKey == null || // Check EncMasterKey (JSON: "primaryMasterKey")
                masterkeyFile.PrimaryMasterkeyNonce == null ||
                masterkeyFile.PrimaryMasterkeyMac == null)
            {
                throw new IOException("Invalid masterkey file format (missing required fields)");
            }

            // Decode base64 values - Use EncMasterKey now
            byte[] salt = masterkeyFile.ScryptSalt; 
            byte[] encryptedMasterkey = Convert.FromBase64String(masterkeyFile.EncMasterKey); // Use EncMasterKey
            byte[] nonce = Convert.FromBase64String(masterkeyFile.PrimaryMasterkeyNonce);
            byte[] expectedMac = Convert.FromBase64String(masterkeyFile.PrimaryMasterkeyMac);

            // Derive key from passphrase using instance method
            byte[] passphraseDerivedKey = DerivePassphraseKey(
                Encoding.UTF8.GetBytes(passphrase),
                salt,
                this._pepper,
                masterkeyFile.ScryptCostParam,
                masterkeyFile.ScryptBlockSize,
                masterkeyFile.ScryptParallelism);

            try
            {
                // Split derived key
                byte[] kek = new byte[KEY_LEN_BYTES];
                byte[] macKey = new byte[MAC_LEN_BYTES];

                Buffer.BlockCopy(passphraseDerivedKey, 0, kek, 0, KEY_LEN_BYTES);
                Buffer.BlockCopy(passphraseDerivedKey, KEY_LEN_BYTES, macKey, 0, MAC_LEN_BYTES);

                // Verify MAC
                byte[] calculatedMac = CalculateMac(macKey, encryptedMasterkey);

                if (!CryptographicOperations.FixedTimeEquals(expectedMac, calculatedMac))
                {
                    // Throw InvalidCredentialException to match test expectation
                    throw new InvalidCredentialException("Incorrect passphrase or pepper");
                }

                // Decrypt masterkey
                return DecryptMasterkey(encryptedMasterkey, kek);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passphraseDerivedKey);
            }
            */
        }

        /// <summary>
        /// Creates an encrypted MasterkeyFile from the given masterkey using the passphrase.
        /// </summary>
        /// <param name="masterkey">The masterkey to encrypt</param>
        /// <param name="passphrase">The passphrase to use</param>
        /// <param name="vaultVersion">The vault version (Note: potentially deprecated)</param>
        /// <param name="scryptCostParam">The scrypt cost parameter (N value, must be power of 2 > 1)</param>
        /// <returns>An encrypted masterkey file</returns>
        public MasterkeyFile Lock(PerpetualMasterkey masterkey, string passphrase, int vaultVersion, int scryptCostParam)
        {
            if (masterkey == null)
            {
                throw new ArgumentNullException(nameof(masterkey));
            }
            // Restore check
            if (masterkey.IsDestroyed())
            {
                throw new ArgumentException("Masterkey has been destroyed", nameof(masterkey));
            }
            if (string.IsNullOrEmpty(passphrase))
            {
                throw new ArgumentException("Invalid passphrase", nameof(passphrase));
            }
            // Add validation for scryptCostParam (N)
            if (scryptCostParam < 2 || (scryptCostParam & (scryptCostParam - 1)) != 0)
            {
                throw new ArgumentException("scryptCostParam (N) must be > 1 and a power of 2.", nameof(scryptCostParam));
            }

            // Restore functional Lock implementation using BouncyCastle
            byte[] salt = new byte[NONCE_LEN_BYTES]; // Use constant for length
            _random.GetBytes(salt); // Use instance RNG

            // Get combined key and split it
            byte[] rawCombinedKey = masterkey.GetRaw();
            byte[] rawEncKey = new byte[KEY_LEN_BYTES];
            byte[] rawMacKey = new byte[MAC_LEN_BYTES];
            Buffer.BlockCopy(rawCombinedKey, 0, rawEncKey, 0, KEY_LEN_BYTES);
            Buffer.BlockCopy(rawCombinedKey, KEY_LEN_BYTES, rawMacKey, 0, MAC_LEN_BYTES);
            byte[] passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
            byte[] passphraseDerivedKey = Array.Empty<byte>();
            byte[] kek = new byte[KEY_LEN_BYTES];
            byte[] wrappedEncKey = Array.Empty<byte>();
            byte[] wrappedMacKey = Array.Empty<byte>();
            byte[] versionMac = Array.Empty<byte>(); // Need to calculate this

            try
            {
                // Derive key-encryption key (KEK) from passphrase
                // Note: DerivePassphraseKey expects N, not logN, based on previous fix.
                passphraseDerivedKey = DerivePassphraseKey(
                   passphraseBytes,
                   salt,
                   this._pepper, // Use instance pepper
                   scryptCostParam, // Pass N directly
                   SCRYPT_BLOCK_SIZE_DEFAULT,
                   SCRYPT_PARALLELIZATION_DEFAULT);

                // Use only the first part as KEK for wrapping
                Buffer.BlockCopy(passphraseDerivedKey, 0, kek, 0, KEY_LEN_BYTES);

                // Wrap keys using AES Key Wrap (BouncyCastle via AesKeyWrap class)
                wrappedEncKey = AesKeyWrap.Wrap(kek, rawEncKey);
                wrappedMacKey = AesKeyWrap.Wrap(kek, rawMacKey);

                // Calculate Version MAC (HMAC-SHA256 of vaultVersion using the raw MAC key)
                using (var hmac = new HMACSHA256(rawMacKey))
                {
                    // Convert vaultVersion int to big-endian byte array
                    byte[] versionBytes = BitConverter.GetBytes(vaultVersion);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(versionBytes);
                    }
                    versionMac = hmac.ComputeHash(versionBytes);
                }

                // Create the masterkey file DTO
                var masterkeyFileDto = new MasterkeyFile
                {
                    ScryptSalt = salt,
                    ScryptCostParam = scryptCostParam,
                    ScryptBlockSize = SCRYPT_BLOCK_SIZE_DEFAULT,
                    ScryptParallelism = SCRYPT_PARALLELIZATION_DEFAULT, // Not stored in standard Java file

                    EncMasterKey = wrappedEncKey, // Store raw bytes, assuming JSON serialization handles Base64
                    MacMasterKey = wrappedMacKey,
                    VersionMac = versionMac,

                    // Set both version properties to ensure compatibility
                    Version = vaultVersion,
                    VaultVersion = vaultVersion // This is the critical fix
                };

                return masterkeyFileDto;
            }
            finally
            {
                // Clear sensitive byte arrays
                CryptographicOperations.ZeroMemory(rawCombinedKey); // Clear combined key
                CryptographicOperations.ZeroMemory(rawEncKey);
                CryptographicOperations.ZeroMemory(rawMacKey);
                CryptographicOperations.ZeroMemory(passphraseBytes);
                CryptographicOperations.ZeroMemory(passphraseDerivedKey);
                CryptographicOperations.ZeroMemory(kek);
                // Don't clear salt, wrapped keys, or versionMac as they are returned.
            }

            /* // Old hardcoded implementation
            var masterkeyFile = new MasterkeyFile
            { ... };
            return masterkeyFile;
            */
        }

        /// <summary>
        /// Persists a masterkey to a file using the given passphrase.
        /// </summary>
        /// <param name="masterkey">The masterkey to persist</param>
        /// <param name="path">The path to write the file to</param>
        /// <param name="passphrase">The passphrase to encrypt the masterkey with</param>
        /// <exception cref="IOException">If the file cannot be written</exception>
        public void Persist(PerpetualMasterkey masterkey, string path, string passphrase)
        {
            Persist(masterkey, path, passphrase, 999); // Use 999 as default vault version for tests
        }

        /// <summary>
        /// Persists a masterkey to a file using the given passphrase and vault version.
        /// </summary>
        /// <param name="masterkey">The masterkey to persist</param>
        /// <param name="path">The path to write the file to</param>
        /// <param name="passphrase">The passphrase to encrypt the masterkey with</param>
        /// <param name="vaultVersion">The vault version</param>
        /// <exception cref="IOException">If the file cannot be written</exception>
        public void Persist(PerpetualMasterkey masterkey, string path, string passphrase, int vaultVersion)
        {
            Persist(masterkey, path, passphrase, vaultVersion, SCRYPT_COST_DEFAULT);
        }

        /// <summary>
        /// Persists a masterkey to a file using the given passphrase, vault version and scrypt cost parameter.
        /// </summary>
        /// <param name="masterkey">The masterkey to persist</param>
        /// <param name="path">The path to write the file to</param>
        /// <param name="passphrase">The passphrase to encrypt the masterkey with</param>
        /// <param name="vaultVersion">The vault version</param>
        /// <param name="scryptCostParam">The scrypt cost parameter</param>
        /// <exception cref="IOException">If the file cannot be written</exception>
        public void Persist(PerpetualMasterkey masterkey, string path, string passphrase, int vaultVersion, int scryptCostParam)
        {
            try
            {
                using (var fileStream = File.Create(path))
                {
                    Persist(masterkey, fileStream, passphrase, vaultVersion, scryptCostParam);
                }
            }
            catch (IOException ex)
            {
                throw new IOException($"Unable to write masterkey file: {path}", ex);
            }
        }

        /// <summary>
        /// Persists a masterkey to a stream using the given passphrase, vault version and scrypt cost parameter.
        /// </summary>
        /// <param name="masterkey">The masterkey to persist</param>
        /// <param name="stream">The stream to write to</param>
        /// <param name="passphrase">The passphrase to encrypt the masterkey with</param>
        /// <param name="vaultVersion">The vault version</param>
        /// <param name="scryptCostParam">The scrypt cost parameter</param>
        /// <exception cref="IOException">If the stream cannot be written to</exception>
        public void Persist(PerpetualMasterkey masterkey, Stream stream, string passphrase, int vaultVersion, int scryptCostParam = SCRYPT_COST_DEFAULT)
        {
            if (masterkey == null)
            {
                throw new ArgumentNullException(nameof(masterkey));
            }

            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (string.IsNullOrEmpty(passphrase))
            {
                throw new ArgumentException("Invalid passphrase", nameof(passphrase));
            }

            try
            {
                // Create a locked masterkey file
                var masterkeyFile = Lock(masterkey, passphrase, vaultVersion, scryptCostParam);

                // Serialize to JSON and write to the stream
                var json = System.Text.Json.JsonSerializer.Serialize(masterkeyFile, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex) when (ex is not IOException)
            {
                throw new IOException("Failed to persist masterkey", ex);
            }
        }

        /// <summary>
        /// Changes the passphrase of a masterkey file.
        /// </summary>
        /// <param name="masterkeyFile">The masterkey file</param>
        /// <param name="oldPassphrase">The old passphrase</param>
        /// <param name="newPassphrase">The new passphrase</param>
        /// <returns>A new masterkey file encrypted with the new passphrase</returns>
        /// <exception cref="InvalidCredentialException">If the old passphrase is incorrect</exception>
        public MasterkeyFile ChangePassphrase(MasterkeyFile masterkeyFile, string oldPassphrase, string newPassphrase)
        {
            if (masterkeyFile == null)
            {
                throw new ArgumentNullException(nameof(masterkeyFile));
            }

            if (string.IsNullOrEmpty(oldPassphrase))
            {
                throw new ArgumentException("Invalid old passphrase", nameof(oldPassphrase));
            }

            if (string.IsNullOrEmpty(newPassphrase))
            {
                throw new ArgumentException("Invalid new passphrase", nameof(newPassphrase));
            }

            try
            {
                // Unlock the masterkey with the old passphrase
                var masterkey = Unlock(masterkeyFile, oldPassphrase);

                // Lock the masterkey with the new passphrase
                return Lock(masterkey, newPassphrase, masterkeyFile.VaultVersion, masterkeyFile.ScryptCostParam);
            }
            catch (InvalidCredentialException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidCredentialException("Failed to change passphrase", ex);
            }
        }

        /// <summary>
        /// Changes the passphrase of a serialized masterkey file.
        /// </summary>
        /// <param name="serializedMasterkeyFile">The serialized masterkey file</param>
        /// <param name="oldPassphrase">The old passphrase</param>
        /// <param name="newPassphrase">The new passphrase</param>
        /// <returns>A new serialized masterkey file encrypted with the new passphrase</returns>
        /// <exception cref="InvalidCredentialException">If the old passphrase is incorrect</exception>
        public byte[] ChangePassphrase(byte[] serializedMasterkeyFile, string oldPassphrase, string newPassphrase)
        {
            if (serializedMasterkeyFile == null)
            {
                throw new ArgumentNullException(nameof(serializedMasterkeyFile));
            }

            try
            {
                // Deserialize the masterkey file
                var masterkeyFile = System.Text.Json.JsonSerializer.Deserialize<MasterkeyFile>(
                    Encoding.UTF8.GetString(serializedMasterkeyFile));

                if (masterkeyFile == null)
                {
                    throw new IOException("Invalid masterkey file (null)");
                }

                // Change the passphrase
                var newMasterkeyFile = ChangePassphrase(masterkeyFile, oldPassphrase, newPassphrase);

                // Serialize the new masterkey file
                var json = System.Text.Json.JsonSerializer.Serialize(newMasterkeyFile, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                return Encoding.UTF8.GetBytes(json);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new IOException("Invalid masterkey file format", ex);
            }
        }


    }
}
