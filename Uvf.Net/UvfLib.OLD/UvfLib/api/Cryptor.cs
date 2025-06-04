using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Main entry point for all cryptographic operations.
    /// </summary>
    public interface Cryptor : IDisposable
    {
        /// <summary>
        /// Encryption and decryption of file content.
        /// </summary>
        /// <returns>Utility for encrypting and decrypting file content</returns>
        IFileContentCryptor FileContentCryptor();

        /// <summary>
        /// Encryption and decryption of file headers.
        /// </summary>
        /// <returns>Utility for encrypting and decrypting file headers</returns>
        FileHeaderCryptor FileHeaderCryptor();

        /// <summary>
        /// Encryption and decryption of file headers.
        /// </summary>
        /// <param name="revision">The revision of the seed to derive subkeys</param>
        /// <returns>Utility for encrypting and decrypting file headers</returns>
        /// <remarks>Only relevant for Universal Vault Format, for Cryptomator Vault Format see <see cref="FileHeaderCryptor()"/></remarks>
        FileHeaderCryptor FileHeaderCryptor(int revision);

        /// <summary>
        /// Encryption and decryption of file names in Cryptomator Vault Format.
        /// </summary>
        /// <returns>Utility for encrypting and decrypting file names</returns>
        /// <remarks>Only relevant for Cryptomator Vault Format, for Universal Vault Format see <see cref="FileNameCryptor(int)"/></remarks>
        FileNameCryptor FileNameCryptor();

        /// <summary>
        /// Encryption and decryption of file names in Universal Vault Format.
        /// </summary>
        /// <param name="revision">The revision of the seed to derive subkeys</param>
        /// <returns>Utility for encrypting and decrypting file names</returns>
        /// <remarks>Only relevant for Universal Vault Format, for Cryptomator Vault Format see <see cref="FileNameCryptor()"/></remarks>
        FileNameCryptor FileNameCryptor(int revision);

        /// <summary>
        /// High-Level API for file name encryption and decryption
        /// </summary>
        /// <returns>Utility for encryption and decryption of file names in the context of a directory</returns>
        DirectoryContentCryptor DirectoryContentCryptor();

        /// <summary>
        /// Securely destroys this cryptor and all associated keys.
        /// </summary>
        void Destroy();

        /// <summary>
        /// Checks if this cryptor has been destroyed.
        /// </summary>
        /// <returns>True if this cryptor has been destroyed, false otherwise</returns>
        bool IsDestroyed();
    }
} 