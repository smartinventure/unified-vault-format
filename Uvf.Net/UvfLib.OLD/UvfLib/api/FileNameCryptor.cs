using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Provides operations to encrypt and decrypt file names.
    /// </summary>
    public interface FileNameCryptor
    {
        /// <summary>
        /// Encrypts a file name.
        /// </summary>
        /// <param name="cleartextName">The cleartext file name</param>
        /// <returns>The encrypted file name</returns>
        string EncryptFilename(string cleartextName);
        
        /// <summary>
        /// Encrypts a file name, using a custom prefix (only for UVF format).
        /// </summary>
        /// <param name="cleartextName">The cleartext file name</param>
        /// <param name="prefix">Custom prefix for the resulting encrypted file name</param>
        /// <returns>The encrypted file name</returns>
        string EncryptFilename(string cleartextName, string prefix);
        
        /// <summary>
        /// Decrypts a file name.
        /// </summary>
        /// <param name="ciphertextName">The encrypted file name</param>
        /// <returns>The decrypted file name</returns>
        /// <exception cref="AuthenticationFailedException">If the encrypted file name's authenticity cannot be verified</exception>
        string DecryptFilename(string ciphertextName);
    }
} 