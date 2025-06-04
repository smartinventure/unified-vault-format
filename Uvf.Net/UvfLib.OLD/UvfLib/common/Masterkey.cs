using System;
using System.Security.Cryptography;
using UvfLib.Api;

namespace UvfLib.Common
{
    /// <summary>
    /// Represents a cryptographic master key for a Cryptomator vault.
    /// Implements the Api.Masterkey interface.
    /// </summary>
    public class Masterkey : Api.Masterkey // Implement the interface
    {
        /// <summary>
        /// The expected length of subkeys (encryption and MAC) in bytes.
        /// </summary>
        public const int SubkeyLength = 32;
        /// <summary>
        /// The total key length in bytes (EncKey + MacKey).
        /// </summary>
        public const int KeyLength = SubkeyLength * 2; // 64 bytes total

        private byte[] _rawKey;
        private bool _destroyed;

        // Private constructor used by static factory methods
        private Masterkey(byte[] rawKey, bool copy = true)
        {
            if (rawKey == null || rawKey.Length != KeyLength)
            {
                throw new ArgumentException($"Raw key must be exactly {KeyLength} bytes", nameof(rawKey));
            }

            if (copy)
            {
                _rawKey = new byte[KeyLength];
                Buffer.BlockCopy(rawKey, 0, _rawKey, 0, KeyLength);
            }
            else // Use directly only when generated internally
            {
                _rawKey = rawKey;
            }
            _destroyed = false;
        }

        // --- Interface Implementation ---

        /// <inheritdoc/>
        public byte[] RootDirId()
        {
            // As per Cryptomator spec: https://docs.cryptomator.org/security/vault/#directory-ids
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
            if (!_destroyed)
            {
                CryptographicOperations.ZeroMemory(_rawKey);
                _destroyed = true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Destroy();
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public byte[] GetRaw()
        {
            if (_destroyed) throw new InvalidOperationException("Masterkey has been destroyed");
            byte[] copy = new byte[KeyLength];
            Buffer.BlockCopy(_rawKey, 0, copy, 0, KeyLength);
            return copy;
        }

        // --- Static Factory Methods (Implementing Interface Statics) ---

        /// <summary>
        /// Generates a new Masterkey using the provided (or default) CSPRNG.
        /// </summary>
        public static Masterkey Generate(RandomNumberGenerator? csprng = null)
        {
            byte[] key = new byte[KeyLength];
            try
            {
                var rng = csprng ?? RandomNumberGenerator.Create();
                rng.GetBytes(key);
                // Pass copy=false as we own the newly generated array
                return new Masterkey(key, copy: false);
            }
            catch
            {
                // Ensure key material is cleared if constructor fails
                CryptographicOperations.ZeroMemory(key);
                throw;
            }
            // No finally needed here as ZeroMemory happens on exception,
            // and the key array goes out of scope on success (owned by new Masterkey)
        }

        /// <summary>
        /// Creates a Masterkey from separate encryption and MAC keys.
        /// </summary>
        public static Masterkey From(DestroyableSecretKey encKey, DestroyableSecretKey macKey)
        {
            if (encKey == null) throw new ArgumentNullException(nameof(encKey));
            if (macKey == null) throw new ArgumentNullException(nameof(macKey));
            if (encKey.IsDestroyed) throw new ArgumentException("Encryption key is destroyed", nameof(encKey));
            if (macKey.IsDestroyed) throw new ArgumentException("MAC key is destroyed", nameof(macKey));

            byte[] encKeyBytes = encKey.GetEncoded();
            byte[] macKeyBytes = macKey.GetEncoded();
            byte[] combinedKey = new byte[KeyLength];

            try
            {
                if (encKeyBytes.Length != SubkeyLength)
                    throw new ArgumentException($"Encryption key must be {SubkeyLength} bytes", nameof(encKey));
                if (macKeyBytes.Length != SubkeyLength)
                    throw new ArgumentException($"MAC key must be {SubkeyLength} bytes", nameof(macKey));

                Buffer.BlockCopy(encKeyBytes, 0, combinedKey, 0, SubkeyLength);
                Buffer.BlockCopy(macKeyBytes, 0, combinedKey, SubkeyLength, SubkeyLength);

                // Pass copy=false as we own the newly created combined array
                return new Masterkey(combinedKey, copy: false);
            }
            finally
            {
                // Clear temporary arrays
                CryptographicOperations.ZeroMemory(encKeyBytes);
                CryptographicOperations.ZeroMemory(macKeyBytes);
                // DO NOT zero combinedKey here, its ownership is transferred to the new Masterkey instance when copy: false
                // CryptographicOperations.ZeroMemory(combinedKey); 
            }
        }

        // --- Additional Methods (Not in Java Interface, but useful for C#) ---
        // Optional: Could add GetEncKey/GetMacKey if needed by other C# code

        /// <summary>
        /// Gets the encryption subkey.
        /// </summary>
        /// <returns>A new DestroyableSecretKey containing the encryption key material.</returns>
        public DestroyableSecretKey GetEncKey()
        {
            if (_destroyed) throw new InvalidOperationException("Masterkey has been destroyed");
            return new DestroyableSecretKey(_rawKey, 0, SubkeyLength, "AES"); // Assuming AES
        }

        /// <summary>
        /// Gets the MAC subkey.
        /// </summary>
        /// <returns>A new DestroyableSecretKey containing the MAC key material.</returns>
        public DestroyableSecretKey GetMacKey()
        {
            if (_destroyed) throw new InvalidOperationException("Masterkey has been destroyed");
            return new DestroyableSecretKey(_rawKey, SubkeyLength, SubkeyLength, "HmacSHA256"); // Assuming HmacSHA256
        }

        // Remove methods not aligned with Java interface:
        // public static Masterkey CreateNew() => Generate(); // Replaced by Generate
        // public static Masterkey CreateFromRaw(byte[] rawKey) // Replaced by From (conceptually)
        // public byte[] RawKey => _rawKey; // Removed from public API
        // public MasterkeyFile CreateMasterkeyFile(string passphrase) // Removed - Mock logic, belongs elsewhere
        // public static Masterkey DecryptMasterkey(MasterkeyFile masterkeyFile, string passphrase) // Removed - Mock logic, belongs elsewhere
    }
}