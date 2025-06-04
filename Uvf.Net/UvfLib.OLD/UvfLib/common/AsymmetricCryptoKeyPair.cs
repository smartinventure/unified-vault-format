using System;
using System.Security.Cryptography;

namespace UvfLib.Common
{
    /// <summary>
    /// Represents an asymmetric cryptographic key pair.
    /// </summary>
    public class AsymmetricCryptoKeyPair
    {
        private readonly RSAParameters? _rsaPublicKey;
        private readonly RSAParameters? _rsaPrivateKey;
        private readonly ECParameters? _ecPublicKey;
        private readonly ECParameters? _ecPrivateKey;

        /// <summary>
        /// Whether this key pair is an RSA key pair.
        /// </summary>
        public bool IsRSA => _rsaPublicKey.HasValue;

        /// <summary>
        /// Whether this key pair is an ECDsa key pair.
        /// </summary>
        public bool IsECDsa => _ecPublicKey.HasValue;

        /// <summary>
        /// Creates a new asymmetric key pair from RSA parameters.
        /// </summary>
        /// <param name="publicKey">The public key parameters</param>
        /// <param name="privateKey">The private key parameters</param>
        public AsymmetricCryptoKeyPair(RSAParameters publicKey, RSAParameters privateKey)
        {
            _rsaPublicKey = publicKey;
            _rsaPrivateKey = privateKey;
            _ecPublicKey = null;
            _ecPrivateKey = null;
        }

        /// <summary>
        /// Creates a new asymmetric key pair from EC parameters.
        /// </summary>
        /// <param name="publicKey">The public key parameters</param>
        /// <param name="privateKey">The private key parameters</param>
        public AsymmetricCryptoKeyPair(ECParameters publicKey, ECParameters privateKey)
        {
            _rsaPublicKey = null;
            _rsaPrivateKey = null;
            _ecPublicKey = publicKey;
            _ecPrivateKey = privateKey;
        }

        /// <summary>
        /// Creates a new RSA instance from this key pair.
        /// </summary>
        /// <returns>An RSA instance initialized with this key pair</returns>
        /// <exception cref="InvalidOperationException">If this is not an RSA key pair</exception>
        public RSA CreateRSA()
        {
            if (!IsRSA)
                throw new InvalidOperationException("This is not an RSA key pair");

            RSA rsa = RSA.Create();
            rsa.ImportParameters(_rsaPrivateKey.Value);
            return rsa;
        }

        /// <summary>
        /// Creates a new ECDsa instance from this key pair.
        /// </summary>
        /// <returns>An ECDsa instance initialized with this key pair</returns>
        /// <exception cref="InvalidOperationException">If this is not an ECDsa key pair</exception>
        public ECDsa CreateECDsa()
        {
            if (!IsECDsa)
                throw new InvalidOperationException("This is not an ECDsa key pair");

            ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportParameters(_ecPrivateKey.Value);
            return ecdsa;
        }

        /// <summary>
        /// Creates a key pair from an RSA instance.
        /// </summary>
        /// <param name="rsa">The RSA instance</param>
        /// <returns>A new asymmetric key pair</returns>
        public static AsymmetricCryptoKeyPair FromRSA(RSA rsa)
        {
            if (rsa == null)
                throw new ArgumentNullException(nameof(rsa));

            return new AsymmetricCryptoKeyPair(
                rsa.ExportParameters(false),
                rsa.ExportParameters(true));
        }

        /// <summary>
        /// Creates a key pair from an ECDsa instance.
        /// </summary>
        /// <param name="ecdsa">The ECDsa instance</param>
        /// <returns>A new asymmetric key pair</returns>
        public static AsymmetricCryptoKeyPair FromECDsa(ECDsa ecdsa)
        {
            if (ecdsa == null)
                throw new ArgumentNullException(nameof(ecdsa));

            return new AsymmetricCryptoKeyPair(
                ecdsa.ExportParameters(false),
                ecdsa.ExportParameters(true));
        }

        /// <summary>
        /// Creates a key pair from an ECKeyPair instance.
        /// </summary>
        /// <param name="ecKeyPair">The ECKeyPair instance</param>
        /// <returns>A new asymmetric key pair</returns>
        public static AsymmetricCryptoKeyPair FromECKeyPair(ECKeyPair ecKeyPair)
        {
            if (ecKeyPair == null)
                throw new ArgumentNullException(nameof(ecKeyPair));

            ECDsa ecdsa = ecKeyPair.PrivateKey;
            return FromECDsa(ecdsa);
        }
    }
}