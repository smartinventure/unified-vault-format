using StorageLib.Abstractions;
using UvfLib.Storage;
using UvfLib.Storage.Decorators;
using UvfLib.Vault;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace ExampleVaultApp
{
    /// <summary>
    /// Tests Cryptomator V8 functionality using ONLY the IStorage interface.
    /// This is a pure test of the storage abstraction layer without any direct file system operations.
    /// Replicates the testrun --cryptomator functionality from the old Program.cs
    /// but uses IStorage decorators for all operations including source file access.
    /// </summary>
    public class DirectCryptomatorTest
    {
        private readonly string _sourceFolderPath;
        private readonly string _vaultFolderPath;
        private readonly string _decryptedFolderPath;
        private readonly string _password;
        
        private readonly Stopwatch _stopwatch = new();
        private long _totalBytesProcessed = 0;

        public DirectCryptomatorTest(string sourceFolderPath, string vaultFolderPath, string decryptedFolderPath, string password)
        {
            _sourceFolderPath = sourceFolderPath;
            _vaultFolderPath = vaultFolderPath;
            _decryptedFolderPath = decryptedFolderPath;
            _password = password;
        }

        public async Task RunTestAsync()
        {
            Console.WriteLine("===== DirectCryptomatorTest - Pure IStorage Implementation =====");
            Console.WriteLine($"Source: {_sourceFolderPath}");
            Console.WriteLine($"Vault: {_vaultFolderPath}");
            Console.WriteLine($"Decrypted: {_decryptedFolderPath}");
            Console.WriteLine();

            try
            {
                // Phase 1: Setup storage interfaces
                var (sourceStorage, vaultStorage, decryptedStorage) = await SetupStorageInterfacesAsync();
                
                using (sourceStorage)
                using (vaultStorage)
                using (decryptedStorage)
                {
                    // Phase 2: Cleanup using IStorage
                    await CleanupTestDirectoriesAsync(vaultStorage, decryptedStorage);
                    
                    // Phase 3: Ensure source data exists
                    await EnsureSourceDataAsync(sourceStorage);
                    
                    // Phase 4: Encryption phase (source -> vault)
                    await EncryptionPhaseAsync(sourceStorage, vaultStorage);
                    
                    // Phase 5: Decryption phase (vault -> decrypted)
                    await DecryptionPhaseAsync(vaultStorage, decryptedStorage);
                    
                    // Phase 6: Verification phase (source vs decrypted)
                    await VerificationPhaseAsync(sourceStorage, decryptedStorage);
                }
                
                Console.WriteLine("✅ DirectCryptomatorTest completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ DirectCryptomatorTest failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task<(IStorage sourceStorage, IStorage vaultStorage, IStorage decryptedStorage)> SetupStorageInterfacesAsync()
        {
            Console.WriteLine("🔧 Setting up storage interfaces...");
            
            // Create source storage (plain LocalStorage for reading source files)
            var sourceStorage = await StorageFactory.CreateInitializedLocalStorageAsync(_sourceFolderPath);
            Console.WriteLine($"✅ Source storage initialized: {_sourceFolderPath}");
            
            // Create decrypted storage (plain LocalStorage for writing decrypted files)
            var decryptedStorage = await StorageFactory.CreateInitializedLocalStorageAsync(_decryptedFolderPath);
            Console.WriteLine($"✅ Decrypted storage initialized: {_decryptedFolderPath}");
            
            // Create vault storage (CryptomatorStorageDecorator)
            await SetupVaultDirectoryAsync(); // Ensure vault directory exists
            var vault = await CreateOrLoadVaultAsync();
            
            var vaultLocalStorage = await StorageFactory.CreateInitializedLocalStorageAsync("/");
            
            var vaultStorage = new CryptomatorStorageDecorator(
                vaultLocalStorage, 
                vault, 
                vaultBasePath: _vaultFolderPath);
            
            Console.WriteLine($"✅ Vault storage initialized: {_vaultFolderPath}");
            
            return (sourceStorage, vaultStorage, decryptedStorage);
        }

        private Task SetupVaultDirectoryAsync()
        {
            // Only create the vault directory if it doesn't exist
            // We still need this minimal file system operation to ensure the vault can be created
            if (!Directory.Exists(_vaultFolderPath))
            {
                Directory.CreateDirectory(_vaultFolderPath);
            }
            return Task.CompletedTask;
        }

        private Task<VaultHandler> CreateOrLoadVaultAsync()
        {
            string masterkeyPath = Path.Combine(_vaultFolderPath, "masterkey.cryptomator");
            
            if (!File.Exists(masterkeyPath))
            {
                Console.WriteLine("🔧 Creating new Cryptomator V8 vault...");
                VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultFolderPath, _password);
                Console.WriteLine("✅ Vault created");
            }
            else
            {
                Console.WriteLine("🔧 Loading existing Cryptomator V8 vault...");
            }
            
            byte[] masterkeyBytes = File.ReadAllBytes(masterkeyPath);
            var vault = VaultHandler.LoadCryptomatorV8Vault(masterkeyBytes, _password);
            Console.WriteLine("✅ Vault loaded");
            
            return Task.FromResult(vault);
        }

        private async Task CleanupTestDirectoriesAsync(IStorage vaultStorage, IStorage decryptedStorage)
        {
            Console.WriteLine("🧹 Cleaning up test directories using IStorage...");
            
            // Clean vault storage (delete all virtual files/directories)
            await CleanupStorageAsync(vaultStorage, "vault");
            
            // Clean decrypted storage (delete all files/directories)
            await CleanupStorageAsync(decryptedStorage, "decrypted");
            
            Console.WriteLine("✅ Cleanup complete");
        }

        private async Task CleanupStorageAsync(IStorage storage, string storageName)
        {
            try
            {
                if (await storage.DirectoryExistsAsync("/"))
                {
                    var entries = await storage.ReadDirAsync("/", true);
                    
                    foreach (var entry in entries)
                    {
                        if (entry.IsDirectory)
                        {
                            await DeleteDirectoryRecursiveAsync(storage, entry.VirtualPath);
                        }
                        else
                        {
                            await storage.DeleteAsync(entry.VirtualPath);
                        }
                    }
                    
                    Console.WriteLine($"   ✅ Cleaned {storageName} storage ({entries.Count()} items removed)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️ Warning: Could not fully clean {storageName} storage: {ex.Message}");
            }
        }

        private async Task DeleteDirectoryRecursiveAsync(IStorage storage, string virtualPath)
        {
            if (await storage.DirectoryExistsAsync(virtualPath))
            {
                var entries = await storage.ReadDirAsync(virtualPath, true);
                
                foreach (var entry in entries)
                {
                    if (entry.IsDirectory)
                    {
                        await DeleteDirectoryRecursiveAsync(storage, entry.VirtualPath);
                    }
                    else
                    {
                        await storage.DeleteAsync(entry.VirtualPath);
                    }
                }
                
                await storage.DeleteDirectoryAsync(virtualPath);
            }
        }

        private async Task EnsureSourceDataAsync(IStorage sourceStorage)
        {
            Console.WriteLine("📋 Ensuring source data exists...");
            
            // Check if source directory has any content
            bool hasContent = false;
            if (await sourceStorage.DirectoryExistsAsync("/"))
            {
                var entries = await sourceStorage.ReadDirAsync("/", true);
                hasContent = entries.Count() > 0;
            }
            
            if (!hasContent)
            {
                Console.WriteLine("⚠️ Source directory is empty, creating test data using IStorage...");
                
                // Create test files using IStorage interface
                await CreateTestFileAsync(sourceStorage, "/test.txt", "Hello, World! This is a test file.");
                await CreateTestFileAsync(sourceStorage, "/readme.md", "# Test Vault\n\nThis is a test markdown file for vault encryption testing.");
                
                // Create subdirectory with files
                await sourceStorage.CreateDirectoryAsync("/subfolder");
                await CreateTestFileAsync(sourceStorage, "/subfolder/nested.txt", "This is a nested file in a subdirectory.");
                await CreateTestFileAsync(sourceStorage, "/subfolder/data.json", "{\"message\": \"JSON test data\", \"number\": 42}");
                
                // Create deeper nesting
                await sourceStorage.CreateDirectoryAsync("/subfolder/deep");
                await CreateTestFileAsync(sourceStorage, "/subfolder/deep/deep_file.txt", "Deep nested file content for testing directory structures.");
                
                Console.WriteLine("✅ Test data created");
            }
            else
            {
                var entries = await sourceStorage.ReadDirAsync("/", true);
                Console.WriteLine($"✅ Source data exists ({entries.Count()} items found)");
            }
        }

        private async Task CreateTestFileAsync(IStorage storage, string virtualPath, string content)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(content);
            
            IntPtr fileHandle = await storage.OpenAsync(virtualPath, OpenFlags.Create | OpenFlags.WriteOnly);
            try
            {
                IntPtr dataPtr = Marshal.AllocHGlobal(data.Length);
                try
                {
                    Marshal.Copy(data, 0, dataPtr, data.Length);
                    await storage.WriteAsync(fileHandle, 0, data.Length, dataPtr);
                    Console.WriteLine($"   📄 Created: {virtualPath} ({data.Length} bytes)");
                }
                finally
                {
                    Marshal.FreeHGlobal(dataPtr);
                }
            }
            finally
            {
                await storage.CloseAsync(fileHandle);
            }
        }

        private async Task EncryptionPhaseAsync(IStorage sourceStorage, IStorage vaultStorage)
        {
            Console.WriteLine("\n📦 Starting encryption phase (IStorage -> IStorage)...");
            
            _stopwatch.Restart();
            _totalBytesProcessed = 0;
            
            // Process source directory recursively using only IStorage operations
            await ProcessDirectoryForEncryptionAsync(sourceStorage, vaultStorage, "/", "/");
            
            _stopwatch.Stop();
            PrintSpeed("Encryption", _totalBytesProcessed, _stopwatch.Elapsed);
            Console.WriteLine("✅ Encryption phase complete");
        }

        private async Task ProcessDirectoryForEncryptionAsync(IStorage sourceStorage, IStorage vaultStorage, string sourceVirtualPath, string vaultVirtualPath)
        {
            Console.WriteLine($"📁 Processing directory: {sourceVirtualPath} -> {vaultVirtualPath}");
            
            // Create virtual directory in vault (except root)
            if (vaultVirtualPath != "/")
            {
                await vaultStorage.CreateDirectoryAsync(vaultVirtualPath);
                Console.WriteLine($"   Created vault directory: {vaultVirtualPath}");
            }
            
            // Check if source directory exists
            if (!await sourceStorage.DirectoryExistsAsync(sourceVirtualPath))
            {
                Console.WriteLine($"   Source directory does not exist: {sourceVirtualPath}");
                return;
            }
            
            // Get directory contents from source
            var entries = await sourceStorage.ReadDirAsync(sourceVirtualPath, true);
            
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    // Process subdirectory
                    string vaultSubPath = vaultVirtualPath == "/" ? $"/{entry.Filename}" : $"{vaultVirtualPath}/{entry.Filename}";
                    await ProcessDirectoryForEncryptionAsync(sourceStorage, vaultStorage, entry.VirtualPath, vaultSubPath);
                }
                else
                {
                    // Process file
                    string vaultFilePath = vaultVirtualPath == "/" ? $"/{entry.Filename}" : $"{vaultVirtualPath}/{entry.Filename}";
                    await CopyFileAsync(sourceStorage, vaultStorage, entry.VirtualPath, vaultFilePath, "encryption");
                }
            }
        }

        private async Task DecryptionPhaseAsync(IStorage vaultStorage, IStorage decryptedStorage)
        {
            Console.WriteLine("\n📦 Starting decryption phase (IStorage -> IStorage)...");
            
            _stopwatch.Restart();
            _totalBytesProcessed = 0;
            
            // Process vault directory recursively using only IStorage operations
            await ProcessDirectoryForDecryptionAsync(vaultStorage, decryptedStorage, "/", "/");
            
            _stopwatch.Stop();
            PrintSpeed("Decryption", _totalBytesProcessed, _stopwatch.Elapsed);
            Console.WriteLine("✅ Decryption phase complete");
        }

        private async Task ProcessDirectoryForDecryptionAsync(IStorage vaultStorage, IStorage decryptedStorage, string vaultVirtualPath, string decryptedVirtualPath)
        {
            Console.WriteLine($"📁 Processing virtual directory: {vaultVirtualPath} -> {decryptedVirtualPath}");
            
            // Create target directory (except root)
            if (decryptedVirtualPath != "/")
            {
                await decryptedStorage.CreateDirectoryAsync(decryptedVirtualPath);
                Console.WriteLine($"   Created decrypted directory: {decryptedVirtualPath}");
            }
            
            // Check if vault directory exists
            if (!await vaultStorage.DirectoryExistsAsync(vaultVirtualPath))
            {
                Console.WriteLine($"   Vault directory does not exist: {vaultVirtualPath}");
                return;
            }
            
            // Get directory contents from vault
            var entries = await vaultStorage.ReadDirAsync(vaultVirtualPath, true);
            
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    // Process subdirectory
                    string decryptedSubPath = decryptedVirtualPath == "/" ? $"/{entry.Filename}" : $"{decryptedVirtualPath}/{entry.Filename}";
                    await ProcessDirectoryForDecryptionAsync(vaultStorage, decryptedStorage, entry.VirtualPath, decryptedSubPath);
                }
                else
                {
                    // Process file
                    string decryptedFilePath = decryptedVirtualPath == "/" ? $"/{entry.Filename}" : $"{decryptedVirtualPath}/{entry.Filename}";
                    await CopyFileAsync(vaultStorage, decryptedStorage, entry.VirtualPath, decryptedFilePath, "decryption");
                }
            }
        }

        private async Task CopyFileAsync(IStorage sourceStorage, IStorage targetStorage, string sourceVirtualPath, string targetVirtualPath, string operation)
        {
            Console.WriteLine($"📄 {operation}: {sourceVirtualPath} -> {targetVirtualPath}");
            
            // Get source file info
            var sourceFileInfo = await sourceStorage.GetFileInfoAsync(sourceVirtualPath);
            long fileSize = sourceFileInfo.Size;
            
            // Open source file for reading
            IntPtr sourceHandle = await sourceStorage.OpenAsync(sourceVirtualPath, OpenFlags.ReadOnly);
            try
            {
                // Open target file for writing
                IntPtr targetHandle = await targetStorage.OpenAsync(targetVirtualPath, OpenFlags.Create | OpenFlags.WriteOnly);
                try
                {
                    if (fileSize > 0)
                    {
                        // Allocate buffer for file data
                        IntPtr dataPtr = Marshal.AllocHGlobal((int)fileSize);
                        try
                        {
                            // Read from source
                            await sourceStorage.ReadAsync(sourceHandle, 0, fileSize, dataPtr);
                            
                            // Write to target
                            await targetStorage.WriteAsync(targetHandle, 0, fileSize, dataPtr);
                            
                            _totalBytesProcessed += fileSize;
                            Console.WriteLine($"   ✅ {operation} complete: {Path.GetFileName(targetVirtualPath)} ({fileSize} bytes)");
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(dataPtr);
                        }
                    }
                    else
                    {
                        // Empty file - just create it
                        Console.WriteLine($"   ✅ {operation} complete: {Path.GetFileName(targetVirtualPath)} (empty file)");
                    }
                }
                finally
                {
                    await targetStorage.CloseAsync(targetHandle);
                }
            }
            finally
            {
                await sourceStorage.CloseAsync(sourceHandle);
            }
        }

        private async Task VerificationPhaseAsync(IStorage sourceStorage, IStorage decryptedStorage)
        {
            Console.WriteLine("\n🔍 Starting verification phase (IStorage vs IStorage)...");
            
            var sourceFiles = await CollectFilesAsync(sourceStorage, "source");
            var decryptedFiles = await CollectFilesAsync(decryptedStorage, "decrypted");
            
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
                    Console.WriteLine($"❌ {sourceFile.Key} - Missing in decrypted storage");
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

        private async Task<Dictionary<string, FileInfo>> CollectFilesAsync(IStorage storage, string storageName)
        {
            Console.WriteLine($"📋 Collecting files from {storageName} storage...");
            var files = new Dictionary<string, FileInfo>();
            
            if (await storage.DirectoryExistsAsync("/"))
            {
                await CollectFilesRecursiveAsync(storage, "/", files);
            }
            
            Console.WriteLine($"   📊 Found {files.Count} files in {storageName} storage");
            return files;
        }

        private async Task CollectFilesRecursiveAsync(IStorage storage, string virtualPath, Dictionary<string, FileInfo> files)
        {
            var entries = await storage.ReadDirAsync(virtualPath, true);
            
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    // Recursively process subdirectory
                    await CollectFilesRecursiveAsync(storage, entry.VirtualPath, files);
                }
                else
                {
                    // Process file - read and hash it
                    var fileInfo = await storage.GetFileInfoAsync(entry.VirtualPath);
                    long fileSize = fileInfo.Size;
                    
                    string hash;
                    if (fileSize > 0)
                    {
                        // Read file data and calculate hash
                        IntPtr fileHandle = await storage.OpenAsync(entry.VirtualPath, OpenFlags.ReadOnly);
                        try
                        {
                            IntPtr dataPtr = Marshal.AllocHGlobal((int)fileSize);
                            try
                            {
                                await storage.ReadAsync(fileHandle, 0, fileSize, dataPtr);
                                
                                byte[] fileData = new byte[fileSize];
                                Marshal.Copy(dataPtr, fileData, 0, (int)fileSize);
                                
                                hash = CalculateMD5Hash(fileData);
                            }
                            finally
                            {
                                Marshal.FreeHGlobal(dataPtr);
                            }
                        }
                        finally
                        {
                            await storage.CloseAsync(fileHandle);
                        }
                    }
                    else
                    {
                        // Empty file
                        hash = CalculateMD5Hash(Array.Empty<byte>());
                    }
                    
                    // Use relative path as key (remove leading slash for consistency)
                    string relativePath = entry.VirtualPath.TrimStart('/');
                    if (string.IsNullOrEmpty(relativePath))
                    {
                        relativePath = entry.Filename; // Root level file
                    }
                    
                    files[relativePath] = new FileInfo { Hash = hash, Size = fileSize };
                }
            }
        }

        private static string CalculateMD5Hash(byte[] data)
        {
            using var md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(data);
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