using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;

namespace UvfLib.Common
{
    /// <summary>
    /// Implementation of the AES Key Wrap algorithm as specified in RFC 3394.
    /// </summary>
    public static class AesKeyWrap
    {
        /// <summary>
        /// Wraps a key using the AES Key Wrap algorithm.
        /// </summary>
        /// <param name="kek">The Key Encryption Key (KEK)</param>
        /// <param name="keyToWrap">The key to wrap</param>
        /// <returns>The wrapped key</returns>
        /// <exception cref="ArgumentNullException">If kek or keyToWrap is null</exception>
        /// <exception cref="ArgumentException">If keyToWrap length is not a multiple of 8 bytes</exception>
        public static byte[] Wrap(byte[] kek, byte[] keyToWrap)
        {
            if (kek == null) throw new ArgumentNullException(nameof(kek));
            if (keyToWrap == null) throw new ArgumentNullException(nameof(keyToWrap));
            if (keyToWrap.Length % 8 != 0) throw new ArgumentException("Key to wrap must be a multiple of 8 bytes", nameof(keyToWrap));

            var engine = new AesWrapEngine();
            engine.Init(true, new KeyParameter(kek)); // true for encryption
            try
            {
                return engine.Wrap(keyToWrap, 0, keyToWrap.Length);
            }
            catch (Exception ex) // Catch broader exception for robustness
            {
                throw new CryptographicException("AES key wrap failed.", ex);
            }
        }

        /// <summary>
        /// Unwraps a key using the AES Key Wrap algorithm.
        /// </summary>
        /// <param name="kek">The Key Encryption Key (KEK)</param>
        /// <param name="wrappedKey">The wrapped key</param>
        /// <returns>The unwrapped key</returns>
        /// <exception cref="ArgumentNullException">If kek or wrappedKey is null</exception>
        /// <exception cref="ArgumentException">If wrappedKey length is not at least 16 bytes or not a multiple of 8 bytes</exception>
        /// <exception cref="CryptographicException">If key unwrapping fails due to integrity check failure</exception>
        public static byte[] Unwrap(byte[] kek, byte[] wrappedKey)
        {
            if (kek == null) throw new ArgumentNullException(nameof(kek));
            if (wrappedKey == null) throw new ArgumentNullException(nameof(wrappedKey));
            if (wrappedKey.Length < 16 || wrappedKey.Length % 8 != 0) throw new ArgumentException("Wrapped key must be at least 16 bytes and a multiple of 8 bytes", nameof(wrappedKey));

            var engine = new AesWrapEngine();
            engine.Init(false, new KeyParameter(kek)); // false for decryption
            try
            {
                byte[] unwrapped = engine.Unwrap(wrappedKey, 0, wrappedKey.Length);
                return unwrapped;
            }
            catch (Org.BouncyCastle.Crypto.InvalidCipherTextException ex)
            {
                throw new CryptographicException("Key unwrap integrity check failed", ex);
            }
            catch (Exception ex) // Catch broader exception
            {
                throw new CryptographicException("AES key unwrap failed.", ex);
            }
        }
    }
}