using System;
using System.Security.Cryptography;
using UvfLib.Api;

namespace UvfLib.V3
{
    /// <summary>
    /// Implementation of the CryptoFactory interface for v3 format.
    /// </summary>
    internal sealed class CryptoFactoryImpl : CryptoFactory
    {
        private readonly RevolvingMasterkey _revolvingMasterkey;
        private readonly RandomNumberGenerator _random;

        /// <summary>
        /// Creates a new crypto factory.
        /// </summary>
        /// <param name="revolvingMasterkey">The revolving masterkey</param>
        public CryptoFactoryImpl(RevolvingMasterkey revolvingMasterkey)
        {
            _revolvingMasterkey = revolvingMasterkey ?? throw new ArgumentNullException(nameof(revolvingMasterkey));
            _random = RandomNumberGenerator.Create();
        }

        /// <summary>
        /// Creates a cryptor instance.
        /// </summary>
        /// <returns>A new cryptor</returns>
        public Cryptor Create()
        {
            return new CryptorImpl(_revolvingMasterkey, _random);
        }

        /// <summary>
        /// Creates a file name cryptor.
        /// </summary>
        /// <returns>A file name cryptor</returns>
        public FileNameCryptor CreateFileNameCryptor()
        {
            return new FileNameCryptorImpl(_revolvingMasterkey, _random);
        }

        /// <summary>
        /// Creates a file content cryptor.
        /// </summary>
        /// <returns>A file content cryptor</returns>
        public IFileContentCryptor CreateFileContentCryptor()
        {
            return new FileContentCryptorImpl(_revolvingMasterkey, _random);
        }

        /// <summary>
        /// Disposes of resources.
        /// </summary>
        public void Dispose()
        {
            _random.Dispose();

            // If masterkey is disposable, dispose it
            if (_revolvingMasterkey is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}