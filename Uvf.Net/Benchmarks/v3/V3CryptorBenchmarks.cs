using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;
using System.Security.Cryptography;
using UvfLib._old.v3;
using UvfLib._old.common;
using UvfLib._old.api;

// Adjust namespace to correct benchmark project location
namespace UvfLib.Benchmarks.v3
{
    // Minimal adapter implementing RevolvingMasterkey for benchmark purposes
    internal class BenchmarkRevolvingMasterkeyAdapter : RevolvingMasterkey
    {
        private readonly PerpetualMasterkey _wrappedKey;

        public BenchmarkRevolvingMasterkeyAdapter(PerpetualMasterkey wrappedKey)
        {
            _wrappedKey = wrappedKey ?? throw new ArgumentNullException(nameof(wrappedKey));
        }

        // --- Masterkey Methods (Delegated) ---
        public DestroyableSecretKey GetEncKey() => _wrappedKey.GetEncKey();
        public DestroyableSecretKey GetMacKey() => _wrappedKey.GetMacKey();
        public byte[] GetRaw() => _wrappedKey.GetRaw();
        public void Destroy() => _wrappedKey.Destroy();
        public bool IsDestroyed() => _wrappedKey.IsDestroyed();
        public void Dispose() => _wrappedKey.Dispose();
        public byte[] GetRootDirId() => _wrappedKey.GetRootDirId();


        // --- RevolvingMasterkey Specific Methods (Minimal Implementation for Benchmark) ---
        public int GetCurrentRevision() => 8; // Fixed value for benchmark
        public int GetInitialRevision() => 8; // Fixed value
        public int GetFirstRevision() => 8; // Fixed value
        public bool HasRevision(int revision) => revision == 8; // Only support fixed revision

        public DestroyableMasterkey Current()
        {
            // Assume PerpetualMasterkey can be used as DestroyableMasterkey
            if (_wrappedKey is DestroyableMasterkey dm) return dm;
            throw new NotSupportedException("Wrapped PerpetualMasterkey cannot be cast to DestroyableMasterkey");
        }

        public DestroyableMasterkey GetBySeedId(string seedId)
        {
            throw new NotSupportedException("GetBySeedId not supported for benchmark adapter");
        }

        public DestroyableSecretKey SubKey(int seedId, int size, byte[] context, string algorithm)
        {
            // Simple HKDF derivation using perpetual key for benchmark setup
            byte[] master = _wrappedKey.GetRaw();
            byte[] derived = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, size, salt: context, info: System.Text.Encoding.UTF8.GetBytes($"BenchmarkDerivation_{seedId}"));
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(master); // Zero out the copy
            return new DestroyableSecretKey(derived, algorithm);
        }
    }

    [MemoryDiagnoser] // Add memory diagnoser to check allocations
    public class V3CryptorBenchmarks
    {
        private const int DataSize = 32 * 1024; // 32 KiB, similar to Cryptomator chunk size

        private byte[]? _masterKeyBytes;
        private PerpetualMasterkey? _perpetualMasterkey;
        private BenchmarkRevolvingMasterkeyAdapter? _revolvingMasterkeyAdapter; // Use the adapter
        private RandomNumberGenerator? _rng;
        private FileContentCryptorImpl? _fileContentCryptor;
        private FileHeaderImpl? _header;
        private byte[]? _plaintextChunk;
        private byte[]? _ciphertextChunk;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _masterKeyBytes = new byte[64]; // PerpetualMasterkey needs 64 bytes (2x32)
            RandomNumberGenerator.Fill(_masterKeyBytes);
            _perpetualMasterkey = new PerpetualMasterkey(_masterKeyBytes);
            _revolvingMasterkeyAdapter = new BenchmarkRevolvingMasterkeyAdapter(_perpetualMasterkey);

            _rng = RandomNumberGenerator.Create();

            // Pass the adapter implementing RevolvingMasterkey
            _fileContentCryptor = new FileContentCryptorImpl(_revolvingMasterkeyAdapter, _rng);

            byte[] headerNonce = new byte[FileHeaderImpl.NONCE_LEN];
            _rng.GetBytes(headerNonce);
            byte[] contentKeyBytes = new byte[32];
            _rng.GetBytes(contentKeyBytes);
            var contentKey = new DestroyableSecretKey(contentKeyBytes, "ContentKey");
            _header = new FileHeaderImpl(8, headerNonce, contentKey);

            _plaintextChunk = new byte[DataSize];
            RandomNumberGenerator.Fill(_plaintextChunk);

            if (_fileContentCryptor == null || _header == null || _plaintextChunk == null)
            {
                throw new InvalidOperationException("Setup failed: fields null");
            }
            _ciphertextChunk = _fileContentCryptor.EncryptChunk(_plaintextChunk.AsMemory(), 0, _header).ToArray();
        }

        [Benchmark]
        public byte[] EncryptChunkBenchmark()
        {
            if (_fileContentCryptor == null || _header == null || _plaintextChunk == null)
            {
                throw new InvalidOperationException("Benchmark setup invalid");
            }
            return _fileContentCryptor.EncryptChunk(_plaintextChunk.AsMemory(), 0, _header).ToArray();
        }

        [Benchmark]
        public byte[] DecryptChunkBenchmark()
        {
            if (_fileContentCryptor == null || _header == null || _ciphertextChunk == null)
            {
                throw new InvalidOperationException("Benchmark setup invalid");
            }
            return _fileContentCryptor.DecryptChunk(_ciphertextChunk.AsMemory(), 0, _header, true).ToArray();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            // Dispose the adapter, which should delegate to the perpetual key
            _revolvingMasterkeyAdapter?.Dispose();
            _rng?.Dispose();
            _header?.Dispose();
        }
    }
}