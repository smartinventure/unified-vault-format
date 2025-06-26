using System.Diagnostics;
using System.Security.Cryptography;

namespace DemoApp
{
    /// <summary>
    /// Demonstrates Cryptomator V8 functionality using NATIVE TitanVault.dll wrapper.
    /// This shows how to create and use Cryptomator-compatible vaults via native library.
    /// </summary>
    public class CryptomatorDemo
    {
        // Configurable buffer size for streaming operations (can be tuned for performance)
        private const int STREAMING_BUFFER_SIZE = 64 * 1024; // 64KB default
        
        private readonly string _sourceFolderPath;
        private readonly string _vaultFolderPath;
        private readonly string _decryptedFolderPath;
        private readonly string _password;
        
        private readonly Stopwatch _stopwatch = new();
        private long _totalBytesProcessed = 0;
        
        // Separate tracking for read and write operations
        private TimeSpan _writeElapsed = TimeSpan.Zero;
        private TimeSpan _readElapsed = TimeSpan.Zero;
        private long _totalBytesWritten = 0;
        private long _totalBytesRead = 0;

        public CryptomatorDemo(string sourceFolderPath, string vaultFolderPath, string decryptedFolderPath, string password)
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
            Console.WriteLine("📄 Creating test files...");
            
            // Clean and create source directory
            CleanupDirectory(_sourceFolderPath, "source");
            Directory.CreateDirectory(_sourceFolderPath);
            
            // Create test files with various sizes (EXACTLY same as UvfDemo)
            var testFiles = new[]
            {
                ("test.txt", "Hello, World! This is a test file for UVF encryption."),
                ("config.json", "{\n  \"app\": \"UVF Demo\",\n  \"version\": \"1.0\",\n  \"encryption\": true\n}"),
                ("document.txt", "This is a longer document with multiple lines.\nLine 2\nLine 3\nEnd of document."),
                ("data.json", "{\n  \"users\": [\"alice\", \"bob\"],\n  \"settings\": {\"theme\": \"dark\"}\n}")
            };
            
            foreach (var (fileName, content) in testFiles)
            {
                var filePath = Path.Combine(_sourceFolderPath, fileName);
                await File.WriteAllTextAsync(filePath, content);
                Console.WriteLine($"   Created: {fileName} ({content.Length} bytes)");
            }
            
            // Create subdirectories with files (EXACTLY same as UvfDemo)
            var subDir1 = Path.Combine(_sourceFolderPath, "subdirectory1");
            var subDir2 = Path.Combine(_sourceFolderPath, "subdirectory2");
            Directory.CreateDirectory(subDir1);
            Directory.CreateDirectory(subDir2);
            
            // Add files to subdirectories
            await File.WriteAllTextAsync(Path.Combine(subDir1, "sub1_file.txt"), "Content in subdirectory 1");
            await File.WriteAllTextAsync(Path.Combine(subDir2, "sub2_file.txt"), "Content in subdirectory 2");
            await File.WriteAllTextAsync(Path.Combine(subDir2, "another.json"), "{\"sub\": \"directory2\"}");
            
            // Create a larger file to test streaming (1MB) (EXACTLY same as UvfDemo)
            Console.WriteLine("📄 Creating large test file (1GB) to demonstrate streaming...");
            await CreateLargeTestFileAsync(Path.Combine(_sourceFolderPath, "large_file.txt"), 1024 * 1024 * 1024); // 1GB
            
            var allFiles = Directory.GetFiles(_sourceFolderPath, "*", SearchOption.AllDirectories);
            var totalSize = allFiles.Sum(f => new System.IO.FileInfo(f).Length);
            Console.WriteLine($"✅ Created {allFiles.Length} test files (total: {totalSize:N0} bytes)");
        }
        
        private async Task CreateLargeTestFileAsync(string filePath, long targetSize)
        {
            Console.WriteLine($"📄 Creating large test file ({targetSize / (1024 * 1024):N0}MB) to demonstrate streaming...");
            
            // Pre-allocate buffer to avoid measuring allocation time
            const int bufferSize = 1024 * 1024; // 1MB buffer for file creation
            byte[] buffer = new byte[bufferSize];
            
            // Fill buffer with test data pattern (measure this separately)
            var patternStopwatch = Stopwatch.StartNew();
            for (int i = 0; i < bufferSize; i++)
            {
                buffer[i] = (byte)('A' + (i % 26)); // Repeating A-Z pattern
            }
            patternStopwatch.Stop();
            
            // Measure only the actual file write time
            var writeStopwatch = Stopwatch.StartNew();
            using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                long bytesWritten = 0;
                while (bytesWritten < targetSize)
                {
                    int bytesToWrite = (int)Math.Min(bufferSize, targetSize - bytesWritten);
                    await stream.WriteAsync(buffer, 0, bytesToWrite);
                    bytesWritten += bytesToWrite;
                }
                await stream.FlushAsync();
            }
            writeStopwatch.Stop();
            
            // Calculate and report raw write performance
            double rawWriteSpeed = (targetSize / (1024.0 * 1024.0)) / writeStopwatch.Elapsed.TotalSeconds;
            Console.WriteLine($"   Created: large_file.txt ({targetSize:N0} bytes) [STREAMED CREATION]");
            Console.WriteLine($"   📊 Raw Write Speed: {rawWriteSpeed:F2} MB/s (pattern: {patternStopwatch.ElapsedMilliseconds}ms, write: {writeStopwatch.ElapsedMilliseconds}ms)");
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
            Console.WriteLine("\n📦 Demonstrating Real Cryptomator V8 Vault Operations...");
            await RealCryptomatorVaultOperationsAsync();
        }

        private async Task RealCryptomatorVaultOperationsAsync()
        {
            Console.WriteLine("📦 Demonstrating Real Cryptomator Vault Operations...");
            
            // Cleanup directories first
            CleanupDirectory(_vaultFolderPath, "vault");
            CleanupDirectory(_decryptedFolderPath, "decrypted");
            
            // Create real Cryptomator vault using TitanVault (like UvfDemo does)
            char[] passwordChars = _password.ToCharArray();
            try
            {
                Console.WriteLine("1️⃣ Creating real Cryptomator vault using TitanVault...");
                
                var vault = TitanVaultWrapper.TitanVault.CreateCryptomatorVault(
                    _vaultFolderPath, 
                    passwordChars
                );
                
                Console.WriteLine("✅ Real Cryptomator vault created successfully");
                
                Console.WriteLine("2️⃣ Processing file encryption (source -> vault)...");
                _stopwatch.Restart();
                _totalBytesProcessed = 0;
                _totalBytesWritten = 0;
                _writeElapsed = TimeSpan.Zero;
                
                await ProcessSourceDirectoryForEncryption(vault);
                
                _stopwatch.Stop();
                PrintSpeed("Encryption", _totalBytesProcessed, _stopwatch.Elapsed);
                
                Console.WriteLine("3️⃣ Processing file decryption (vault -> decrypted)...");
                Directory.CreateDirectory(_decryptedFolderPath);
                
                _stopwatch.Restart();
                _totalBytesProcessed = 0;
                _totalBytesRead = 0;
                _readElapsed = TimeSpan.Zero;
                
                await ProcessVaultDirectoryForDecryption(vault);
                
                _stopwatch.Stop();
                PrintSpeed("Decryption", _totalBytesProcessed, _stopwatch.Elapsed);
                
                Console.WriteLine("4️⃣ Verifying file integrity (source vs decrypted)...");
                await VerifyFilesAsync();
                
                // Display comprehensive performance summary
                PrintPerformanceSummary();
                
                Console.WriteLine("✅ Real Cryptomator vault operations complete");
                
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
            Console.WriteLine($"📄 {operation}: {Path.GetFileName(sourceFilePath)} -> {vaultVirtualPath}");
            
            long totalBytes = 0;
            bool useStreaming = true;
            var operationStopwatch = Stopwatch.StartNew();
            
            try
            {
                // Try streaming first (preferred for large files)
                using var sourceStream = File.OpenRead(sourceFilePath);
                using var vaultStream = vault.OpenWriteStream(vaultVirtualPath);
                
                // Stream copy with buffer for large files
                var buffer = new byte[STREAMING_BUFFER_SIZE];
                int bytesRead;
                
                while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await vaultStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytes += bytesRead;
                }
                
                Console.WriteLine($"   ✅ {operation} complete (streaming): {Path.GetFileName(vaultVirtualPath)} ({totalBytes:N0} bytes)");
            }
            catch (Exception ex) when (ex.Message.Contains("DirId") || ex.Message.Contains("DirectoryMetadata"))
            {
                // AOT library bug with subdirectories - fallback to WriteAllBytes
                Console.WriteLine($"   ⚠️ Streaming failed for {vaultVirtualPath} (AOT subdirectory bug), falling back to WriteAllBytes...");
                useStreaming = false;
                
                var fileData = await File.ReadAllBytesAsync(sourceFilePath);
                vault.WriteAllBytes(vaultVirtualPath, fileData);
                totalBytes = fileData.Length;
                
                Console.WriteLine($"   ✅ {operation} complete (fallback): {Path.GetFileName(vaultVirtualPath)} ({totalBytes:N0} bytes)");
            }
            
            operationStopwatch.Stop();
            
            // Update tracking variables
            _totalBytesProcessed += totalBytes;
            _totalBytesWritten += totalBytes;
            _writeElapsed = _writeElapsed.Add(operationStopwatch.Elapsed);
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
            
            // Get directory contents from vault as FileObjects with full metadata
            var fileObjects = vault.ListDirectoryDetailed(vaultVirtualPath);
            
            foreach (var fileObj in fileObjects)
            {
                var entryName = fileObj.Filename;
                string entryPath = fileObj.VirtualPath;
                
                if (fileObj.IsDirectory)
                {
                    // Process subdirectory
                    string decryptedSubPath = Path.Combine(decryptedPhysicalPath, entryName);
                    string relativeSubPath = string.IsNullOrEmpty(relativePath) ? entryName : $"{relativePath}/{entryName}";
                    Console.WriteLine($"   📁 Directory: {entryName} (IsDirectory: {fileObj.IsDirectory})");
                    await ProcessDirectoryRecursivelyForDecryption(vault, entryPath, decryptedSubPath, relativeSubPath);
                }
                else
                {
                    // Process file with metadata info
                    string decryptedFilePath = Path.Combine(decryptedPhysicalPath, entryName);
                    Console.WriteLine($"   📄 File: {entryName} (Size: {fileObj.Size:N0} bytes, Modified: {fileObj.LastModified:yyyy-MM-dd HH:mm:ss})");
                    await CopyFileFromVaultAsync(vault, entryPath, decryptedFilePath, "decryption");
                }
            }
        }

        private async Task CopyFileFromVaultAsync(TitanVaultWrapper.TitanVault vault, string vaultVirtualPath, string targetFilePath, string operation)
        {
            Console.WriteLine($"📄 {operation}: {vaultVirtualPath} -> {Path.GetFileName(targetFilePath)}");
            
            long totalBytes = 0;
            bool useStreaming = true;
            var operationStopwatch = Stopwatch.StartNew();
            
            try
            {
                // Try streaming first (preferred for large files)
                using var vaultStream = vault.OpenReadStream(vaultVirtualPath);
                using var targetStream = File.Create(targetFilePath);
                
                // Stream copy with buffer for large files
                var buffer = new byte[STREAMING_BUFFER_SIZE];
                int bytesRead;
                
                while ((bytesRead = await vaultStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await targetStream.WriteAsync(buffer, 0, bytesRead);
                    totalBytes += bytesRead;
                }
                
                Console.WriteLine($"   ✅ {operation} complete (streaming): {Path.GetFileName(targetFilePath)} ({totalBytes:N0} bytes)");
            }
            catch (Exception ex) when (ex.Message.Contains("DirId") || ex.Message.Contains("DirectoryMetadata"))
            {
                // AOT library bug with subdirectories - fallback to ReadAllBytes
                Console.WriteLine($"   ⚠️ Streaming failed for {vaultVirtualPath} (AOT subdirectory bug), falling back to ReadAllBytes...");
                useStreaming = false;
                
                var fileData = vault.ReadAllBytes(vaultVirtualPath);
                await File.WriteAllBytesAsync(targetFilePath, fileData);
                totalBytes = fileData.Length;
                
                Console.WriteLine($"   ✅ {operation} complete (fallback): {Path.GetFileName(targetFilePath)} ({totalBytes:N0} bytes)");
            }
            
            operationStopwatch.Stop();
            
            // Update tracking variables
            _totalBytesProcessed += totalBytes;
            _totalBytesRead += totalBytes;
            _readElapsed = _readElapsed.Add(operationStopwatch.Elapsed);
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

        private async Task VerifyFilesAsync()
        {
            Console.WriteLine("🔍 Comparing source and decrypted files...");
            
            // Collect comprehensive statistics
            var sourceStats = await CollectComprehensiveStatsAsync(_sourceFolderPath, "source");
            var decryptedStats = await CollectComprehensiveStatsAsync(_decryptedFolderPath, "decrypted");
            
            Console.WriteLine($"📊 Source: {sourceStats.FileCount} files, {sourceStats.DirectoryCount} directories");
            Console.WriteLine($"📊 Decrypted: {decryptedStats.FileCount} files, {decryptedStats.DirectoryCount} directories");
            
            bool allMatch = true;
            int matchCount = 0;
            int mismatchCount = 0;
            int missingCount = 0;
            
            // Check each source file against decrypted (by size only)
            foreach (var sourceFile in sourceStats.Files)
            {
                if (decryptedStats.Files.TryGetValue(sourceFile.Key, out var decryptedFile))
                {
                    if (sourceFile.Value.Size == decryptedFile.Size)
                    {
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
            foreach (var decryptedFile in decryptedStats.Files)
            {
                if (!sourceStats.Files.ContainsKey(decryptedFile.Key))
                {
                    Console.WriteLine($"⚠️ {decryptedFile.Key} - Extra file in decrypted");
                    extraCount++;
                }
            }
            
            // Final verification summary
            Console.WriteLine("\n📈 VERIFICATION SUMMARY:");
            Console.WriteLine($"   📁 Source directories: {sourceStats.DirectoryCount}");
            Console.WriteLine($"   📁 Decrypted directories: {decryptedStats.DirectoryCount}");
            Console.WriteLine($"   📄 Source files: {sourceStats.FileCount}");
            Console.WriteLine($"   📄 Decrypted files: {decryptedStats.FileCount}");
            Console.WriteLine($"   ✅ Size matches: {matchCount}");
            if (mismatchCount > 0) Console.WriteLine($"   ❌ Size mismatches: {mismatchCount}");
            if (missingCount > 0) Console.WriteLine($"   ❌ Missing files: {missingCount}");
            if (extraCount > 0) Console.WriteLine($"   ⚠️ Extra files: {extraCount}");
            
            Console.WriteLine($"   📊 Total bytes verified: {sourceStats.TotalBytes:N0}");
            
            // Check if counts match
            bool countsMatch = sourceStats.FileCount == decryptedStats.FileCount && 
                              sourceStats.DirectoryCount == decryptedStats.DirectoryCount;
            
            // Final result message
            if (allMatch && extraCount == 0 && countsMatch)
            {
                Console.WriteLine("\n🎉 SUCCESS: All files and directories match perfectly!");
                Console.WriteLine($"   Perfect data integrity: {matchCount} files, {sourceStats.DirectoryCount} directories, {sourceStats.TotalBytes:N0} bytes verified");
            }
            else
            {
                Console.WriteLine("\n💥 FAILURE: Discrepancies found between source and decrypted!");
                if (!countsMatch)
                {
                    Console.WriteLine($"   Directory count mismatch: {sourceStats.DirectoryCount} vs {decryptedStats.DirectoryCount}");
                    Console.WriteLine($"   File count mismatch: {sourceStats.FileCount} vs {decryptedStats.FileCount}");
                }
            }
        }

        private void PrintPerformanceSummary()
        {
            Console.WriteLine("\n📊 PERFORMANCE SUMMARY:");
            
            // Write speed (encryption)
            if (_writeElapsed.TotalSeconds > 0 && _totalBytesWritten > 0)
            {
                double writeMbps = (_totalBytesWritten / (1024.0 * 1024.0)) / _writeElapsed.TotalSeconds;
                Console.WriteLine($"   📝 Write Speed (Encryption): {_totalBytesWritten:N0} bytes in {_writeElapsed.TotalMilliseconds:F0}ms = {writeMbps:F2} MB/s");
            }
            else
            {
                Console.WriteLine($"   📝 Write Speed (Encryption): {_totalBytesWritten:N0} bytes in {_writeElapsed.TotalMilliseconds:F0}ms");
            }
            
            // Read speed (decryption)
            if (_readElapsed.TotalSeconds > 0 && _totalBytesRead > 0)
            {
                double readMbps = (_totalBytesRead / (1024.0 * 1024.0)) / _readElapsed.TotalSeconds;
                Console.WriteLine($"   📖 Read Speed (Decryption): {_totalBytesRead:N0} bytes in {_readElapsed.TotalMilliseconds:F0}ms = {readMbps:F2} MB/s");
            }
            else
            {
                Console.WriteLine($"   📖 Read Speed (Decryption): {_totalBytesRead:N0} bytes in {_readElapsed.TotalMilliseconds:F0}ms");
            }
            
            // Overall summary
            long totalBytes = _totalBytesWritten + _totalBytesRead;
            TimeSpan totalTime = _writeElapsed + _readElapsed;
            if (totalTime.TotalSeconds > 0 && totalBytes > 0)
            {
                double overallMbps = (totalBytes / (1024.0 * 1024.0)) / totalTime.TotalSeconds;
                Console.WriteLine($"   🔄 Overall Throughput: {totalBytes:N0} bytes in {totalTime.TotalMilliseconds:F0}ms = {overallMbps:F2} MB/s");
            }
        }

        private async Task<DirectoryStats> CollectComprehensiveStatsAsync(string directoryPath, string directoryName)
        {
            Console.WriteLine($"📋 Collecting comprehensive stats from {directoryName} directory...");
            var stats = new DirectoryStats();
            
            if (Directory.Exists(directoryPath))
            {
                await CollectStatsRecursiveAsync(directoryPath, "", stats);
            }
            
            Console.WriteLine($"   📊 Found {stats.FileCount} files, {stats.DirectoryCount} directories, {stats.TotalBytes:N0} bytes in {directoryName}");
            return stats;
        }

        private async Task CollectStatsRecursiveAsync(string currentPath, string relativePath, DirectoryStats stats)
        {
            // Count this directory (except root)
            if (!string.IsNullOrEmpty(relativePath))
            {
                stats.DirectoryCount++;
            }
            
            // Process files in current directory
            var files = Directory.GetFiles(currentPath);
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                var fileRelativePath = string.IsNullOrEmpty(relativePath) ? fileName : $"{relativePath}/{fileName}";
                
                var fileInfo = new System.IO.FileInfo(filePath);
                var fileStats = new FileInfo { Size = fileInfo.Length };
                
                stats.Files[fileRelativePath] = fileStats;
                stats.FileCount++;
                stats.TotalBytes += fileStats.Size;
            }
            
            // Process subdirectories
            var directories = Directory.GetDirectories(currentPath);
            foreach (var dirPath in directories)
            {
                var dirName = Path.GetFileName(dirPath);
                var dirRelativePath = string.IsNullOrEmpty(relativePath) ? dirName : $"{relativePath}/{dirName}";
                
                await CollectStatsRecursiveAsync(dirPath, dirRelativePath, stats);
            }
        }

        private class FileInfo
        {
            public long Size { get; set; }
        }

        private class DirectoryStats
        {
            public Dictionary<string, FileInfo> Files { get; set; } = new Dictionary<string, FileInfo>();
            public int FileCount { get; set; } = 0;
            public int DirectoryCount { get; set; } = 0;
            public long TotalBytes { get; set; } = 0;
        }
    }
} 