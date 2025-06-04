using System;
using System.Security.Cryptography;

namespace UvfLib.Common
{
    /// <summary>
    /// Represents a secret key whose material can be securely destroyed.
    /// Similar to javax.security.auth.Destroyable and Java's DestroyableSecretKey.
    /// </summary>
    public sealed class DestroyableSecretKey : IDisposable
    {
        private byte[] _keyMaterial;
        private bool _destroyed;
        private readonly string _algorithm; // Optional: Store algorithm like Java

        /// <summary>
        /// Gets the algorithm name associated with this key.
        /// </summary>
        public string Algorithm
        {
            get
            {
                if (_destroyed)
                {
                    throw new InvalidOperationException("Key has been destroyed.");
                }
                return _algorithm;
            }
        }

        /// <summary>
        /// Checks if the key has been destroyed.
        /// </summary>
        public bool IsDestroyed => _destroyed;

        /// <summary>
        /// Creates a new DestroyableSecretKey by copying the provided key material.
        /// </summary>
        /// <param name="keyMaterial">The secret key bytes.</param>
        /// <param name="algorithm">The algorithm name (e.g., "AES", "HmacSHA256").</param>
        public DestroyableSecretKey(byte[] keyMaterial, string algorithm)
        {
            if (keyMaterial == null) throw new ArgumentNullException(nameof(keyMaterial));
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            _keyMaterial = new byte[keyMaterial.Length];
            Buffer.BlockCopy(keyMaterial, 0, _keyMaterial, 0, keyMaterial.Length);
            _algorithm = algorithm;
            _destroyed = false;
        }

        /// <summary>
        /// Creates a new DestroyableSecretKey by copying a portion of the provided key material.
        /// </summary>
        /// <param name="keyMaterial">The source byte array.</param>
        /// <param name="offset">The starting offset in the source array.</param>
        /// <param name="length">The number of bytes to copy.</param>
        /// <param name="algorithm">The algorithm name.</param>
        public DestroyableSecretKey(byte[] keyMaterial, int offset, int length, string algorithm)
        {
            if (keyMaterial == null) throw new ArgumentNullException(nameof(keyMaterial));
            if (algorithm == null) throw new ArgumentNullException(nameof(algorithm));
            if (offset < 0 || length < 0 || offset + length > keyMaterial.Length)
                throw new ArgumentOutOfRangeException("Invalid offset or length.");

            _keyMaterial = new byte[length];
            Buffer.BlockCopy(keyMaterial, offset, _keyMaterial, 0, length);
            _algorithm = algorithm;
            _destroyed = false;
        }

        /// <summary>
        /// Returns a copy of the key material.
        /// </summary>
        /// <returns>A copy of the key bytes.</returns>
        /// <exception cref="InvalidOperationException">If the key has been destroyed.</exception>
        public byte[] GetEncoded()
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Key has been destroyed.");
            }
            // Return a copy to prevent external modification of the internal array
            byte[] copy = new byte[_keyMaterial.Length];
            Buffer.BlockCopy(_keyMaterial, 0, copy, 0, _keyMaterial.Length);
            return copy;
        }

        /// <summary>
        /// Securely destroys the key material by overwriting it with zeroes.
        /// </summary>
        public void Destroy()
        {
            if (!_destroyed)
            {
                CryptographicOperations.ZeroMemory(_keyMaterial);
                _destroyed = true;
            }
        }

        /// <summary>
        /// Disposes the key by calling Destroy().
        /// </summary>
        public void Dispose()
        {
            Destroy();
        }
    }
}