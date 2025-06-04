using System;
using System.Security.Cryptography;
using UvfLib.Api; // For Masterkey interface, now explicitly used below
using UvfLib.Common; // For DestroyableSecretKey and CryptographicOperations

namespace UvfLib.Common
{
    /// <summary>
    /// Represents a perpetual masterkey consisting of an encryption key and a MAC key.
    /// This is typically used in older Cryptomator vault formats.
    /// </summary>
    public sealed class PerpetualMasterkey : UvfLib.Api.DestroyableMasterkey // Implement DestroyableMasterkey
    {
        public const string ENC_ALG = "AES";
        public const string MAC_ALG = "HmacSHA256";
        public const int SUBKEY_LEN_BYTES = 32;

        private readonly byte[] _key;
        private bool _destroyed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PerpetualMasterkey"/> class.
        /// The provided key must be exactly 64 bytes (32 for encryption, 32 for MAC).
        /// </summary>
        /// <param name="key">The raw byte array containing both encryption and MAC keys.</param>
        /// <exception cref="ArgumentNullException">If key is null.</exception>
        /// <exception cref="ArgumentException">If key length is not 64 bytes.</exception>
        public PerpetualMasterkey(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != SUBKEY_LEN_BYTES + SUBKEY_LEN_BYTES)
            {
                throw new ArgumentException($"Invalid raw key length. Expected {SUBKEY_LEN_BYTES + SUBKEY_LEN_BYTES} bytes, got {key.Length}", nameof(key));
            }
            _key = new byte[key.Length];
            _destroyed = false;
            Buffer.BlockCopy(key, 0, _key, 0, key.Length);
        }

        /// <summary>
        /// Generates a new PerpetualMasterkey using a cryptographic secure random number generator.
        /// </summary>
        /// <param name="csprng">The random number generator to use.</param>
        /// <returns>A new PerpetualMasterkey.</returns>
        public static PerpetualMasterkey Generate(RandomNumberGenerator csprng)
        {
            if (csprng == null) throw new ArgumentNullException(nameof(csprng));
            byte[] key = new byte[SUBKEY_LEN_BYTES + SUBKEY_LEN_BYTES];
            try
            {
                csprng.GetBytes(key);
                return new PerpetualMasterkey(key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        /// <summary>
        /// Creates a PerpetualMasterkey from separate encryption and MAC keys.
        /// </summary>
        /// <param name="encKey">The encryption key (must be 32 bytes).</param>
        /// <param name="macKey">The MAC key (must be 32 bytes).</param>
        /// <returns>A new PerpetualMasterkey.</returns>
        /// <exception cref="ArgumentNullException">If encKey or macKey is null.</exception>
        public static PerpetualMasterkey From(DestroyableSecretKey encKey, DestroyableSecretKey macKey)
        {
            if (encKey == null) throw new ArgumentNullException(nameof(encKey));
            if (macKey == null) throw new ArgumentNullException(nameof(macKey));

            byte[] encKeyBytes = encKey.GetEncoded();
            byte[] macKeyBytes = macKey.GetEncoded();

            if (encKeyBytes.Length != SUBKEY_LEN_BYTES) throw new ArgumentException("Invalid key length of encKey", nameof(encKey));
            if (macKeyBytes.Length != SUBKEY_LEN_BYTES) throw new ArgumentException("Invalid key length of macKey", nameof(macKey));

            byte[] key = new byte[SUBKEY_LEN_BYTES + SUBKEY_LEN_BYTES];
            try
            {
                Buffer.BlockCopy(encKeyBytes, 0, key, 0, SUBKEY_LEN_BYTES);
                Buffer.BlockCopy(macKeyBytes, 0, key, SUBKEY_LEN_BYTES, SUBKEY_LEN_BYTES);
                return new PerpetualMasterkey(key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                // Note: Original Java code doesn't zero encKeyBytes/macKeyBytes here as they are from DestroyableSecretKey
            }
        }

        /// <summary>
        /// Gets the encryption subkey.
        /// </summary>
        /// <returns>A new DestroyableSecretKey copy of the subkey used for encryption.</returns>
        public DestroyableSecretKey GetEncKey()
        {
            if (_destroyed) throw new ObjectDisposedException(nameof(PerpetualMasterkey));
            return new DestroyableSecretKey(_key, 0, SUBKEY_LEN_BYTES, ENC_ALG);
        }

        /// <summary>
        /// Gets the MAC subkey.
        /// </summary>
        /// <returns>A new DestroyableSecretKey copy of the subkey used for message authentication.</returns>
        public DestroyableSecretKey GetMacKey()
        {
            if (_destroyed) throw new ObjectDisposedException(nameof(PerpetualMasterkey));
            return new DestroyableSecretKey(_key, SUBKEY_LEN_BYTES, SUBKEY_LEN_BYTES, MAC_ALG);
        }

        /// <summary>
        /// Gets the raw byte array containing both encryption and MAC keys.
        /// Returns a clone of the key material.
        /// </summary>
        /// <returns>A clone of the raw key byte array.</returns>
        public byte[] GetRaw()
        {
            if (_destroyed) throw new ObjectDisposedException(nameof(PerpetualMasterkey));
            return (byte[])_key.Clone();
        }

        // --- Masterkey Interface Implementation ---

        /// <inheritdoc/>
        public byte[] GetRootDirId()
        {
            // As per Cryptomator specification for perpetual masterkeys / older formats
            return Array.Empty<byte>();
        }

        /// <inheritdoc/>
        public bool IsDestroyed()
        {
            return _destroyed;
        }

        /// <inheritdoc/>
        public void Destroy()
        {
            Dispose(true);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern
        private void Dispose(bool disposing)
        {
            if (!_destroyed)
            {
                if (disposing)
                {
                    // No other managed objects to dispose in this class currently
                }
                CryptographicOperations.ZeroMemory(_key);
                _destroyed = true;
            }
        }

        ~PerpetualMasterkey()
        {
            Dispose(false);
        }

        // Optional: Equals and GetHashCode if needed for collections/comparison
        public override bool Equals(object? obj)
        {
            if (obj is PerpetualMasterkey other)
            {
                if (_destroyed && other._destroyed) return true; // Both destroyed considered equal
                if (_destroyed || other._destroyed) return false; // One destroyed, other not
                return CryptographicOperations.TimeConstantEquals(_key, other._key);
            }
            return false;
        }

        public override int GetHashCode()
        {
            if (_key == null) return 0;
            unchecked // Overflow is fine, just wrap
            {
                int hash = 17;
                foreach (byte b in _key)
                {
                    hash = hash * 31 + b;
                }
                return hash;
            }
        }
    }
} 