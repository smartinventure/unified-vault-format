/*******************************************************************************
 * Copyright (c) 2016 Sebastian Stenzel and others.
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the accompanying LICENSE.txt.
 *
 * Contributors:
 *     Sebastian Stenzel - initial API and implementation
 *******************************************************************************/

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

using System;
using System.Collections.Generic;
using System.Text;
using UvfLib.Api;
using UvfLib.Common;

namespace UvfLib.CryptomatorV8
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
        /// </summary>
        /// <param name="cleartextName">The cleartext filename</param>
        /// <param name="directoryId">The Base64Url encoded directory ID</param>
        /// <returns>The encrypted filename (including .c9r extension)</returns>
        public string EncryptFilename(string cleartextName, string directoryId)
        {
            byte[] dirIdBytes = Base64Url.Decode(directoryId);
            var fileNameCryptor = _cryptor.FileNameCryptorInternal();
            string ciphertext = fileNameCryptor.EncryptFilename(cleartextName, dirIdBytes);
            return ciphertext + Constants.C9R_FILE_EXT;
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
        /// </summary>
        /// <param name="ciphertextName">The encrypted filename (including .c9r extension)</param>
        /// <param name="directoryId">The Base64Url encoded directory ID</param>
        /// <returns>The decrypted filename</returns>
        /// <exception cref="AuthenticationFailedException">If decryption fails</exception>
        /// <exception cref="ArgumentException">If filename format is invalid</exception>
        public string DecryptFilename(string ciphertextName, string directoryId)
        {
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
            /// </summary>
            /// <param name="ciphertext">The encrypted filename (including .c9r extension)</param>
            /// <returns>The decrypted filename</returns>
            /// <exception cref="AuthenticationFailedException">If decryption fails</exception>
            /// <exception cref="ArgumentException">If filename format is invalid</exception>
            public string Decrypt(string ciphertext)
            {
                // Remove the .c9r extension
                if (!ciphertext.EndsWith(Constants.C9R_FILE_EXT))
                {
                    throw new ArgumentException($"Not a {Constants.C9R_FILE_EXT} file: {ciphertext}");
                }
                
                string ciphertextWithoutExt = ciphertext.Substring(0, ciphertext.Length - Constants.C9R_FILE_EXT.Length);
                return _fileNameCryptor.DecryptFilename(ciphertextWithoutExt, _dirId);
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
            /// </summary>
            /// <param name="plaintext">The cleartext filename</param>
            /// <returns>The encrypted filename (including .c9r extension)</returns>
            public string Encrypt(string plaintext)
            {
                string ciphertext = _fileNameCryptor.EncryptFilename(plaintext, _dirId);
                return ciphertext + Constants.C9R_FILE_EXT;
            }
        }
    }
} 