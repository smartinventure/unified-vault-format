/*******************************************************************************
 * Copyright (c) 2016 Sebastian Stenzel and others 
 * Copyright (c) 2025 Smart In Venture GmbH for C# Porting
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the accompanying LICENSE.txt.
 *
 * Contributors:
 *     Sebastian Stenzel - initial API and implementation
 *     
 *     Smart In Venture GmbH - C# Porting (c) 2025
 *     
 *******************************************************************************/

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

using System.Security.Cryptography;
using UvfLib.Api;
using UvfLib.Common;
using UvfLib.VaultHelpers; // Added for VaultKeyHelper
using UvfLib.Jwe; // For JweVaultManager and UvfMasterkeyPayload
using System.IO; // For File operations
using System.Text; // For Encoding
using System.Text.Json; // For JsonSerializer
using System.Collections.Generic; // Added for Dictionary and List
using System.Linq; // Added for Linq operations if needed
using UvfLib.V3; // Added for UVFMasterkeyImpl constants if any, and HKDFHelper
using System.Buffers.Binary; // Added for BinaryPrimitives

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("UvfLib.Tests")]

namespace UvfLib
{
    /// <summary>
    /// Represents an unlocked Uvf vault and provides high-level access
    /// to its cryptographic operations.
    /// </summary>
    public sealed class Vault : IDisposable
    {
        private readonly Cryptor _cryptor;
        private readonly PerpetualMasterkey? _perpetualMasterkey; // For older formats or if UVFMasterkey can provide one
        private readonly RevolvingMasterkey _revolvingMasterkey; // Main masterkey for UVF
        private static readonly RandomNumberGenerator CsPrng = RandomNumberGenerator.Create(); // Static instance for loading
        private bool _disposed = false;

        /// <summary>
        /// Gets the file content cryptor.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the cryptor or file content cryptor is not available.</exception>
        public IFileContentCryptor FileContentCryptor
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Vault));
                if (_cryptor == null) throw new InvalidOperationException("Cryptor not initialized.");
                var fcCryptor = _cryptor.FileContentCryptor();
                if (fcCryptor == null) throw new InvalidOperationException("File content cryptor not available.");
                return fcCryptor;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Vault"/> class.
        /// Private constructor to force usage of static factory methods like Load.
        /// </summary>
        /// <param name="cryptor">The initialized cryptor for this vault.</param>
        /// <param name="masterkey">The underlying masterkey.</param>
        private Vault(Cryptor cryptor, PerpetualMasterkey masterkey)
        {
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _perpetualMasterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            // Always adapt PerpetualMasterkey to a RevolvingMasterkey (UVFMasterkeyImpl) for this constructor
            // The null second argument to UVFMasterkeyImpl for kdfSalt is a placeholder, review if it's appropriate for legacy adaptation.
            _revolvingMasterkey = new V3.UVFMasterkeyImpl(masterkey.GetRaw(), null); 
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Vault"/> class with both masterkey types.
        /// </summary>
        /// <param name="cryptor">The initialized cryptor for this vault.</param>
        /// <param name="masterkey">The perpetual masterkey.</param>
        /// <param name="revolvingMasterkey">The revolving masterkey used by the cryptor.</param>
        private Vault(Cryptor cryptor, PerpetualMasterkey masterkey, RevolvingMasterkey revolvingMasterkey)
        {
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _perpetualMasterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            _revolvingMasterkey = revolvingMasterkey ?? throw new ArgumentNullException(nameof(revolvingMasterkey));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Vault"/> class with only the revolving masterkey.
        /// </summary>
        /// <param name="cryptor">The initialized cryptor for this vault.</param>
        /// <param name="revolvingMasterkey">The revolving masterkey used by the cryptor.</param>
        private Vault(Cryptor cryptor, RevolvingMasterkey revolvingMasterkey)
        {
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _revolvingMasterkey = revolvingMasterkey ?? throw new ArgumentNullException(nameof(revolvingMasterkey));
            _perpetualMasterkey = null; // Or try to adapt if RevolvingMasterkey can provide a Perpetual variant
        }

        /// <summary>
        /// Releases all resources used by the Vault.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                // Dispose the masterkeys
                _perpetualMasterkey?.Dispose();
                
                if (_revolvingMasterkey != null && _revolvingMasterkey is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                // Dispose the cryptor if it implements IDisposable
                if (_cryptor is IDisposable cryptorDisposable)
                {
                    cryptorDisposable.Dispose();
                }

                _disposed = true;
            }
        }

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
            return CreateNewCryptomatorV8VaultFileContent(password, pepper);
        }

        /// <summary>
        /// Creates the encrypted JWE content (UTF-8 bytes) for a new UVF vault file.
        /// This is the content for "masterkey.cryptomator" or "vault.uvf" in the new format.
        /// </summary>
        /// <param name="password">The password for the new vault.</param>
        /// <returns>A byte array containing the encrypted UVF vault file data (JWE string as UTF-8 bytes).</returns>
        public static byte[] CreateNewUvfVaultFileContent(string password)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));

            using var rng = RandomNumberGenerator.Create();

            byte[] primaryEncryptionKey = new byte[32];
            rng.GetBytes(primaryEncryptionKey);
            byte[] primaryHmacKey = new byte[32];
            rng.GetBytes(primaryHmacKey);
            byte[] seedValue = new byte[32];
            rng.GetBytes(seedValue);
            int initialSeedId = 1;
            byte[] kdfSaltForSeeds = new byte[32];
            rng.GetBytes(kdfSaltForSeeds);
            byte[] rootDirIdContext = Encoding.ASCII.GetBytes("rootDirId");
            byte[] rootDirId = HKDF.DeriveKey(HashAlgorithmName.SHA512, seedValue, UvfLib.V3.Constants.DIR_ID_SIZE, kdfSaltForSeeds, rootDirIdContext);

            byte[] initialSeedIdBytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(initialSeedIdBytes, initialSeedId);

            var payload = new UvfMasterkeyPayload
            {
                UvfSpecVersion = 1,
                Keys = new List<PayloadKey>
                {
                    new PayloadKey 
                    {
                        Id = "1", 
                        Purpose = "org.cryptomator.masterkey", 
                        Alg = "AES-256-RAW", 
                        Value = Base64Url.Encode(primaryEncryptionKey)
                    },
                    new PayloadKey 
                    {
                        Id = "2", 
                        Purpose = "org.cryptomator.hmacMasterkey", 
                        Alg = "HMAC-SHA256-RAW", 
                        Value = Base64Url.Encode(primaryHmacKey)
                    }
                },
                Kdf = new PayloadKdf
                {
                    Type = "HKDF-SHA512",
                    Salt = Base64Url.Encode(kdfSaltForSeeds)
                },
                Seeds = new List<PayloadSeed>
                {
                    new PayloadSeed
                    {
                        Id = Base64Url.Encode(initialSeedIdBytes),
                        Value = Base64Url.Encode(seedValue),
                        Created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }
                },
                RootDirId = Base64Url.Encode(rootDirId)
            };

            string jweString = JweVaultManager.CreateVault(payload, password);
            return Encoding.UTF8.GetBytes(jweString);
        }

        /// <summary>
        /// Creates a new UVF vault file (e.g., masterkey.cryptomator or vault.uvf) at the specified path.
        /// </summary>
        /// <param name="filePath">The path where the UVF vault file will be created.</param>
        /// <param name="password">The password for the new vault.</param>
        public static void CreateNewUvfVault(string filePath, string password)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            byte[] uvfFileContent = CreateNewUvfVaultFileContent(password);
            File.WriteAllBytes(filePath, uvfFileContent);
        }

        /// <summary>
        /// Loads a UVF vault using its JWE-formatted key file content and password.
        /// </summary>
        /// <param name="uvfFileContent">The byte content of the UVF vault file (JWE string).</param>
        /// <param name="password">The vault password.</param>
        /// <returns>An initialized Vault instance ready for operations.</returns>
        public static Vault LoadUvfVault(byte[] uvfFileContent, string password)
        {
            if (uvfFileContent == null || uvfFileContent.Length == 0) throw new ArgumentNullException(nameof(uvfFileContent));
            if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));

            string jweString = Encoding.UTF8.GetString(uvfFileContent);
            UVFMasterkey? uvfMasterkey = null;
            Api.Cryptor? cryptor = null; 
            try
            {
                UvfMasterkeyPayload payload = JweVaultManager.LoadVaultPayload(jweString, password);
                
                // Instead of re-serializing, pass the payload object directly if UVFMasterkeyImpl can accept it.
                // For now, assuming FromDecryptedPayload expects JSON string as per current V3.UVFMasterkeyImpl.
                string jsonPayloadString = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                
                // Api.UVFMasterkey.FromDecryptedPayload is the entry point
                // This will internally create a V3.UVFMasterkeyImpl instance.
                uvfMasterkey = (UVFMasterkey)Api.UVFMasterkey.FromDecryptedPayload(jsonPayloadString);

                CryptorProvider provider = CryptorProvider.ForScheme(CryptorProvider.Scheme.UVF_DRAFT);
                using var csprng = RandomNumberGenerator.Create();
                cryptor = provider.Provide(uvfMasterkey, csprng);

                return new Vault(cryptor, uvfMasterkey);
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
        /// <param name="password">The vault password.</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        /// <returns>An initialized Vault instance ready for operations.</returns>
        /// <exception cref="ArgumentNullException">If key content or password is null.</exception>
        /// <exception cref="InvalidPassphraseException">If the password is incorrect.</exception>
        /// <exception cref="AuthenticationFailedException">If the master key file MAC is invalid.</exception>
        /// <exception cref="UnsupportedVaultFormatException">If the vault format is not supported.</exception>
        /// <exception cref="MasterkeyLoadingFailedException">For other key loading errors.</exception>
        /// <exception cref="CryptoException">For general cryptographic errors.</exception>
        public static Vault LoadCryptomatorV8Vault(byte[] encryptedKeyFileContent, string password, byte[]? pepper = null)
        {
            if (encryptedKeyFileContent == null) throw new ArgumentNullException(nameof(encryptedKeyFileContent));
            if (password == null) throw new ArgumentNullException(nameof(password));
            byte[] effectivePepper = pepper ?? Array.Empty<byte>();

            // This path uses MasterkeyFileAccess for the old format.
            MasterkeyFile masterkeyFile = MasterkeyFile.FromJson(encryptedKeyFileContent); 
            var keyAccessor = new MasterkeyFileAccess(effectivePepper, RandomNumberGenerator.Create());
            PerpetualMasterkey perpetualMasterkey = keyAccessor.Unlock(masterkeyFile, password);
            
            // For Cryptomator V8, we use the new CryptomatorV8 provider
            Api.Cryptor? cryptor = null;
            try 
            {
                CryptorProvider provider = CryptorProvider.ForScheme(CryptorProvider.Scheme.SIV_GCM);
                using var csprng = RandomNumberGenerator.Create();
                cryptor = provider.Provide(perpetualMasterkey, csprng);
                return new Vault(cryptor, perpetualMasterkey);
            }
            catch (Exception)
            {
                (cryptor as IDisposable)?.Dispose();
                perpetualMasterkey.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Changes the password for an existing JWE UVF vault file's content.
        /// </summary>
        /// <param name="encryptedUvfFileContent">The current byte content of the vault.uvf file.</param>
        /// <param name="oldPassword">The current vault password.</param>
        /// <param name="newPassword">The desired new vault password.</param>
        /// <returns>A byte array containing the newly encrypted vault.uvf file data.</returns>
        /// <exception cref="ArgumentNullException">If file content or passwords are null.</exception>
        /// <exception cref="InvalidPassphraseException">If the oldPassword is incorrect.</exception>
        public static byte[] ChangeUvfVaultPassword(byte[] encryptedUvfFileContent, string oldPassword, string newPassword)
        {
            // This reuses JweVaultManager which is correct.
            string oldJwe = Encoding.UTF8.GetString(encryptedUvfFileContent);
            UvfMasterkeyPayload payload = JweVaultManager.LoadVaultPayload(oldJwe, oldPassword); // Decrypt with old
            string newJwe = JweVaultManager.CreateVault(payload, newPassword); // Re-encrypt with new
            return Encoding.UTF8.GetBytes(newJwe);
        }

        /// <summary>
        /// Changes the password for an existing Cryptomator V8 vault file's content.
        /// </summary>
        /// <param name="encryptedKeyFileContent">The current byte content of the masterkey.cryptomator file.</param>
        /// <param name="oldPassword">The current vault password.</param>
        /// <param name="newPassword">The desired new vault password.</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        /// <returns>A byte array containing the newly encrypted masterkey.cryptomator file data.</returns>
        /// <exception cref="ArgumentNullException">If file content or passwords are null.</exception>
        /// <exception cref="InvalidPassphraseException">If the oldPassword is incorrect.</exception>
        /// <exception cref="AuthenticationFailedException">If the master key file MAC is invalid.</exception>
        /// <exception cref="CryptoException">If key operations fail.</exception>
        public static byte[] ChangeCryptomatorV8VaultPassword(byte[] encryptedKeyFileContent, string oldPassword, string newPassword, byte[]? pepper = null)
        {
            if (encryptedKeyFileContent == null) throw new ArgumentNullException(nameof(encryptedKeyFileContent));
            if (oldPassword == null) throw new ArgumentNullException(nameof(oldPassword));
            if (newPassword == null) throw new ArgumentNullException(nameof(newPassword));
            byte[] effectivePepper = pepper ?? Array.Empty<byte>();

            // Use MasterkeyFileAccess.ChangePassphrase method which handles the encryption/decryption
            var keyAccessor = new MasterkeyFileAccess(effectivePepper, RandomNumberGenerator.Create());
            return keyAccessor.ChangePassphrase(encryptedKeyFileContent, oldPassword, newPassword);
        }

        /// <summary>
        /// Creates the encrypted master key file content for a new Cryptomator V8 vault.
        /// </summary>
        /// <param name="password">The password for the new vault.</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        /// <returns>A byte array containing the encrypted master key file data.</returns>
        /// <exception cref="ArgumentNullException">If password is null.</exception>
        /// <exception cref="CryptoException">If key generation or encryption fails.</exception>
        public static byte[] CreateNewCryptomatorV8VaultFileContent(string password, byte[]? pepper = null)
        {
            // Delegate to VaultKeyHelper (this was previously CreateNewLegacyVaultKeyFileContent)
            return VaultKeyHelper.CreateNewVaultKeyFileContentInternal(password, pepper);
        }

        /// <summary>
        /// Creates a new Cryptomator V8 vault file (masterkey.cryptomator) at the specified path.
        /// </summary>
        /// <param name="filePath">The path where the vault file will be created.</param>
        /// <param name="password">The password for the new vault.</param>
        /// <param name="pepper">Optional pepper to use during key derivation. If null, an empty pepper is used.</param>
        public static void CreateNewCryptomatorV8Vault(string filePath, string password, byte[]? pepper = null)
        {
            if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException(nameof(filePath));
            byte[] vaultFileContent = CreateNewCryptomatorV8VaultFileContent(password, pepper);
            File.WriteAllBytes(filePath, vaultFileContent);
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
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            if (plaintextFilename == null) throw new ArgumentNullException(nameof(plaintextFilename));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");

            DirectoryMetadata rootMetadata = dirCryptor.RootDirectoryMetadata(); // This now returns metadata with empty children list
            Api.IDirectoryContentCryptor.Encrypting nameEncryptor = dirCryptor.FileNameEncryptor(rootMetadata);
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
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            if (encryptedFilename == null) throw new ArgumentNullException(nameof(encryptedFilename));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");

            DirectoryMetadata rootMetadata = dirCryptor.RootDirectoryMetadata();
            Api.IDirectoryContentCryptor.Decrypting nameDecryptor = dirCryptor.FileNameDecryptor(rootMetadata);
            return nameDecryptor.Decrypt(encryptedFilename);
        }

        /// <summary>
        /// Gets the encrypted directory path for the vault's root directory.
        /// </summary>
        /// <returns>The encrypted path (e.g., "d/XX/YYYY...").</returns>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public string GetRootDirectoryPath()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");

            DirectoryMetadata rootMetadata = dirCryptor.RootDirectoryMetadata();
            return dirCryptor.DirPath(rootMetadata);
        }

        /// <summary>
        /// Gets the DirectoryMetadata for the vault's root directory.
        /// </summary>
        /// <returns>The DirectoryMetadata for the root.</returns>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public DirectoryMetadata GetRootDirectoryMetadata()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");
            return dirCryptor.RootDirectoryMetadata();
        }

        /// <summary>
        /// Returns a Stream that encrypts data as it is written to the underlying output stream.
        /// Handles file header creation and chunk encryption automatically.
        /// </summary>
        /// <param name="outputStream">The stream to write the encrypted data (header + content) to.</param>
        /// <param name="leaveOpen">Whether to leave the underlying outputStream open when the encrypting stream is disposed.</param>
        /// <returns>A Stream wrapper that performs encryption.</returns>
        /// <exception cref="ArgumentNullException">If outputStream is null.</exception>
        /// <exception cref="ArgumentException">If outputStream is not writable.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public Stream GetEncryptingStream(Stream outputStream, bool leaveOpen = false)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            return VaultStreamHelper.GetEncryptingStreamInternal(_cryptor, outputStream, leaveOpen);
        }

        /// <summary>
        /// Returns a Stream that decrypts data as it is read from the underlying input stream.
        /// Handles file header reading/decryption and chunk decryption automatically.
        /// </summary>
        /// <param name="inputStream">The stream to read the encrypted data (header + content) from.</param>
        /// <param name="leaveOpen">Whether to leave the underlying inputStream open when the decrypting stream is disposed.</param>
        /// <returns>A Stream wrapper that performs decryption.</returns>
        /// <exception cref="ArgumentNullException">If inputStream is null.</exception>
        /// <exception cref="ArgumentException">If inputStream is not readable.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        /// <exception cref="InvalidCiphertextException">If the header or content ciphertext is invalid/corrupt.</exception>
        /// <exception cref="AuthenticationFailedException">If header or content authentication fails.</exception>
        public Stream GetDecryptingStream(Stream inputStream, bool leaveOpen = false)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            return VaultStreamHelper.GetDecryptingStreamInternal(_cryptor, inputStream, leaveOpen);
        }

        // --- Directory Metadata Operations ---

        /// <summary>
        /// Creates a new DirectoryMetadata object containing a unique directory ID.
        /// This object is needed before encrypting its content for a dir.uvf file.
        /// </summary>
        /// <returns>A new DirectoryMetadata instance.</returns>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public DirectoryMetadata CreateNewDirectoryMetadata()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");
            return dirCryptor.NewDirectoryMetadata(); // This now returns metadata with empty children list
        }

        /// <summary>
        /// Encrypts the given DirectoryMetadata.
        /// The result is the binary content to be written to a dir.uvf file.
        /// </summary>
        /// <param name="metadata">The directory metadata to encrypt.</param>
        /// <returns>The encrypted binary content for a dir.uvf file.</returns>
        /// <exception cref="ArgumentNullException">If metadata is null.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        /// <exception cref="CryptoException">If encryption fails.</exception>
        public byte[] EncryptDirectoryMetadata(DirectoryMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");
            // This will now use the V3.DirectoryContentCryptorImpl which serializes children to JSON
            return dirCryptor.EncryptDirectoryMetadata(metadata);
        }

        /// <summary>
        /// Decrypts directory metadata (dir.uvf content).
        /// </summary>
        /// <param name="encryptedMetadataBytes">The encrypted metadata bytes (full content of dir.uvf).</param>
        /// <returns>The decrypted directory metadata.</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails due to authentication issues.</exception>
        public DirectoryMetadata DecryptDirectoryMetadata(byte[] encryptedMetadataBytes)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            return ((Api.DirectoryContentCryptor)dirCryptor).DecryptDirectoryMetadata(encryptedMetadataBytes);
        }

        /// <summary>
        /// Decrypts directory metadata (dir.uvf content).
        /// </summary>
        /// <param name="encryptedMetadataBytes">The encrypted metadata bytes (full content of dir.uvf).</param>
        /// <param name="directorysOwnDirId">The Base64Url encoded DirId of the directory. (Not used in v3)</param>
        /// <returns>The decrypted directory metadata.</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails due to authentication issues.</exception>
        [Obsolete("Use DecryptDirectoryMetadata(byte[] encryptedMetadataBytes) instead. The dirId parameter is not used in v3.")]
        public DirectoryMetadata DecryptDirectoryMetadata(byte[] encryptedMetadataBytes, string directorysOwnDirId)
        {
            throw new NotSupportedException("Use DecryptDirectoryMetadata(byte[] encryptedMetadataBytes) instead. The dirId parameter is not used in v3.");
        }

        // --- Contextual Filename/Path Operations ---

        /// <summary>
        /// Encrypts a filename using the context of a specific directory.
        /// </summary>
        /// <param name="plaintextFilename">The original filename.</param>
        /// <param name="directoryMetadata">The DirectoryMetadata of the parent directory.</param>
        /// <returns>The encrypted filename (Base64URL encoded + .uvf extension).</returns>
        /// <exception cref="ArgumentNullException">If plaintextFilename or directoryMetadata is null.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        /// <exception cref="CryptoException">If encryption fails.</exception>
        public string EncryptFilename(string plaintextFilename, DirectoryMetadata directoryMetadata)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            return VaultDirectoryHelper.EncryptFilenameInternal(_cryptor, directoryMetadata, plaintextFilename);
        }

        /// <summary>
        /// Decrypts a filename using the context of a specific directory.
        /// </summary>
        /// <param name="encryptedFilename">The encrypted filename (including .uvf extension).</param>
        /// <param name="directoryMetadata">The DirectoryMetadata of the parent directory.</param>
        /// <returns>The original plaintext filename.</returns>
        /// <exception cref="ArgumentNullException">If encryptedFilename or directoryMetadata is null.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        /// <exception cref="ArgumentException">If the encryptedFilename format is invalid.</exception>
        /// <exception cref="AuthenticationFailedException">If the filename authentication fails.</exception>
        /// <exception cref="CryptoException">If decryption fails.</exception>
        public string DecryptFilename(string encryptedFilename, DirectoryMetadata directoryMetadata)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            return VaultDirectoryHelper.DecryptFilenameInternal(_cryptor, directoryMetadata, encryptedFilename);
        }

        /// <summary>
        /// Gets the encrypted directory path for a specific directory.
        /// </summary>
        /// <param name="directoryMetadata">The DirectoryMetadata of the directory.</param>
        /// <returns>The encrypted path (e.g., "d/XX/YYYY...").</returns>
        /// <exception cref="ArgumentNullException">If directoryMetadata is null.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public string GetDirectoryPath(DirectoryMetadata directoryMetadata)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            return VaultDirectoryHelper.GetDirectoryPathInternal(_cryptor, directoryMetadata);
        }

        /// <summary>
        /// Gets the physical vault path for a directory based on its DirId string (Base64Url encoded).
        /// Useful for finding a child directory's physical path when its DirId is known from parent metadata.
        /// </summary>
        /// <param name="dirIdBase64Url">The Base64Url encoded DirId of the directory.</param>
        /// <param name="seedId">The seed ID associated with this directory (e.g., from parent or vault default).</param>
        /// <returns>The relative physical path (e.g., "d/XX/YYYY...").</returns>
        public string GetDirectoryPathByDirIdString(string dirIdBase64Url, int seedId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(Vault));
            if (string.IsNullOrEmpty(dirIdBase64Url)) throw new ArgumentNullException(nameof(dirIdBase64Url));

            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");

            byte[] dirIdBytes = Base64Url.Decode(dirIdBase64Url);
            // Create a temporary DirectoryMetadata instance to pass to the existing DirPath method.
            // The children list can be empty as it's not used by DirPath itself.
            var tempMetadata = new V3.DirectoryMetadataImpl(seedId, dirIdBytes); 
            return dirCryptor.DirPath(tempMetadata);
        }

        /// <summary>
        /// Gets the vault-specific physical path for a directory based on its Base64Url encoded DirId.
        /// Example: "d/AB/CDEFG..."
        /// </summary>
        /// <param name="dirIdBase64Url">The Base64Url encoded DirId of the directory.</param>
        /// <returns>The relative physical path within the vault.</returns>
        /// <exception cref="ArgumentNullException">If dirIdBase64Url is null.</exception>
        /// <exception cref="ArgumentException">If dirIdBase64Url is empty or invalid Base64Url, or if the DirId length is incorrect after decoding.</exception>
        public string GetDirectoryPathByDirId(string dirIdBase64Url)
        {
            if (string.IsNullOrEmpty(dirIdBase64Url))
            {
                throw new ArgumentException("DirId (Base64Url) cannot be null or empty.", nameof(dirIdBase64Url));
            }

            byte[] dirIdBytes;
            try
            {
                dirIdBytes = Base64Url.Decode(dirIdBase64Url);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("Invalid Base64Url format for DirId.", nameof(dirIdBase64Url), ex);
            }

            if (dirIdBytes.Length != UvfLib.V3.Constants.DIR_ID_SIZE)
            {
                throw new ArgumentException($"Decoded DirId must be {UvfLib.V3.Constants.DIR_ID_SIZE} bytes long.", nameof(dirIdBase64Url));
            }

            FileNameCryptor fileNameCryptor = _cryptor.FileNameCryptor(_revolvingMasterkey.GetCurrentRevision());
            if (!(fileNameCryptor is FileNameCryptorImpl fileNameCryptorImpl))
            {
                throw new InvalidOperationException("Unable to get FileNameCryptorImpl instance for hashing DirId.");
            }
            string hashedDirId = fileNameCryptorImpl.HashDirectoryId(dirIdBytes);
            return UvfLib.V3.Constants.VAULT_DIR_PREFIX + hashedDirId.Substring(0, 2) + "/" + hashedDirId.Substring(2);
        }

        /// <summary>
        /// Calculates the expected size of an encrypted file based on its original size.
        /// </summary>
        /// <param name="sourceFileSize">The size of the source file in bytes</param>
        /// <returns>The expected size of the encrypted file in bytes</returns>
        public static long CalculateExpectedEncryptedSize(long sourceFileSize)
        {
            return VaultFileHelper.CalculateExpectedEncryptedSize(sourceFileSize);
        }

        /// <summary>
        /// Calculates the expected size of a decrypted file based on its encrypted size.
        /// </summary>
        /// <param name="encryptedFileSize">The size of the encrypted file in bytes</param>
        /// <returns>The expected size of the decrypted file in bytes</returns>
        /// <exception cref="ArgumentException">Thrown when the encrypted file size is invalid</exception>
        public static long CalculateExpectedDecryptedSize(long encryptedFileSize)
        {
            return VaultFileHelper.CalculateExpectedDecryptedSize(encryptedFileSize);
        }

        /// <summary>
        /// Provides access to the underlying Cryptor instance.
        /// Primarily for advanced use cases or when direct access to cryptor sub-components is needed.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the cryptor is not initialized.</exception>
        /// <exception cref="ObjectDisposedException">If the Vault has been disposed.</exception>
        internal Cryptor Cryptor // Made internal for helpers like VaultStreamHelper
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Vault));
                if (_cryptor == null) throw new InvalidOperationException("Cryptor not initialized.");
                return _cryptor;
            }
        }

        // These explicit interface methods are now less relevant if using the contextual encryptor/decryptor above.
        // They could be removed or marked obsolete if the contextual approach is preferred.
        public string EncryptFilename(string cleartextName, string directoryId)
        {
            throw new NotSupportedException("Use contextual FileNameEncryptor obtained via DirectoryMetadata.");
        }

    }
}