using System;
using System.Security.Cryptography;

namespace UvfLib.Common
{
    /// <summary>
    /// Utility class for AES-CTR encryption/decryption
    /// </summary>
    public static class AesCtrCryptor
    {
        /// <summary>
        /// Encrypts data using AES-CTR.
        /// </summary>
        /// <param name="data">The data to encrypt</param>
        /// <param name="key">The encryption key</param>
        /// <param name="iv">The initialization vector (counter)</param>
        /// <returns>The encrypted data</returns>
        public static byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));

            using (var supplier = CipherSupplier.AES_CTR.EncryptionCipher(key, iv))
            {
                return supplier.TransformFinalBlock(data, 0, data.Length);
            }
        }

        /// <summary>
        /// Decrypts data using AES-CTR.
        /// </summary>
        /// <param name="data">The data to decrypt</param>
        /// <param name="key">The decryption key</param>
        /// <param name="iv">The initialization vector (counter)</param>
        /// <returns>The decrypted data</returns>
        public static byte[] Decrypt(byte[] data, byte[] key, byte[] iv)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));

            using (var supplier = CipherSupplier.AES_CTR.DecryptionCipher(key, iv))
            {
                return supplier.TransformFinalBlock(data, 0, data.Length);
            }
        }
    }
}