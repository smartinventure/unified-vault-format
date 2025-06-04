using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Provides operations to encrypt and decrypt file headers.
    /// </summary>
    public interface FileHeaderCryptor
    {
        /// <summary>
        /// Creates a new file header with random content.
        /// </summary>
        /// <returns>A newly created file header</returns>
        FileHeader Create();
        
        /// <summary>
        /// Gets the size of an encrypted header in bytes.
        /// </summary>
        /// <returns>The number of bytes of an encrypted header</returns>
        int HeaderSize();
        
        /// <summary>
        /// Gets the size of an encrypted header in bytes.
        /// </summary>
        /// <returns>The number of bytes of an encrypted header</returns>
        int GetHeaderSize();
        
        /// <summary>
        /// Encrypts a file header.
        /// </summary>
        /// <param name="header">The header to encrypt</param>
        /// <returns>The encrypted header as a byte buffer</returns>
        Memory<byte> EncryptHeader(FileHeader header);
        
        /// <summary>
        /// Decrypts a file header.
        /// </summary>
        /// <param name="ciphertextHeader">The encrypted header</param>
        /// <returns>The decrypted header</returns>
        /// <exception cref="AuthenticationFailedException">If the header's authenticity cannot be verified</exception>
        FileHeader DecryptHeader(ReadOnlyMemory<byte> ciphertextHeader);
    }
} 