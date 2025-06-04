using System;
using System.Security.Cryptography;

namespace UvfLib.Common
{
    /// <summary>
    /// Utility class for AES-GCM encryption/decryption
    /// </summary>
    public static class AesGcmCryptor
    {
        /// <summary>
        /// Encrypts data using AES-GCM.
        /// </summary>
        /// <param name="data">The data to encrypt</param>
        /// <param name="key">The encryption key</param>
        /// <param name="iv">The initialization vector</param>
        /// <returns>The encrypted data with authentication tag appended</returns>
        public static byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));

            using var supplier = CipherSupplier.AES_GCM.EncryptionCipher(key, iv);
            // The transform will append the authentication tag to the ciphertext
            return supplier.TransformFinalBlock(data, 0, data.Length);
        }

        /// <summary>
        /// Decrypts data using AES-GCM.
        /// </summary>
        /// <param name="data">The data to decrypt (including authentication tag)</param>
        /// <param name="key">The decryption key</param>
        /// <param name="iv">The initialization vector</param>
        /// <returns>The decrypted data</returns>
        public static byte[] Decrypt(byte[] data, byte[] key, byte[] iv)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));

            using var supplier = CipherSupplier.AES_GCM.DecryptionCipher(key, iv);
            // The transform will verify the authentication tag
            return supplier.TransformFinalBlock(data, 0, data.Length);
        }
    }
}