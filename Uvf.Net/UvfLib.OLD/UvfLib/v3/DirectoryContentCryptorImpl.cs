using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using UvfLib.Api;
using UvfLib.Common;

namespace UvfLib.V3
{
    /// <summary>
    /// Implementation of the DirectoryContentCryptor interface for v3 format.
    /// Handles encryption and decryption of directory metadata (dir.uvf files)
    /// and provides access to directory path generation and filename cryptors.
    /// </summary>
    internal sealed class DirectoryContentCryptorImpl : DirectoryContentCryptor
    {
        private readonly RevolvingMasterkey _masterkey;
        private readonly RandomNumberGenerator _random;
        private readonly CryptorImpl _cryptor;

        /// <summary>
        /// Creates a new directory content cryptor.
        /// </summary>
        /// <param name="masterkey">The masterkey</param>
        /// <param name="random">The random number generator</param>
        /// <param name="cryptor">The cryptor</param>
        public DirectoryContentCryptorImpl(RevolvingMasterkey masterkey, RandomNumberGenerator random, CryptorImpl cryptor)
        {
            _masterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
        }

        // DIRECTORY METADATA

        /// <summary>
        /// Gets the root directory metadata.
        /// </summary>
        /// <returns>The root directory metadata</returns>
        public DirectoryMetadata RootDirectoryMetadata()
        {
            byte[] dirId = _masterkey.GetRootDirId();
            return new DirectoryMetadataImpl(_masterkey.GetFirstRevision(), dirId);
        }

        /// <summary>
        /// Creates a new directory metadata object, typically for a new subdirectory.
        /// </summary>
        /// <returns>The new directory metadata</returns>
        public DirectoryMetadata NewDirectoryMetadata()
        {
            byte[] dirId = new byte[Constants.DIR_ID_SIZE]; // Use defined constant
            _random.GetBytes(dirId);
            return new DirectoryMetadataImpl(_masterkey.GetCurrentRevision(), dirId);
        }

        /// <summary>
        /// Decrypts the given directory metadata (content of a dir.uvf file).
        /// According to the UVF spec, dir.uvf contains only the 32-byte directory ID.
        /// </summary>
        /// <param name="ciphertext">The encrypted directory metadata (full content of dir.uvf, including its header).</param>
        /// <returns>The decrypted directory metadata.</returns>
        /// <exception cref="AuthenticationFailedException">If the ciphertext is unauthentic.</exception>
        /// <exception cref="ArgumentException">If ciphertext is invalid.</exception>
        public DirectoryMetadata DecryptDirectoryMetadata(byte[] ciphertext)
        {
            // According to Java implementation, dir.uvf is always 128 bytes
            if (ciphertext == null || ciphertext.Length != 128)
            {
                throw new ArgumentException($"Invalid dir.uvf length: expected 128 bytes, got {ciphertext?.Length ?? 0}", nameof(ciphertext));
            }

            // Decrypt the file header (first 80 bytes of dir.uvf)
            var headerCryptor = _cryptor.FileHeaderCryptor();
            FileHeader header = headerCryptor.DecryptHeader(ciphertext.AsSpan(0, FileHeaderImpl.SIZE).ToArray());
            var fileHeaderImpl = FileHeaderImpl.Cast(header);

            // The remaining 48 bytes contain the encrypted dirId (32 bytes) + GCM tag (16 bytes)
            // Use FileContentCryptor to decrypt the chunk
            var fileContentCryptor = _cryptor.FileContentCryptor();
            ReadOnlyMemory<byte> encryptedChunk = new ReadOnlyMemory<byte>(ciphertext, FileHeaderImpl.SIZE, ciphertext.Length - FileHeaderImpl.SIZE);
            
            // Decrypt as chunk 0 with last chunk flag set to true
            byte[] decryptedDirId = fileContentCryptor.DecryptChunk(encryptedChunk, 0, header, true).ToArray();
            
            if (decryptedDirId.Length != Constants.DIR_ID_SIZE)
            {
                throw new InvalidOperationException($"Decrypted dirId has invalid length: expected {Constants.DIR_ID_SIZE}, got {decryptedDirId.Length}");
            }

            // Create and return the directory metadata
            return new DirectoryMetadataImpl(fileHeaderImpl.GetSeedId(), decryptedDirId);
        }

        // Keep the old method for backward compatibility but mark it obsolete
        [Obsolete("Use DecryptDirectoryMetadata(byte[] ciphertext) instead. The dirId parameter is not used.")]
        public DirectoryMetadata DecryptDirectoryMetadata(byte[] ciphertext, byte[] directorysOwnDirIdBytes)
        {
            return DecryptDirectoryMetadata(ciphertext);
        }

        /// <summary>
        /// Encrypts the given DirectoryMetadata to produce the content of a dir.uvf file.
        /// According to the UVF spec, dir.uvf contains only the 32-byte directory ID.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata to encrypt.</param>
        /// <returns>The encrypted binary content for a dir.uvf file (always 128 bytes).</returns>
        public byte[] EncryptDirectoryMetadata(DirectoryMetadata directoryMetadata)
        {
            DirectoryMetadataImpl metadataImpl = DirectoryMetadataImpl.Cast(directoryMetadata);
            
            // Get the raw dirId bytes (32 bytes)
            byte[] dirIdBytes = metadataImpl.GetDirIdBytes();

            // Create the header using the directory's own SeedId
            var headerCryptor = _cryptor.FileHeaderCryptor(metadataImpl.SeedId);
            FileHeader header = headerCryptor.Create();
            byte[] headerBytes = headerCryptor.EncryptHeader(header).ToArray();

            // Use FileContentCryptor to encrypt the dirId as a single chunk
            var fileContentCryptor = _cryptor.FileContentCryptor();
            ReadOnlyMemory<byte> dirIdMemory = new ReadOnlyMemory<byte>(dirIdBytes);
            
            // Encrypt as chunk 0 with last chunk flag set to true
            byte[] encryptedChunk = fileContentCryptor.EncryptChunk(dirIdMemory, 0, header).ToArray();
            
            // Combine header (80 bytes) + encrypted chunk (48 bytes) = 128 bytes total
            byte[] result = new byte[headerBytes.Length + encryptedChunk.Length];
            Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
            Buffer.BlockCopy(encryptedChunk, 0, result, headerBytes.Length, encryptedChunk.Length);
            
            return result;
        }

        // DIR PATH

        /// <summary>
        /// Gets the directory path for the given directory metadata.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>The directory path</returns>
        public string DirPath(DirectoryMetadata directoryMetadata)
        {
            DirectoryMetadataImpl metadataImpl = DirectoryMetadataImpl.Cast(directoryMetadata);
            // Get the FileNameCryptor for the SeedId specified in the directory's metadata
            FileNameCryptorImpl fileNameCryptor = (FileNameCryptorImpl)_cryptor.FileNameCryptor(metadataImpl.SeedId);
            // Use the raw DirId bytes from the metadata
            string dirIdStr = fileNameCryptor.HashDirectoryId(metadataImpl.GetDirIdBytes());

            return Constants.VAULT_DIR_PREFIX + dirIdStr.Substring(0, 2) + "/" + dirIdStr.Substring(2);
        }

        // FILE NAMES

        /// <summary>
        /// Gets a file name decryptor for the given directory.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>A file name decryptor</returns>
        public Api.IDirectoryContentCryptor.Decrypting FileNameDecryptor(DirectoryMetadata directoryMetadata)
        {
            DirectoryMetadataImpl metadataImpl = DirectoryMetadataImpl.Cast(directoryMetadata);
            byte[] dirIdBytes = metadataImpl.GetDirIdBytes();
            // Get the FileNameCryptor for the SeedId specified in the directory's metadata
            FileNameCryptorImpl fileNameCryptor = (FileNameCryptorImpl)_cryptor.FileNameCryptor(metadataImpl.SeedId);

            return new NameDecryptor(fileNameCryptor, dirIdBytes);
        }

        /// <summary>
        /// Gets a file name encryptor for the given directory.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>A file name encryptor</returns>
        public Api.IDirectoryContentCryptor.Encrypting FileNameEncryptor(DirectoryMetadata directoryMetadata)
        {
            DirectoryMetadataImpl metadataImpl = DirectoryMetadataImpl.Cast(directoryMetadata);
            byte[] dirIdBytes = metadataImpl.GetDirIdBytes();
            // Get the FileNameCryptor for the SeedId specified in the directory's metadata
            FileNameCryptorImpl fileNameCryptor = (FileNameCryptorImpl)_cryptor.FileNameCryptor(metadataImpl.SeedId);

            return new NameEncryptor(fileNameCryptor, dirIdBytes);
        }
        
        // Private helper classes for name encryption/decryption context
        private class NameDecryptor : Api.IDirectoryContentCryptor.Decrypting
        {
            private readonly FileNameCryptorImpl _fileNameCryptor;
            private readonly byte[] _dirIdBytes;

            public NameDecryptor(FileNameCryptorImpl fileNameCryptor, byte[] dirIdBytes)
            {
                _fileNameCryptor = fileNameCryptor;
                _dirIdBytes = dirIdBytes;
            }

            public string Decrypt(string ciphertextName)
            {
                // The DirId bytes are used as Associated Data in filename decryption
                return _fileNameCryptor.DecryptFilename(ciphertextName, _dirIdBytes);
            }
        }

        private class NameEncryptor : Api.IDirectoryContentCryptor.Encrypting
        {
            private readonly FileNameCryptorImpl _fileNameCryptor;
            private readonly byte[] _dirIdBytes;

            public NameEncryptor(FileNameCryptorImpl fileNameCryptor, byte[] dirIdBytes)
            {
                _fileNameCryptor = fileNameCryptor;
                _dirIdBytes = dirIdBytes;
            }

            public string Encrypt(string plaintextName)
            {
                // The DirId bytes are used as Associated Data in filename encryption
                return _fileNameCryptor.EncryptFilename(plaintextName, _dirIdBytes);
            }
        }

        // These explicit interface methods are now less relevant if using the contextual encryptor/decryptor above.
        // They could be removed or marked obsolete if the contextual approach is preferred.
        public string EncryptFilename(string cleartextName, string directoryId)
        {
            throw new NotSupportedException("Use contextual FileNameEncryptor obtained via DirectoryMetadata.");
        }

        public string EncryptFilename(string cleartextName, string directoryId, Dictionary<string, string> associatedData)
        {
            throw new NotSupportedException("Use contextual FileNameEncryptor obtained via DirectoryMetadata.");
        }

        public string DecryptFilename(string ciphertextName, string directoryId)
        {
            throw new NotSupportedException("Use contextual FileNameDecryptor obtained via DirectoryMetadata.");
        }

        public string DecryptFilename(string ciphertextName, string directoryId, Dictionary<string, string> associatedData)
        {
            throw new NotSupportedException("Use contextual FileNameDecryptor obtained via DirectoryMetadata.");
        }
    }
}