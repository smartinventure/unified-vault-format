using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UvfLib.Vault;

namespace UvfLib.Tests.ChunkAware
{
    [TestClass]
    public class CryptomatorRandomWriteTests
    {
        private const string TestPassword = "TestPassword123!";
        private string _tempDirectory = null!;
        private string _vaultDirectory = null!;
        private string _masterkeyPath = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "CryptomatorRandomWriteTests", Guid.NewGuid().ToString());
            _vaultDirectory = Path.Combine(_tempDirectory, "vault");
            Directory.CreateDirectory(_vaultDirectory);
            _masterkeyPath = Path.Combine(_vaultDirectory, "masterkey.cryptomator");
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [TestMethod]
        public async Task CreateVault_WriteFile_100KB_VerifyChunks()
        {
            // Arrange: Create 100KB test data (should span 4 chunks: 32KB + 32KB + 32KB + 4KB)
            var testData = CreateTestData(100 * 1024); // 100KB
            var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);

            // Act: Create Cryptomator vault and write file
            VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultDirectory, passwordBytes);
            
            using var vault = VaultHandler.LoadCryptomatorV8Vault(File.ReadAllBytes(_masterkeyPath), passwordBytes);
            var testFilePath = Path.Combine(_tempDirectory, "encrypted_test_file.cryptomator");
            
            // Write the 100KB file
            using (var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.Write))
            using (var encryptingStream = vault.GetRandomWriteEncryptingStream(fileStream))
            {
                await encryptingStream.WriteAsync(testData, 0, testData.Length);
                await encryptingStream.FlushAsync();
            }

            // Assert: Verify file was written and can be read back
            using (var fileStream = new FileStream(testFilePath, FileMode.Open, FileAccess.Read))
            using (var decryptingStream = vault.GetDecryptingStream(fileStream))
            {
                var readData = new byte[testData.Length];
                int totalRead = 0;
                int bytesRead;
                while (totalRead < readData.Length && (bytesRead = await decryptingStream.ReadAsync(readData, totalRead, readData.Length - totalRead)) > 0)
                {
                    totalRead += bytesRead;
                }

                Assert.AreEqual(testData.Length, totalRead, "Should read back the same amount of data");
                CollectionAssert.AreEqual(testData, readData, "Data should match exactly");
            }

            // Verify encrypted file size (header + 4 chunks with overhead)
            var encryptedSize = new FileInfo(testFilePath).Length;
            var expectedSize = 68 + (3 * (32 * 1024 + 28)) + (4 * 1024 + 28); // header + 3 full chunks + 1 partial chunk
            Assert.IsTrue(encryptedSize >= expectedSize - 100 && encryptedSize <= expectedSize + 100, 
                $"Encrypted file size {encryptedSize} should be approximately {expectedSize}");
        }

        [TestMethod]
        public async Task RandomWrite_WithinSingleChunk_ShouldSucceed()
        {
            // Arrange
            var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);
            VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultDirectory, passwordBytes);
            
            using var vault = VaultHandler.LoadCryptomatorV8Vault(File.ReadAllBytes(_masterkeyPath), passwordBytes);
            var testFilePath = Path.Combine(_tempDirectory, "random_write_test.cryptomator");

            var initialData = Encoding.UTF8.GetBytes("Hello World! This is a test file for random writes.");
            var modificationData = Encoding.UTF8.GetBytes("MODIFIED");

            // Act: Create file and perform random writes within same chunk
            using (var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.ReadWrite))
            using (var stream = vault.GetRandomWriteEncryptingStream(fileStream))
            {
                // Write initial data
                await stream.WriteAsync(initialData, 0, initialData.Length);
                
                // Seek to position 6 and overwrite "World" with "MODIFIED"
                stream.Seek(6, SeekOrigin.Begin);
                await stream.WriteAsync(modificationData, 0, modificationData.Length);
                
                await stream.FlushAsync();
            }

            // Assert: Verify the modification
            using (var fileStream = new FileStream(testFilePath, FileMode.Open, FileAccess.Read))
            using (var decryptingStream = vault.GetDecryptingStream(fileStream))
            {
                var readData = new byte[initialData.Length];
                await decryptingStream.ReadAsync(readData, 0, readData.Length);
                
                var result = Encoding.UTF8.GetString(readData);
                Assert.IsTrue(result.StartsWith("Hello MODIFIED"), $"Expected 'Hello MODIFIED', got '{result}'");
            }
        }

        [TestMethod]
        public async Task RandomWrite_AcrossTwoChunks_ShouldSucceed()
        {
            // Arrange: Test data that spans across chunk boundary
            var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);
            VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultDirectory, passwordBytes);
            
            using var vault = VaultHandler.LoadCryptomatorV8Vault(File.ReadAllBytes(_masterkeyPath), passwordBytes);
            var testFilePath = Path.Combine(_tempDirectory, "cross_chunk_test.cryptomator");

            // Create initial data that fills first chunk and starts second
            var chunkSize = 32 * 1024;
            var initialData = CreateTestData(chunkSize + 1000); // 32KB + 1000 bytes
            var overlappingData = CreateTestData(10 * 1024); // 10KB that will overlap chunks
            var seekPosition = chunkSize - 5000; // 5KB before chunk boundary

            // Act: Write data that crosses chunk boundary
            using (var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.ReadWrite))
            using (var stream = vault.GetRandomWriteEncryptingStream(fileStream))
            {
                // Write initial data
                await stream.WriteAsync(initialData, 0, initialData.Length);
                
                // Seek to position that will cause write to span two chunks
                stream.Seek(seekPosition, SeekOrigin.Begin);
                await stream.WriteAsync(overlappingData, 0, overlappingData.Length);
                
                await stream.FlushAsync();
            }

            // Assert: Verify the data was written correctly
            using (var fileStream = new FileStream(testFilePath, FileMode.Open, FileAccess.Read))
            using (var decryptingStream = vault.GetDecryptingStream(fileStream))
            {
                // Read the modified section
                decryptingStream.Seek(seekPosition, SeekOrigin.Begin);
                var readData = new byte[overlappingData.Length];
                int totalRead = 0;
                int bytesRead;
                while (totalRead < readData.Length && (bytesRead = await decryptingStream.ReadAsync(readData, totalRead, readData.Length - totalRead)) > 0)
                {
                    totalRead += bytesRead;
                }
                
                Assert.AreEqual(overlappingData.Length, totalRead, "Should read back the overlapping data");
                CollectionAssert.AreEqual(overlappingData, readData, "Overlapping data should match");
            }
        }

        [TestMethod]
        public async Task RandomWrite_AcrossThreeChunks_ShouldSucceed()
        {
            // Arrange: Test data that spans three chunks (66KB)
            var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);
            VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultDirectory, passwordBytes);
            
            using var vault = VaultHandler.LoadCryptomatorV8Vault(File.ReadAllBytes(_masterkeyPath), passwordBytes);
            var testFilePath = Path.Combine(_tempDirectory, "three_chunk_test.cryptomator");

            var chunkSize = 32 * 1024;
            var initialData = CreateTestData(3 * chunkSize); // 96KB (3 full chunks)
            var overlappingData = CreateTestData(66 * 1024); // 66KB spanning 3 chunks
            var seekPosition = chunkSize / 2; // Middle of first chunk

            // Act: Write data across three chunks
            using (var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.ReadWrite))
            using (var stream = vault.GetRandomWriteEncryptingStream(fileStream))
            {
                // Write initial data
                await stream.WriteAsync(initialData, 0, initialData.Length);
                
                // Seek to position in first chunk and write data spanning three chunks
                stream.Seek(seekPosition, SeekOrigin.Begin);
                await stream.WriteAsync(overlappingData, 0, overlappingData.Length);
                
                await stream.FlushAsync();
            }

            // Assert: Verify the data was written correctly
            using (var fileStream = new FileStream(testFilePath, FileMode.Open, FileAccess.Read))
            using (var decryptingStream = vault.GetDecryptingStream(fileStream))
            {
                // Read the modified section
                decryptingStream.Seek(seekPosition, SeekOrigin.Begin);
                var readData = new byte[overlappingData.Length];
                int totalRead = 0;
                int bytesRead;
                while (totalRead < readData.Length && (bytesRead = await decryptingStream.ReadAsync(readData, totalRead, readData.Length - totalRead)) > 0)
                {
                    totalRead += bytesRead;
                }
                
                Assert.AreEqual(overlappingData.Length, totalRead, "Should read back all overlapping data");
                CollectionAssert.AreEqual(overlappingData, readData, "Three-chunk overlapping data should match");
            }
        }

        [TestMethod]
        public async Task MultipleRandomWrites_SameChunk_ShouldSucceed()
        {
            // Arrange
            var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);
            VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultDirectory, passwordBytes);
            
            using var vault = VaultHandler.LoadCryptomatorV8Vault(File.ReadAllBytes(_masterkeyPath), passwordBytes);
            var testFilePath = Path.Combine(_tempDirectory, "multiple_writes_test.cryptomator");

            var initialData = CreateTestData(1024); // 1KB
            var modification1 = Encoding.UTF8.GetBytes("MOD1");
            var modification2 = Encoding.UTF8.GetBytes("MOD2");
            var modification3 = Encoding.UTF8.GetBytes("MOD3");

            // Act: Perform multiple random writes to same chunk
            using (var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.ReadWrite))
            using (var stream = vault.GetRandomWriteEncryptingStream(fileStream))
            {
                // Write initial data
                await stream.WriteAsync(initialData, 0, initialData.Length);
                
                // Multiple random writes within the same chunk
                stream.Seek(100, SeekOrigin.Begin);
                await stream.WriteAsync(modification1, 0, modification1.Length);
                
                stream.Seek(200, SeekOrigin.Begin);
                await stream.WriteAsync(modification2, 0, modification2.Length);
                
                stream.Seek(300, SeekOrigin.Begin);
                await stream.WriteAsync(modification3, 0, modification3.Length);
                
                await stream.FlushAsync();
            }

            // Assert: Verify all modifications are present
            using (var fileStream = new FileStream(testFilePath, FileMode.Open, FileAccess.Read))
            using (var decryptingStream = vault.GetDecryptingStream(fileStream))
            {
                var readData = new byte[initialData.Length];
                await decryptingStream.ReadAsync(readData, 0, readData.Length);
                
                // Check each modification
                var mod1Result = Encoding.UTF8.GetString(readData, 100, modification1.Length);
                var mod2Result = Encoding.UTF8.GetString(readData, 200, modification2.Length);
                var mod3Result = Encoding.UTF8.GetString(readData, 300, modification3.Length);
                
                Assert.AreEqual("MOD1", mod1Result, "First modification should be present");
                Assert.AreEqual("MOD2", mod2Result, "Second modification should be present");
                Assert.AreEqual("MOD3", mod3Result, "Third modification should be present");
            }
        }

        [TestMethod]
        public void ChunkAwareStream_SeekAndPosition_ShouldWork()
        {
            // Arrange
            var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);
            VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultDirectory, passwordBytes);
            
            using var vault = VaultHandler.LoadCryptomatorV8Vault(File.ReadAllBytes(_masterkeyPath), passwordBytes);
            var testFilePath = Path.Combine(_tempDirectory, "seek_test.cryptomator");

            // Act & Assert: Test seeking functionality
            using var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.ReadWrite);
            using var stream = vault.GetRandomWriteEncryptingStream(fileStream);
            
            // Test initial position
            Assert.AreEqual(0, stream.Position, "Initial position should be 0");
            
            // Test seeking from begin
            var newPos = stream.Seek(1000, SeekOrigin.Begin);
            Assert.AreEqual(1000, newPos, "Seek from begin should return new position");
            Assert.AreEqual(1000, stream.Position, "Position should be updated");
            
            // Test seeking from current
            newPos = stream.Seek(500, SeekOrigin.Current);
            Assert.AreEqual(1500, newPos, "Seek from current should add to current position");
            Assert.AreEqual(1500, stream.Position, "Position should be updated");
            
            // Test setting position directly
            stream.Position = 2000;
            Assert.AreEqual(2000, stream.Position, "Direct position setting should work");
        }

        [TestMethod]
        public async Task CryptomatorCompatibility_VerifyChunkStructure()
        {
            // Arrange: Test that our chunk structure is compatible with Cryptomator
            var passwordBytes = Encoding.UTF8.GetBytes(TestPassword);
            VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultDirectory, passwordBytes);
            
            using var vault = VaultHandler.LoadCryptomatorV8Vault(File.ReadAllBytes(_masterkeyPath), passwordBytes);
            var testFilePath = Path.Combine(_tempDirectory, "compatibility_test.cryptomator");

            var testData = Encoding.UTF8.GetBytes("Cryptomator compatibility test data");

            // Act: Write using ChunkAwareEncryptingStream
            using (var fileStream = new FileStream(testFilePath, FileMode.Create, FileAccess.Write))
            using (var encryptingStream = vault.GetRandomWriteEncryptingStream(fileStream))
            {
                await encryptingStream.WriteAsync(testData, 0, testData.Length);
                await encryptingStream.FlushAsync();
            }

            // Assert: Verify can be read with standard DecryptingStream
            using (var fileStream = new FileStream(testFilePath, FileMode.Open, FileAccess.Read))
            using (var decryptingStream = vault.GetDecryptingStream(fileStream))
            {
                var readData = new byte[testData.Length];
                await decryptingStream.ReadAsync(readData, 0, readData.Length);
                
                CollectionAssert.AreEqual(testData, readData, "Data should be compatible with standard Cryptomator decryption");
            }
        }

        private static byte[] CreateTestData(int size)
        {
            var data = new byte[size];
            var random = new Random(42); // Use fixed seed for reproducible tests
            random.NextBytes(data);
            return data;
        }
    }
}
 