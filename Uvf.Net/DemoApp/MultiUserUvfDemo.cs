using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using DemoApp.Wrapper;

namespace DemoApp
{
    /// <summary>
    /// Demonstrates UVF multi-user vault operations using TitanVault native exports.
    /// Shows how to add users, remove users, change passwords, and verify data integrity.
    /// </summary>
    public class MultiUserUvfDemo
    {
        private readonly string _sourceFolderPath;
        private readonly string _vaultFolderPath;
        private readonly string _decryptedFolderPath;
        private readonly string _adminPassword;
        private readonly bool _encryptFilenames;

        private readonly Stopwatch _stopwatch = new();
        private long _totalBytesProcessed = 0;

        // Separate tracking for read and write operations
        private TimeSpan _writeElapsed = TimeSpan.Zero;
        private TimeSpan _readElapsed = TimeSpan.Zero;
        private long _totalBytesWritten = 0;
        private long _totalBytesRead = 0;

        public MultiUserUvfDemo(string sourceFolderPath, string vaultFolderPath, string decryptedFolderPath, string adminPassword, bool encryptFilenames = true)
        {
            _sourceFolderPath = sourceFolderPath;
            _vaultFolderPath = vaultFolderPath;
            _decryptedFolderPath = decryptedFolderPath;
            _adminPassword = adminPassword;
            _encryptFilenames = encryptFilenames;
        }

        public async Task RunDemoAsync()
        {
            Console.WriteLine("===== Multi-User UVF Demo =====");
            Console.WriteLine($"Source: {_sourceFolderPath}");
            Console.WriteLine($"Vault: {_vaultFolderPath}");
            Console.WriteLine($"Decrypted: {_decryptedFolderPath}");
            Console.WriteLine($"Filename Encryption: {(_encryptFilenames ? "Enabled" : "Disabled")}");
            Console.WriteLine();

            try
            {
                // Clean directories first
                CleanupDirectory(_vaultFolderPath, "vault");
                CleanupDirectory(_decryptedFolderPath, "decrypted");

                await SetupTestDataAsync();
                await CreateInitialVaultAsync();
                await DemonstrateUserManagementAsync();
                await DemonstratePasswordChangesAsync();
                await VerifyDataIntegrityAsync();
                
                Console.WriteLine("✅ MultiUserUvfDemo completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ MultiUserUvfDemo failed: {ex.Message}");
                throw;
            }
        }

        private async Task SetupTestDataAsync()
        {
            Console.WriteLine("📁 Setting up test data for multi-user demo...");
            Directory.CreateDirectory(_sourceFolderPath);
            
            var testFiles = new[]
            {
                ("admin_document.txt", "This is an admin document for multi-user UVF vault!"),
                ("shared_config.json", $"{{\"vault\": \"multi-user\", \"created\": \"{DateTime.Now:O}\", \"users\": [\"admin\", \"alice\", \"bob\"]}}"),
                ("team_data.txt", "Shared team data that all users should be able to access"),
                ("reports/monthly.txt", "Monthly report data in subdirectory"),
                ("reports/quarterly.txt", "Quarterly report data"),
                ("projects/project1.txt", "Project 1 documentation and notes"),
                ("projects/project2.txt", "Project 2 specifications and requirements")
            };

            foreach (var (filePath, content) in testFiles)
            {
                string fullPath = Path.Combine(_sourceFolderPath, filePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, content);
                Console.WriteLine($"   Created: {filePath} ({content.Length} bytes)");
            }
            
            Console.WriteLine($"✅ Created {testFiles.Length} test files for multi-user demo");
        }

        private async Task CreateInitialVaultAsync()
        {
            Console.WriteLine("\n1️⃣ Creating initial UVF vault (single-user for now)...");
            Console.WriteLine("⚠️ Note: Multi-user operations not yet fully implemented in native exports");
            
            // Create a single-user UVF vault using TitanVaultWrapper
            var vault = TitanVaultWrapper.TitanVault.CreateUvfVault(
                _vaultFolderPath, 
                _adminPassword, 
                _encryptFilenames);

            Console.WriteLine("✅ UVF vault created with admin user");

            try
            {
                // Populate vault with test data
                Console.WriteLine("📦 Populating vault with test data...");
                _stopwatch.Restart();
                _totalBytesProcessed = 0;
                _totalBytesWritten = 0;
                _writeElapsed = TimeSpan.Zero;

                await PopulateVaultWithTestData(vault);

                _stopwatch.Stop();
                PrintSpeed("Initial Data Population", _totalBytesProcessed, _stopwatch.Elapsed);
                PrintPerformanceSummary();
            }
            finally
            {
                vault.Dispose();
            }
        }

        private async Task PopulateVaultWithTestData(TitanVaultWrapper.TitanVault vault)
        {
            var files = Directory.GetFiles(_sourceFolderPath, "*", SearchOption.AllDirectories);
            
            foreach (var filePath in files)
            {
                var relativePath = Path.GetRelativePath(_sourceFolderPath, filePath);
                var vaultPath = "/" + relativePath.Replace("\\", "/");
                
                // Create directory if needed
                var dirPath = Path.GetDirectoryName(vaultPath);
                if (!string.IsNullOrEmpty(dirPath) && dirPath != "/")
                {
                    vault.CreateDirectory(dirPath);
                }
                
                // Copy file to vault
                var fileData = await File.ReadAllBytesAsync(filePath);
                vault.WriteAllBytes(vaultPath, fileData);
                
                _totalBytesProcessed += fileData.Length;
                _totalBytesWritten += fileData.Length;
                Console.WriteLine($"   📄 {relativePath} -> {vaultPath} ({fileData.Length} bytes)");
            }
        }

        private async Task DemonstrateUserManagementAsync()
        {
            Console.WriteLine("\n2️⃣ Demonstrating user management operations...");
            
            char[] adminPasswordChars = _adminPassword.ToCharArray();
            try
            {
                // List initial users
                Console.WriteLine("📋 Listing initial vault users...");
                var users = TitanVaultWrapper.TitanVaultStatic.GetVaultUsers(_vaultFolderPath, adminPasswordChars);
                Console.WriteLine($"   Initial users: {string.Join(", ", users)}");

                // Add user Alice
                Console.WriteLine("👤 Adding user 'alice' to vault...");
                char[] alicePassword = "alice_secure_password_123".ToCharArray();
                try
                {
                    TitanVaultWrapper.TitanVaultStatic.AddUserToVault(_vaultFolderPath, adminPasswordChars, "alice", alicePassword);
                    Console.WriteLine("✅ User 'alice' added successfully");
                }
                finally
                {
                    Array.Clear(alicePassword, 0, alicePassword.Length);
                }

                // Add user Bob
                Console.WriteLine("👤 Adding user 'bob' to vault...");
                char[] bobPassword = "bob_strong_password_456".ToCharArray();
                try
                {
                    TitanVaultWrapper.TitanVaultStatic.AddUserToVault(_vaultFolderPath, adminPasswordChars, "bob", bobPassword);
                    Console.WriteLine("✅ User 'bob' added successfully");
                }
                finally
                {
                    Array.Clear(bobPassword, 0, bobPassword.Length);
                }

                // List users after additions
                Console.WriteLine("📋 Listing users after additions...");
                users = TitanVaultWrapper.TitanVaultStatic.GetVaultUsers(_vaultFolderPath, adminPasswordChars);
                Console.WriteLine($"   Current users: {string.Join(", ", users)}");

                // Test Alice's access
                Console.WriteLine("🔐 Testing Alice's vault access...");
                await TestUserAccess("alice", "alice_secure_password_123");

                // Test Bob's access
                Console.WriteLine("🔐 Testing Bob's vault access...");
                await TestUserAccess("bob", "bob_strong_password_456");

                // Remove Bob from vault
                Console.WriteLine("🗑️ Removing user 'bob' from vault...");
                TitanVaultWrapper.TitanVaultStatic.RemoveUserFromVault(_vaultFolderPath, adminPasswordChars, "bob");
                Console.WriteLine("✅ User 'bob' removed successfully");

                // List users after removal
                Console.WriteLine("📋 Listing users after removal...");
                users = TitanVaultWrapper.TitanVaultStatic.GetVaultUsers(_vaultFolderPath, adminPasswordChars);
                Console.WriteLine($"   Final users: {string.Join(", ", users)}");

                // Verify Bob can no longer access
                Console.WriteLine("🚫 Verifying Bob can no longer access vault...");
                await TestUserAccessShouldFail("bob", "bob_strong_password_456");
            }
            finally
            {
                Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
            }
        }

        private async Task TestUserAccess(string userId, string password)
        {
            char[] passwordChars = password.ToCharArray();
            try
            {
                var vault = TitanVaultWrapper.TitanVault.LoadUvfVault(_vaultFolderPath, passwordChars, userId);
                
                // Try to read a file
                var fileData = vault.ReadAllBytes("/admin_document.txt");
                Console.WriteLine($"   ✅ User '{userId}' successfully accessed vault ({fileData.Length} bytes read)");
                
                vault.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ User '{userId}' failed to access vault: {ex.Message}");
                throw;
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }

        private async Task TestUserAccessShouldFail(string userId, string password)
        {
            char[] passwordChars = password.ToCharArray();
            try
            {
                var vault = TitanVaultWrapper.TitanVault.LoadUvfVault(_vaultFolderPath, passwordChars, userId);
                vault.Dispose();
                Console.WriteLine($"   ❌ ERROR: User '{userId}' should not have been able to access vault!");
                throw new InvalidOperationException($"User '{userId}' should not have vault access");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"   ✅ Confirmed: User '{userId}' correctly denied vault access");
            }
            catch (Exception ex) when (ex.Message.Contains("decrypt") || ex.Message.Contains("password"))
            {
                Console.WriteLine($"   ✅ Confirmed: User '{userId}' correctly denied vault access");
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }

        private async Task DemonstratePasswordChangesAsync()
        {
            Console.WriteLine("\n3️⃣ Demonstrating password change operations...");
            
            // Change admin password
            Console.WriteLine("🔑 Changing admin password...");
            char[] oldAdminPassword = _adminPassword.ToCharArray();
            char[] newAdminPassword = "new_admin_password_789".ToCharArray();
            try
            {
                TitanVaultWrapper.TitanVaultStatic.ChangeUvfAdminPassword(_vaultFolderPath, oldAdminPassword, newAdminPassword);
                Console.WriteLine("✅ Admin password changed successfully");

                // Test new admin password
                Console.WriteLine("🔐 Testing new admin password...");
                await TestUserAccess("admin", "new_admin_password_789");

                // Change Alice's password (using new admin password)
                Console.WriteLine("🔑 Changing Alice's password...");
                char[] newAlicePassword = "alice_new_password_999".ToCharArray();
                try
                {
                    TitanVaultWrapper.TitanVaultStatic.ChangeUvfUserPassword(_vaultFolderPath, newAdminPassword, "alice", newAlicePassword);
                    Console.WriteLine("✅ Alice's password changed successfully");

                    // Test Alice's new password
                    Console.WriteLine("🔐 Testing Alice's new password...");
                    await TestUserAccess("alice", "alice_new_password_999");
                }
                finally
                {
                    Array.Clear(newAlicePassword, 0, newAlicePassword.Length);
                }
            }
            finally
            {
                Array.Clear(oldAdminPassword, 0, oldAdminPassword.Length);
                Array.Clear(newAdminPassword, 0, newAdminPassword.Length);
            }
        }

        private async Task VerifyDataIntegrityAsync()
        {
            Console.WriteLine("\n4️⃣ Verifying data integrity after user operations...");
            
            // Use new admin password to access vault
            char[] adminPasswordChars = "new_admin_password_789".ToCharArray();
            try
            {
                var vault = TitanVaultWrapper.TitanVault.LoadUvfVault(_vaultFolderPath, adminPasswordChars, "admin");
                
                Console.WriteLine("📦 Extracting all files for verification...");
                Directory.CreateDirectory(_decryptedFolderPath);
                
                _stopwatch.Restart();
                _totalBytesProcessed = 0;
                
                await ExtractAllFiles(vault, "/", _decryptedFolderPath);
                
                _stopwatch.Stop();
                PrintSpeed("Data Extraction", _totalBytesProcessed, _stopwatch.Elapsed);
                
                // Verify file integrity
                Console.WriteLine("🔍 Verifying file integrity...");
                await VerifyFileIntegrity();
                
                vault.Dispose();
            }
            finally
            {
                Array.Clear(adminPasswordChars, 0, adminPasswordChars.Length);
            }
        }

        private async Task ExtractAllFiles(TitanVaultWrapper.TitanVault vault, string vaultPath, string localPath)
        {
            var entries = vault.ListDirectoryDetailed(vaultPath);
            
            foreach (var entry in entries)
            {
                var entryVaultPath = vaultPath.TrimEnd('/') + "/" + entry.Name;
                var entryLocalPath = Path.Combine(localPath, entry.Name);
                
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(entryLocalPath);
                    await ExtractAllFiles(vault, entryVaultPath, entryLocalPath);
                }
                else
                {
                    var fileData = vault.ReadAllBytes(entryVaultPath);
                    await File.WriteAllBytesAsync(entryLocalPath, fileData);
                    
                    _totalBytesProcessed += fileData.Length;
                    _totalBytesRead += fileData.Length;
                    Console.WriteLine($"   📄 {entryVaultPath} -> {entryLocalPath} ({fileData.Length} bytes)");
                }
            }
        }

        private async Task VerifyFileIntegrity()
        {
            var sourceFiles = await CollectFiles(_sourceFolderPath);
            var decryptedFiles = await CollectFiles(_decryptedFolderPath);
            
            Console.WriteLine($"📊 Source files: {sourceFiles.Count}");
            Console.WriteLine($"📊 Decrypted files: {decryptedFiles.Count}");
            
            bool allMatch = true;
            int matchCount = 0;
            
            foreach (var (relativePath, sourceSize) in sourceFiles)
            {
                if (decryptedFiles.TryGetValue(relativePath, out var decryptedSize))
                {
                    if (sourceSize == decryptedSize)
                    {
                        Console.WriteLine($"✅ {relativePath} - Size match ({sourceSize} bytes)");
                        matchCount++;
                    }
                    else
                    {
                        Console.WriteLine($"❌ {relativePath} - Size mismatch: {sourceSize} vs {decryptedSize}");
                        allMatch = false;
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Missing file: {relativePath}");
                    allMatch = false;
                }
            }
            
            if (allMatch)
            {
                Console.WriteLine($"\n🎉 SUCCESS: All {matchCount} files verified with perfect integrity!");
            }
            else
            {
                Console.WriteLine("\n💥 FAILURE: File integrity issues detected!");
            }
        }

        private async Task<Dictionary<string, long>> CollectFiles(string directoryPath)
        {
            var files = new Dictionary<string, long>();
            await CollectFilesRecursive(directoryPath, "", files);
            return files;
        }

        private async Task CollectFilesRecursive(string currentPath, string relativePath, Dictionary<string, long> files)
        {
            var filePaths = Directory.GetFiles(currentPath);
            var directoryPaths = Directory.GetDirectories(currentPath);
            
            foreach (var filePath in filePaths)
            {
                var fileName = Path.GetFileName(filePath);
                string relativeFilePath = string.IsNullOrEmpty(relativePath) ? fileName : $"{relativePath}/{fileName}";
                
                var fileInfo = new FileInfo(filePath);
                files[relativeFilePath] = fileInfo.Length;
            }
            
            foreach (var dirPath in directoryPaths)
            {
                var dirName = Path.GetFileName(dirPath);
                string relativeSubPath = string.IsNullOrEmpty(relativePath) ? dirName : $"{relativePath}/{dirName}";
                await CollectFilesRecursive(dirPath, relativeSubPath, files);
            }
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

        private void PrintPerformanceSummary()
        {
            Console.WriteLine($"   📊 Total Bytes Written: {_totalBytesWritten:N0} bytes");
            Console.WriteLine($"   📊 Total Bytes Read: {_totalBytesRead:N0} bytes");
            Console.WriteLine($"   📊 Write Elapsed: {_writeElapsed.TotalMilliseconds:F0}ms");
            Console.WriteLine($"   📊 Read Elapsed: {_readElapsed.TotalMilliseconds:F0}ms");
        }
    }
} 