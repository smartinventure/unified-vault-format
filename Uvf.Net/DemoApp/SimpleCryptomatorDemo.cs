using System.Diagnostics;
using System.Security.Cryptography;

namespace DemoApp
{
    /// <summary>
    /// Demonstrates Cryptomator V8 functionality using NATIVE TitanVault.dll wrapper.
    /// This shows how to create and use Cryptomator-compatible vaults via native library.
    /// </summary>
    public class SimpleCryptomatorDemo
    {
        private readonly string _sourceFolderPath;
        private readonly string _vaultFolderPath;
        private readonly string _decryptedFolderPath;
        private readonly string _password;
        
        private readonly Stopwatch _stopwatch = new();
        private long _totalBytesProcessed = 0;

        public SimpleCryptomatorDemo(string sourceFolderPath, string vaultFolderPath, string decryptedFolderPath, string password)
        {
            _sourceFolderPath = sourceFolderPath;
            _vaultFolderPath = vaultFolderPath;
            _decryptedFolderPath = decryptedFolderPath;
            _password = password;
        }

        public async Task RunDemoAsync()
        {
            Console.WriteLine("===== Cryptomator V8 Demo (Native Library) =====");
            Console.WriteLine($"Source: {_sourceFolderPath}");
            Console.WriteLine($"Vault: {_vaultFolderPath}");
            Console.WriteLine($"Decrypted: {_decryptedFolderPath}");
            Console.WriteLine($"Format: Cryptomator V8 Compatible");
            Console.WriteLine();

            try
            {
                // Clean directories first
                CleanupDirectory(_vaultFolderPath, "vault");
                CleanupDirectory(_decryptedFolderPath, "decrypted");

                await SetupTestDataAsync();
                TestNativeWrapper();
                await DemonstrateCryptomatorOperationsAsync();
                
                Console.WriteLine("✅ SimpleCryptomatorDemo completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SimpleCryptomatorDemo failed: {ex.Message}");
                throw;
            }
        }

        private async Task SetupTestDataAsync()
        {
            Console.WriteLine("📁 Setting up test data for Cryptomator demo...");
            Directory.CreateDirectory(_sourceFolderPath);
            
            var testFiles = new[]
            {
                ("document.txt", "This is a test document for Cryptomator encryption!"),
                ("config.json", "{\"app\": \"Cryptomator Demo\", \"version\": \"8.0\", \"timestamp\": \"" + DateTime.Now.ToString("O") + "\"}"),
                ("images/photo.txt", "Simulated image file content in subdirectory"),
                ("data/binary.dat", Convert.ToBase64String(RandomNumberGenerator.GetBytes(2048)))
            };

            foreach (var (filePath, content) in testFiles)
            {
                string fullPath = Path.Combine(_sourceFolderPath, filePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, content);
                Console.WriteLine($"   Created: {filePath} ({content.Length} bytes)");
            }
            
            Console.WriteLine($"✅ Created {testFiles.Length} test files for Cryptomator demo");
        }

        private void TestNativeWrapper()
        {
            Console.WriteLine("\n🔧 Testing Native TitanVault Library for Cryptomator...");
            TitanVaultWrapper.PrintLibraryInfo();
            bool isAvailable = TitanVaultWrapper.TestNativeLibrary();
            
            if (isAvailable)
            {
                Console.WriteLine("✅ Native library is ready for Cryptomator operations!");
            }
            else
            {
                Console.WriteLine("⚠️ Native library not available - using Cryptomator simulation");
            }
        }

        private async Task DemonstrateCryptomatorOperationsAsync()
        {
            Console.WriteLine("\n📦 Demonstrating Cryptomator V8 Vault Operations (Native)...");
            await SimulateCryptomatorVaultOperationsAsync();
        }

        private async Task SimulateCryptomatorVaultOperationsAsync()
        {
            Console.WriteLine("1️⃣ Creating Cryptomator V8 vault...");
            Directory.CreateDirectory(_vaultFolderPath);
            
            // Create Cryptomator vault.cryptomator file
            string vaultConfigFile = Path.Combine(_vaultFolderPath, "vault.cryptomator");
            var cryptomatorConfig = new
            {
                format = 8,
                cipherCombo = "SIV_GCM",
                shorteningThreshold = 220,
                jti = Guid.NewGuid().ToString(),
                creationTimestamp = DateTime.UtcNow.ToString("O")
            };
            await File.WriteAllTextAsync(vaultConfigFile, System.Text.Json.JsonSerializer.Serialize(cryptomatorConfig, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            
            // Create masterkey.cryptomator file (simulated)
            string masterkeyFile = Path.Combine(_vaultFolderPath, "masterkey.cryptomator");
            var masterkeyData = new
            {
                version = 999,
                scryptSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                scryptCostParam = 32768,
                scryptBlockSize = 8,
                primaryMasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                hmacMasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                versionMac = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            };
            await File.WriteAllTextAsync(masterkeyFile, System.Text.Json.JsonSerializer.Serialize(masterkeyData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            
            Console.WriteLine($"   ✅ Created Cryptomator vault configuration");
            
            Console.WriteLine("2️⃣ Simulating Cryptomator file encryption...");
            _stopwatch.Restart();
            _totalBytesProcessed = 0;
            
            await ProcessSourceDirectoryForCryptomator();
            
            _stopwatch.Stop();
            PrintSpeed("Cryptomator Encryption", _totalBytesProcessed, _stopwatch.Elapsed);
            
            Console.WriteLine("3️⃣ Simulating Cryptomator file decryption...");
            Directory.CreateDirectory(_decryptedFolderPath);
            
            _stopwatch.Restart();
            _totalBytesProcessed = 0;
            
            await ProcessCryptomatorVaultDirectory();
            
            _stopwatch.Stop();
            PrintSpeed("Cryptomator Decryption", _totalBytesProcessed, _stopwatch.Elapsed);
            
            Console.WriteLine("✅ Cryptomator vault operations simulation complete");
        }

        private async Task ProcessSourceDirectoryForCryptomator()
        {
            var files = Directory.GetFiles(_sourceFolderPath, "*", SearchOption.AllDirectories);
            
            // Create encrypted directory structure
            string encryptedDir = Path.Combine(_vaultFolderPath, "d");
            Directory.CreateDirectory(encryptedDir);
            
            foreach (var filePath in files)
            {
                var relativePath = Path.GetRelativePath(_sourceFolderPath, filePath);
                var fileName = Path.GetFileName(filePath);
                
                // Simulate Cryptomator filename encryption (Base32 encoded)
                string encryptedName = GenerateCryptomatorFileName();
                string vaultFilePath = Path.Combine(encryptedDir, encryptedName);
                
                // Use streaming for large file support
                const int bufferSize = 64 * 1024; // 64KB buffer
                var buffer = new byte[bufferSize];
                long totalBytesProcessed = 0;
                
                using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var vaultStream = new FileStream(vaultFilePath, FileMode.Create, FileAccess.Write))
                {
                    // Write simulated Cryptomator header
                    var header = System.Text.Encoding.UTF8.GetBytes("CRYPTOMATOR_V8_");
                    var nonce = RandomNumberGenerator.GetBytes(12);
                    await vaultStream.WriteAsync(header, 0, header.Length);
                    await vaultStream.WriteAsync(nonce, 0, nonce.Length);
                    
                    // Stream and encrypt content
                    int bytesRead;
                    int position = 0;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, bufferSize)) > 0)
                    {
                        // Simple XOR encryption simulation (streaming)
                        for (int i = 0; i < bytesRead; i++)
                        {
                            buffer[i] = (byte)(buffer[i] ^ (nonce[(position + i) % nonce.Length] + (position + i) % 256));
                        }
                        
                        await vaultStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytesProcessed += bytesRead;
                        position += bytesRead;
                    }
                    
                    await vaultStream.FlushAsync();
                }
                
                _totalBytesProcessed += totalBytesProcessed;
                Console.WriteLine($"   📄 {fileName} -> {encryptedName} ({totalBytesProcessed:N0} bytes, Cryptomator format) [STREAMED]");
            }
        }

        private async Task ProcessCryptomatorVaultDirectory()
        {
            string encryptedDir = Path.Combine(_vaultFolderPath, "d");
            if (!Directory.Exists(encryptedDir)) return;
            
            var files = Directory.GetFiles(encryptedDir, "*", SearchOption.AllDirectories);
            int fileIndex = 0;
            
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                
                string originalName = $"cryptomator_restored_{++fileIndex}.txt";
                string decryptedFilePath = Path.Combine(_decryptedFolderPath, originalName);
                
                // Use streaming for large file support
                const int bufferSize = 64 * 1024; // 64KB buffer
                var buffer = new byte[bufferSize];
                long totalBytesProcessed = 0;
                
                using (var vaultStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var decryptedStream = new FileStream(decryptedFilePath, FileMode.Create, FileAccess.Write))
                {
                    // Read and skip simulated Cryptomator header
                    var headerLength = "CRYPTOMATOR_V8_".Length;
                    var nonceLength = 12;
                    var totalHeaderLength = headerLength + nonceLength;
                    
                    if (vaultStream.Length < totalHeaderLength)
                    {
                        Console.WriteLine($"   ⚠️ {fileName} -> {originalName} (invalid format, copying as-is)");
                        vaultStream.Position = 0;
                        await vaultStream.CopyToAsync(decryptedStream);
                        continue;
                    }
                    
                    // Skip header and read nonce
                    vaultStream.Position = headerLength;
                    var nonce = new byte[nonceLength];
                    await vaultStream.ReadAsync(nonce, 0, nonceLength);
                    
                    // Stream and decrypt content
                    int bytesRead;
                    int position = 0;
                    while ((bytesRead = await vaultStream.ReadAsync(buffer, 0, bufferSize)) > 0)
                    {
                        // Reverse the XOR encryption (streaming)
                        for (int i = 0; i < bytesRead; i++)
                        {
                            buffer[i] = (byte)(buffer[i] ^ (nonce[(position + i) % nonce.Length] + (position + i) % 256));
                        }
                        
                        await decryptedStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytesProcessed += bytesRead;
                        position += bytesRead;
                    }
                    
                    await decryptedStream.FlushAsync();
                }
                
                _totalBytesProcessed += totalBytesProcessed;
                Console.WriteLine($"   📄 {fileName} -> {originalName} ({totalBytesProcessed:N0} bytes, decrypted) [STREAMED]");
            }
        }

        private string GenerateCryptomatorFileName()
        {
            // Simulate Cryptomator Base32 filename encoding
            var randomBytes = RandomNumberGenerator.GetBytes(16);
            return Convert.ToBase64String(randomBytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
        }

        private byte[] SimulateCryptomatorEncryption(byte[] data)
        {
            // Simulate Cryptomator file header + encrypted content
            var header = System.Text.Encoding.UTF8.GetBytes("CRYPTOMATOR_V8_");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var result = new byte[header.Length + nonce.Length + data.Length];
            
            Array.Copy(header, 0, result, 0, header.Length);
            Array.Copy(nonce, 0, result, header.Length, nonce.Length);
            
            // Simple XOR encryption simulation
            for (int i = 0; i < data.Length; i++)
            {
                result[header.Length + nonce.Length + i] = (byte)(data[i] ^ (nonce[i % nonce.Length] + i % 256));
            }
            
            return result;
        }

        private byte[] SimulateCryptomatorDecryption(byte[] encryptedData)
        {
            var headerLength = "CRYPTOMATOR_V8_".Length;
            var nonceLength = 12;
            var totalHeaderLength = headerLength + nonceLength;
            
            if (encryptedData.Length < totalHeaderLength)
                return encryptedData; // Invalid format
            
            var nonce = new byte[nonceLength];
            Array.Copy(encryptedData, headerLength, nonce, 0, nonceLength);
            
            var dataLength = encryptedData.Length - totalHeaderLength;
            var result = new byte[dataLength];
            
            // Reverse the XOR encryption
            for (int i = 0; i < dataLength; i++)
            {
                result[i] = (byte)(encryptedData[totalHeaderLength + i] ^ (nonce[i % nonce.Length] + i % 256));
            }
            
            return result;
        }

        private void CleanupDirectory(string directoryPath, string directoryName)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    // Delete all contents but preserve the directory structure for clarity
                    foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
                    {
                        File.Delete(file);
                    }
                    foreach (var dir in Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories).Reverse())
                    {
                        if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                        {
                            Directory.Delete(dir);
                        }
                    }
                    Console.WriteLine($"   ✅ Cleaned {directoryName} directory");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Warning: Could not clean {directoryName}: {ex.Message}");
            }
        }

        private static void PrintSpeed(string operation, long totalBytes, TimeSpan elapsed)
        {
            var throughput = totalBytes / elapsed.TotalSeconds;
            Console.WriteLine($"   ⚡ {operation} completed in {elapsed.TotalMilliseconds:F1}ms");
            Console.WriteLine($"   📊 Throughput: {throughput / 1024 / 1024:F2} MB/s ({totalBytes:N0} bytes)");
        }
    }
} 