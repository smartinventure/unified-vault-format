// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.


using System;
using System.Collections.Generic;
using System.Text;
using UvfLib.Core.Api;
using UvfLib.Core.Common;

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// Directory content cryptor implementation for Cryptomator v2.
    /// Handles directory metadata and filename encryption/decryption.
    /// </summary>
    internal class DirectoryContentCryptorImpl : DirectoryContentCryptor
    {
        private readonly CryptorImpl _cryptor;

        /// <summary>
        /// Initializes a new instance of the DirectoryContentCryptorImpl class.
        /// </summary>
        /// <param name="cryptor">The cryptor instance</param>
        public DirectoryContentCryptorImpl(CryptorImpl cryptor)
        {
            _cryptor = cryptor ?? throw new ArgumentNullException(nameof(cryptor));
        }

        // DIRECTORY METADATA

        /// <summary>
        /// Gets the root directory metadata.
        /// </summary>
        /// <returns>The root directory metadata</returns>
        public DirectoryMetadata RootDirectoryMetadata()
        {
            // Root directory has empty byte array as ID
            return new DirectoryMetadataImpl(Array.Empty<byte>());
        }

        /// <summary>
        /// Creates new directory metadata.
        /// </summary>
        /// <returns>The new directory metadata</returns>
        public DirectoryMetadata NewDirectoryMetadata()
        {
            // Generate a UUID string as directory ID (as in Java implementation)
            string uuid = Guid.NewGuid().ToString();
            byte[] dirId = Encoding.ASCII.GetBytes(uuid);
            return new DirectoryMetadataImpl(dirId);
        }

        /// <summary>
        /// Decrypts directory metadata.
        /// In Cryptomator v8 (based on v2), directory IDs are stored in plaintext in dir.c9r files.
        /// </summary>
        /// <param name="ciphertext">The raw bytes from dir.c9r file (actually plaintext directory ID)</param>
        /// <returns>The directory metadata</returns>
        public DirectoryMetadata DecryptDirectoryMetadata(byte[] ciphertext)
        {
            // In Cryptomator v8 (based on v2), directory IDs are stored in plaintext
            // The 'ciphertext' parameter is actually plaintext directory ID bytes
            return new DirectoryMetadataImpl(ciphertext);
        }

        /// <summary>
        /// Decrypts directory metadata (obsolete overload).
        /// </summary>
        /// <param name="ciphertext">The raw bytes from dir.c9r file</param>
        /// <param name="directorysOwnDirIdBytes">Ignored in v2</param>
        /// <returns>The directory metadata</returns>
        [Obsolete("Use DecryptDirectoryMetadata(byte[] ciphertext) instead. The dirId parameter is not used in v2.")]
        public DirectoryMetadata DecryptDirectoryMetadata(byte[] ciphertext, byte[] directorysOwnDirIdBytes)
        {
            // Just call the main method, ignoring the directory ID parameter
            return DecryptDirectoryMetadata(ciphertext);
        }

        /// <summary>
        /// Encrypts directory metadata.
        /// In v2, directory IDs are stored in plaintext, so no encryption is needed.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata to encrypt</param>
        /// <returns>The raw bytes for dir.c9r file</returns>
        public byte[] EncryptDirectoryMetadata(DirectoryMetadata directoryMetadata)
        {
            // In v2, dirId is stored in plaintext
            DirectoryMetadataImpl metadataImpl = DirectoryMetadataImpl.Cast(directoryMetadata);
            return metadataImpl.DirIdBytes();
        }

        // DIR PATH

        /// <summary>
        /// Computes the directory path for the given directory metadata.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>The directory path (e.g., "d/AB/CDEFG...")</returns>
        public string DirPath(DirectoryMetadata directoryMetadata)
        {
            DirectoryMetadataImpl metadataImpl = DirectoryMetadataImpl.Cast(directoryMetadata);
            string dirIdStr = _cryptor.FileNameCryptorInternal().HashDirectoryId(metadataImpl.DirIdBytes());
            
            // Ensure the hash is exactly 32 characters
            if (dirIdStr.Length != 32)
            {
                throw new InvalidOperationException($"Directory ID hash length expected 32, got {dirIdStr.Length}");
            }
            
            return "d/" + dirIdStr.Substring(0, 2) + "/" + dirIdStr.Substring(2);
        }

        // FILE NAMES

        /// <summary>
        /// Gets a file name decryptor for the given directory.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>A file name decryptor</returns>
        public IDirectoryContentCryptor.Decrypting FileNameDecryptor(DirectoryMetadata directoryMetadata)
        {
            DirectoryMetadataImpl metadataImpl = DirectoryMetadataImpl.Cast(directoryMetadata);
            byte[] dirId = metadataImpl.DirIdBytes();
            FileNameCryptorImpl fileNameCryptor = _cryptor.FileNameCryptorInternal();
            
            return new V2FileNameDecryptor(fileNameCryptor, dirId);
        }

        /// <summary>
        /// Gets a file name encryptor for the given directory.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>A file name encryptor</returns>
        public IDirectoryContentCryptor.Encrypting FileNameEncryptor(DirectoryMetadata directoryMetadata)
        {
            DirectoryMetadataImpl metadataImpl = DirectoryMetadataImpl.Cast(directoryMetadata);
            byte[] dirId = metadataImpl.DirIdBytes();
            FileNameCryptorImpl fileNameCryptor = _cryptor.FileNameCryptorInternal();
            
            return new V2FileNameEncryptor(fileNameCryptor, dirId);
        }

        /// <summary>
        /// Encrypts a filename in the context of a directory.
        /// Returns the full encrypted filename with .c9r extension (no automatic shortening).
        /// </summary>
        /// <param name="cleartextName">The cleartext filename</param>
        /// <param name="directoryId">The Base64Url encoded directory ID</param>
        /// <returns>The encrypted filename (including .c9r extension)</returns>
        public string EncryptFilename(string cleartextName, string directoryId)
        {
            byte[] dirIdBytes = Base64Url.Decode(directoryId);
            var fileNameCryptor = _cryptor.FileNameCryptorInternal();
            string ciphertext = fileNameCryptor.EncryptFilename(cleartextName, dirIdBytes);
            string fullEncryptedName = ciphertext + Constants.C9R_FILE_EXT;
            
            // Return the full encrypted filename - shortening should be handled at the storage layer
            return fullEncryptedName;
        }

        /// <summary>
        /// Encrypts a filename in the context of a directory with associated data.
        /// </summary>
        /// <param name="cleartextName">The cleartext filename</param>
        /// <param name="directoryId">The Base64Url encoded directory ID</param>
        /// <param name="associatedData">Associated data (ignored in v2)</param>
        /// <returns>The encrypted filename (including .c9r extension)</returns>
        public string EncryptFilename(string cleartextName, string directoryId, Dictionary<string, string> associatedData)
        {
            // v2 doesn't use associated data, so just call the main method
            return EncryptFilename(cleartextName, directoryId);
        }

        /// <summary>
        /// Decrypts a filename in the context of a directory.
        /// Handles both normal encrypted filenames and shortened directory names.
        /// </summary>
        /// <param name="ciphertextName">The encrypted filename (including .c9r extension) or shortened directory name</param>
        /// <param name="directoryId">The Base64Url encoded directory ID</param>
        /// <returns>The decrypted filename</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails</exception>
        /// <exception cref="ArgumentException">If filename format is invalid</exception>
        /// <exception cref="NotSupportedException">If trying to decrypt a shortened name without the original</exception>
        public string DecryptFilename(string ciphertextName, string directoryId)
        {
            // Check if this is a shortened directory name
            if (NameShorteningHelper.IsShortenedDirectory(ciphertextName))
            {
                throw new NotSupportedException(
                    $"Cannot decrypt shortened directory name '{ciphertextName}' directly. " +
                    "The original long encrypted filename must be read from the name.c9s file first. " +
                    "Use DecryptShortenedFilename() method instead.");
            }
            
            // Remove the .c9r extension
            if (!ciphertextName.EndsWith(Constants.C9R_FILE_EXT))
            {
                throw new ArgumentException($"Not a {Constants.C9R_FILE_EXT} file: {ciphertextName}");
            }
            
            string ciphertextWithoutExt = ciphertextName.Substring(0, ciphertextName.Length - Constants.C9R_FILE_EXT.Length);
            byte[] dirIdBytes = Base64Url.Decode(directoryId);
            var fileNameCryptor = _cryptor.FileNameCryptorInternal();
            return fileNameCryptor.DecryptFilename(ciphertextWithoutExt, dirIdBytes);
        }

        /// <summary>
        /// Decrypts a shortened filename by reading the original encrypted filename from the name.c9s file.
        /// This method requires access to the storage layer to read the inflated name file.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name (e.g., "ABC123.c9s")</param>
        /// <param name="originalEncryptedFilename">The original long encrypted filename read from name.c9s file</param>
        /// <param name="directoryId">The Base64Url encoded directory ID</param>
        /// <returns>The decrypted filename</returns>
        /// <exception cref="ArgumentException">If the shortened name doesn't match the original filename</exception>
        /// <exception cref="AuthenticationFailedException">If decryption fails</exception>
        public string DecryptShortenedFilename(string shortenedDirectoryName, string originalEncryptedFilename, string directoryId)
        {
            // Validate that the shortened name matches the original filename
            if (!NameShorteningHelper.ValidateShortenedName(shortenedDirectoryName, originalEncryptedFilename))
            {
                throw new ArgumentException(
                    $"Shortened directory name '{shortenedDirectoryName}' does not match " +
                    $"original encrypted filename '{originalEncryptedFilename}'");
            }

            // Now decrypt the original filename normally
            return DecryptFilename(originalEncryptedFilename, directoryId);
        }

        /// <summary>
        /// Decrypts a filename in the context of a directory with associated data.
        /// </summary>
        /// <param name="ciphertextName">The encrypted filename (including .c9r extension)</param>
        /// <param name="directoryId">The Base64Url encoded directory ID</param>
        /// <param name="associatedData">Associated data (ignored in v2)</param>
        /// <returns>The decrypted filename</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails</exception>
        /// <exception cref="ArgumentException">If filename format is invalid</exception>
        public string DecryptFilename(string ciphertextName, string directoryId, Dictionary<string, string> associatedData)
        {
            // v2 doesn't use associated data, so just call the main method
            return DecryptFilename(ciphertextName, directoryId);
        }

        /// <summary>
        /// File name decryptor implementation.
        /// </summary>
        private class V2FileNameDecryptor : IDirectoryContentCryptor.Decrypting
        {
            private readonly FileNameCryptorImpl _fileNameCryptor;
            private readonly byte[] _dirId;

            public V2FileNameDecryptor(FileNameCryptorImpl fileNameCryptor, byte[] dirId)
            {
                _fileNameCryptor = fileNameCryptor;
                _dirId = dirId;
            }

            /// <summary>
            /// Decrypts a filename.
            /// Handles both normal encrypted filenames and shortened directory names.
            /// </summary>
            /// <param name="ciphertext">The encrypted filename (including .c9r extension) or shortened directory name</param>
            /// <returns>The decrypted filename</returns>
            /// <exception cref="AuthenticationFailedException">If decryption fails</exception>
            /// <exception cref="ArgumentException">If filename format is invalid</exception>
            /// <exception cref="NotSupportedException">If trying to decrypt a shortened name without the original</exception>
            public string Decrypt(string ciphertext)
            {
                // Check if this is a shortened directory name
                if (NameShorteningHelper.IsShortenedDirectory(ciphertext))
                {
                    throw new NotSupportedException(
                        $"Cannot decrypt shortened directory name '{ciphertext}' directly. " +
                        "The original long encrypted filename must be read from the name.c9s file first. " +
                        "Use the parent DirectoryContentCryptor.DecryptShortenedFilename() method instead.");
                }
                
                // Remove the .c9r extension
                if (!ciphertext.EndsWith(Constants.C9R_FILE_EXT))
                {
                    throw new ArgumentException($"Not a {Constants.C9R_FILE_EXT} file: {ciphertext}");
                }
                
                string ciphertextWithoutExt = ciphertext.Substring(0, ciphertext.Length - Constants.C9R_FILE_EXT.Length);
                
                // Add debugging information to help diagnose Base64 format issues
                try
                {
                return _fileNameCryptor.DecryptFilename(ciphertextWithoutExt, _dirId);
                }
                catch (System.FormatException ex) when (ex.Message.Contains("Base-64"))
                {
                    throw new ArgumentException(
                        $"Base64 format error in filename decryption. " +
                        $"Original ciphertext: '{ciphertext}' " +
                        $"After extension removal: '{ciphertextWithoutExt}' " +
                        $"Extension constant: '{Constants.C9R_FILE_EXT}' " +
                        $"Ciphertext length: {ciphertext.Length} " +
                        $"Extension length: {Constants.C9R_FILE_EXT.Length} " +
                        $"Result length: {ciphertextWithoutExt.Length}", ex);
                }
            }
        }

        /// <summary>
        /// File name encryptor implementation.
        /// </summary>
        private class V2FileNameEncryptor : IDirectoryContentCryptor.Encrypting
        {
            private readonly FileNameCryptorImpl _fileNameCryptor;
            private readonly byte[] _dirId;

            public V2FileNameEncryptor(FileNameCryptorImpl fileNameCryptor, byte[] dirId)
            {
                _fileNameCryptor = fileNameCryptor;
                _dirId = dirId;
            }

            /// <summary>
            /// Encrypts a filename.
            /// Returns the full encrypted filename with .c9r extension (no automatic shortening).
            /// </summary>
            /// <param name="plaintext">The cleartext filename</param>
            /// <returns>The encrypted filename (including .c9r extension)</returns>
            public string Encrypt(string plaintext)
            {
                string ciphertext = _fileNameCryptor.EncryptFilename(plaintext, _dirId);
                string fullEncryptedName = ciphertext + Constants.C9R_FILE_EXT;
                
                // Return the full encrypted filename - shortening should be handled at the storage layer
                return fullEncryptedName;
            }
        }
    }
} 
