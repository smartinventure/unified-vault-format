using System;
using System.IO;
using System.Security.Cryptography;

namespace UvfLib.Common
{
    /// <summary>
    /// Represents an Elliptic Curve Key Pair that can be used for signing and verification
    /// </summary>
    public class ECKeyPair : IDisposable
    {
        private ECDsa _keyPair;
        private ECCurve _curve;
        private bool _isDestroyed = false;

        /// <summary>
        /// Creates a new ECKeyPair from an existing private key
        /// </summary>
        /// <param name="privateKey">The private key for this key pair</param>
        public ECKeyPair(ECDsa privateKey)
        {
            _keyPair = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
            _curve = privateKey.ExportParameters(false).Curve;
        }

        /// <summary>
        /// Creates a new ECKeyPair from an existing private key with a specific curve
        /// </summary>
        /// <param name="privateKey">The private key for this key pair</param>
        /// <param name="curve">The EC curve for this key pair</param>
        public ECKeyPair(ECDsa privateKey, ECCurve curve)
        {
            _keyPair = privateKey ?? throw new ArgumentNullException(nameof(privateKey));
            _curve = curve;
        }

        /// <summary>
        /// Gets the private key used by this key pair
        /// </summary>
        public ECDsa PrivateKey => _isDestroyed ? throw new InvalidOperationException("Key pair has been destroyed") : _keyPair;

        /// <summary>
        /// Generates a new EC Key Pair using the provided curve
        /// </summary>
        /// <param name="curve">The EC curve to use for key generation</param>
        /// <returns>A new ECKeyPair</returns>
        public static ECKeyPair Generate(ECCurve curve)
        {
            ECDsa ecdsa = ECDsa.Create(curve);
            return new ECKeyPair(ecdsa, curve);
        }

        /// <summary>
        /// Creates an ECKeyPair from an existing private key
        /// </summary>
        /// <param name="privateKeyBytes">The private key in PKCS#8 format</param>
        /// <param name="curve">The EC curve used for this key</param>
        /// <returns>A new ECKeyPair</returns>
        public static ECKeyPair FromPrivateKey(byte[] privateKeyBytes, ECCurve curve)
        {
            if (privateKeyBytes == null || privateKeyBytes.Length == 0)
            {
                throw new ArgumentException("Invalid private key", nameof(privateKeyBytes));
            }

            ECDsa ecdsa = ECDsa.Create(curve);
            ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);
            return new ECKeyPair(ecdsa, curve);
        }

        /// <summary>
        /// Gets the public key parameters
        /// </summary>
        /// <returns>The public key ECParameters</returns>
        public ECParameters GetPublic()
        {
            if (_isDestroyed)
                throw new InvalidOperationException("Key pair has been destroyed");

            return _keyPair.ExportParameters(false);
        }

        /// <summary>
        /// Gets the private key parameters
        /// </summary>
        /// <returns>The private key ECParameters</returns>
        public ECParameters GetPrivate()
        {
            if (_isDestroyed)
                throw new InvalidOperationException("Key pair has been destroyed");

            return _keyPair.ExportParameters(true);
        }

        /// <summary>
        /// Exports the private key in PKCS#8 format
        /// </summary>
        /// <returns>The private key bytes</returns>
        public byte[] ExportPrivateKeyPkcs8()
        {
            return PrivateKey.ExportPkcs8PrivateKey();
        }

        /// <summary>
        /// Exports the public key in X.509 SubjectPublicKeyInfo format
        /// </summary>
        /// <returns>The public key bytes</returns>
        public byte[] ExportPublicKey()
        {
            return PrivateKey.ExportSubjectPublicKeyInfo();
        }

        /// <summary>
        /// Signs the provided data using this key pair's private key
        /// </summary>
        /// <param name="data">The data to sign</param>
        /// <returns>The signature bytes</returns>
        public byte[] SignData(byte[] data)
        {
            return PrivateKey.SignData(data, HashAlgorithmName.SHA256);
        }

        /// <summary>
        /// Verifies that the signature was created for the specified data using the private key
        /// corresponding to this key pair's public key
        /// </summary>
        /// <param name="data">The data to verify</param>
        /// <param name="signature">The signature to verify</param>
        /// <returns>True if the signature is valid, false otherwise</returns>
        public bool VerifyData(byte[] data, byte[] signature)
        {
            return PrivateKey.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }

        /// <summary>
        /// Verifies that a public key is valid
        /// </summary>
        /// <param name="publicKey">The public key bytes to verify</param>
        /// <param name="curve">The expected curve of the public key</param>
        /// <returns>True if the public key is valid, false otherwise</returns>
        public static bool VerifyPublicKey(byte[] publicKey, ECCurve curve)
        {
            if (publicKey == null || publicKey.Length == 0)
            {
                return false;
            }

            try
            {
                ECDsa ecdsa = ECDsa.Create(curve);
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Destroys the key pair, making it unusable
        /// </summary>
        public void Destroy()
        {
            if (!_isDestroyed)
            {
                _keyPair.Dispose();
                _keyPair = null;
                _isDestroyed = true;
            }
        }

        /// <summary>
        /// Checks if the key pair has been destroyed
        /// </summary>
        /// <returns>True if the key pair has been destroyed, false otherwise</returns>
        public bool IsDestroyed()
        {
            return _isDestroyed;
        }

        /// <summary>
        /// Disposes the key pair
        /// </summary>
        public void Dispose()
        {
            Destroy();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Determines whether this instance and another specified ECKeyPair object have the same value.
        /// </summary>
        /// <param name="obj">The ECKeyPair to compare to this instance.</param>
        /// <returns>true if the value of the obj parameter is the same as this instance; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            if (obj == null || obj.GetType() != typeof(ECKeyPair))
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            ECKeyPair other = (ECKeyPair)obj;
            if (_isDestroyed || other.IsDestroyed())
                return false;

            // Compare the private key parameters
            ECParameters thisParams = GetPrivate();
            ECParameters otherParams = other.GetPrivate();

            return CryptographicOperations.FixedTimeEquals(thisParams.D, otherParams.D);
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A 32-bit signed integer hash code.</returns>
        public override int GetHashCode()
        {
            if (_isDestroyed)
                return 0;

            // Use the D value from the private key for the hash code
            return BitConverter.ToInt32(GetPrivate().D, 0);
        }
    }
}