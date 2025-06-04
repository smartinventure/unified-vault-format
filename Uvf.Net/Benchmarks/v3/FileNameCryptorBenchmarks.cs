using BenchmarkDotNet.Attributes;
using UvfLib.Api;
using System;
using System.Security.Cryptography;
using System.Text;
using UvfLib._old.v3;
using UvfLib._old.common;

namespace UvfLib.Benchmarks.v3
{
    [MemoryDiagnoser]
    public class FileNameCryptorBenchmarks
    {
        private byte[]? _masterKeyBytes;
        private PerpetualMasterkey? _perpetualMasterkey;
        private BenchmarkRevolvingMasterkeyAdapter? _revolvingMasterkeyAdapter;
        private RandomNumberGenerator? _rng;
        private FileNameCryptorImpl? _fileNameCryptor;

        private string _plaintextFilename = "very_long_filename_to_test_performance_!@#$%^&*()_+=-`~.txt";
        private byte[] _dirId;
        private string? _encryptedFilename;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _masterKeyBytes = new byte[64]; // PerpetualMasterkey needs 64 bytes
            RandomNumberGenerator.Fill(_masterKeyBytes);
            _perpetualMasterkey = new PerpetualMasterkey(_masterKeyBytes);
            _revolvingMasterkeyAdapter = new BenchmarkRevolvingMasterkeyAdapter(_perpetualMasterkey);

            _rng = RandomNumberGenerator.Create();

            // Use current revision (adapter provides 8)
            _fileNameCryptor = new FileNameCryptorImpl(_revolvingMasterkeyAdapter, _rng);

            // Generate a random directory ID for context
            _dirId = new byte[32]; // Example length
            _rng.GetBytes(_dirId);

            // Encrypt once for decryption benchmark
            if (_fileNameCryptor == null || _dirId == null)
            {
                throw new InvalidOperationException("Setup failed");
            }
            _encryptedFilename = _fileNameCryptor.EncryptFilename(_plaintextFilename, _dirId);
        }

        [Benchmark]
        public string EncryptFilenameBenchmark()
        {
            if (_fileNameCryptor == null || _dirId == null)
            {
                throw new InvalidOperationException("Setup failed");
            }
            return _fileNameCryptor.EncryptFilename(_plaintextFilename, _dirId);
        }

        [Benchmark]
        public string DecryptFilenameBenchmark()
        {
            if (_fileNameCryptor == null || _dirId == null || _encryptedFilename == null)
            {
                throw new InvalidOperationException("Setup failed");
            }
            return _fileNameCryptor.DecryptFilename(_encryptedFilename, _dirId);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _revolvingMasterkeyAdapter?.Dispose();
            _rng?.Dispose();
            // Other fields don't require disposal
        }
    }
}