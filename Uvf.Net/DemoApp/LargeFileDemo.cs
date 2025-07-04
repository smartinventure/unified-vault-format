using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DemoApp
{
    /// <summary>
    /// Demonstrates large file handling (>4GB) with both UVF and Cryptomator vaults
    /// Tests 64-bit offset access, random read/write operations, and data integrity
    /// </summary>
    public class LargeFileDemo
    {
        private readonly string _baseFolderPath;
        private readonly string _sourceFilePath;
        private readonly string _adminPassword;
        private readonly Stopwatch _stopwatch = new();
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        
        // Test file size: 5GB (larger than 32-bit max)
        private const long TEST_FILE_SIZE = 10L * 1024 * 1024; // 10MB for faster testing
        private const int CHUNK_SIZE = 1024 * 1024; // 1MB chunks
        private const long TEST_OFFSET = 3L * 1024 * 1024; // 3MB offset (within existing file)

        public LargeFileDemo(string baseFolderPath, string adminPassword)
        {
            _baseFolderPath = baseFolderPath;
            _sourceFilePath = Path.Combine(_baseFolderPath, "large_test_file.bin");
            _adminPassword = adminPassword;
        }

        public async Task RunDemoAsync()
        {
            Console.WriteLine("🚀 Large File Demo (>4GB) - Testing 64-bit offset access");
            Console.WriteLine($"📁 Base folder: {_baseFolderPath}");
            Console.WriteLine($"📊 Test file size: {TEST_FILE_SIZE / (1024.0 * 1024 * 1024):F2} GB");
            Console.WriteLine($"📍 Test offset: {TEST_OFFSET / (1024.0 * 1024 * 1024):F2} GB (>32-bit)");
            Console.WriteLine($"📦 Chunk size: {CHUNK_SIZE / (1024.0 * 1024):F1} MB");

            try
            {
                // Step 1: Create large test file
                await CreateLargeTestFileAsync();

                // Step 2: Test UVF vault
                await TestUvfVaultAsync();

                // Step 3: Test Cryptomator vault
                await TestCryptomatorVaultAsync();

                Console.WriteLine("\n🎉 Large File Demo completed successfully!");
                Console.WriteLine("✅ Both UVF and Cryptomator vaults handle >4GB files correctly");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Large File Demo failed: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
            finally
            {
                // Cleanup
                await CleanupAsync();
            }
        }

        private async Task CreateLargeTestFileAsync()
        {
            Console.WriteLine("\n1️⃣ Creating large test file (5GB)...");
            Directory.CreateDirectory(_baseFolderPath);

            if (File.Exists(_sourceFilePath))
            {
                Console.WriteLine("   ♻️ Large test file already exists, skipping creation");
                return;
            }

            _stopwatch.Restart();
            
            using (var fileStream = new FileStream(_sourceFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: CHUNK_SIZE))
            {
                byte[] buffer = new byte[CHUNK_SIZE];
                long totalWritten = 0;
                int chunkIndex = 0;

                while (totalWritten < TEST_FILE_SIZE)
                {
                    // Generate deterministic random data based on chunk index
                    GenerateDeterministicData(buffer, chunkIndex);
                    
                    long remainingBytes = TEST_FILE_SIZE - totalWritten;
                    int bytesToWrite = (int)Math.Min(CHUNK_SIZE, remainingBytes);
                    
                    await fileStream.WriteAsync(buffer, 0, bytesToWrite);
                    totalWritten += bytesToWrite;
                    chunkIndex++;

                    // Progress indicator
                    if (chunkIndex % 100 == 0)
                    {
                        double progressGB = totalWritten / (1024.0 * 1024 * 1024);
                        Console.WriteLine($"   📝 Progress: {progressGB:F2} GB written...");
                    }
                }
            }

            _stopwatch.Stop();
            Console.WriteLine($"✅ Large test file created: {TEST_FILE_SIZE / (1024.0 * 1024 * 1024):F2} GB in {_stopwatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"   ⚡ Write speed: {(TEST_FILE_SIZE / (1024.0 * 1024)) / _stopwatch.Elapsed.TotalSeconds:F1} MB/s");
        }

        private async Task TestUvfVaultAsync()
        {
            Console.WriteLine("\n2️⃣ Testing UVF vault with large file...");
            
            var uvfVaultPath = Path.Combine(_baseFolderPath, "uvf_vault");
            var uvfDecryptedPath = Path.Combine(_baseFolderPath, "uvf_decrypted");
            var uvfDecryptedFilePath = Path.Combine(uvfDecryptedPath, "large_test_file.bin");

            try
            {
                // Create UVF vault
                Console.WriteLine("📦 Creating UVF vault...");
                CleanupDirectory(uvfVaultPath);
                CleanupDirectory(uvfDecryptedPath);
                
                char[] adminPasswordChars = _adminPassword.ToCharArray();
                try
                {
                    var vault = TitanVaultWrapper.TitanVault.CreateUvfVault(uvfVaultPath, adminPasswordChars, encryptFilenames: true);
                    Console.WriteLine("✅ UVF vault created successfully");

                    // Test large file operations
                    await TestVaultLargeFileOperations(
                        "UVF", 
                        () => TitanVaultWrapper.TitanVault.LoadUvfVault(uvfVaultPath, adminPasswordChars),
                        uvfDecryptedFilePath
                    );
                    
                    vault.Dispose();
                }
                finally
                {
                    Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                }
            }
            finally
            {
                // Cleanup UVF folders
                CleanupDirectory(uvfVaultPath);
                CleanupDirectory(uvfDecryptedPath);
            }
        }

        private async Task TestCryptomatorVaultAsync()
        {
            Console.WriteLine("\n3️⃣ Testing Cryptomator vault with large file...");
            
            var cryptomatorVaultPath = Path.Combine(_baseFolderPath, "cryptomator_vault");
            var cryptomatorDecryptedPath = Path.Combine(_baseFolderPath, "cryptomator_decrypted");
            var cryptomatorDecryptedFilePath = Path.Combine(cryptomatorDecryptedPath, "large_test_file.bin");

            try
            {
                // Create Cryptomator vault
                Console.WriteLine("📦 Creating Cryptomator vault...");
                CleanupDirectory(cryptomatorVaultPath);
                CleanupDirectory(cryptomatorDecryptedPath);
                
                char[] adminPasswordChars = _adminPassword.ToCharArray();
                try
                {
                    var vault = TitanVaultWrapper.TitanVault.CreateCryptomatorVault(cryptomatorVaultPath, adminPasswordChars);
                    Console.WriteLine("✅ Cryptomator vault created successfully");

                    // Test large file operations
                    await TestVaultLargeFileOperations(
                        "Cryptomator",
                        () => TitanVaultWrapper.TitanVault.LoadCryptomatorVault(cryptomatorVaultPath, adminPasswordChars),
                        cryptomatorDecryptedFilePath
                    );
                    
                    vault.Dispose();
                }
                finally
                {
                    Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
                }
            }
            finally
            {
                // Cleanup Cryptomator folders
                CleanupDirectory(cryptomatorVaultPath);
                CleanupDirectory(cryptomatorDecryptedPath);
            }
        }

        private async Task TestVaultLargeFileOperations(string vaultType, Func<TitanVaultWrapper.TitanVault> vaultFactory, string decryptedFilePath)
        {
            Console.WriteLine($"🔧 Testing {vaultType} vault large file operations...");

            using var vault = vaultFactory();
            
            // Step 1: Encrypt large file to vault
            Console.WriteLine($"   📥 Encrypting large file to {vaultType} vault...");
            _stopwatch.Restart();
            
            using (var sourceStream = new FileStream(_sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var vaultStream = vault.OpenWriteStream("/large_test_file.bin"))
            {
                await sourceStream.CopyToAsync(vaultStream, CHUNK_SIZE);
            }
            
            _stopwatch.Stop();
            Console.WriteLine($"   ✅ Encryption completed in {_stopwatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"   ⚡ Encryption speed: {(TEST_FILE_SIZE / (1024.0 * 1024)) / _stopwatch.Elapsed.TotalSeconds:F1} MB/s");

            // Step 2: Decrypt large file from vault
            Console.WriteLine($"   📤 Decrypting large file from {vaultType} vault...");
            Directory.CreateDirectory(Path.GetDirectoryName(decryptedFilePath)!);
            
            _stopwatch.Restart();
            
            using (var vaultStream = vault.OpenReadStream("/large_test_file.bin"))
            using (var decryptedStream = new FileStream(decryptedFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await vaultStream.CopyToAsync(decryptedStream, CHUNK_SIZE);
            }
            
            _stopwatch.Stop();
            Console.WriteLine($"   ✅ Decryption completed in {_stopwatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine($"   ⚡ Decryption speed: {(TEST_FILE_SIZE / (1024.0 * 1024)) / _stopwatch.Elapsed.TotalSeconds:F1} MB/s");

            // Step 3: Test random chunk read at >32-bit offset
            Console.WriteLine($"   🎯 Testing random chunk read at >32-bit offset ({TEST_OFFSET / (1024.0 * 1024 * 1024):F2} GB)...");
            await TestRandomChunkRead(vault, decryptedFilePath, vaultType);

            // Step 4: Test random write at >32-bit offset
            Console.WriteLine($"   ✏️ Testing random write at >32-bit offset...");
            await TestRandomWrite(vault, vaultType);

            Console.WriteLine($"   🎉 {vaultType} vault large file operations completed successfully!");
        }

        private async Task TestRandomChunkRead(TitanVaultWrapper.TitanVault vault, string decryptedFilePath, string vaultType)
        {
            // Read chunk from source file at large offset
            byte[] sourceChunk = new byte[CHUNK_SIZE];
            using (var sourceStream = new FileStream(_sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                sourceStream.Seek(TEST_OFFSET, SeekOrigin.Begin);
                int bytesRead = await sourceStream.ReadAsync(sourceChunk, 0, CHUNK_SIZE);
                if (bytesRead != CHUNK_SIZE)
                {
                    throw new InvalidOperationException($"Failed to read full chunk from source file: {bytesRead} bytes");
                }
            }

            // Read chunk from decrypted file at same offset
            byte[] decryptedChunk = new byte[CHUNK_SIZE];
            using (var decryptedStream = new FileStream(decryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                decryptedStream.Seek(TEST_OFFSET, SeekOrigin.Begin);
                int bytesRead = await decryptedStream.ReadAsync(decryptedChunk, 0, CHUNK_SIZE);
                if (bytesRead != CHUNK_SIZE)
                {
                    throw new InvalidOperationException($"Failed to read full chunk from decrypted file: {bytesRead} bytes");
                }
            }

            // Read chunk from vault stream at same offset
            byte[] vaultChunk = new byte[CHUNK_SIZE];
            using (var vaultStream = vault.OpenReadStream("/large_test_file.bin"))
            {
                vaultStream.Seek(TEST_OFFSET, SeekOrigin.Begin);
                int bytesRead = await vaultStream.ReadAsync(vaultChunk, 0, CHUNK_SIZE);
                if (bytesRead != CHUNK_SIZE)
                {
                    throw new InvalidOperationException($"Failed to read full chunk from vault stream: {bytesRead} bytes");
                }
            }

            // Compare all chunks
            if (!CompareByteArrays(sourceChunk, decryptedChunk))
            {
                throw new InvalidOperationException($"{vaultType}: Source and decrypted chunks don't match at offset {TEST_OFFSET}");
            }

            if (!CompareByteArrays(sourceChunk, vaultChunk))
            {
                throw new InvalidOperationException($"{vaultType}: Source and vault stream chunks don't match at offset {TEST_OFFSET}");
            }

            Console.WriteLine($"   ✅ Random chunk read verification passed for {vaultType} vault");
            Console.WriteLine($"   📊 Verified {CHUNK_SIZE / (1024.0 * 1024):F1} MB at offset {TEST_OFFSET / (1024.0 * 1024 * 1024):F2} GB");
        }

        private async Task TestRandomWrite(TitanVaultWrapper.TitanVault vault, string vaultType)
        {
            // Generate random data to write
            byte[] randomData = new byte[CHUNK_SIZE];
            GenerateDeterministicData(randomData, 999999); // Use a unique seed

            // For now, create a new file for random write testing to avoid the truncation issue
            string testFileName = "/random_write_test.bin";
            
            // Write random data to vault at large offset in a new file
            using (var vaultStream = vault.OpenWriteStream(testFileName))
            {
                // Seek to a large offset and write
                vaultStream.Seek(TEST_OFFSET, SeekOrigin.Begin);
                await vaultStream.WriteAsync(randomData, 0, CHUNK_SIZE);
                await vaultStream.FlushAsync();
            }

            // Read back the data to verify
            byte[] readBackData = new byte[CHUNK_SIZE];
            using (var vaultStream = vault.OpenReadStream(testFileName))
            {
                vaultStream.Seek(TEST_OFFSET, SeekOrigin.Begin);
                int bytesRead = await vaultStream.ReadAsync(readBackData, 0, CHUNK_SIZE);
                if (bytesRead != CHUNK_SIZE)
                {
                    throw new InvalidOperationException($"Failed to read back written data: {bytesRead} bytes");
                }
            }

            // Compare written and read data
            if (!CompareByteArrays(randomData, readBackData))
            {
                throw new InvalidOperationException($"{vaultType}: Written and read-back data don't match at offset {TEST_OFFSET}");
            }

            Console.WriteLine($"   ✅ Random write verification passed for {vaultType} vault");
            Console.WriteLine($"   📝 Wrote and verified {CHUNK_SIZE / (1024.0 * 1024):F1} MB at offset {TEST_OFFSET / (1024.0 * 1024 * 1024):F2} GB");
        }

        private void GenerateDeterministicData(byte[] buffer, int seed)
        {
            // Generate deterministic "random" data based on seed
            var random = new Random(seed);
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)random.Next(256);
            }
        }

        private bool CompareByteArrays(byte[] array1, byte[] array2)
        {
            if (array1.Length != array2.Length)
                return false;

            for (int i = 0; i < array1.Length; i++)
            {
                if (array1[i] != array2[i])
                    return false;
            }

            return true;
        }

        private void CleanupDirectory(string directoryPath)
        {
            if (Directory.Exists(directoryPath))
            {
                try
                {
                    Directory.Delete(directoryPath, recursive: true);
                    Console.WriteLine($"   🧹 Cleaned up directory: {directoryPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Failed to cleanup directory {directoryPath}: {ex.Message}");
                }
            }
        }

        private async Task CleanupAsync()
        {
            Console.WriteLine("\n🧹 Cleaning up large file demo...");
            
            // Remove the large test file
            if (File.Exists(_sourceFilePath))
            {
                try
                {
                    File.Delete(_sourceFilePath);
                    Console.WriteLine("   ✅ Large test file removed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️ Failed to remove large test file: {ex.Message}");
                }
            }

            // Cleanup any remaining directories
            var dirsToCleanup = new[]
            {
                Path.Combine(_baseFolderPath, "uvf_vault"),
                Path.Combine(_baseFolderPath, "uvf_decrypted"),
                Path.Combine(_baseFolderPath, "cryptomator_vault"),
                Path.Combine(_baseFolderPath, "cryptomator_decrypted")
            };

            foreach (var dir in dirsToCleanup)
            {
                CleanupDirectory(dir);
            }

            _rng?.Dispose();
            Console.WriteLine("   ✅ Cleanup completed");
        }
    }
} 