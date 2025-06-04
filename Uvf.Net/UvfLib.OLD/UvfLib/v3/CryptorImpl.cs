using System;
using System.Security.Cryptography;
using UvfLib.Api;

namespace UvfLib.V3
{
    /// <summary>
    /// Implementation of the Cryptor interface for v3 format.
    /// </summary>
    internal sealed class CryptorImpl : ICryptor
    {
        private readonly RevolvingMasterkey _masterkey;
        private readonly FileContentCryptorImpl _fileContentCryptor;
        private readonly RandomNumberGenerator _random;

        /// <summary>
        /// Package-private constructor.
        /// Use CryptoFactory to obtain a Cryptor instance.
        /// </summary>
        /// <param name="masterkey">The masterkey</param>
        /// <param name="random">The random number generator</param>
        internal CryptorImpl(RevolvingMasterkey masterkey, RandomNumberGenerator random)
        {
            _masterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _fileContentCryptor = new FileContentCryptorImpl(_masterkey, _random);
        }

        /// <inheritdoc/>
        public IFileContentCryptor FileContentCryptor()
        {
            AssertNotDestroyed();
            return _fileContentCryptor;
        }

        /// <inheritdoc/>
        public FileHeaderCryptor FileHeaderCryptor()
        {
            return FileHeaderCryptor(_masterkey.GetCurrentRevision());
        }

        /// <inheritdoc/>
        public FileHeaderCryptor FileHeaderCryptor(int revision)
        {
            AssertNotDestroyed();
            return new FileHeaderCryptorImpl(_masterkey, _random, revision);
        }

        /// <inheritdoc/>
        public FileNameCryptor FileNameCryptor()
        {
            throw new NotSupportedException("Direct access to FileNameCryptor without specified revision is not supported in v3");
        }

        /// <inheritdoc/>
        public FileNameCryptor FileNameCryptor(int revision)
        {
            AssertNotDestroyed();
            if (!_masterkey.HasRevision(revision))
            {
                throw new ArgumentException($"Masterkey does not have revision {revision}", nameof(revision));
            }
            return new FileNameCryptorImpl(_masterkey, _random, revision);
        }

        /// <inheritdoc/>
        public DirectoryContentCryptor DirectoryContentCryptor()
        {
            AssertNotDestroyed();
            return new DirectoryContentCryptorImpl(_masterkey, _random, this);
        }

        /// <inheritdoc/>
        public bool IsDestroyed()
        {
            return _masterkey.IsDestroyed();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Destroy();
        }

        /// <inheritdoc/>
        public void Destroy()
        {
            _masterkey.Destroy();
        }

        private void AssertNotDestroyed()
        {
            if (IsDestroyed())
            {
                throw new InvalidOperationException("Cryptor destroyed.");
            }
        }
    }
}