using System;
using System.Collections.Generic;

namespace UvfLib.Api
{
    /// <summary>
    /// Provides operations to encrypt and decrypt directory contents.
    /// </summary>
    public interface DirectoryContentCryptor
    {
        /// <summary>
        /// Gets the root directory metadata.
        /// </summary>
        /// <returns>The root directory metadata</returns>
        DirectoryMetadata RootDirectoryMetadata();

        /// <summary>
        /// Creates a new directory metadata.
        /// </summary>
        /// <returns>The new directory metadata</returns>
        DirectoryMetadata NewDirectoryMetadata();

        /// <summary>
        /// Encrypts directory metadata.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata to encrypt</param>
        /// <returns>The encrypted directory metadata</returns>
        byte[] EncryptDirectoryMetadata(DirectoryMetadata directoryMetadata);

        /// <summary>
        /// Decrypts the given directory metadata (content of a dir.uvf file).
        /// </summary>
        /// <param name="ciphertext">The encrypted directory metadata (full content of dir.uvf, including its header).</param>
        /// <returns>The decrypted directory metadata.</returns>
        /// <exception cref="AuthenticationFailedException">If the ciphertext is unauthentic.</exception>
        DirectoryMetadata DecryptDirectoryMetadata(byte[] ciphertext);

        /// <summary>
        /// Decrypts directory metadata.
        /// </summary>
        /// <param name="ciphertext">The encrypted directory metadata (full content of dir.uvf, including its header).</param>
        /// <param name="directorysOwnDirIdBytes">The raw DirId bytes of the directory to which this ciphertext belongs. This is crucial context for AAD.</param>
        /// <returns>The decrypted directory metadata</returns>
        /// <exception cref="AuthenticationFailedException">If the ciphertext is unauthentic.</exception>
        [Obsolete("Use DecryptDirectoryMetadata(byte[] ciphertext) instead. The dirId parameter is not used in v3.")]
        DirectoryMetadata DecryptDirectoryMetadata(byte[] ciphertext, byte[] directorysOwnDirIdBytes);

        /// <summary>
        /// Gets the directory path for the given directory metadata.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>The directory path</returns>
        string DirPath(DirectoryMetadata directoryMetadata);

        /// <summary>
        /// Gets a file name encryptor for the given directory.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>A file name encryptor</returns>
        IDirectoryContentCryptor.Encrypting FileNameEncryptor(DirectoryMetadata directoryMetadata);

        /// <summary>
        /// Gets a file name decryptor for the given directory.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>A file name decryptor</returns>
        IDirectoryContentCryptor.Decrypting FileNameDecryptor(DirectoryMetadata directoryMetadata);

        /// <summary>
        /// Encrypts a file name in the context of a directory.
        /// </summary>
        /// <param name="cleartextName">The cleartext file name</param>
        /// <param name="directoryId">The ID of the directory containing the file</param>
        /// <returns>The encrypted file name</returns>
        string EncryptFilename(string cleartextName, string directoryId);

        /// <summary>
        /// Encrypts a file name in the context of a directory, with a custom prefix.
        /// </summary>
        /// <param name="cleartextName">The cleartext file name</param>
        /// <param name="directoryId">The ID of the directory containing the file</param>
        /// <param name="associatedData">Additional data to associate with the encrypted file name</param>
        /// <returns>The encrypted file name</returns>
        string EncryptFilename(string cleartextName, string directoryId, Dictionary<string, string> associatedData);

        /// <summary>
        /// Decrypts a file name in the context of a directory.
        /// </summary>
        /// <param name="ciphertextName">The encrypted file name</param>
        /// <param name="directoryId">The ID of the directory containing the file</param>
        /// <returns>The decrypted file name</returns>
        /// <exception cref="AuthenticationFailedException">If the authenticity of the encrypted file name cannot be verified</exception>
        string DecryptFilename(string ciphertextName, string directoryId);

        /// <summary>
        /// Decrypts a file name in the context of a directory.
        /// </summary>
        /// <param name="ciphertextName">The encrypted file name</param>
        /// <param name="directoryId">The ID of the directory containing the file</param>
        /// <param name="associatedData">Dictionary to store associated data read from the encrypted file name</param>
        /// <returns>The decrypted file name</returns>
        /// <exception cref="AuthenticationFailedException">If the authenticity of the encrypted file name cannot be verified</exception>
        string DecryptFilename(string ciphertextName, string directoryId, Dictionary<string, string> associatedData);
    }

    /// <summary>
    /// Interfaces for directory content cryptor operations.
    /// </summary>
    public interface IDirectoryContentCryptor
    {
        /// <summary>
        /// Interface for decrypting file names.
        /// </summary>
        public interface Decrypting
        {
            /// <summary>
            /// Decrypts a single filename.
            /// </summary>
            /// <param name="ciphertext">The full filename to decrypt, including the file extension</param>
            /// <returns>The decrypted filename</returns>
            /// <exception cref="AuthenticationFailedException">If the ciphertext is unauthentic</exception>
            /// <exception cref="ArgumentException">If the filename does not meet the expected format</exception>
            string Decrypt(string ciphertext);
        }

        /// <summary>
        /// Interface for encrypting file names.
        /// </summary>
        public interface Encrypting
        {
            /// <summary>
            /// Encrypts a single filename.
            /// </summary>
            /// <param name="plaintext">The full filename to encrypt, including the file extension</param>
            /// <returns>The encrypted filename</returns>
            string Encrypt(string plaintext);
        }
    }
}