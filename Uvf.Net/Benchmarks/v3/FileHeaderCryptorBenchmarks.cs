using BenchmarkDotNet.Attributes;
using System;
using System.Security.Cryptography;
using UvfLib._old.v3;
using UvfLib._old.common;
using UvfLib._old.api;

namespace UvfLib.Benchmarks.v3
{
    [MemoryDiagnoser]
    public class FileHeaderCryptorBenchmarks
    {
        private byte[]? _masterKeyBytes;
        private PerpetualMasterkey? _perpetualMasterkey;
        private BenchmarkRevolvingMasterkeyAdapter? _revolvingMasterkeyAdapter;
        private RandomNumberGenerator? _rng;
        private FileHeaderCryptorImpl? _headerCryptor;

        private FileHeader? _plaintextHeader;
        private byte[]? _encryptedHeaderBytes;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _masterKeyBytes = new byte[64]; // PerpetualMasterkey needs 64 bytes
            RandomNumberGenerator.Fill(_masterKeyBytes);
            _perpetualMasterkey = new PerpetualMasterkey(_masterKeyBytes);
            _revolvingMasterkeyAdapter = new BenchmarkRevolvingMasterkeyAdapter(_perpetualMasterkey);

            _rng = RandomNumberGenerator.Create();

            // Assuming revision 8 for the benchmark
            _headerCryptor = new FileHeaderCryptorImpl(_revolvingMasterkeyAdapter, _rng, 8);

            // Create a header instance for encryption/decryption benchmarks
            _plaintextHeader = _headerCryptor.Create();
            if (_plaintextHeader == null) throw new InvalidOperationException("Failed to create plaintext header in setup.");

            // Encrypt it once to have data for the decryption benchmark
            _encryptedHeaderBytes = _headerCryptor.EncryptHeader(_plaintextHeader).ToArray();
            if (_encryptedHeaderBytes == null) throw new InvalidOperationException("Failed to encrypt header in setup.");
        }

        [Benchmark]
        public FileHeader CreateHeaderBenchmark()
        {
            if (_headerCryptor == null) throw new InvalidOperationException("Setup failed");
            // Benchmark creating a new header (includes random generation)
            return _headerCryptor.Create();
        }

        [Benchmark]
        public byte[] EncryptHeaderBenchmark()
        {
            if (_headerCryptor == null || _plaintextHeader == null) throw new InvalidOperationException("Setup failed");
            // Benchmark encrypting the pre-created header
            return _headerCryptor.EncryptHeader(_plaintextHeader).ToArray();
        }

        [Benchmark]
        public FileHeader DecryptHeaderBenchmark()
        {
            if (_headerCryptor == null || _encryptedHeaderBytes == null) throw new InvalidOperationException("Setup failed");
            // Benchmark decrypting the pre-encrypted header bytes
            return _headerCryptor.DecryptHeader(_encryptedHeaderBytes.AsMemory());
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _revolvingMasterkeyAdapter?.Dispose();
            _rng?.Dispose();
            _plaintextHeader?.Dispose(); // Dispose the header created in setup
            // Note: Headers returned by benchmarks might not be disposed unless explicitly handled
        }
    }
}