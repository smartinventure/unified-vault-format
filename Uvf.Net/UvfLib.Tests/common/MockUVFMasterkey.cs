using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using UvfLib._old.api;
using UvfLib._old.common;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// Mock implementation of UVFMasterkey for testing purposes
    /// </summary>
    public class MockUVFMasterkey : UVFMasterkey, DestroyableMasterkey
    {
        private readonly Dictionary<int, byte[]> _seedMap;
        private readonly byte[] _kdfSalt;
        private readonly int _initialSeed;
        private readonly int _latestSeed;
        private readonly byte[] _encKey;
        private readonly byte[] _macKey;
        private readonly byte[] _rawKey;
        private bool _destroyed;

        /// <summary>
        /// The dictionary of seed IDs to seed values.
        /// </summary>
        public Dictionary<int, byte[]> Seeds => _seedMap;

        /// <summary>
        /// The KDF salt used for key derivation.
        /// </summary>
        public byte[] KdfSalt => _kdfSalt;

        /// <summary>
        /// The initial seed ID.
        /// </summary>
        public int InitialSeed => _initialSeed;

        /// <summary>
        /// The latest seed ID.
        /// </summary>
        public int LatestSeed => _latestSeed;

        /// <summary>
        /// The root directory ID.
        /// </summary>
        public byte[] RootDirId { get; }

        /// <summary>
        /// The first revision.
        /// </summary>
        public int FirstRevision { get; }

        /// <summary>
        /// Creates a mock UVF masterkey with the specified seeds and salt.
        /// </summary>
        /// <param name="seedMap">Dictionary mapping seed IDs to seed values</param>
        /// <param name="kdfSalt">Salt value for key derivation</param>
        /// <param name="initialSeed">ID of the initial seed</param>
        /// <param name="latestSeed">ID of the latest seed</param>
        public MockUVFMasterkey(
            Dictionary<int, byte[]> seedMap,
            byte[] kdfSalt,
            int initialSeed,
            int latestSeed)
        {
            _seedMap = seedMap ?? throw new ArgumentNullException(nameof(seedMap));
            _kdfSalt = kdfSalt ?? throw new ArgumentNullException(nameof(kdfSalt));
            _initialSeed = initialSeed;
            _latestSeed = latestSeed;

            // Generate a root directory ID for testing
            RootDirId = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(RootDirId);
            }

            FirstRevision = 1;

            _rawKey = new byte[64]; // Assuming KeyLength = 64 based on the error
            _encKey = new byte[32]; // Assuming EncKeyLength = 32
            _macKey = new byte[32]; // Assuming MacKeyLength = 32

            // Generate deterministic key for testing
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(_rawKey);
            }

            // Split the key
            Buffer.BlockCopy(_rawKey, 0, _encKey, 0, _encKey.Length);
            Buffer.BlockCopy(_rawKey, _encKey.Length, _macKey, 0, _macKey.Length);

            _destroyed = false;
        }

        /// <summary>
        /// Gets the encryption key component
        /// </summary>
        public byte[] GetEncKey() => !_destroyed ? _encKey : throw new InvalidOperationException("Key has been destroyed");

        /// <summary>
        /// Gets the MAC key component
        /// </summary>
        public byte[] GetMacKey() => !_destroyed ? _macKey : throw new InvalidOperationException("Key has been destroyed");

        /// <summary>
        /// Securely destroys the key material
        /// </summary>
        public void Destroy()
        {
            if (!_destroyed)
            {
                Array.Clear(_rawKey, 0, _rawKey.Length);
                Array.Clear(_encKey, 0, _encKey.Length);
                Array.Clear(_macKey, 0, _macKey.Length);
                _destroyed = true;
            }
        }

        /// <summary>
        /// Indicates whether this key has been destroyed
        /// </summary>
        /// <returns>True if the key has been destroyed, false otherwise</returns>
        public bool IsDestroyed() => _destroyed;

        /// <summary>
        /// Gets a copy of the raw key material
        /// </summary>
        /// <returns>A copy of the raw key</returns>
        public byte[] GetRaw()
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Key has been destroyed");
            }

            var copy = new byte[_rawKey.Length];
            Buffer.BlockCopy(_rawKey, 0, copy, 0, _rawKey.Length);
            return copy;
        }

        /// <summary>
        /// Gets the raw key material for the DestroyableMasterkey interface
        /// </summary>
        /// <returns>The raw key material</returns>
        public byte[] GetRawKey()
        {
            return GetRaw();
        }

        /// <summary>
        /// Gets the current revision
        /// </summary>
        /// <returns>The current revision</returns>
        public int GetCurrentRevision() => 1;

        /// <summary>
        /// Gets the initial revision
        /// </summary>
        /// <returns>The initial revision</returns>
        public int GetInitialRevision() => 1;

        /// <summary>
        /// Gets the first revision
        /// </summary>
        /// <returns>The first revision</returns>
        public int GetFirstRevision() => FirstRevision;

        /// <summary>
        /// Gets the root directory ID
        /// </summary>
        /// <returns>The root directory ID</returns>
        public byte[] GetRootDirId() => RootDirId;

        /// <summary>
        /// Checks if this key has the given revision
        /// </summary>
        /// <param name="revision">The revision to check</param>
        /// <returns>True if the key has the given revision</returns>
        public bool HasRevision(int revision) => revision >= 1 && revision <= GetCurrentRevision();

        /// <summary>
        /// Gets the current master key
        /// </summary>
        /// <returns>The current master key</returns>
        public DestroyableMasterkey Current()
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Key has been destroyed");
            }

            // This is a mock implementation for testing
            return new SimpleMasterkey(_rawKey);
        }

        /// <summary>
        /// Gets a master key by its seed ID
        /// </summary>
        /// <param name="seedId">The seed ID</param>
        /// <returns>The master key</returns>
        public DestroyableMasterkey GetBySeedId(string seedId)
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Key has been destroyed");
            }

            // This is a mock implementation for testing
            if (string.IsNullOrEmpty(seedId))
            {
                throw new ArgumentException("Invalid seed ID", nameof(seedId));
            }

            return new SimpleMasterkey(_rawKey);
        }

        /// <summary>
        /// Derive a key from this master key
        /// </summary>
        /// <param name="seedId">Seed identifier</param>
        /// <param name="size">Key size in bytes</param>
        /// <param name="context">Context for key derivation</param>
        /// <param name="algorithm">Algorithm for which this key will be used</param>
        /// <returns>A derived key</returns>
        public DestroyableSecretKey SubKey(int seedId, int size, byte[] context, string algorithm)
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Key has been destroyed");
            }

            // Generate a deterministic key based on the parameters
            using (var hmac = new HMACSHA256(_rawKey))
            {
                // Create a combined context including seedId and algorithm
                using (var ms = new System.IO.MemoryStream())
                using (var writer = new System.IO.BinaryWriter(ms))
                {
                    writer.Write(seedId);
                    if (context != null)
                    {
                        writer.Write(context.Length);
                        writer.Write(context);
                    }
                    if (algorithm != null)
                    {
                        writer.Write(algorithm);
                    }

                    var combinedContext = ms.ToArray();
                    var hash = hmac.ComputeHash(combinedContext);

                    // Generate a key of the requested size
                    byte[] keyBytes = new byte[size];
                    for (int i = 0; i < size; i++)
                    {
                        keyBytes[i] = hash[i % hash.Length];
                    }

                    return new DestroyableSecretKey(keyBytes, algorithm);
                }
            }
        }

        /// <summary>
        /// Gets the version of this master key.
        /// </summary>
        /// <returns>The version</returns>
        public int Version() => 3;

        /// <summary>
        /// Creates a new independent copy of this master key.
        /// </summary>
        /// <returns>A new copy of this master key</returns>
        [return: MaybeNull]
        public UVFMasterkey Copy()
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Key has been destroyed");
            }

            // Create a deep copy of seeds
            var seedsCopy = new Dictionary<int, byte[]>();
            foreach (var kvp in _seedMap)
            {
                var seedCopy = new byte[kvp.Value.Length];
                Buffer.BlockCopy(kvp.Value, 0, seedCopy, 0, kvp.Value.Length);
                seedsCopy.Add(kvp.Key, seedCopy);
            }

            // Create a copy of KdfSalt
            var kdfSaltCopy = new byte[_kdfSalt.Length];
            Buffer.BlockCopy(_kdfSalt, 0, kdfSaltCopy, 0, _kdfSalt.Length);

            return new MockUVFMasterkey(seedsCopy, kdfSaltCopy, _initialSeed, _latestSeed);
        }

        /// <summary>
        /// Gets key data based on the master key, that can be used to derive secrets.
        /// </summary>
        /// <param name="context">The context for the derivation (will be UTF-8 encoded)</param>
        /// <returns>The derived key data</returns>
        public byte[] KeyData(string context)
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Key has been destroyed");
            }

            return KeyData(Encoding.UTF8.GetBytes(context));
        }

        /// <summary>
        /// Gets key data based on the master key, that can be used to derive secrets.
        /// </summary>
        /// <param name="context">The context for the derivation</param>
        /// <returns>The derived key data</returns>
        public byte[] KeyData(byte[] context)
        {
            if (_destroyed)
            {
                throw new InvalidOperationException("Key has been destroyed");
            }

            // For testing, just return a hash of the context with the raw key
            using (var hmac = new HMACSHA256(_rawKey))
            {
                return hmac.ComputeHash(context);
            }
        }

        /// <summary>
        /// Gets a deterministically generated unique key identifier for this key.
        /// </summary>
        /// <returns>A key identifier that is unique to this key</returns>
        public byte[] KeyID()
        {
            // For testing, return a hash of the raw key
            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(_rawKey);
            }
        }

        /// <summary>
        /// The key ID as a hexadecimal string.
        /// </summary>
        /// <returns>The key ID as a hexadecimal string</returns>
        public string KeyIDHex()
        {
            var keyId = KeyID();
            return BitConverter.ToString(keyId).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Disposes the masterkey, calling Destroy()
        /// </summary>
        public void Dispose()
        {
            Destroy();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Simple implementation of DestroyableMasterkey for testing
        /// </summary>
        private class SimpleMasterkey : DestroyableMasterkey
        {
            private readonly byte[] _key;
            private bool _destroyed;

            public SimpleMasterkey(byte[] key)
            {
                _key = new byte[key.Length];
                Buffer.BlockCopy(key, 0, _key, 0, key.Length);
                _destroyed = false;
            }

            public void Destroy()
            {
                if (!_destroyed)
                {
                    Array.Clear(_key, 0, _key.Length);
                    _destroyed = true;
                }
            }

            public byte[] GetRaw()
            {
                if (_destroyed)
                {
                    throw new InvalidOperationException("Key has been destroyed");
                }

                byte[] copy = new byte[_key.Length];
                Buffer.BlockCopy(_key, 0, copy, 0, _key.Length);
                return copy;
            }

            public byte[] GetRawKey()
            {
                return GetRaw(); // Delegate to GetRaw() for compatibility
            }

            public bool IsDestroyed() => _destroyed;

            public void Dispose()
            {
                Destroy();
                GC.SuppressFinalize(this);
            }
        }

        public Dictionary<int, byte[]> GetSeedMap()
        {
            return _seedMap;
        }

        public byte[] GetKdfSalt()
        {
            return _kdfSalt;
        }

        public int GetInitialSeed()
        {
            return _initialSeed;
        }

        public int GetLatestSeed()
        {
            return _latestSeed;
        }
    }
}