using System;
using System.Security.Cryptography;

namespace UvfLib.Core.Api
{
    /// <summary>
    /// Provider interface for creating Cryptor instances.
    /// </summary>
    public interface CryptorProvider
    {
        /// <summary>
        /// A combination of ciphers to use for filename and file content encryption
        /// </summary>
        public enum Scheme
        {
            /// <summary>
            /// AES-SIV for file name encryption
            /// AES-CTR + HMAC for content encryption
            /// </summary>
            SIV_CTRMAC,

            /// <summary>
            /// AES-SIV for file name encryption
            /// AES-GCM for content encryption
            /// </summary>
            SIV_GCM,

            /// <summary>
            /// Experimental implementation of UVF draft
            /// </summary>
            [Obsolete("May be removed any time")]
            UVF_DRAFT,
        }

        /// <summary>
        /// Finds a CryptorProvider implementation for the given combination of ciphers.
        /// </summary>
        /// <param name="scheme">A cipher combination</param>
        /// <returns>A CryptorProvider implementation supporting the requested scheme</returns>
        /// <exception cref="NotSupportedException">If the scheme is not implemented</exception>
        public static CryptorProvider ForScheme(Scheme scheme)
        {
            return scheme switch
            {
                Scheme.UVF_DRAFT => new V3.CryptorProviderImpl(),
                Scheme.SIV_GCM => new CryptomatorV8.CryptorProviderImpl(),
                _ => throw new NotSupportedException($"Scheme not supported: {scheme}")
            };
        }

        /// <summary>
        /// Gets the combination of ciphers used by this CryptorProvider implementation.
        /// </summary>
        /// <returns>The scheme</returns>
        Scheme GetScheme();

        /// <summary>
        /// Creates a new Cryptor instance for the given key
        /// </summary>
        /// <param name="masterkey">The key used by the returned cryptor during encryption and decryption</param>
        /// <param name="random">A secure random number generator used to seed internal RNGs</param>
        /// <returns>A new cryptor</returns>
        ICryptor Provide(Masterkey masterkey, RandomNumberGenerator random);
    }
}
