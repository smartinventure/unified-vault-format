using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using ExampleVaultApp.Wrapper;

namespace ExampleVaultApp
{
    /// <summary>
    /// Tests Cryptomator vault functionality using C-style wrapper calls (but with managed implementation).
    /// This allows testing the same wrapper logic that the AOT exports use, but with debugging capability.
    /// </summary>
    public class CryptomatorTestCStyle
    {
        private readonly string _sourceFolderPath;
        private readonly string _vaultFolderPath;
        private readonly string _decryptedFolderPath;
        private readonly string _password;

        private readonly Stopwatch _stopwatch = new();
        private long _totalBytesProcessed = 0;

        public CryptomatorTestCStyle(string sourceFolderPath, string vaultFolderPath, string decryptedFolderPath, string password)
        {
            _sourceFolderPath = sourceFolderPath ?? throw new ArgumentNullException(nameof(sourceFolderPath));
            _vaultFolderPath = vaultFolderPath ?? throw new ArgumentNullException(nameof(vaultFolderPath));
            _decryptedFolderPath = decryptedFolderPath ?? throw new ArgumentNullException(nameof(decryptedFolderPath));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        public async Task RunTestAsync()
        {
            Console.WriteLine("===== CryptomatorTestCStyle - C-Style Wrapper (Managed Implementation) =====");
            Console.WriteLine($"Source: {_sourceFolderPath}");
            Console.WriteLine($"Vault: {_vaultFolderPath}");
            Console.WriteLine($"Decrypted: {_decryptedFolderPath}");
            Console.WriteLine();

            try
            {
                // Phase 1: Cleanup first (before creating vault)
                CleanupDirectory(_vaultFolderPath, "vault");
                CleanupDirectory(_decryptedFolderPath, "decrypted");
                
                // Phase 2: Create fresh vault using C-style wrapper
                await CreateVaultWithCStyleAsync();
                
                // Phase 3: Encryption phase (File -> C-style wrapper)
                await EncryptionPhaseAsync();
                
                // Phase 4: Decryption phase (C-style wrapper -> File)
                await DecryptionPhaseAsync();
                
                // Phase 5: Verification phase (File vs File)
                await VerificationPhaseAsync();
                
                Console.WriteLine("✅ CryptomatorTestCStyle completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ CryptomatorTestCStyle failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private unsafe Task CreateVaultWithCStyleAsync()
        {
            Console.WriteLine("\n🆕 Creating Cryptomator vault using C-style wrapper...");
            
            // Phase 2: Create fresh vault directory
            Directory.CreateDirectory(_vaultFolderPath);

            // Convert strings to byte arrays for C-style call
            byte[] vaultPathBytes = Encoding.UTF8.GetBytes(_vaultFolderPath);
            byte[] passwordBytes = Encoding.UTF8.GetBytes(_password);

            // Call the managed wrapper using unsafe pointers (same as C-style)
            fixed (byte* vaultPathPtr = vaultPathBytes)
            fixed (byte* passwordPtr = passwordBytes)
            {
                int result = TitanVaultNativeMethods.CreateCryptomatorVault(
                    vaultPathPtr, vaultPathBytes.Length,
                    passwordPtr, passwordBytes.Length
                );

                if (result != 0)
                {
                    // Get error message using C-style call
                    IntPtr errorPtr = TitanVaultNativeMethods.GetLastError();
                    string errorMessage = errorPtr != IntPtr.Zero 
                        ? Marshal.PtrToStringAnsi(errorPtr) ?? "Unknown error"
                        : "Unknown error";
                    
                    // Free the error string
                    if (errorPtr != IntPtr.Zero)
                    {
                        TitanVaultNativeMethods.FreeString(errorPtr);
                    }
                    
                    throw new Exception($"Failed to create Cryptomator vault. Error code: {result}, Message: {errorMessage}");
                }
            }

            Console.WriteLine("✅ Cryptomator vault created successfully using C-style wrapper");

            // Verify vault creation - Cryptomator vaults have different file structure
            string masterkeyFile = Path.Combine(_vaultFolderPath, "masterkey.cryptomator");
            string vaultConfigFile = Path.Combine(_vaultFolderPath, "vault.cryptomator");
            
            if (!File.Exists(masterkeyFile))
            {
                throw new Exception("masterkey.cryptomator file was not created");
            }
            
            if (!File.Exists(vaultConfigFile))
            {
                throw new Exception("vault.cryptomator file was not created");
            }

            var masterkeyInfo = new System.IO.FileInfo(masterkeyFile);
            var vaultConfigInfo = new System.IO.FileInfo(vaultConfigFile);
            
            Console.WriteLine($"✅ Masterkey file created: {masterkeyFile} ({masterkeyInfo.Length} bytes)");
            Console.WriteLine($"✅ Vault config file created: {vaultConfigFile} ({vaultConfigInfo.Length} bytes)");
            
            return Task.CompletedTask;
        }

        private async Task EncryptionPhaseAsync()
        {
            Console.WriteLine("📦 Starting encryption phase (File -> C-Style Wrapper)...");

            _stopwatch.Restart();
            _totalBytesProcessed = 0;

            // Load the vault using C-style wrapper
            using var vault = LoadVaultWithCStyle();
            
            // Process source directory recursively
            await ProcessDirectoryForEncryptionAsync(_sourceFolderPath, vault, "", "/");

            _stopwatch.Stop();
            PrintSpeed("Encryption", _totalBytesProcessed, _stopwatch.Elapsed);
            Console.WriteLine("✅ Encryption phase complete");
        }

        private async Task DecryptionPhaseAsync()
        {
            Console.WriteLine("📦 Starting decryption phase (C-Style Wrapper -> File)...");

            _stopwatch.Restart();
            _totalBytesProcessed = 0;

            // Ensure decrypted directory exists
            Directory.CreateDirectory(_decryptedFolderPath);

            // Load the vault using C-style wrapper
            using var vault = LoadVaultWithCStyle();
            
            // Process vault directory recursively
            await ProcessDirectoryForDecryptionAsync(vault, "/", _decryptedFolderPath, "");

            _stopwatch.Stop();
            PrintSpeed("Decryption", _totalBytesProcessed, _stopwatch.Elapsed);
            Console.WriteLine("✅ Decryption phase complete");
        }

        private TitanVaultWrapper.TitanVault LoadVaultWithCStyle()
        {
            char[] passwordChars = _password.ToCharArray();
            try
            {
                return TitanVaultWrapper.TitanVault.LoadCryptomatorVault(_vaultFolderPath, passwordChars);
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }

        private async Task ProcessDirectoryForEncryptionAsync(string sourcePhysicalPath, TitanVaultWrapper.TitanVault vault, string relativeSourcePath, string vaultVirtualPath)
        {
            Console.WriteLine($"📁 Processing directory: {relativeSourcePath} -> {vaultVirtualPath}");
            
            // Create virtual directory in vault (except root)
            if (vaultVirtualPath != "/")
            {
                vault.CreateDirectory(vaultVirtualPath);
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

        private async Task ProcessDirectoryForDecryptionAsync(TitanVaultWrapper.TitanVault vault, string vaultVirtualPath, string decryptedPhysicalPath, string relativeDecryptedPath)
        {
            Console.WriteLine($"📁 Processing virtual directory: {vaultVirtualPath} -> {relativeDecryptedPath}");
            
            // Create target directory (except root)
            if (!string.IsNullOrEmpty(relativeDecryptedPath))
            {
                Directory.CreateDirectory(decryptedPhysicalPath);
                Console.WriteLine($"   Created decrypted directory: {relativeDecryptedPath}");
            }
            
            // Check if vault directory exists
            if (!vault.DirectoryExists(vaultVirtualPath))
            {
                Console.WriteLine($"   Vault directory does not exist: {vaultVirtualPath}");
                return;
            }
            
            // Get directory contents from vault
            var entries = vault.ListDirectoryDetailed(vaultVirtualPath);
            
            foreach (var entry in entries)
            {
                string entryName = entry.Name;
                string entryPath = vaultVirtualPath == "/" ? $"/{entryName}" : $"{vaultVirtualPath}/{entryName}";

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

        private async Task CopyFileToVaultAsync(string sourceFilePath, TitanVaultWrapper.TitanVault vault, string vaultVirtualPath, string operation)
        {
            Console.WriteLine($"📄 {operation}: {Path.GetFileName(sourceFilePath)} -> {vaultVirtualPath}");
            
            // Use streaming operations instead of ReadAllBytes/WriteAllBytes
            using var sourceStream = File.OpenRead(sourceFilePath);
            using var vaultStream = vault.OpenWriteStream(vaultVirtualPath);
            
            // Stream copy with buffer for large files
            const int bufferSize = 64 * 1024; // 64KB buffer
            var buffer = new byte[bufferSize];
            int bytesRead;
            long totalBytes = 0;
            
            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await vaultStream.WriteAsync(buffer, 0, bytesRead);
                totalBytes += bytesRead;
            }
            
            _totalBytesProcessed += totalBytes;
            Console.WriteLine($"   ✅ {operation} complete: {Path.GetFileName(vaultVirtualPath)} ({totalBytes:N0} bytes)");
        }

        private async Task CopyFileFromVaultAsync(TitanVaultWrapper.TitanVault vault, string vaultVirtualPath, string targetFilePath, string operation)
        {
            Console.WriteLine($"📄 {operation}: {vaultVirtualPath} -> {Path.GetFileName(targetFilePath)}");
            
            // Use streaming operations instead of ReadAllBytes/WriteAllBytes
            using var vaultStream = vault.OpenReadStream(vaultVirtualPath);
            using var targetStream = File.Create(targetFilePath);
            
            // Stream copy with buffer for large files
            const int bufferSize = 64 * 1024; // 64KB buffer
            var buffer = new byte[bufferSize];
            int bytesRead;
            long totalBytes = 0;
            
            while ((bytesRead = await vaultStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await targetStream.WriteAsync(buffer, 0, bytesRead);
                totalBytes += bytesRead;
            }
            
            _totalBytesProcessed += totalBytes;
            Console.WriteLine($"   ✅ {operation} complete: {Path.GetFileName(targetFilePath)} ({totalBytes:N0} bytes)");
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
                        Console.WriteLine($"✅ {sourceFile.Key} - Perfect match ({sourceFile.Value.Size:N0} bytes)");
                        matchCount++;
                    }
                    else
                    {
                        Console.WriteLine($"❌ {sourceFile.Key} - Mismatch detected!");
                        Console.WriteLine($"   Source:    {sourceFile.Value.Size:N0} bytes, MD5: {sourceFile.Value.Hash}");
                        Console.WriteLine($"   Decrypted: {decryptedFile.Size:N0} bytes, MD5: {decryptedFile.Hash}");
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
            
            // Final result message
            if (allMatch && extraCount == 0)
            {
                Console.WriteLine("\n🎉 SUCCESS: All source items exist in decrypted storage with matching content and directory structure!");
                Console.WriteLine($"   Perfect data integrity: {matchCount} files, {_totalBytesProcessed:N0} bytes verified");
            }
            else
            {
                Console.WriteLine("\n💥 FAILURE: Discrepancies found between source and decrypted storage!");
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

        private class FileInfo
        {
            public string Hash { get; set; } = "";
            public long Size { get; set; }
        }
    }
} 