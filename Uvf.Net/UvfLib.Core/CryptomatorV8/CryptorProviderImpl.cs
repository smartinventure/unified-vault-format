// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

using System;
using System.Security.Cryptography;
using UvfLib.Core.Api;
using UvfLib.Core.Common;

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// Provides cryptors for Cryptomator v2 format.
    /// </summary>
    public sealed class CryptorProviderImpl : CryptorProvider
    {
        /// <summary>
        /// Gets the scheme used by this provider.
        /// </summary>
        /// <returns>The cryptographic scheme</returns>
        public CryptorProvider.Scheme GetScheme()
        {
            return CryptorProvider.Scheme.SIV_GCM;
        }

        /// <summary>
        /// Creates a new cryptor instance for the given key.
        /// </summary>
        /// <param name="masterkey">The key used by the returned cryptor during encryption and decryption</param>
        /// <param name="random">A secure random number generator used to seed internal RNGs</param>
        /// <returns>A new cryptor</returns>
        /// <exception cref="ArgumentException">If masterkey is not a PerpetualMasterkey</exception>
        public ICryptor Provide(UvfLib.Core.Api.Masterkey masterkey, RandomNumberGenerator random)
        {
            if (masterkey is PerpetualMasterkey perpetualMasterkey)
            {
                // Create a reseeding secure random generator as in the Java implementation
                var reseedingRandom = CreateReseedingSecureRandom(random);
                return new CryptorImpl(perpetualMasterkey, reseedingRandom);
            }
            else
            {
                throw new ArgumentException("V2 Cryptor requires a PerpetualMasterkey", nameof(masterkey));
            }
        }

        /// <summary>
        /// Creates a reseeding secure random number generator.
        /// This mimics the Java ReseedingSecureRandom behavior.
        /// </summary>
        /// <param name="random">The source random number generator</param>
        /// <returns>A reseeding random number generator</returns>
        private static RandomNumberGenerator CreateReseedingSecureRandom(RandomNumberGenerator random)
        {
            // In a full implementation, this would create a wrapper that reseeds periodically
            // For now, we'll just return the original random generator
            // TODO: Implement proper reseeding logic if needed
            return random;
        }

        public ICryptor Provide(string vaultFormat)
        {
            throw new UvfLib.Core.Api.UnsupportedVaultFormatException(
                new Uri("unknown://"), 
                VaultFormat.Unknown, 
                $"Unsupported vault format: {vaultFormat}");
        }
    }
} 
