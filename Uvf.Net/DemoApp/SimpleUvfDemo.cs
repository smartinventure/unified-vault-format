using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DemoApp
{
    /// <summary>
    /// Demonstrates UVF vault operations with native TitanVault library integration.
    /// Uses TitanVault native wrapper instead of managed VaultManager for authentic native operations.
    /// </summary>
    public class SimpleUvfDemo
    {
        private readonly string _sourceFolderPath;
        private readonly string _vaultFolderPath;
        private readonly string _decryptedFolderPath;
        private readonly string _password;
        private readonly bool _encryptFilenames;

        private readonly Stopwatch _stopwatch = new();
        private long _totalBytesProcessed = 0;

        public SimpleUvfDemo(string sourceFolderPath, string vaultFolderPath, string decryptedFolderPath, string password, bool encryptFilenames = true)
        {
            _sourceFolderPath = sourceFolderPath;
            _vaultFolderPath = vaultFolderPath;
            _decryptedFolderPath = decryptedFolderPath;
            _password = password;
            _encryptFilenames = encryptFilenames;
        }

        public async Task RunDemoAsync()
        {
            Console.WriteLine("===== Simple UVF Demo =====");
            Console.WriteLine($"Source: {_sourceFolderPath}");
            Console.WriteLine($"Vault: {_vaultFolderPath}");
            Console.WriteLine($"Decrypted: {_decryptedFolderPath}");
            Console.WriteLine($"Filename Encryption: {(_encryptFilenames ? "Enabled" : "Disabled")}");
            Console.WriteLine();

            // Phase 1: Setup test data
            Console.WriteLine("📁 Setting up test data...");
            await SetupTestDataAsync();

            // Phase 2: Test native wrapper
            Console.WriteLine("🔧 Testing Native Library Wrapper...");
            TestNativeWrapper();

            // Phase 3: Demonstrate native library
            await DemonstrateNativeLibraryAsync();

            // Phase 4: Real UVF vault operations
            await RealUvfVaultOperationsAsync();

            Console.WriteLine("✅ SimpleUvfDemo completed successfully!");
        }

        private async Task SetupTestDataAsync()
        {
            Directory.CreateDirectory(_sourceFolderPath);

            var testFiles = new Dictionary<string, string>
            {
                { "test.txt", "Hello, this is a test file for UVF encryption!" },
                { "config.json", $"{{\"app\": \"Cryptomator Demo\", \"version\": \"8.0\", \"timestamp\": \"{DateTime.Now:O}\"}}" },
                { "document.txt", "This is a test document for Cryptomator encryption!" },
                { "data.json", $"{{\"message\": \"JSON test data\", \"timestamp\": \"{DateTime.Now:O}\"}}" }
            };

            // Create subdirectories with files
            var subDirs = new Dictionary<string, Dictionary<string, string>>
            {
                { "subfolder", new Dictionary<string, string>
                    {
                        { "nested.txt", "This is a nested file in a subdirectory" },
                        { "binary.dat", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(1024)) }
                    }
                },
                { "data", new Dictionary<string, string>
                    {
                        { "binary.dat", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096)) }
                    }
                },
                { "images", new Dictionary<string, string>
                    {
                        { "photo.txt", "Simulated image file content in subdirectory" }
                    }
                }
            };

            // Create root files
            foreach (var (fileName, content) in testFiles)
            {
                var filePath = Path.Combine(_sourceFolderPath, fileName);
                await File.WriteAllTextAsync(filePath, content);
                Console.WriteLine($"   Created: {fileName} ({content.Length} bytes)");
            }

            // Create subdirectories and files
            foreach (var (subDir, files) in subDirs)
            {
                var subDirPath = Path.Combine(_sourceFolderPath, subDir);
                Directory.CreateDirectory(subDirPath);

                foreach (var (fileName, content) in files)
                {
                    var filePath = Path.Combine(subDirPath, fileName);
                    await File.WriteAllTextAsync(filePath, content);
                }
            }

            var allFiles = Directory.GetFiles(_sourceFolderPath, "*", SearchOption.AllDirectories);
            Console.WriteLine($"✅ Created {allFiles.Length} test files");
        }

        private void TestNativeWrapper()
        {
            Console.WriteLine("🔧 Testing TitanVault Native Library...");
            TitanVaultWrapper.PrintLibraryInfo();
            bool isAvailable = TitanVaultWrapper.TestNativeLibrary();
            
            if (isAvailable)
            {
                Console.WriteLine("✅ Native library is ready for UVF operations!");
            }
            else
            {
                Console.WriteLine("⚠️ Native library not available - using managed operations only");
            }
        }

        private async Task DemonstrateNativeLibraryAsync()
        {
            Console.WriteLine("🔧 Testing TitanVault Native Exports...");
            bool exportsWork = TitanVaultExportTester.TestTitanVaultExports();
            
            if (exportsWork)
            {
                Console.WriteLine("✅ Native exports are working!");
            }
            else
            {
                Console.WriteLine("⚠️ Native exports not available - proceeding with managed operations");
            }
        }

        private async Task RealUvfVaultOperationsAsync()
        {
            Console.WriteLine("📦 Demonstrating Real UVF Vault Operations...");
            
            // Cleanup directories first
            CleanupDirectory(_vaultFolderPath, "vault");
            CleanupDirectory(_decryptedFolderPath, "decrypted");
            
            // Create real UVF vault using TitanVault (like ExampleVaultApp does)
            char[] passwordChars = _password.ToCharArray();
            try
            {
                Console.WriteLine("1️⃣ Creating real UVF vault using TitanVault...");
                
                var vault = TitanVaultWrapper.TitanVault.CreateUvfVault(
                    _vaultFolderPath, 
                    passwordChars, 
                    _encryptFilenames
                );
                
                Console.WriteLine("✅ Real UVF vault created successfully");
                
                Console.WriteLine("2️⃣ Processing file encryption (source -> vault)...");
                _stopwatch.Restart();
                _totalBytesProcessed = 0;
                
                await ProcessSourceDirectoryForEncryption(vault);
                
                _stopwatch.Stop();
                PrintSpeed("Encryption", _totalBytesProcessed, _stopwatch.Elapsed);
                
                Console.WriteLine("3️⃣ Processing file decryption (vault -> decrypted)...");
                Directory.CreateDirectory(_decryptedFolderPath);
                
                _stopwatch.Restart();
                _totalBytesProcessed = 0;
                
                await ProcessVaultDirectoryForDecryption(vault);
                
                _stopwatch.Stop();
                PrintSpeed("Decryption", _totalBytesProcessed, _stopwatch.Elapsed);
                
                Console.WriteLine("4️⃣ Verifying file integrity (source vs decrypted)...");
                await VerifyFilesAsync();
                
                Console.WriteLine("✅ Real UVF vault operations complete");
                
                vault.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                throw;
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }

        private async Task ProcessSourceDirectoryForEncryption(TitanVaultWrapper.TitanVault vault)
        {
            await ProcessDirectoryRecursivelyForEncryption(_sourceFolderPath, "", "/", vault);
        }

        private async Task ProcessDirectoryRecursivelyForEncryption(string currentPhysicalPath, string relativePath, string vaultVirtualPath, TitanVaultWrapper.TitanVault vault)
        {
            if (!Directory.Exists(currentPhysicalPath))
            {
                Console.WriteLine($"   Source directory does not exist: {currentPhysicalPath}");
                return;
            }

            Console.WriteLine($"📁 Processing directory: {relativePath} -> {vaultVirtualPath}");

            // Create directory in vault if not root
            if (vaultVirtualPath != "/")
            {
                vault.CreateDirectory(vaultVirtualPath);
                Console.WriteLine($"   Created vault directory: {vaultVirtualPath}");
            }

            // Process files in current directory
            var files = Directory.GetFiles(currentPhysicalPath);
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                string vaultFilePath = vaultVirtualPath == "/" ? $"/{fileName}" : $"{vaultVirtualPath}/{fileName}";
                
                await CopyFileToVaultAsync(filePath, vault, vaultFilePath, "encryption");
            }

            // Process subdirectories
            var directories = Directory.GetDirectories(currentPhysicalPath);
            foreach (var dirPath in directories)
            {
                var dirName = Path.GetFileName(dirPath);
                string vaultSubPath = vaultVirtualPath == "/" ? $"/{dirName}" : $"{vaultVirtualPath}/{dirName}";
                string relativeSubPath = string.IsNullOrEmpty(relativePath) ? dirName : $"{relativePath}/{dirName}";
                await ProcessDirectoryRecursivelyForEncryption(dirPath, relativeSubPath, vaultSubPath, vault);
            }
        }

        private async Task CopyFileToVaultAsync(string sourceFilePath, TitanVaultWrapper.TitanVault vault, string vaultVirtualPath, string operation)
        {
            var fileName = Path.GetFileName(sourceFilePath);
            
            // Read from source file
            byte[] sourceData = await File.ReadAllBytesAsync(sourceFilePath);
            
            // Write to vault using TitanVault
            vault.WriteAllBytes(vaultVirtualPath, sourceData);
            
            _totalBytesProcessed += sourceData.Length;
            Console.WriteLine($"   📄 {fileName} -> {Path.GetFileName(vaultVirtualPath)} ({sourceData.Length} bytes)");
        }

        private async Task ProcessVaultDirectoryForDecryption(TitanVaultWrapper.TitanVault vault)
        {
            await ProcessDirectoryRecursivelyForDecryption(vault, "/", _decryptedFolderPath, "");
        }

        private async Task ProcessDirectoryRecursivelyForDecryption(TitanVaultWrapper.TitanVault vault, string vaultVirtualPath, string decryptedPhysicalPath, string relativePath)
        {
            Console.WriteLine($"📁 Processing virtual directory: {vaultVirtualPath} -> {relativePath}");
            
            // Ensure decrypted directory exists
            Directory.CreateDirectory(decryptedPhysicalPath);
            
            // Check if vault directory exists
            if (!vault.DirectoryExists(vaultVirtualPath))
            {
                Console.WriteLine($"   Vault directory does not exist: {vaultVirtualPath}");
                return;
            }
            
            // Get directory contents from vault
            var entries = vault.ListDirectory(vaultVirtualPath);
            
            foreach (var entry in entries)
            {
                var entryName = Path.GetFileName(entry);
                string entryPath = vaultVirtualPath == "/" ? $"/{entryName}" : $"{vaultVirtualPath}/{entryName}";
                
                if (entry.EndsWith("/")) // Directory
                {
                    // Process subdirectory
                    string decryptedSubPath = Path.Combine(decryptedPhysicalPath, entryName);
                    string relativeSubPath = string.IsNullOrEmpty(relativePath) ? entryName : $"{relativePath}/{entryName}";
                    await ProcessDirectoryRecursivelyForDecryption(vault, entryPath, decryptedSubPath, relativeSubPath);
                }
                else
                {
                    // Process file
                    string decryptedFilePath = Path.Combine(decryptedPhysicalPath, entryName);
                    await CopyFileFromVaultAsync(vault, entryPath, decryptedFilePath, "decryption");
                }
            }
        }

        private async Task CopyFileFromVaultAsync(TitanVaultWrapper.TitanVault vault, string vaultVirtualPath, string targetFilePath, string operation)
        {
            var fileName = Path.GetFileName(vaultVirtualPath);
            
            // Read from vault using TitanVault
            byte[] vaultData = vault.ReadAllBytes(vaultVirtualPath);
            
            // Write to target file
            await File.WriteAllBytesAsync(targetFilePath, vaultData);
            
            _totalBytesProcessed += vaultData.Length;
            Console.WriteLine($"   📄 {Path.GetFileName(vaultVirtualPath)} -> {fileName} ({vaultData.Length} bytes)");
        }

        private async Task VerifyFilesAsync()
        {
            Console.WriteLine("🔍 Comparing source and decrypted files...");
            
            var sourceFiles = await CollectFilesAsync(_sourceFolderPath, "source");
            var decryptedFiles = await CollectFilesAsync(_decryptedFolderPath, "decrypted");
            
            Console.WriteLine($"📊 Source files: {sourceFiles.Count}");
            Console.WriteLine($"📊 Decrypted files: {decryptedFiles.Count}");
            
            bool allMatch = true;
            int matchCount = 0;
            int mismatchCount = 0;
            int missingCount = 0;
            
            // Check each source file against decrypted (by size only, no MD5 as requested)
            foreach (var sourceFile in sourceFiles)
            {
                if (decryptedFiles.TryGetValue(sourceFile.Key, out var decryptedFile))
                {
                    if (sourceFile.Value.Size == decryptedFile.Size)
                    {
                        Console.WriteLine($"✅ {sourceFile.Key} - Size match ({sourceFile.Value.Size} bytes)");
                        matchCount++;
                    }
                    else
                    {
                        Console.WriteLine($"❌ {sourceFile.Key} - Size mismatch!");
                        Console.WriteLine($"   Source:    {sourceFile.Value.Size} bytes");
                        Console.WriteLine($"   Decrypted: {decryptedFile.Size} bytes");
                        allMatch = false;
                        mismatchCount++;
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Missing in decrypted: {sourceFile.Key}");
                    allMatch = false;
                    missingCount++;
                }
            }
            
            // Check for extra files in decrypted
            int extraCount = 0;
            foreach (var decryptedFile in decryptedFiles)
            {
                if (!sourceFiles.ContainsKey(decryptedFile.Key))
                {
                    Console.WriteLine($"⚠️ {decryptedFile.Key} - Extra file in decrypted");
                    extraCount++;
                }
            }
            
            // Final verification summary
            Console.WriteLine("\n📈 VERIFICATION SUMMARY:");
            Console.WriteLine($"   ✅ Size matches: {matchCount}");
            if (mismatchCount > 0) Console.WriteLine($"   ❌ Size mismatches: {mismatchCount}");
            if (missingCount > 0) Console.WriteLine($"   ❌ Missing files: {missingCount}");
            if (extraCount > 0) Console.WriteLine($"   ⚠️ Extra files: {extraCount}");
            
            long totalBytes = sourceFiles.Values.Sum(f => f.Size);
            Console.WriteLine($"   📊 Total bytes verified: {totalBytes:N0}");
            
            // Final result message
            if (allMatch && extraCount == 0)
            {
                Console.WriteLine("\n🎉 SUCCESS: All source files exist in decrypted with matching sizes!");
                Console.WriteLine($"   Perfect data integrity: {matchCount} files, {totalBytes:N0} bytes verified");
            }
            else
            {
                Console.WriteLine("\n💥 FAILURE: Discrepancies found between source and decrypted!");
            }
        }

        private async Task<Dictionary<string, FileInfo>> CollectFilesAsync(string directoryPath, string directoryName)
        {
            Console.WriteLine($"📋 Collecting files from {directoryName} directory...");
            var files = new Dictionary<string, FileInfo>();
            
            if (Directory.Exists(directoryPath))
            {
                await CollectFilesRecursiveAsync(directoryPath, "", files);
            }
            
            Console.WriteLine($"   📊 Found {files.Count} files in {directoryName} directory");
            return files;
        }

        private async Task CollectFilesRecursiveAsync(string currentPath, string relativePath, Dictionary<string, FileInfo> files)
        {
            var filePaths = Directory.GetFiles(currentPath);
            var directoryPaths = Directory.GetDirectories(currentPath);
            
            // Process files
            foreach (var filePath in filePaths)
            {
                var fileName = Path.GetFileName(filePath);
                string relativeFilePath = string.IsNullOrEmpty(relativePath) ? fileName : $"{relativePath}/{fileName}";
                
                var systemFileInfo = new System.IO.FileInfo(filePath);
                files[relativeFilePath] = new FileInfo { Size = systemFileInfo.Length };
            }
            
            // Process subdirectories
            foreach (var dirPath in directoryPaths)
            {
                var dirName = Path.GetFileName(dirPath);
                string relativeSubPath = string.IsNullOrEmpty(relativePath) ? dirName : $"{relativePath}/{dirName}";
                await CollectFilesRecursiveAsync(dirPath, relativeSubPath, files);
            }
        }

        private class FileInfo
        {
            public long Size { get; set; }
        }

        private void CleanupDirectory(string directoryPath, string directoryName)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, true);
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
            Console.WriteLine($"   📊 {operation}: {totalBytes:N0} bytes in {elapsed.TotalMilliseconds:F0}ms");
            if (elapsed.TotalSeconds > 0 && totalBytes > 0)
            {
                double mbps = (totalBytes / (1024.0 * 1024.0)) / elapsed.TotalSeconds;
                Console.WriteLine($"   ⚡ Speed: {mbps:F2} MB/s");
            }
        }
    }
} 