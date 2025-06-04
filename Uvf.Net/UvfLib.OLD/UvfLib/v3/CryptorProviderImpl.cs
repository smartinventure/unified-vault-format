using System;
using System.Security.Cryptography;
using UvfLib.Api;

namespace UvfLib.V3
{
    /// <summary>
    /// Provides cryptors for Universal Vault Format.
    /// </summary>
    public sealed class CryptorProviderImpl : CryptorProvider
    {
        /// <summary>
        /// Gets the scheme used by this provider.
        /// </summary>
        /// <returns>The cryptographic scheme</returns>
        public CryptorProvider.Scheme GetScheme()
        {
            return CryptorProvider.Scheme.UVF_DRAFT;
        }

        /// <summary>
        /// Creates a new cryptor instance for the given key.
        /// </summary>
        /// <param name="masterkey">The key used by the returned cryptor during encryption and decryption</param>
        /// <param name="random">A secure random number generator used to seed internal RNGs</param>
        /// <returns>A new cryptor</returns>
        /// <exception cref="ArgumentException">If masterkey is not a RevolvingMasterkey</exception>
        public ICryptor Provide(Masterkey masterkey, RandomNumberGenerator random)
        {
            if (masterkey is RevolvingMasterkey revolvingMasterkey)
            {
                return new CryptorImpl(revolvingMasterkey, random);
            }
            else
            {
                throw new ArgumentException("V3 Cryptor requires a RevolvingMasterkey", nameof(masterkey));
            }
        }
    }
}