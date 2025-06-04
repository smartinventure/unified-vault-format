using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization; // Required for JsonPropertyName, etc.
using UvfLib.Api;
using UvfLib.Common;
using UvfLib.Jwe; // For UvfMasterkeyPayload and its sub-classes
using Jose; // For Base64Url if not using a custom one
using System.Diagnostics.CodeAnalysis;
using System.Globalization; // For DateTimeStyles

namespace UvfLib.V3
{
    /// <summary>
    /// Implementation of the UVFMasterkey interface for Universal Vault Format.
    /// (Original structure before refactoring attempt)
    /// </summary>
    public sealed class UVFMasterkeyImpl : UVFMasterkey, DestroyableMasterkey, RevolvingMasterkey, Api.Masterkey
    {
        private static readonly byte[] ROOT_DIRID_KDF_CONTEXT = Encoding.ASCII.GetBytes("rootDirId");

        private readonly Dictionary<int, byte[]> _seeds; // Represents parsed seeds from payload
        private byte[]? _kdfSalt; // From payload.kdf.salt
        private int _initialSeedId; // Derived from the seed with the earliest creation or explicitly marked
        private int _latestSeedId;  // Derived from the seed with the latest creation or explicitly marked
        
        // Store the primary encryption and HMAC keys from the payload
        private byte[]? _primaryEncryptionKey;
        private byte[]? _primaryHmacKey;
        private string? _rootDirIdValue; // From payload.rootDirId

        private bool _destroyed = false;

        // Properties to implement UVFMasterkey interface
        public Dictionary<int, byte[]> Seeds => _seeds; // Consider returning a read-only view or copy
        public byte[] KdfSalt => _kdfSalt != null ? (byte[])_kdfSalt.Clone() : Array.Empty<byte>();
        
        // InitialSeed and LatestSeed might need re-evaluation based on how they are determined from payload.seeds
        public int InitialSeed => _initialSeedId;
        public int LatestSeed => _latestSeedId;
        public byte[] RootDirId => GetRootDirId(); // Always return the derived RootDirId, ensuring consistency
        public int FirstRevision => GetFirstRevision(); // This likely maps to initialSeedId

        // Properties to implement Masterkey interface
        public bool IsDestroyed => _destroyed;

        /// <summary>
        /// Creates a new UVF masterkey.
        /// </summary>
        /// <param name="seeds">The seeds</param>
        /// <param name="kdfSalt">The KDF salt</param>
        /// <param name="initialSeed">The initial seed ID</param>
        /// <param name="latestSeed">The latest seed ID</param>
        public UVFMasterkeyImpl(Dictionary<int, byte[]> seeds, byte[] kdfSalt, int initialSeed, int latestSeed)
        {
            if (seeds == null)
                throw new ArgumentNullException(nameof(seeds));
            if (kdfSalt == null)
                throw new ArgumentNullException(nameof(kdfSalt));

            // Defensive copy of seeds
            _seeds = new Dictionary<int, byte[]>(seeds.Count);
            foreach (var entry in seeds)
            {
                byte[] seedCopy = new byte[entry.Value.Length];
                Buffer.BlockCopy(entry.Value, 0, seedCopy, 0, entry.Value.Length);
                _seeds.Add(entry.Key, seedCopy);
            }

            // Defensive copy of salt
            _kdfSalt = new byte[kdfSalt.Length];
            Buffer.BlockCopy(kdfSalt, 0, _kdfSalt, 0, kdfSalt.Length);

            _initialSeedId = initialSeed;
            _latestSeedId = latestSeed;
            _destroyed = false;
        }

        /// <summary>
        /// Creates a UVF masterkey from a UvfMasterkeyPayload (JWE format).
        /// </summary>
        /// <param name="payload">The payload parsed from JWE</param>
        private UVFMasterkeyImpl(UvfMasterkeyPayload payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            _seeds = new Dictionary<int, byte[]>();
            
            // Parse keys from payload
            if (payload.Keys != null)
            {
                foreach (var key in payload.Keys)
                {
                    if (key.Purpose == "org.cryptomator.masterkey" && key.Alg == "AES-256-RAW")
                    {
                        _primaryEncryptionKey = Jose.Base64Url.Decode(key.Value);
                    }
                    else if (key.Purpose == "org.cryptomator.hmacMasterkey" && key.Alg == "HMAC-SHA256-RAW")
                    {
                        _primaryHmacKey = Jose.Base64Url.Decode(key.Value);
                    }
                }
            }

            // Parse KDF salt
            if (payload.Kdf != null && !string.IsNullOrEmpty(payload.Kdf.Salt))
            {
                _kdfSalt = Jose.Base64Url.Decode(payload.Kdf.Salt);
            }

            // Parse seeds and determine initial/latest based on Created timestamps
            if (payload.Seeds != null && payload.Seeds.Any())
            {
                DateTimeOffset earliestTime = DateTimeOffset.MaxValue;
                DateTimeOffset latestTime = DateTimeOffset.MinValue;
                int tempInitialSeedId = 0;
                int tempLatestSeedId = 0;
                bool firstSeedProcessed = false;

                foreach (var seedEntry in payload.Seeds)
                {
                    if (!string.IsNullOrEmpty(seedEntry.Id) && !string.IsNullOrEmpty(seedEntry.Value) && !string.IsNullOrEmpty(seedEntry.Created))
                    {
                        int currentSeedId = SeedIdConverter.ToInt(seedEntry.Id);
                        byte[] currentSeedValue = Jose.Base64Url.Decode(seedEntry.Value);
                        _seeds[currentSeedId] = currentSeedValue;

                        if (DateTimeOffset.TryParseExact(seedEntry.Created, "yyyy-MM-ddTHH:mm:ssZ", 
                                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, 
                                out DateTimeOffset createdTime))
                        {
                            if (!firstSeedProcessed)
                            {
                                earliestTime = createdTime;
                                latestTime = createdTime;
                                tempInitialSeedId = currentSeedId;
                                tempLatestSeedId = currentSeedId;
                                firstSeedProcessed = true;
                            }
                            else
                            {
                                if (createdTime < earliestTime)
                                {
                                    earliestTime = createdTime;
                                    tempInitialSeedId = currentSeedId;
                                }
                                if (createdTime > latestTime)
                                {
                                    latestTime = createdTime;
                                    tempLatestSeedId = currentSeedId;
                                }
                            }
                        }
                        else
                        {
                            // Handle or log parsing error for Created timestamp if necessary
                            // For now, if a timestamp is invalid, this seed won't be considered for initial/latest
                            // based on time. This could lead to fallback to min/max if all timestamps are bad.
                             Debug.WriteLine($"Warning: Could not parse Created timestamp '{seedEntry.Created}' for seed ID '{seedEntry.Id}'.");
                        }
                    }
                }
                
                if(firstSeedProcessed) // Ensure at least one valid seed with timestamp was processed
                {
                    _initialSeedId = tempInitialSeedId;
                    _latestSeedId = tempLatestSeedId;
                }
                else if (_seeds.Any()) // Fallback if no valid Created timestamps, revert to Min/Max of numerical IDs
                {
                    Debug.WriteLine("Warning: No valid Created timestamps found for seeds. Falling back to Min/Max of seed IDs for initial/latest.");
                    _initialSeedId = _seeds.Keys.Min();
                    _latestSeedId = _seeds.Keys.Max();
                }
                else // No seeds at all
                {
                    _initialSeedId = 0;
                    _latestSeedId = 0;
                }
            }
            else
            {
                _initialSeedId = 0;
                _latestSeedId = 0;
            }

            // Parse root directory ID
            _rootDirIdValue = payload.RootDirId;

            _destroyed = false;
        }

        /// <summary>
        /// Creates a UVF masterkey from a JSON payload string (decrypted from JWE).
        /// </summary>
        /// <param name="jsonPayload">The JSON payload string</param>
        /// <returns>A UVF masterkey</returns>
        public static UVFMasterkey FromDecryptedPayload(string jsonPayload)
        {
            if (string.IsNullOrEmpty(jsonPayload))
                throw new ArgumentException("JSON payload must not be null or empty", nameof(jsonPayload));

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Helpful if casing mismatches UVF spec
                };
                UvfMasterkeyPayload? payload = JsonSerializer.Deserialize<UvfMasterkeyPayload>(jsonPayload, options);

                if (payload == null)
                {
                    throw new JsonException("Failed to deserialize JSON payload into UvfMasterkeyPayload.");
                }
                
                if (payload.UvfSpecVersion != 1)
                {
                    throw new ArgumentException($"Unsupported UVF specification version: {payload.UvfSpecVersion}");
                }

                return new UVFMasterkeyImpl(payload);
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JsonException in FromDecryptedPayload: {ex.Message}");
                throw new ArgumentException("Invalid JSON payload format for UVFMasterkey.", nameof(jsonPayload), ex);
            }
            catch (Exception ex) // Catch other potential errors during construction
            {
                Debug.WriteLine($"Exception in FromDecryptedPayload: {ex.GetType().Name} - {ex.Message}");
                throw; // Re-throw to indicate failure
            }
        }

        /// <summary>
        /// Generates a UvfMasterkeyPayload object from the current master key state.
        /// This is used when creating a new JWE vault.
        /// </summary>
        public UvfMasterkeyPayload ToMasterkeyPayload()
        {
            ThrowIfDestroyed();

            var payload = new UvfMasterkeyPayload
            {
                UvfSpecVersion = 1, // Current version
                Keys = new List<PayloadKey>()
            };

            if (_primaryEncryptionKey != null)
            {
                payload.Keys.Add(new PayloadKey
                {
                    Id = Jose.Base64Url.Encode(BitConverter.GetBytes(1)), // Example ID, consider a more robust ID generation
                    Purpose = "org.cryptomator.masterkey",
                    Alg = "AES-256-RAW",
                    Value = Jose.Base64Url.Encode(_primaryEncryptionKey)
                });
            }

            if (_primaryHmacKey != null)
            {
                payload.Keys.Add(new PayloadKey
                {
                    Id = Jose.Base64Url.Encode(BitConverter.GetBytes(2)), // Example ID
                    Purpose = "org.cryptomator.hmacMasterkey",
                    Alg = "HMAC-SHA256-RAW",
                    Value = Jose.Base64Url.Encode(_primaryHmacKey)
                });
            }
            
            // Add other keys if the model supports them

            if (_kdfSalt != null && _kdfSalt.Length > 0)
            {
                payload.Kdf = new PayloadKdf
                {
                    Type = "HKDF-SHA512", 
                    Salt = Jose.Base64Url.Encode(_kdfSalt)
                };
            }

            if (_seeds != null && _seeds.Any())
            {
                payload.Seeds = new List<PayloadSeed>();
                // Need a way to get/store 'created' timestamp for seeds if they are to be fully represented.
                // For now, creating minimal seed entries.
                foreach (var seedEntry in _seeds)
                {
                    payload.Seeds.Add(new PayloadSeed
                    {
                        Id = Jose.Base64Url.Encode(BitConverter.GetBytes(seedEntry.Key).Reverse().ToArray()), // Convert to big-endian
                        Value = Jose.Base64Url.Encode(seedEntry.Value),
                        // Created = DateTime.UtcNow.ToString("o") // Placeholder for RFC3339 timestamp
                        // This needs to be the original creation timestamp if available, or omitted if not meaningful
                    });
                }
            }
            
            if (!string.IsNullOrEmpty(_rootDirIdValue))
            {
                payload.RootDirId = _rootDirIdValue; // Already Base64Url encoded
            }

            return payload;
        }
        
        // Constructor from individual components (e.g. for generating a new key from scratch)
        // This constructor needs to be able to generate the content that ToMasterkeyPayload() would serialize.
        public UVFMasterkeyImpl(byte[] primaryEncKey, byte[]? primaryMacKey, Dictionary<int, byte[]>? seeds = null, byte[]? kdfSalt = null, string? rootDirIdBase64 = null, int initialSeed = 0, int latestSeed = 0)
        {
            _primaryEncryptionKey = (byte[])primaryEncKey.Clone();
            _primaryHmacKey = primaryMacKey != null ? (byte[])primaryMacKey.Clone() : null;
            
            _seeds = new Dictionary<int, byte[]>();
            if (seeds != null)
            {
                foreach (var entry in seeds)
                {
                    _seeds.Add(entry.Key, (byte[])entry.Value.Clone());
                }
            }
            
            _kdfSalt = kdfSalt != null ? (byte[])kdfSalt.Clone() : null;
            _rootDirIdValue = rootDirIdBase64;
            _initialSeedId = initialSeed;
            _latestSeedId = latestSeed; // Or derive if seeds are provided

            if (_seeds.Any() && initialSeed == 0 && latestSeed == 0)
            {
                 // Simplified logic: if seeds provided but no initial/latest, pick first/last by key
                _initialSeedId = _seeds.Keys.Min();
                _latestSeedId = _seeds.Keys.Max();
            }
            else if (!_seeds.Any()) // No seeds explicitly given, potentially use primary keys as a default "seed 0"
            {
                 // This part is speculative and depends on how a "seedless" masterkey is defined by UVF.
                 // For now, if no seeds, initial/latest remain 0.
            }
        }

        /// <summary>
        /// Creates a UVF masterkey from raw key material (e.g. newly generated key bytes).
        /// This method should prepare a UVFMasterkeyImpl instance that can then be
        /// serialized via ToMasterkeyPayload() for vault creation.
        /// </summary>
        public static UVFMasterkey CreateFromRaw(byte[] rawKeyEncryption, byte[]? rawKeyMac)
        {
            if (rawKeyEncryption == null || rawKeyEncryption.Length == 0)
                throw new ArgumentNullException(nameof(rawKeyEncryption));

            // For a new key, we typically start with one seed (or no seeds if using direct keys)
            // and no KDF salt unless explicitly defined for derivation from these raw keys.
            // RootDirId can also be generated or left null initially.

            // Example: Create a simple masterkey with the provided raw keys as the primary keys
            // and no initial seeds or specific KDF salt.
            // The 'purpose' and 'alg' for these keys would be set in ToMasterkeyPayload().
            
            // This constructor now sets the internal fields directly.
            return new UVFMasterkeyImpl(rawKeyEncryption, rawKeyMac);
        }
        
        // Overload CreateFromRaw to match the one in the interface UVFMasterkey
        public static UVFMasterkey CreateFromRaw(byte[] rawKey)
        {
             // This existing static factory on the interface expects a single rawKey.
             // How should this single rawKey be interpreted for UVFMasterkeyImpl which has distinct enc/mac keys?
             // Option 1: Assume rawKey is a combined key (e.g., first 32 for enc, next 32 for mac)
             // Option 2: Assume rawKey is only the encryption key, MAC key is derived or not used initially.
             // Option 3: Throw not supported if a single undifferentiated key is insufficient.

            if (rawKey == null) throw new ArgumentNullException(nameof(rawKey));
            if (rawKey.Length < 32) throw new ArgumentException("Raw key material too short.", nameof(rawKey));

            // Example: Assume rawKey is the primary encryption key, and HMAC key is derived or not set initially.
            // This is a placeholder and needs to align with actual UVF spec for "raw key" import.
            byte[] encKey = new byte[32];
            Buffer.BlockCopy(rawKey, 0, encKey, 0, 32);
            
            byte[]? macKey = null;
            if (rawKey.Length >= 64)
            {
                macKey = new byte[32];
                Buffer.BlockCopy(rawKey, 32, macKey, 0, 32);
            }
            // This simplified CreateFromRaw(byte[] rawKey) needs to be carefully considered.
            // The new constructor UVFMasterkeyImpl(byte[] primaryEncKey, byte[]? primaryMacKey, ...) is more explicit.
            return new UVFMasterkeyImpl(encKey, macKey);
        }

        /// <summary>
        /// Gets the version of this master key.
        /// </summary>
        /// <returns>The version</returns>
        public int Version()
        {
            ThrowIfDestroyed();
            return 1; // Current UVF version is 1, as per payload UvfSpecVersion
        }

        /// <summary>
        /// Gets the current revision of this key.
        /// </summary>
        /// <returns>The current revision</returns>
        public int GetCurrentRevision()
        {
            ThrowIfDestroyed();
            return _latestSeedId;
        }

        /// <summary>
        /// Creates a new independent copy of this master key.
        /// </summary>
        /// <returns>A new copy of this master key</returns>
        [return: MaybeNull]
        public UVFMasterkey Copy()
        {
            ThrowIfDestroyed();
            // To properly copy, we should serialize to payload and deserialize,
            // or ensure deep copy of all relevant fields.
            // The constructor from payload (if public) or a dedicated copy constructor is better.
            
            // Simple field-wise copy (adjust if new fields are added):
            var seedsCopy = _seeds.ToDictionary(entry => entry.Key, entry => (byte[])entry.Value.Clone());
            var kdfSaltCopy = _kdfSalt != null ? (byte[])_kdfSalt.Clone() : null;
            var primaryEncCopy = _primaryEncryptionKey != null ? (byte[])_primaryEncryptionKey.Clone() : null;
            var primaryHmacCopy = _primaryHmacKey != null ? (byte[])_primaryHmacKey.Clone() : null;

            if (primaryEncCopy == null) return null; // Cannot copy if essential key is missing

            var copy = new UVFMasterkeyImpl(primaryEncCopy, primaryHmacCopy, seedsCopy, kdfSaltCopy, _rootDirIdValue, _initialSeedId, _latestSeedId);
            return copy;
        }

        /// <summary>
        /// Gets key data based on the master key, that can be used to derive secrets.
        /// </summary>
        /// <param name="context">The context for the derivation (will be UTF-8 encoded)</param>
        /// <returns>The derived key data</returns>
        public byte[] KeyData(string context)
        {
            return KeyData(Encoding.UTF8.GetBytes(context));
        }

        /// <summary>
        /// Gets key data based on the master key, that can be used to derive secrets.
        /// </summary>
        /// <param name="context">The context for the derivation</param>
        /// <returns>The derived key data</returns>
        public byte[] KeyData(byte[] context)
        {
            ThrowIfDestroyed();
            // This method's original logic might need to change significantly
            // based on how keys are structured in the UVF payload.
            // If "key data" refers to the primary encryption key directly:
            if (_primaryEncryptionKey == null) throw new InvalidOperationException("Primary encryption key is not available.");
            
            // If KDF is specified in payload (e.g., HKDF-SHA512) and we have a salt,
            // then KeyData should probably derive from _primaryEncryptionKey using HKDF.
            if (_kdfSalt != null && _primaryEncryptionKey != null)
            {
                 // Assuming HKDF-SHA512 as per typical UVF usage.
                 // The HKDFHelper.Derive method needs to be checked for compatibility.
                 // It expects a master key (IKM), salt, context (info), and output length.
                return HKDFHelper.HkdfSha512(_kdfSalt, _primaryEncryptionKey, context, _primaryEncryptionKey.Length);
            }
            
            // If no KDF or salt, what should KeyData return? The raw primary key? Or is KDF always implied?
            // For now, returning a clone of the primary encryption key if no KDF salt.
            // This behavior needs to be confirmed against UVF specification for KeyData.
            return (byte[])_primaryEncryptionKey.Clone();
        }

        /// <summary>
        /// Gets a deterministically generated unique key identifier for this key.
        /// </summary>
        /// <returns>A key identifier that is unique to this key</returns>
        public byte[] KeyID()
        {
            ThrowIfDestroyed();
            // A key ID should uniquely identify this master key.
            // It could be a hash of the primary key(s) or a specific ID from the payload if available.
            // The UVF payload does not have a single top-level "keyID" for the masterkey itself.
            // It has IDs for individual keys within the "keys" array.
            // For now, deriving from the primary encryption key.
            if (_primaryEncryptionKey == null) throw new InvalidOperationException("Primary encryption key is not available for ID generation.");
            return SHA256.HashData(_primaryEncryptionKey); // Example: SHA256 hash of primary enc key
        }

        /// <summary>
        /// The key ID as a hexadecimal string.
        /// </summary>
        /// <returns>The key ID as a hexadecimal string</returns>
        public string KeyIDHex()
        {
            byte[] keyId = KeyID();
            return Convert.ToHexString(keyId).ToLowerInvariant();
        }

        /// <summary>
        /// Derive a key from this master key.
        /// </summary>
        /// <param name="seedId">Seed identifier</param>
        /// <param name="size">Key size in bytes</param>
        /// <param name="context">Context for key derivation</param>
        /// <param name="algorithm">Algorithm for which this key will be used</param>
        /// <returns>A derived key that must be destroyed after usage</returns>
        public DestroyableSecretKey SubKey(int seedId, int size, byte[] context, string algorithm)
        {
            ThrowIfDestroyed();

            if (context == null)
                throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrEmpty(algorithm))
                throw new ArgumentException("Algorithm must not be null or empty", nameof(algorithm));

            if (!_seeds.TryGetValue(seedId, out byte[] ikm))
                throw new ArgumentException($"No seed for revision {seedId}", nameof(seedId));

            // Use HKDF-SHA512 as specified by kdf property in JSON format
            byte[] subkey = HKDF.DeriveKey(HashAlgorithmName.SHA512, ikm, size, _kdfSalt, context);

            try
            {
                return new DestroyableSecretKey(subkey, algorithm);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(subkey);
            }
        }

        /// <summary>
        /// Gets a copy of the raw key material. Caller is responsible for zeroing out the memory when done.
        /// </summary>
        /// <returns>The raw key material</returns>
        public byte[] GetRaw()
        {
            ThrowIfDestroyed();

            if (_primaryEncryptionKey == null) 
            {
                // Or throw, or return empty, depending on expected behavior if keys are missing.
                return Array.Empty<byte>(); 
            }

            // Concatenate primary encryption key and HMAC key (if it exists)
            // This aligns more with typical GetRaw() expectations for an Api.Masterkey
            int totalLength = _primaryEncryptionKey.Length + (_primaryHmacKey?.Length ?? 0);
            byte[] rawKeyMaterial = new byte[totalLength];
            Buffer.BlockCopy(_primaryEncryptionKey, 0, rawKeyMaterial, 0, _primaryEncryptionKey.Length);
            if (_primaryHmacKey != null)
            {
                Buffer.BlockCopy(_primaryHmacKey, 0, rawKeyMaterial, _primaryEncryptionKey.Length, _primaryHmacKey.Length);
            }
            return rawKeyMaterial;
        }

        /// <summary>
        /// Securely destroys the key material.
        /// </summary>
        public void Destroy()
        {
            if (!_destroyed)
            {
                if (_primaryEncryptionKey != null)
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(_primaryEncryptionKey);
                    _primaryEncryptionKey = null;
                }
                if (_primaryHmacKey != null)
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(_primaryHmacKey);
                    _primaryHmacKey = null;
                }

                foreach (var entry in _seeds.ToList()) // ToList() to allow modification during iteration
                {
                    if (entry.Value != null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(entry.Value);
                }
                _seeds.Clear(); // Clears the dictionary

                if (_kdfSalt != null)
                {
                    System.Security.Cryptography.CryptographicOperations.ZeroMemory(_kdfSalt);
                    _kdfSalt = null;
                }
                _destroyed = true;
            }
        }

        /// <summary>
        /// Disposes of the key, securely destroying it.
        /// </summary>
        public void Dispose()
        {
            Destroy();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gets a copy of the raw key material. The caller is responsible for securely erasing this data when done.
        /// </summary>
        /// <returns>The raw key material</returns>
        public byte[] GetRawKey()
        {
            ThrowIfDestroyed();
            return GetRaw();
        }

        /// <summary>
        /// Gets the current master key.
        /// </summary>
        /// <returns>The current master key</returns>
        public DestroyableMasterkey Current()
        {
            ThrowIfDestroyed();
            return this;
        }

        /// <summary>
        /// Gets the initial revision of this key.
        /// </summary>
        /// <returns>The initial revision</returns>
        public int GetInitialRevision()
        {
            ThrowIfDestroyed();
            return _initialSeedId;
        }

        /// <summary>
        /// Checks if this key has the given revision.
        /// </summary>
        /// <param name="revision">The revision to check</param>
        /// <returns>True if the key has the given revision, false otherwise</returns>
        public bool HasRevision(int revision)
        {
            ThrowIfDestroyed();
            return _seeds.ContainsKey(revision);
        }

        /// <summary>
        /// Gets the root directory ID for this masterkey.
        /// Derivation uses the initialSeed's value as IKM, matching Java's implementation.
        /// </summary>
        /// <returns>The root directory ID</returns>
        public byte[] GetRootDirId()
        {
            ThrowIfDestroyed();
            if (!_seeds.TryGetValue(_initialSeedId, out byte[]? initialSeedValue))
            {
                throw new InvalidOperationException($"Seed value for initialSeed ID {_initialSeedId} not found.");
            }
            if (initialSeedValue == null) // Should not happen if TryGetValue succeeds and value is not null
            {
                 throw new InvalidOperationException($"Initial seed value for ID {_initialSeedId} is null.");
            }
            if (_kdfSalt == null)
            {
                throw new InvalidOperationException("KDF salt is not available for RootDirId derivation.");
            }
            return HKDF.DeriveKey(HashAlgorithmName.SHA512, initialSeedValue, 32, _kdfSalt, ROOT_DIRID_KDF_CONTEXT);
        }

        /// <summary>
        /// Gets the first revision of this key.
        /// </summary>
        /// <returns>The first revision</returns>
        public int GetFirstRevision()
        {
            ThrowIfDestroyed();
            return _initialSeedId;
        }

        /// <summary>
        /// Gets a master key by its seed ID.
        /// </summary>
        /// <param name="seedId">The seed ID</param>
        /// <returns>The master key</returns>
        /// <exception cref="ArgumentException">If no key with the given seed ID exists</exception>
        public DestroyableMasterkey GetBySeedId(string seedId)
        {
            ThrowIfDestroyed();
            if (string.IsNullOrEmpty(seedId))
                throw new ArgumentNullException(nameof(seedId));

            try
            {
                // Convert seedId from Base64URL to bytes
                byte[] seedIdBytes = Jose.Base64Url.Decode(seedId);

                // Ensure we have 4 bytes for the seedId
                if (seedIdBytes.Length < 4)
                {
                    byte[] paddedBytes = new byte[4];
                    Array.Copy(seedIdBytes, 0, paddedBytes, 4 - seedIdBytes.Length, seedIdBytes.Length);
                    seedIdBytes = paddedBytes;
                }

                int seedIdInt = BitConverter.IsLittleEndian
                    ? BinaryPrimitives.ReadInt32BigEndian(seedIdBytes)
                    : BitConverter.ToInt32(seedIdBytes);

                if (!_seeds.ContainsKey(seedIdInt))
                    throw new ArgumentException($"No seed with string ID \"{seedId}\" (decoded to int ID {seedIdInt}) exists", nameof(seedId));

                return this;
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"Invalid seed ID format: {seedId}", nameof(seedId), ex);
            }
        }

        private void ThrowIfDestroyed()
        {
            if (_destroyed)
            {
                throw new ObjectDisposedException(GetType().FullName, "Masterkey has been destroyed");
            }
        }

        // Explicitly implement Api.Masterkey.IsDestroyed()
        bool Api.Masterkey.IsDestroyed()
        {
            return this._destroyed;
        }
    }
}