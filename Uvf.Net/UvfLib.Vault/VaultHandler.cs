// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("UvfLib.Tests")]

namespace UvfLib.Vault
{
    /// <summary>
    /// Main entry point for vault operations.
    /// Provides high-level methods for creating, loading, and managing encrypted vaults.
    /// Handles both UVF and Cryptomator V8 vault formats.
    /// </summary>
    public sealed partial class VaultHandler : IDisposable
    {
        private readonly UvfLib.Core.Api.Cryptor _cryptor;
        private readonly UvfLib.Core.Common.PerpetualMasterkey? _perpetualMasterkey; // For older formats or if UVFMasterkey can provide one
        private UvfLib.Core.Api.RevolvingMasterkey _revolvingMasterkey; // Main masterkey for UVF - made non-readonly for key rotation
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
                if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
                if (_cryptor == null) throw new InvalidOperationException("Cryptor not initialized.");
                var fcCryptor = _cryptor.FileContentCryptor();
                if (fcCryptor == null) throw new InvalidOperationException("File content cryptor not available.");
                return fcCryptor;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VaultHandler"/> class.
        /// Private constructor to force usage of static factory methods like Load.
        /// </summary>
        /// <param name="cryptor">The initialized cryptor for this vault.</param>
        /// <param name="masterkey">The underlying masterkey.</param>
        private VaultHandler(UvfLib.Core.Api.Cryptor cryptor, UvfLib.Core.Common.PerpetualMasterkey masterkey)
        {
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _perpetualMasterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            // Always adapt PerpetualMasterkey to a RevolvingMasterkey (UVFMasterkeyImpl) for this constructor
            // The null second argument to UVFMasterkeyImpl for kdfSalt is a placeholder, review if it's appropriate for legacy adaptation.
            _revolvingMasterkey = new UvfLib.Core.V3.UVFMasterkeyImpl(masterkey.GetRaw(), null);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VaultHandler"/> class with both masterkey types.
        /// </summary>
        /// <param name="cryptor">The initialized cryptor for this vault.</param>
        /// <param name="masterkey">The perpetual masterkey.</param>
        /// <param name="revolvingMasterkey">The revolving masterkey used by the cryptor.</param>
        private VaultHandler(UvfLib.Core.Api.Cryptor cryptor, UvfLib.Core.Common.PerpetualMasterkey masterkey, UvfLib.Core.Api.RevolvingMasterkey revolvingMasterkey)
        {
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
            _perpetualMasterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            _revolvingMasterkey = revolvingMasterkey ?? throw new ArgumentNullException(nameof(revolvingMasterkey));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VaultHandler"/> class with only the revolving masterkey.
        /// </summary>
        /// <param name="cryptor">The initialized cryptor for this vault.</param>
        /// <param name="revolvingMasterkey">The revolving masterkey used by the cryptor.</param>
        private VaultHandler(UvfLib.Core.Api.Cryptor cryptor, UvfLib.Core.Api.RevolvingMasterkey revolvingMasterkey)
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
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            return new VaultHelpers.EncryptingStream(_cryptor, outputStream, leaveOpen);
        }

        public Stream GetEncryptingStreamWithExistingHeader(Stream outputStream, bool leaveOpen = false)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            if (!outputStream.CanWrite) throw new ArgumentException("Output stream must be writable", nameof(outputStream));

            // CRITICAL FIX: Always read header from the beginning of the stream
            long originalPosition = outputStream.CanSeek ? outputStream.Position : 0;
            if (outputStream.CanSeek)
            {
                outputStream.Position = 0;
            }

            // Read the existing header from the stream
            byte[] encryptedHeader = new byte[UvfLib.Core.V3.FileHeaderImpl.SIZE];
            int bytesRead = outputStream.Read(encryptedHeader, 0, encryptedHeader.Length);
            if (bytesRead < encryptedHeader.Length)
            {
                throw new InvalidOperationException("Stream ended before header could be fully read.");
            }
            
            var existingHeader = _cryptor.FileHeaderCryptor().DecryptHeader(encryptedHeader);
            
            // CRITICAL FIX: Restore original position after reading header
            // This ensures WriteOnly streams continue from their original position
            if (outputStream.CanSeek)
            {
                outputStream.Position = originalPosition;
            }
            
            // Create encrypting stream with existing header to preserve the header nonce
            return new VaultHelpers.EncryptingStream(_cryptor, outputStream, leaveOpen, existingHeader);
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
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            return new VaultHelpers.DecryptingStream(_cryptor, inputStream, leaveOpen);
        }

        /// <summary>
        /// Opens an encrypted file for reading and returns a stream that decrypts data automatically.
        /// </summary>
        /// <param name="encryptedFilePath">Path to the encrypted file in the vault</param>
        /// <returns>A stream for reading decrypted content</returns>
        /// <exception cref="ArgumentNullException">If encryptedFilePath is null</exception>
        /// <exception cref="FileNotFoundException">If the encrypted file doesn't exist</exception>
        /// <exception cref="ObjectDisposedException">If the vault has been disposed</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly</exception>
        public Stream GetReadStream(string encryptedFilePath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (string.IsNullOrEmpty(encryptedFilePath)) throw new ArgumentNullException(nameof(encryptedFilePath));
            
            if (!File.Exists(encryptedFilePath))
            {
                throw new FileNotFoundException($"Encrypted file not found: {encryptedFilePath}");
            }

            var fileStream = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return VaultHelpers.VaultStreamHelper.GetDecryptingStreamInternal(_cryptor, fileStream, leaveOpen: false);
        }

        /// <summary>
        /// Creates or overwrites an encrypted file and returns a stream that encrypts data automatically.
        /// </summary>
        /// <param name="encryptedFilePath">Path where the encrypted file will be created/overwritten</param>
        /// <returns>A stream for writing content that gets encrypted</returns>
        /// <exception cref="ArgumentNullException">If encryptedFilePath is null</exception>
        /// <exception cref="ObjectDisposedException">If the vault has been disposed</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly</exception>
        public Stream GetWriteStream(string encryptedFilePath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (string.IsNullOrEmpty(encryptedFilePath)) throw new ArgumentNullException(nameof(encryptedFilePath));
            
            // Ensure the directory exists
            var directory = Path.GetDirectoryName(encryptedFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileStream = new FileStream(encryptedFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            return VaultHelpers.VaultStreamHelper.GetEncryptingStreamInternal(_cryptor, fileStream, leaveOpen: false);
        }

        // --- Directory Metadata Operations ---

        /// <summary>
        /// Creates a new DirectoryMetadata object containing a unique directory ID.
        /// This object is needed before encrypting its content for a dir.uvf file.
        /// </summary>
        /// <returns>A new DirectoryMetadata instance.</returns>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public UvfLib.Core.Api.DirectoryMetadata CreateNewDirectoryMetadata()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");
            return dirCryptor.NewDirectoryMetadata();
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
        public byte[] EncryptDirectoryMetadata(UvfLib.Core.Api.DirectoryMetadata metadata)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");
            return dirCryptor.EncryptDirectoryMetadata(metadata);
        }

        /// <summary>
        /// Decrypts directory metadata (dir.uvf content).
        /// </summary>
        /// <param name="encryptedMetadataBytes">The encrypted metadata bytes (full content of dir.uvf).</param>
        /// <returns>The decrypted directory metadata.</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails due to authentication issues.</exception>
        public UvfLib.Core.Api.DirectoryMetadata DecryptDirectoryMetadata(byte[] encryptedMetadataBytes)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            var dirCryptor = _cryptor.DirectoryContentCryptor();
            return ((UvfLib.Core.Api.DirectoryContentCryptor)dirCryptor).DecryptDirectoryMetadata(encryptedMetadataBytes);
        }

        /// <summary>
        /// Decrypts directory metadata (dir.uvf content).
        /// </summary>
        /// <param name="encryptedMetadataBytes">The encrypted metadata bytes (full content of dir.uvf).</param>
        /// <param name="directorysOwnDirId">The Base64Url encoded DirId of the directory. (Not used in v3)</param>
        /// <returns>The decrypted directory metadata.</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails due to authentication issues.</exception>
        [Obsolete("Use DecryptDirectoryMetadata(byte[] encryptedMetadataBytes) instead. The dirId parameter is not used in v3.")]
        public UvfLib.Core.Api.DirectoryMetadata DecryptDirectoryMetadata(byte[] encryptedMetadataBytes, string directorysOwnDirId)
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
        public string EncryptFilename(string plaintextFilename, UvfLib.Core.Api.DirectoryMetadata directoryMetadata)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
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
        public string DecryptFilename(string encryptedFilename, UvfLib.Core.Api.DirectoryMetadata directoryMetadata)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            return VaultDirectoryHelper.DecryptFilenameInternal(_cryptor, directoryMetadata, encryptedFilename);
        }

        /// <summary>
        /// Gets the encrypted directory path for a specific directory.
        /// </summary>
        /// <param name="directoryMetadata">The DirectoryMetadata of the directory.</param>
        /// <returns>The encrypted path (e.g., "d/XX/YYYY...").</returns>
        /// <exception cref="ArgumentNullException">If directoryMetadata is null.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public string GetDirectoryPath(UvfLib.Core.Api.DirectoryMetadata directoryMetadata)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
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
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (string.IsNullOrEmpty(dirIdBase64Url)) throw new ArgumentNullException(nameof(dirIdBase64Url));

            var dirCryptor = _cryptor.DirectoryContentCryptor();
            if (dirCryptor == null) throw new InvalidOperationException("Directory cryptor not available.");

            byte[] dirIdBytes = Base64Url.Decode(dirIdBase64Url);
            // Create a temporary DirectoryMetadata instance to pass to the existing DirPath method.
            // The children list can be empty as it's not used by DirPath itself.
            // Use the factory method instead of the internal constructor
            var tempMetadata = dirCryptor.NewDirectoryMetadata(); // Use factory method
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

            if (dirIdBytes.Length != UvfLib.Core.V3.Constants.DIR_ID_SIZE)
            {
                throw new ArgumentException($"Decoded DirId must be {UvfLib.Core.V3.Constants.DIR_ID_SIZE} bytes long.", nameof(dirIdBase64Url));
            }

            FileNameCryptor fileNameCryptor = _cryptor.FileNameCryptor(_revolvingMasterkey.GetCurrentRevision());
            if (!(fileNameCryptor is FileNameCryptorImpl fileNameCryptorImpl))
            {
                throw new InvalidOperationException("Unable to get FileNameCryptorImpl instance for hashing DirId.");
            }
            string hashedDirId = fileNameCryptorImpl.HashDirectoryId(dirIdBytes);
            return UvfLib.Core.V3.Constants.VAULT_DIR_PREFIX + hashedDirId.Substring(0, 2) + "/" + hashedDirId.Substring(2);
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
        internal UvfLib.Core.Api.Cryptor Cryptor // Made internal for helpers like VaultStreamHelper
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
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

        // --- Key Rotation Methods ---

        /// <summary>
        /// Rotates the encryption keys for a UVF vault by adding a new seed.
        /// This improves security by ensuring forward secrecy - files encrypted with new seeds
        /// cannot be decrypted even if older seeds are compromised.
        /// </summary>
        /// <param name="encryptedUvfFileContent">The current byte content of the vault.uvf file.</param>
        /// <param name="passwordBytes">Password as UTF-8 encoded bytes</param>
        /// <returns>A byte array containing the vault.uvf file with the new seed added.</returns>
        /// <exception cref="ArgumentNullException">If file content or password is null.</exception>
        /// <exception cref="InvalidPassphraseException">If the password is incorrect.</exception>
        /// <exception cref="AuthenticationFailedException">If authentication fails during decryption.</exception>
        /// <exception cref="CryptoException">If key generation or encryption fails.</exception>
        public static byte[] RotateUvfVaultKey(byte[] encryptedUvfFileContent, byte[] passwordBytes)
        {
            if (encryptedUvfFileContent == null) throw new ArgumentNullException(nameof(encryptedUvfFileContent));
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));

            try
            {
                // 1. Load the current vault payload
                string jweString = Encoding.UTF8.GetString(encryptedUvfFileContent);
                UvfMasterkeyPayload currentPayload = MultiUserJweVaultManager.LoadSingleUserVault(jweString, passwordBytes);

                // 2. Generate a new seed
                byte[] newSeedValue = new byte[32]; // 256-bit seed
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(newSeedValue);
                }

                // 3. Find the highest seed ID and increment it
                int maxSeedId = 0;
                if (currentPayload.Seeds != null && currentPayload.Seeds.Any())
                {
                    foreach (var seed in currentPayload.Seeds)
                    {
                        if (!string.IsNullOrEmpty(seed.Id))
                        {
                            try
                            {
                                // Decode the Base64Url seed ID to get the integer value
                                byte[] seedIdBytes = Base64Url.Decode(seed.Id);
                                if (seedIdBytes.Length >= 4)
                                {
                                    int seedId = BinaryPrimitives.ReadInt32BigEndian(seedIdBytes);
                                    if (seedId > maxSeedId)
                                    {
                                        maxSeedId = seedId;
                                    }
                                }
                            }
                            catch
                            {
                                // Skip invalid seed IDs
                                continue;
                            }
                        }
                    }
                }

                int newSeedId = maxSeedId + 1;

                // 4. Create new seed entry
                byte[] newSeedIdBytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(newSeedIdBytes, newSeedId);

                var newSeed = new PayloadSeed
                {
                    Id = Base64Url.Encode(newSeedIdBytes),
                    Value = Base64Url.Encode(newSeedValue),
                    Created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                // 5. Create updated payload with new seed
                var updatedPayload = new UvfMasterkeyPayload
                {
                    UvfSpecVersion = currentPayload.UvfSpecVersion,
                    Keys = currentPayload.Keys, // Keep existing keys
                    Kdf = currentPayload.Kdf,   // Keep existing KDF settings
                    Seeds = new List<PayloadSeed>(currentPayload.Seeds ?? new List<PayloadSeed>()),
                    RootDirId = currentPayload.RootDirId // Keep existing root directory ID
                };

                // Add the new seed
                updatedPayload.Seeds.Add(newSeed);

                // 6. Re-encrypt the vault with the updated payload
                string rotatedJweString = MultiUserJweVaultManager.CreateSingleUserVault(updatedPayload, passwordBytes);
                byte[] rotatedVaultContent = Encoding.UTF8.GetBytes(rotatedJweString);

                // 7. Clean up sensitive data
                CryptoOps.ZeroMemory(newSeedValue);

                return rotatedVaultContent;
            }
            catch (Exception ex) when (!(ex is ArgumentNullException || ex is InvalidPassphraseException || 
                                       ex is AuthenticationFailedException || ex is CryptoException))
            {
                throw new CryptoException("Key rotation failed", ex);
            }
        }

        /// <summary>
        /// Rotates the encryption keys for this vault instance by adding a new seed.
        /// After rotation, new files and directories will use the new seed for encryption,
        /// providing forward secrecy protection.
        /// 
        /// Note: This method updates the in-memory vault instance. To persist the changes
        /// to disk, the vault file would need to be re-saved separately.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the vault format doesn't support key rotation or vault is not properly initialized.</exception>
        /// <exception cref="CryptoException">If key generation fails.</exception>
        /// <exception cref="ObjectDisposedException">If the vault has been disposed.</exception>
        public void RotateVaultKey()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));

            // Check if we have a revolving masterkey (UVF format)
            if (_revolvingMasterkey == null)
            {
                throw new InvalidOperationException("Key rotation is only supported for UVF format vaults with revolving masterkeys.");
            }

            if (!(_revolvingMasterkey is UVFMasterkey uvfMasterkey))
            {
                throw new InvalidOperationException("Vault does not contain a UVF masterkey that supports rotation.");
            }

            try
            {
                // Generate new seed
                byte[] newSeedValue = new byte[32]; // 256-bit seed
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(newSeedValue);
                }

                // Generate new seed ID (increment the latest)
                int newSeedId = uvfMasterkey.LatestSeed + 1;

                // Create updated seed collection
                var updatedSeeds = new Dictionary<int, byte[]>(uvfMasterkey.Seeds);
                updatedSeeds[newSeedId] = newSeedValue;

                // Create new masterkey with rotated seeds
                var rotatedMasterkey = new UvfLib.Core.V3.UVFMasterkeyImpl(
                    updatedSeeds,
                    uvfMasterkey.KdfSalt,
                    uvfMasterkey.InitialSeed,
                    newSeedId  // Update latest seed to the new one
                );

                // Dispose old masterkey
                if (_revolvingMasterkey is IDisposable disposableOld)
                {
                    disposableOld.Dispose();
                }

                // Update the vault's masterkey
                _revolvingMasterkey = rotatedMasterkey;

                // Clean up sensitive data
                CryptoOps.ZeroMemory(newSeedValue);
            }
            catch (Exception ex)
            {
                throw new CryptoException("Key rotation failed", ex);
            }
        }

        /// <summary>
        /// Gets the current seed ID for the vault.
        /// </summary>
        /// <returns>The current seed ID</returns>
        public int GetCurrentSeedId()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            
            if (_revolvingMasterkey is UvfLib.Core.V3.UVFMasterkeyImpl uvfMasterkey)
            {
                return uvfMasterkey.LatestSeed;
            }
            
            // For legacy formats, return a default seed ID
            return 0;
        }

        /// <summary>
        /// Checks if this vault is using Cryptomator v8 format.
        /// </summary>
        /// <returns>True if this is a Cryptomator v8 vault, false otherwise</returns>
        public bool IsCryptomatorV8()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (_cryptor?.DirectoryContentCryptor() == null) return false;
            return _cryptor.DirectoryContentCryptor().GetType().FullName?.Contains("CryptomatorV8") == true;
        }

        /// <summary>
        /// Gets the directory metadata filename for this vault format and directory type.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata (optional, used to determine if it's root directory)</param>
        /// <returns>The directory metadata filename</returns>
        public string GetDirectoryMetadataFilename(UvfLib.Core.Api.DirectoryMetadata directoryMetadata = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            return VaultDirectoryHelper.GetDirectoryMetadataFilename(_cryptor, directoryMetadata);
        }

        /// <summary>
        /// Gets the directory metadata filename for this vault format.
        /// For backwards compatibility, this assumes non-root directory for Cryptomator v8.
        /// </summary>
        /// <returns>The directory metadata filename</returns>
        public string GetDirectoryMetadataFilename()
        {
            return GetDirectoryMetadataFilename(null);
        }

        /// <summary>
        /// Gets the available seed IDs for the vault.
        /// </summary>
        /// <returns>An enumerable of available seed IDs</returns>
        public IEnumerable<int> GetAvailableSeedIds()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));

            if (_revolvingMasterkey is UVFMasterkey uvfMasterkey)
            {
                return uvfMasterkey.Seeds.Keys.ToList(); // Return a copy to prevent modification
            }

            throw new InvalidOperationException("Seed IDs are only available for UVF vaults.");
        }

        /// <summary>
        /// Gets the directory path for a CryptomatorV8 vault based on a UUID string.
        /// This method converts a UUID string to the corresponding directory path in the vault.
        /// </summary>
        /// <param name="uuidString">The UUID string (e.g., "12345678-1234-1234-1234-123456789abc")</param>
        /// <returns>The directory path (e.g., "d/XX/YYYYYYYY")</returns>
        /// <exception cref="ArgumentNullException">If uuidString is null</exception>
        /// <exception cref="ArgumentException">If uuidString is invalid</exception>
        /// <exception cref="InvalidOperationException">If this is not a CryptomatorV8 vault</exception>
        public string GetCryptomatorV8DirectoryPathByUuid(string uuidString)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (string.IsNullOrEmpty(uuidString)) throw new ArgumentNullException(nameof(uuidString));
            if (!IsCryptomatorV8()) throw new InvalidOperationException("This method is only available for CryptomatorV8 vaults");

            // Convert UUID string to bytes for DirectoryMetadata
            // CryptomatorV8 DirectoryMetadataImpl stores DirId as byte[] internally
            byte[] uuidBytes = System.Text.Encoding.ASCII.GetBytes(uuidString);
            
            // Create DirectoryMetadata directly instead of using reflection (AOT-compatible)
            var coreMetadata = new UvfLib.Core.CryptomatorV8.DirectoryMetadataImpl(uuidBytes);

            // Use the existing directory path calculation
            return VaultDirectoryHelper.GetDirectoryPathInternal(_cryptor, coreMetadata);
        }

        /// <summary>
        /// Creates a DirectoryMetadata instance from a UUID string for CryptomatorV8 vaults.
        /// This method creates metadata that can be used for directory operations.
        /// </summary>
        /// <param name="uuidString">The UUID string (e.g., "12345678-1234-1234-1234-123456789abc")</param>
        /// <returns>DirectoryMetadata instance for the given UUID</returns>
        /// <exception cref="ArgumentNullException">If uuidString is null</exception>
        /// <exception cref="ArgumentException">If uuidString is invalid</exception>
        /// <exception cref="InvalidOperationException">If this is not a CryptomatorV8 vault</exception>
        public UvfLib.Core.Api.DirectoryMetadata CreateCryptomatorV8DirectoryMetadataFromUuid(string uuidString)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (string.IsNullOrEmpty(uuidString)) throw new ArgumentNullException(nameof(uuidString));
            if (!IsCryptomatorV8()) throw new InvalidOperationException("This method is only available for CryptomatorV8 vaults");

            // Convert UUID string to bytes for DirectoryMetadata
            // CryptomatorV8 DirectoryMetadataImpl stores DirId as byte[] internally
            byte[] uuidBytes = System.Text.Encoding.ASCII.GetBytes(uuidString);

            // Create DirectoryMetadata directly instead of using reflection (AOT-compatible)
            return new UvfLib.Core.CryptomatorV8.DirectoryMetadataImpl(uuidBytes);
        }

        // --- Symlink Operations (UVF only) ---

        /// <summary>
        /// Encrypts a symlink target for storage in a symlink.uvf file.
        /// The target is encrypted as file content using the file content cryptor.
        /// </summary>
        /// <param name="symlinkTarget">The plaintext symlink target path</param>
        /// <returns>Encrypted bytes suitable for writing to symlink.uvf</returns>
        /// <exception cref="ArgumentNullException">If symlinkTarget is null</exception>
        /// <exception cref="InvalidOperationException">If this is not a UVF vault or the cryptor is not available</exception>
        /// <exception cref="CryptoException">If encryption fails</exception>
        public byte[] EncryptSymlinkTarget(string symlinkTarget)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (symlinkTarget == null) throw new ArgumentNullException(nameof(symlinkTarget));
            if (IsCryptomatorV8()) throw new InvalidOperationException("Symlinks are only supported in UVF format");

            var fileContentCryptor = _cryptor.FileContentCryptor();
            var fileHeaderCryptor = _cryptor.FileHeaderCryptor();
            if (fileContentCryptor == null) throw new InvalidOperationException("File content cryptor not available.");
            if (fileHeaderCryptor == null) throw new InvalidOperationException("File header cryptor not available.");

            try
            {
                // Convert symlink target to UTF-8 bytes (Normalization Form C as per UVF spec)
                byte[] targetBytes = System.Text.Encoding.UTF8.GetBytes(symlinkTarget.Normalize(System.Text.NormalizationForm.FormC));
                
                // Create file header
                var fileHeader = fileHeaderCryptor.Create();
                
                // Encrypt the header
                Memory<byte> encryptedHeaderMemory = fileHeaderCryptor.EncryptHeader(fileHeader);
                
                // Encrypt the symlink target content in chunks
                using var outputStream = new MemoryStream();
                
                // Write encrypted header first
                outputStream.Write(encryptedHeaderMemory.Span);
                
                // Encrypt content in chunks
                int cleartextChunkSize = fileContentCryptor.CleartextChunkSize();
                long chunkNumber = 0;
                
                for (int offset = 0; offset < targetBytes.Length; offset += cleartextChunkSize)
                {
                    int chunkSize = Math.Min(cleartextChunkSize, targetBytes.Length - offset);
                    ReadOnlyMemory<byte> cleartextChunk = new ReadOnlyMemory<byte>(targetBytes, offset, chunkSize);
                    
                    Memory<byte> encryptedChunk = fileContentCryptor.EncryptChunk(cleartextChunk, chunkNumber, fileHeader);
                    outputStream.Write(encryptedChunk.Span);
                    
                    chunkNumber++;
                }
                
                return outputStream.ToArray();
            }
            catch (Exception ex) when (!(ex is ArgumentNullException || ex is InvalidOperationException))
            {
                throw new CryptoException("Failed to encrypt symlink target", ex);
            }
        }

        /// <summary>
        /// Decrypts a symlink target from encrypted bytes read from a symlink.uvf file.
        /// </summary>
        /// <param name="encryptedSymlinkBytes">Encrypted bytes from symlink.uvf file</param>
        /// <returns>The plaintext symlink target path</returns>
        /// <exception cref="ArgumentNullException">If encryptedSymlinkBytes is null</exception>
        /// <exception cref="InvalidOperationException">If this is not a UVF vault or the cryptor is not available</exception>
        /// <exception cref="AuthenticationFailedException">If the encrypted data authentication fails</exception>
        /// <exception cref="CryptoException">If decryption fails</exception>
        public string DecryptSymlinkTarget(byte[] encryptedSymlinkBytes)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (encryptedSymlinkBytes == null) throw new ArgumentNullException(nameof(encryptedSymlinkBytes));
            if (IsCryptomatorV8()) throw new InvalidOperationException("Symlinks are only supported in UVF format");

            var fileContentCryptor = _cryptor.FileContentCryptor();
            var fileHeaderCryptor = _cryptor.FileHeaderCryptor();
            if (fileContentCryptor == null) throw new InvalidOperationException("File content cryptor not available.");
            if (fileHeaderCryptor == null) throw new InvalidOperationException("File header cryptor not available.");

            try
            {
                using var inputStream = new MemoryStream(encryptedSymlinkBytes);
                
                // Read and decrypt file header
                int headerSize = fileHeaderCryptor.HeaderSize();
                byte[] headerBytes = new byte[headerSize];
                if (inputStream.Read(headerBytes, 0, headerSize) != headerSize)
                {
                    throw new CryptoException("Failed to read complete file header from symlink data");
                }
                
                var fileHeader = fileHeaderCryptor.DecryptHeader(new ReadOnlyMemory<byte>(headerBytes));
                
                // Decrypt the symlink target content in chunks
                using var outputStream = new MemoryStream();
                int ciphertextChunkSize = fileContentCryptor.CiphertextChunkSize();
                long chunkNumber = 0;
                
                byte[] chunkBuffer = new byte[ciphertextChunkSize];
                int bytesRead;
                
                while ((bytesRead = inputStream.Read(chunkBuffer, 0, ciphertextChunkSize)) > 0)
                {
                    ReadOnlyMemory<byte> encryptedChunk = new ReadOnlyMemory<byte>(chunkBuffer, 0, bytesRead);
                    Memory<byte> decryptedChunk = fileContentCryptor.DecryptChunk(encryptedChunk, chunkNumber, fileHeader, true);
                    
                    outputStream.Write(decryptedChunk.Span);
                    chunkNumber++;
                }
                
                byte[] targetBytes = outputStream.ToArray();
                
                // Convert back to string (UTF-8 Normalization Form C)
                return System.Text.Encoding.UTF8.GetString(targetBytes);
            }
            catch (AuthenticationFailedException)
            {
                throw; // Re-throw authentication failures as-is
            }
            catch (Exception ex) when (!(ex is ArgumentNullException || ex is InvalidOperationException))
            {
                throw new CryptoException("Failed to decrypt symlink target", ex);
            }
        }

        /// <summary>
        /// Gets the symlink metadata filename for UVF format.
        /// </summary>
        /// <returns>The symlink metadata filename ("symlink.uvf")</returns>
        /// <exception cref="InvalidOperationException">If this is not a UVF vault</exception>
        public string GetSymlinkMetadataFilename()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (IsCryptomatorV8()) throw new InvalidOperationException("Symlinks are only supported in UVF format");
            
            return UvfLib.Core.V3.Constants.SYMLINK_FILE;
        }

        /// <summary>
        /// Returns a Stream that encrypts data with support for random writes and read-modify-write operations.
        /// This stream supports seeking and writing to any position within the file, making it suitable
        /// for scenarios that require random access to encrypted files.
        /// </summary>
        /// <param name="outputStream">The stream to write encrypted data to.</param>
        /// <param name="existingHeader">Optional existing file header to preserve when opening existing files.</param>
        /// <param name="leaveOpen">Whether to leave the underlying outputStream open when the encrypting stream is disposed.</param>
        /// <returns>A Stream wrapper that performs encryption with random write support.</returns>
        /// <exception cref="ArgumentNullException">If outputStream is null.</exception>
        /// <exception cref="ArgumentException">If outputStream is not writable.</exception>
        /// <exception cref="InvalidOperationException">If the vault is not initialized correctly.</exception>
        public Stream GetRandomWriteEncryptingStream(Stream outputStream, FileHeader? existingHeader = null, bool leaveOpen = false)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(VaultHandler));
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));
            if (!outputStream.CanWrite) throw new ArgumentException("Output stream must be writable", nameof(outputStream));

            FileHeader fileHeader;
            
            if (existingHeader != null)
            {
                // Use the provided existing header
                fileHeader = existingHeader;
            }
            else if (outputStream.CanSeek && outputStream.Length > 0)
            {
                // Try to read existing header from the stream
                long originalPosition = outputStream.Position;
                outputStream.Position = 0;
                
                try
                {
                    byte[] encryptedHeader = new byte[UvfLib.Core.V3.FileHeaderImpl.SIZE];
                    int bytesRead = outputStream.Read(encryptedHeader, 0, encryptedHeader.Length);
                    if (bytesRead == encryptedHeader.Length)
                    {
                        fileHeader = _cryptor.FileHeaderCryptor().DecryptHeader(encryptedHeader);
                    }
                    else
                    {
                        // Not enough data for a header, create new one
                        fileHeader = _cryptor.FileHeaderCryptor().Create();
                    }
                }
                catch
                {
                    // If header reading fails, create a new one
                    fileHeader = _cryptor.FileHeaderCryptor().Create();
                }
                finally
                {
                    outputStream.Position = originalPosition;
                }
            }
            else
            {
                // Create new header for new files
                fileHeader = _cryptor.FileHeaderCryptor().Create();
            }

            return new VaultHelpers.RandomWriteEncryptingStream(outputStream, (ICryptor)_cryptor, fileHeader, leaveOpen);
        }

    }
}