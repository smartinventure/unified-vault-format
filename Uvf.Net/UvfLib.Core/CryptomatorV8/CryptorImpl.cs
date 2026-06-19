// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.


using System;
using System.Security.Cryptography;
using UvfLib.Core.Api;
using UvfLib.Core.Common;

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// Cryptor implementation for Cryptomator v2 format.
    /// Package-private - use CryptorProviderImpl.Provide() to obtain a Cryptor instance.
    /// </summary>
    internal class CryptorImpl : ICryptor
    {
        private readonly PerpetualMasterkey _masterkey;
        private readonly FileContentCryptorImpl _fileContentCryptor;
        private readonly FileHeaderCryptorImpl _fileHeaderCryptor;
        private readonly FileNameCryptorImpl _fileNameCryptor;

        /// <summary>
        /// Initializes a new instance of the CryptorImpl class.
        /// </summary>
        /// <param name="masterkey">The perpetual masterkey</param>
        /// <param name="random">The secure random number generator</param>
        internal CryptorImpl(PerpetualMasterkey masterkey, RandomNumberGenerator random)
        {
            _masterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            _fileHeaderCryptor = new FileHeaderCryptorImpl(masterkey, random);
            _fileContentCryptor = new FileContentCryptorImpl(random);
            _fileNameCryptor = new FileNameCryptorImpl(masterkey);
        }

        /// <summary>
        /// Gets the file content cryptor.
        /// </summary>
        /// <returns>The file content cryptor</returns>
        public IFileContentCryptor FileContentCryptor()
        {
            AssertNotDestroyed();
            return _fileContentCryptor;
        }

        /// <summary>
        /// Gets the file header cryptor.
        /// </summary>
        /// <returns>The file header cryptor</returns>
        public FileHeaderCryptor FileHeaderCryptor()
        {
            AssertNotDestroyed();
            return _fileHeaderCryptor;
        }

        /// <summary>
        /// Gets the file header cryptor for a specific revision (not supported in v2).
        /// </summary>
        /// <param name="revision">The revision number</param>
        /// <returns>Throws UnsupportedOperationException</returns>
        /// <exception cref="NotSupportedException">Always thrown as v2 doesn't support revisions</exception>
        public FileHeaderCryptor FileHeaderCryptor(int revision)
        {
            throw new NotSupportedException("V2 Cryptor does not support revision-specific header cryptors");
        }

        /// <summary>
        /// Gets the file name cryptor.
        /// </summary>
        /// <returns>The file name cryptor</returns>
        public FileNameCryptor FileNameCryptor()
        {
            AssertNotDestroyed();
            return _fileNameCryptor;
        }

        /// <summary>
        /// Gets the file name cryptor for a specific revision (not supported in v2).
        /// </summary>
        /// <param name="revision">The revision number</param>
        /// <returns>Throws UnsupportedOperationException</returns>
        /// <exception cref="NotSupportedException">Always thrown as v2 doesn't support revisions</exception>
        public FileNameCryptor FileNameCryptor(int revision)
        {
            throw new NotSupportedException("V2 Cryptor does not support revision-specific filename cryptors");
        }

        /// <summary>
        /// Gets the directory content cryptor.
        /// </summary>
        /// <returns>The directory content cryptor</returns>
        public DirectoryContentCryptor DirectoryContentCryptor()
        {
            return new DirectoryContentCryptorImpl(this);
        }

        /// <summary>
        /// Checks if this cryptor has been destroyed.
        /// </summary>
        /// <returns>True if destroyed, false otherwise</returns>
        public bool IsDestroyed()
        {
            return _masterkey.IsDestroyed();
        }

        /// <summary>
        /// Destroys this cryptor and all associated keys.
        /// </summary>
        public void Destroy()
        {
            _masterkey.Destroy();
        }

        /// <summary>
        /// Disposes this cryptor by calling Destroy().
        /// </summary>
        public void Dispose()
        {
            Destroy();
        }

        /// <summary>
        /// Gets the internal file name cryptor implementation.
        /// </summary>
        /// <returns>The file name cryptor implementation</returns>
        internal FileNameCryptorImpl FileNameCryptorInternal()
        {
            return _fileNameCryptor;
        }

        /// <summary>
        /// Asserts that this cryptor has not been destroyed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">If the cryptor has been destroyed</exception>
        private void AssertNotDestroyed()
        {
            if (IsDestroyed())
            {
                throw new ObjectDisposedException(nameof(CryptorImpl), "Cryptor has been destroyed");
            }
        }
    }
} 
