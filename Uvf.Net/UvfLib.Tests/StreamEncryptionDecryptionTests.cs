using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Security.Cryptography;
using UvfLib.VaultHelpers;
using UvfLib.Core.V3;
using System.IO;
using System.Collections.Generic;
using UvfLib.Core.Api;

namespace UvfLib.Tests
{
    [TestClass]
    public class StreamEncryptionDecryptionTests
    {
        private const int TestDataSize = 40 * 1024; // 40KB, > 32KB and also more than one chunk
        private const int ReadOffset = 10 * 1024;   // Start reading from 10KB
        private const int ReadLength = 5 * 1024;    // Read 5KB

        [TestMethod]
        public void EncryptThenDecryptPartialStream_ShouldMatchOriginalData()
        {
            // 1. Setup MasterKey and Cryptor
            // For V3, Cryptor is initialized with its master key (RevolvingMasterkey).
            var testSeeds = new Dictionary<int, byte[]> { { 0, new byte[32] } };
            RandomNumberGenerator.Fill(testSeeds[0]); // Fill with random bytes for the seed
            var testKdfSalt = new byte[32];
            RandomNumberGenerator.Fill(testKdfSalt); // Fill with random bytes for KDF salt

            // This RevolvingMasterkey (implemented by UVFMasterkeyImpl) is the key for the vault operations.
            RevolvingMasterkey vaultKey = new UVFMasterkeyImpl(testSeeds, testKdfSalt, 0, 0); 
            
            // Get Cryptor via the public CryptorProvider API
            var cryptorProvider = CryptorProvider.ForScheme(CryptorProvider.Scheme.UVF_DRAFT);
            ICryptor cryptor = cryptorProvider.Provide(vaultKey, RandomNumberGenerator.Create());

            // 2. Generate Original Data
            byte[] originalData = new byte[TestDataSize];
            RandomNumberGenerator.Fill(originalData);

            // 3. Encrypt Data
            byte[] encryptedData;
            using (var originalStream = new MemoryStream(originalData))
            using (var encryptedMemoryStream = new MemoryStream())
            {
                using (var encryptingStream = new EncryptingStream(cryptor, encryptedMemoryStream, true))
                {
                    originalStream.CopyTo(encryptingStream);
                } // EncryptingStream will flush and write header upon dispose
                encryptedData = encryptedMemoryStream.ToArray();
            }

            Assert.IsTrue(encryptedData.Length > TestDataSize, "Encrypted data should be larger due to header and overhead.");

            // 4. Decrypt a Section of the Data
            byte[] decryptedSection = new byte[ReadLength];
            using (var encryptedStream = new MemoryStream(encryptedData))
            using (var decryptingStream = new DecryptingStream(cryptor, encryptedStream, true))
            {
                // Seek to the desired offset in the decrypted stream
                decryptingStream.Seek(ReadOffset, SeekOrigin.Begin);

                // Read the section
                int bytesRead = decryptingStream.Read(decryptedSection, 0, ReadLength);
                Assert.AreEqual(ReadLength, bytesRead, "Should read the requested number of bytes.");
            }

            // 5. Assert the decrypted section matches the original data
            byte[] originalSection = new byte[ReadLength];
            Array.Copy(originalData, ReadOffset, originalSection, 0, ReadLength);

            CollectionAssert.AreEqual(originalSection, decryptedSection, "Decrypted section does not match the original data.");

            // Clean up master key
            vaultKey.Destroy(); // UVFMasterkeyImpl (as RevolvingMasterkey) should implement Destroy
        }
    }
} 