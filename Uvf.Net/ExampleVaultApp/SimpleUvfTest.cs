using UvfLib.Storage;
using UvfLib.Vault;
using UvfLib.Core.Api;
using System.Diagnostics;
using System.Security.Cryptography;
using System.IO;

namespace ExampleVaultApp
{
    /// <summary>
    /// Tests UVF V3 functionality using the simple VaultManager API.
    /// This demonstrates how much easier vault operations become with the high-level VaultManager
    /// compared to the low-level IStorage interface operations in DirectUvfTest.
    /// Uses regular File operations for source/decrypted and VaultManager only for the encrypted vault.
    /// </summary>
    public class SimpleUvfTest
    {
                private readonly string _sourceFolderPath;
        private readonly string _vaultFolderPath;
        private readonly string _decryptedFolderPath;
        private readonly string _password;
        private readonly bool _encryptFilenames;
        private readonly KeyDerivationParameters _keyDerivationParams;

        private readonly Stopwatch _stopwatch = new();
        private long _totalBytesProcessed = 0;

        public SimpleUvfTest(string sourceFolderPath, string vaultFolderPath, string decryptedFolderPath, string password, bool encryptFilenames = true, KeyDerivationParameters? keyDerivationParams = null)
        {
            _sourceFolderPath = sourceFolderPath;
            _vaultFolderPath = vaultFolderPath;
            _decryptedFolderPath = decryptedFolderPath;
            _password = password;
            _encryptFilenames = encryptFilenames;
            _keyDerivationParams = keyDerivationParams ?? KeyDerivationParameters.Default();
        }

        public async Task RunTestAsync()
        {
            Console.WriteLine("===== SimpleUvfTest - VaultManager API =====");
            Console.WriteLine($"Source: {_sourceFolderPath}");
            Console.WriteLine($"Vault: {_vaultFolderPath}");
            Console.WriteLine($"Decrypted: {_decryptedFolderPath}");
            Console.WriteLine();

            try
            {
                // Phase 1: Cleanup first (before creating vault)
                CleanupDirectory(_vaultFolderPath, "vault");
                CleanupDirectory(_decryptedFolderPath, "decrypted");
                
                // Phase 2: Create fresh vault with specified filename encryption setting
                Console.WriteLine($"🆕 Creating fresh UVF vault with filename encryption: {(_encryptFilenames ? "Enabled" : "Disabled")}");
                Console.WriteLine($"🔑 Using key derivation: {_keyDerivationParams.Method} {GetKeyDerivationDetails(_keyDerivationParams)}");
                var vault = await VaultManager.CreateUvfVaultWithKdfAsync(_vaultFolderPath, _password, _encryptFilenames, _keyDerivationParams);
                
                Console.WriteLine("✅ UVF vault ready for operations");
                
                // Phase 3: Source data (use whatever exists in source directory)
                
                // Phase 4: Encryption phase (File -> VaultManager)
                await EncryptionPhaseAsync(vault);
                
                // Phase 5: Decryption phase (VaultManager -> File) - handles vault reloading with auto-detection
                await DecryptionPhaseAsync(vault);
                
                // Phase 6: Verification phase (File vs File)
                await VerificationPhaseAsync();
                
                Console.WriteLine("✅ SimpleUvfTest completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SimpleUvfTest failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void CleanupDirectory(string directoryPath, string directoryName)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                    var directories = Directory.GetDirectories(directoryPath, "*", SearchOption.AllDirectories);
                    
                    // Delete all files
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                    
                    // Delete all directories (in reverse order to handle nested directories)
                    foreach (var dir in directories.Reverse())
                    {
                        Directory.Delete(dir);
                    }
                    
                    Console.WriteLine($"   ✅ Cleaned {directoryName} directory ({files.Length + directories.Length} items removed)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Warning: Could not fully clean {directoryName} directory: {ex.Message}");
            }
        }

        private async Task EncryptionPhaseAsync(VaultManager vault)
        {
            Console.WriteLine("\n📦 Starting encryption phase (File -> VaultManager)...");
            
            _stopwatch.Restart();
            _totalBytesProcessed = 0;
            
            // Process source directory recursively
            await ProcessDirectoryForEncryptionAsync(_sourceFolderPath, vault, "", "/");
            
            _stopwatch.Stop();
            PrintSpeed("Encryption", _totalBytesProcessed, _stopwatch.Elapsed);
            Console.WriteLine("✅ Encryption phase complete");
        }

        private async Task ProcessDirectoryForEncryptionAsync(string sourcePhysicalPath, VaultManager vault, string relativeSourcePath, string vaultVirtualPath)
        {
            Console.WriteLine($"📁 Processing directory: {relativeSourcePath} -> {vaultVirtualPath}");
            
            // Create virtual directory in vault (except root)
            if (vaultVirtualPath != "/")
            {
                await vault.CreateDirectoryAsync(vaultVirtualPath);
                Console.WriteLine($"   Created vault directory: {vaultVirtualPath}");
            }
            
            // Check if source directory exists
            if (!Directory.Exists(sourcePhysicalPath))
            {
                Console.WriteLine($"   Source directory does not exist: {sourcePhysicalPath}");
                return;
            }
            
            // Get directory contents from source
            var files = Directory.GetFiles(sourcePhysicalPath);
            var directories = Directory.GetDirectories(sourcePhysicalPath);
            
            // Process files
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                string vaultFilePath = vaultVirtualPath == "/" ? $"/{fileName}" : $"{vaultVirtualPath}/{fileName}";
                await CopyFileToVaultAsync(filePath, vault, vaultFilePath, "encryption");
            }
            
            // Process subdirectories
            foreach (var dirPath in directories)
            {
                var dirName = Path.GetFileName(dirPath);
                string vaultSubPath = vaultVirtualPath == "/" ? $"/{dirName}" : $"{vaultVirtualPath}/{dirName}";
                string relativeSubPath = string.IsNullOrEmpty(relativeSourcePath) ? dirName : $"{relativeSourcePath}/{dirName}";
                await ProcessDirectoryForEncryptionAsync(dirPath, vault, relativeSubPath, vaultSubPath);
            }
        }

        private async Task DecryptionPhaseAsync(VaultManager vault)
        {
            Console.WriteLine("\n📦 Starting decryption phase (VaultManager -> File)...");
            
            // Close the creation vault and reload with auto-detection
            await vault.CloseVaultAsync();
            vault.Dispose();
            
            // Reload vault with automatic filename encryption detection
            Console.WriteLine("🔍 Reloading vault with automatic filename encryption detection...");
            using var autoDetectedVault = await VaultManager.LoadUvfVaultAsync(_vaultFolderPath, _password);
            
            // Try to detect and display the actual setting
            try
            {
                string vaultUvfFile = Path.Combine(_vaultFolderPath, "vault.uvf");
                byte[] vaultFileContent = await File.ReadAllBytesAsync(vaultUvfFile);
                bool detectedEncryptFilenames = VaultHandler.DetectFilenameEncryption(vaultFileContent, _password);
                Console.WriteLine($"🔍 Auto-detected filename encryption: {(detectedEncryptFilenames ? "Enabled" : "Disabled")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Could not detect filename encryption setting: {ex.Message}");
            }
            
            _stopwatch.Restart();
            _totalBytesProcessed = 0;
            
            // Ensure decrypted directory exists
            Directory.CreateDirectory(_decryptedFolderPath);
            
            // Process vault directory recursively using auto-detected vault
            await ProcessDirectoryForDecryptionAsync(autoDetectedVault, "/", _decryptedFolderPath, "");
            
            _stopwatch.Stop();
            PrintSpeed("Decryption", _totalBytesProcessed, _stopwatch.Elapsed);
            Console.WriteLine("✅ Decryption phase complete");
        }

        private async Task ProcessDirectoryForDecryptionAsync(VaultManager vault, string vaultVirtualPath, string decryptedPhysicalPath, string relativeDecryptedPath)
        {
            Console.WriteLine($"📁 Processing virtual directory: {vaultVirtualPath} -> {relativeDecryptedPath}");
            
            // Create target directory (except root)
            if (!string.IsNullOrEmpty(relativeDecryptedPath))
            {
                Directory.CreateDirectory(decryptedPhysicalPath);
                Console.WriteLine($"   Created decrypted directory: {relativeDecryptedPath}");
            }
            
            // Check if vault directory exists
            if (!await vault.DirectoryExistsAsync(vaultVirtualPath))
            {
                Console.WriteLine($"   Vault directory does not exist: {vaultVirtualPath}");
                return;
            }
            
            // Get directory contents from vault
            var entries = await vault.ListDirectoryAsync(vaultVirtualPath);
            
            foreach (var entry in entries)
            {
                string entryName = entry.Filename;
                string entryPath = string.IsNullOrEmpty(vaultVirtualPath) || vaultVirtualPath == "/" 
                    ? $"/{entryName}" 
                    : $"{vaultVirtualPath}/{entryName}";

                if (entry.IsDirectory)
                {
                    // Process subdirectory
                    string decryptedSubPath = Path.Combine(decryptedPhysicalPath, entryName);
                    string relativeSubPath = string.IsNullOrEmpty(relativeDecryptedPath) ? entryName : $"{relativeDecryptedPath}/{entryName}";
                    await ProcessDirectoryForDecryptionAsync(vault, entryPath, decryptedSubPath, relativeSubPath);
                }
                else
                {
                    // Process file
                    string decryptedFilePath = Path.Combine(decryptedPhysicalPath, entryName);
                    await CopyFileFromVaultAsync(vault, entryPath, decryptedFilePath, "decryption");
                }
            }
        }

        private async Task CopyFileToVaultAsync(string sourceFilePath, VaultManager vault, string vaultVirtualPath, string operation)
        {
            Console.WriteLine($"📄 {operation}: {Path.GetFileName(sourceFilePath)} -> {vaultVirtualPath}");
            
            // Use streaming to support files larger than 2 GB
            var fileInfo = new System.IO.FileInfo(sourceFilePath);
            long fileSize = fileInfo.Length;
            
            using var sourceStream = File.OpenRead(sourceFilePath);
            using var vaultStream = await vault.OpenWriteAsync(vaultVirtualPath);
            
            await sourceStream.CopyToAsync(vaultStream);
            
            _totalBytesProcessed += fileSize;
            Console.WriteLine($"   ✅ {operation} complete: {Path.GetFileName(vaultVirtualPath)} ({fileSize:N0} bytes)");
        }

        private async Task CopyFileFromVaultAsync(VaultManager vault, string vaultVirtualPath, string targetFilePath, string operation)
        {
            Console.WriteLine($"📄 {operation}: {vaultVirtualPath} -> {Path.GetFileName(targetFilePath)}");
            
            // Use streaming to support files larger than 2 GB
            long fileSize = 0;
            
            using var vaultStream = await vault.OpenReadAsync(vaultVirtualPath);
            using var targetStream = File.Create(targetFilePath);
            
            await vaultStream.CopyToAsync(targetStream);
            fileSize = targetStream.Length;
            
            _totalBytesProcessed += fileSize;
            Console.WriteLine($"   ✅ {operation} complete: {Path.GetFileName(targetFilePath)} ({fileSize:N0} bytes)");
        }

        private async Task VerificationPhaseAsync()
        {
            Console.WriteLine("\n🔍 Starting verification phase (File vs File)...");
            
            var sourceFiles = await CollectFilesAsync(_sourceFolderPath, "source");
            var decryptedFiles = await CollectFilesAsync(_decryptedFolderPath, "decrypted");
            
            Console.WriteLine($"📊 Source files: {sourceFiles.Count}");
            Console.WriteLine($"📊 Decrypted files: {decryptedFiles.Count}");
            
            bool allMatch = true;
            int matchCount = 0;
            int mismatchCount = 0;
            int missingCount = 0;
            
            // Check each source file against decrypted
            foreach (var sourceFile in sourceFiles)
            {
                if (decryptedFiles.TryGetValue(sourceFile.Key, out var decryptedFile))
                {
                    if (sourceFile.Value.Hash == decryptedFile.Hash && sourceFile.Value.Size == decryptedFile.Size)
                    {
                        Console.WriteLine($"✅ {sourceFile.Key} - Perfect match ({sourceFile.Value.Size} bytes)");
                        matchCount++;
                    }
                    else
                    {
                        Console.WriteLine($"❌ {sourceFile.Key} - Mismatch detected!");
                        Console.WriteLine($"   Source:    {sourceFile.Value.Size} bytes, MD5: {sourceFile.Value.Hash}");
                        Console.WriteLine($"   Decrypted: {decryptedFile.Size} bytes, MD5: {decryptedFile.Hash}");
                        allMatch = false;
                        mismatchCount++;
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Missing in decrypted storage: {sourceFile.Key}");
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
                    Console.WriteLine($"⚠️ {decryptedFile.Key} - Extra file in decrypted storage");
                    extraCount++;
                }
            }
            
            // Final verification summary
            Console.WriteLine("\n📈 VERIFICATION SUMMARY:");
            Console.WriteLine($"   ✅ Perfect matches: {matchCount}");
            if (mismatchCount > 0) Console.WriteLine($"   ❌ Hash/size mismatches: {mismatchCount}");
            if (missingCount > 0) Console.WriteLine($"   ❌ Missing files: {missingCount}");
            if (extraCount > 0) Console.WriteLine($"   ⚠️ Extra files: {extraCount}");
            
            Console.WriteLine($"   📊 Total bytes verified: {_totalBytesProcessed:N0}");
            
            // Final result message (matching old Program.cs style)
            if (allMatch && extraCount == 0)
            {
                Console.WriteLine("\n🎉 SUCCESS: All source items exist in decrypted storage with matching content and directory structure!");
                Console.WriteLine($"   Perfect data integrity: {matchCount} files, {_totalBytesProcessed:N0} bytes verified");
            }
            else
            {
                Console.WriteLine("\n💥 FAILURE: Discrepancies found between source and decrypted storage!");
                allMatch = false;
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
                
                // Use streaming to calculate hash for large files
                var fileInfo = new System.IO.FileInfo(filePath);
                string hash = await CalculateMD5HashFromFileAsync(filePath);
                
                files[relativeFilePath] = new FileInfo { Hash = hash, Size = fileInfo.Length };
            }
            
            // Process subdirectories
            foreach (var dirPath in directoryPaths)
            {
                var dirName = Path.GetFileName(dirPath);
                string relativeSubPath = string.IsNullOrEmpty(relativePath) ? dirName : $"{relativePath}/{dirName}";
                await CollectFilesRecursiveAsync(dirPath, relativeSubPath, files);
            }
        }

        private static string CalculateMD5Hash(byte[] data)
        {
            using var md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(data);
            return Convert.ToHexString(hashBytes);
        }

        private static async Task<string> CalculateMD5HashFromFileAsync(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hashBytes = await md5.ComputeHashAsync(stream);
            return Convert.ToHexString(hashBytes);
        }

        private static void PrintSpeed(string operation, long totalBytes, TimeSpan elapsed)
        {
            Console.WriteLine($"📊 {operation} completed: {totalBytes:N0} bytes in {elapsed.TotalMilliseconds:F0}ms");
            if (elapsed.TotalSeconds > 0 && totalBytes > 0)
            {
                double mbps = (totalBytes / (1024.0 * 1024.0)) / elapsed.TotalSeconds;
                Console.WriteLine($"   ⚡ Speed: {mbps:F2} MB/s");
            }
        }

        private static string GetKeyDerivationDetails(KeyDerivationParameters kdfParams)
        {
            switch (kdfParams.Method)
            {
                case KeyDerivationMethod.PBKDF2_HMAC_SHA512:
                    return $"({kdfParams.Pbkdf2Iterations:N0} iterations)";
                case KeyDerivationMethod.Scrypt:
                    return $"(N={kdfParams.ScryptN}, r={kdfParams.ScryptR}, p={kdfParams.ScryptP})";
                default:
                    return "";
            }
        }

        private class FileInfo
        {
            public string Hash { get; set; } = "";
            public long Size { get; set; }
        }
    }
} 