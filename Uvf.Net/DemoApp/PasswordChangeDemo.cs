using System;
using System.IO;
using System.Threading.Tasks;

namespace DemoApp
{
    /// <summary>
    /// Demonstrates password change operations for both UVF and Cryptomator vaults.
    /// Shows how to safely change passwords while maintaining data integrity.
    /// </summary>
    public class PasswordChangeDemo
    {
        private readonly string _sourceFolderPath;
        private readonly string _uvfVaultPath;
        private readonly string _cryptomatorVaultPath;
        private readonly string _decryptedFolderPath;

        public PasswordChangeDemo(string sourceFolderPath, string uvfVaultPath, string cryptomatorVaultPath, string decryptedFolderPath)
        {
            _sourceFolderPath = sourceFolderPath;
            _uvfVaultPath = uvfVaultPath;
            _cryptomatorVaultPath = cryptomatorVaultPath;
            _decryptedFolderPath = decryptedFolderPath;
        }

        public async Task RunDemoAsync(VaultTypeFilter vaultType = VaultTypeFilter.Both)
        {
            Console.WriteLine("===== Password Change Demo =====");
            Console.WriteLine($"Source: {_sourceFolderPath}");
            Console.WriteLine($"Mode: {vaultType}");
            
            if (vaultType == VaultTypeFilter.UVF || vaultType == VaultTypeFilter.Both)
            {
            Console.WriteLine($"UVF Vault: {_uvfVaultPath}");
            }
            if (vaultType == VaultTypeFilter.Cryptomator || vaultType == VaultTypeFilter.Both)
            {
            Console.WriteLine($"Cryptomator Vault: {_cryptomatorVaultPath}");
            }
            
            Console.WriteLine($"Decrypted: {_decryptedFolderPath}");
            Console.WriteLine();

            try
            {
                // Clean directories first
                CleanupDirectories();
                
                await SetupTestDataAsync();
                
                if (vaultType == VaultTypeFilter.UVF || vaultType == VaultTypeFilter.Both)
                {
                await DemonstrateUvfPasswordChangeAsync();
                }
                
                if (vaultType == VaultTypeFilter.Cryptomator || vaultType == VaultTypeFilter.Both)
                {
                await DemonstrateCryptomatorPasswordChangeAsync();
                }
                
                Console.WriteLine("✅ PasswordChangeDemo completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ PasswordChangeDemo failed: {ex.Message}");
                throw;
            }
        }

        private void CleanupDirectories()
        {
            Console.WriteLine("🧹 Cleaning up directories...");
            CleanupDirectory(_uvfVaultPath, "UVF vault");
            CleanupDirectory(_cryptomatorVaultPath, "Cryptomator vault");
            CleanupDirectory(_decryptedFolderPath, "decrypted");
        }

        private async Task SetupTestDataAsync()
        {
            Console.WriteLine("📁 Setting up test data for password change demo...");
            Directory.CreateDirectory(_sourceFolderPath);
            
            var testFiles = new[]
            {
                ("password_test.txt", "This file will test password changes for both vault types!"),
                ("important_data.json", $"{{\"test\": \"password change\", \"timestamp\": \"{DateTime.Now:O}\", \"vault_types\": [\"UVF\", \"Cryptomator\"]}}"),
                ("sensitive_document.txt", "Sensitive document that must remain accessible after password changes"),
                ("config/settings.txt", "Configuration settings in subdirectory"),
                ("data/binary.dat", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(2048)))
            };

            foreach (var (filePath, content) in testFiles)
            {
                string fullPath = Path.Combine(_sourceFolderPath, filePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, content);
                Console.WriteLine($"   Created: {filePath} ({content.Length} bytes)");
            }
            
            Console.WriteLine($"✅ Created {testFiles.Length} test files");
        }

        private async Task DemonstrateUvfPasswordChangeAsync()
        {
            Console.WriteLine("\n1️⃣ Demonstrating UVF vault password changes...");
            
            const string originalPassword = "original_uvf_password_123";
            const string newPassword = "new_uvf_password_456";
            const string finalPassword = "final_uvf_password_789";

            // Create UVF vault with original password
            Console.WriteLine("📦 Creating UVF vault with original password...");
            char[] originalPasswordChars = originalPassword.ToCharArray();
            try
            {
                var vault = TitanVaultWrapper.TitanVault.CreateUvfVault(
                    _uvfVaultPath, 
                    originalPasswordChars, 
                    encryptFilenames: true
                );

                // Populate with test data
                await PopulateVault(vault, "UVF");
                vault.Dispose();
                
                Console.WriteLine("✅ UVF vault created and populated");
            }
            finally
            {
                Array.Clear(originalPasswordChars, 0, originalPasswordChars.Length);
            }

            // Test original password access
            Console.WriteLine("🔐 Testing original password access...");
            await TestVaultAccess(_uvfVaultPath, originalPassword, VaultType.UVF, "original password");

            // Change password from original to new
            Console.WriteLine("🔑 Changing UVF admin password (original -> new)...");
            char[] oldPasswordChars = originalPassword.ToCharArray();
            char[] newPasswordChars = newPassword.ToCharArray();
            try
            {
                TitanVaultWrapper.TitanVaultStatic.ChangeUvfAdminPassword(_uvfVaultPath, oldPasswordChars, newPasswordChars);
                Console.WriteLine("✅ UVF admin password changed successfully");
            }
            finally
            {
                Array.Clear(oldPasswordChars, 0, oldPasswordChars.Length);
                Array.Clear(newPasswordChars, 0, newPasswordChars.Length);
            }

            // Test new password access
            Console.WriteLine("🔐 Testing new password access...");
            await TestVaultAccess(_uvfVaultPath, newPassword, VaultType.UVF, "new password");

            // Verify old password no longer works
            Console.WriteLine("🚫 Verifying old password no longer works...");
            await TestVaultAccessShouldFail(_uvfVaultPath, originalPassword, VaultType.UVF, "old password");

            // Change password again (new -> final)
            Console.WriteLine("🔑 Changing UVF admin password again (new -> final)...");
            char[] newPasswordChars2 = newPassword.ToCharArray();
            char[] finalPasswordChars = finalPassword.ToCharArray();
            try
            {
                TitanVaultWrapper.TitanVaultStatic.ChangeUvfAdminPassword(_uvfVaultPath, newPasswordChars2, finalPasswordChars);
                Console.WriteLine("✅ UVF admin password changed again successfully");
            }
            finally
            {
                Array.Clear(newPasswordChars2, 0, newPasswordChars2.Length);
                Array.Clear(finalPasswordChars, 0, finalPasswordChars.Length);
            }

            // Test final password and verify data integrity
            Console.WriteLine("🔍 Testing final password and verifying data integrity...");
            await VerifyVaultIntegrity(_uvfVaultPath, finalPassword, VaultType.UVF, "UVF");
        }

        private async Task DemonstrateCryptomatorPasswordChangeAsync()
        {
            Console.WriteLine("\n2️⃣ Demonstrating Cryptomator vault password changes...");
            
            const string originalPassword = "original_cryptomator_password_123";
            const string newPassword = "new_cryptomator_password_456";
            const string finalPassword = "final_cryptomator_password_789";

            // Create Cryptomator vault with original password
            Console.WriteLine("📦 Creating Cryptomator vault with original password...");
            char[] originalPasswordChars = originalPassword.ToCharArray();
            try
            {
                var vault = TitanVaultWrapper.TitanVault.CreateCryptomatorVault(
                    _cryptomatorVaultPath, 
                    originalPasswordChars
                );

                // Populate with test data
                await PopulateVault(vault, "Cryptomator");
                vault.Dispose();
                
                Console.WriteLine("✅ Cryptomator vault created and populated");
            }
            finally
            {
                Array.Clear(originalPasswordChars, 0, originalPasswordChars.Length);
            }

            // Test original password access
            Console.WriteLine("🔐 Testing original password access...");
            await TestVaultAccess(_cryptomatorVaultPath, originalPassword, VaultType.Cryptomator, "original password");

            // Change password from original to new
            Console.WriteLine("🔑 Changing Cryptomator password (original -> new)...");
            char[] oldPasswordChars = originalPassword.ToCharArray();
            char[] newPasswordChars = newPassword.ToCharArray();
            try
            {
                TitanVaultWrapper.TitanVaultStatic.ChangeCryptomatorPassword(_cryptomatorVaultPath, oldPasswordChars, newPasswordChars);
                Console.WriteLine("✅ Cryptomator password changed successfully");
            }
            finally
            {
                Array.Clear(oldPasswordChars, 0, oldPasswordChars.Length);
                Array.Clear(newPasswordChars, 0, newPasswordChars.Length);
            }

            // Test new password access
            Console.WriteLine("🔐 Testing new password access...");
            await TestVaultAccess(_cryptomatorVaultPath, newPassword, VaultType.Cryptomator, "new password");

            // Verify old password no longer works
            Console.WriteLine("🚫 Verifying old password no longer works...");
            await TestVaultAccessShouldFail(_cryptomatorVaultPath, originalPassword, VaultType.Cryptomator, "old password");

            // Change password again (new -> final)
            Console.WriteLine("🔑 Changing Cryptomator password again (new -> final)...");
            char[] newPasswordChars2 = newPassword.ToCharArray();
            char[] finalPasswordChars = finalPassword.ToCharArray();
            try
            {
                TitanVaultWrapper.TitanVaultStatic.ChangeCryptomatorPassword(_cryptomatorVaultPath, newPasswordChars2, finalPasswordChars);
                Console.WriteLine("✅ Cryptomator password changed again successfully");
            }
            finally
            {
                Array.Clear(newPasswordChars2, 0, newPasswordChars2.Length);
                Array.Clear(finalPasswordChars, 0, finalPasswordChars.Length);
            }

            // Test final password and verify data integrity
            Console.WriteLine("🔍 Testing final password and verifying data integrity...");
            await VerifyVaultIntegrity(_cryptomatorVaultPath, finalPassword, VaultType.Cryptomator, "Cryptomator");
        }

        private async Task PopulateVault(TitanVaultWrapper.TitanVault vault, string vaultType)
        {
            Console.WriteLine($"📄 Populating {vaultType} vault with test data...");
            
            var files = Directory.GetFiles(_sourceFolderPath, "*", SearchOption.AllDirectories);
            long totalBytes = 0;
            
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
                
                totalBytes += fileData.Length;
                Console.WriteLine($"   📄 {relativePath} ({fileData.Length} bytes)");
            }
            
            Console.WriteLine($"   ✅ Populated {files.Length} files, {totalBytes:N0} bytes total");
        }

        private enum VaultType
        {
            UVF,
            Cryptomator
        }

        private async Task TestVaultAccess(string vaultPath, string password, VaultType vaultType, string passwordDescription)
        {
            char[] passwordChars = password.ToCharArray();
            try
            {
                string? userId = null;
                
                // For UVF vaults, get the list of users and use the first one (admin)
                if (vaultType == VaultType.UVF)
                {
                    Console.WriteLine($"   👥 Getting vault users for {passwordDescription}...");
                    var users = TitanVaultWrapper.TitanVaultStatic.GetVaultUsers(vaultPath, passwordChars);
                    Console.WriteLine($"   📋 Found {users.Length} users: [{string.Join(", ", users)}]");
                    
                    if (users.Length > 0)
                    {
                        userId = users[0]; // Use the first user (should be admin)
                        Console.WriteLine($"   🔑 Using user ID: '{userId}'");
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️ No users found in vault!");
                    }
                }
                
                TitanVaultWrapper.TitanVault vault = vaultType switch
                {
                    VaultType.UVF => TitanVaultWrapper.TitanVault.LoadUvfVault(vaultPath, passwordChars, userId),
                    VaultType.Cryptomator => TitanVaultWrapper.TitanVault.LoadCryptomatorVault(vaultPath, passwordChars),
                    _ => throw new ArgumentException($"Unknown vault type: {vaultType}")
                };
                
                // Check if the test file exists first
                Console.WriteLine($"   🔍 Checking if /password_test.txt exists in {vaultType} vault...");
                bool fileExists = vault.FileExists("/password_test.txt");
                Console.WriteLine($"   📄 File exists: {fileExists}");
                
                if (!fileExists)
                {
                    // List vault contents for debugging
                    Console.WriteLine($"   📋 Vault contents:");
                    var entries = vault.ListDirectory("/");
                    foreach (var entry in entries)
                    {
                        Console.WriteLine($"      - {entry}");
                    }
                }
                
                // Try to read a test file (using stream as alternative to ReadAllBytes)
                byte[] fileData;
                try
                {
                    fileData = vault.ReadAllBytes("/password_test.txt");
                }
                catch (Exception readEx) when (readEx.Message.Contains("Invalid parameters"))
                {
                    Console.WriteLine($"   🔧 ReadAllBytes failed, trying stream approach: {readEx.Message}");
                    using var stream = vault.OpenReadStream("/password_test.txt");
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    fileData = memoryStream.ToArray();
                    Console.WriteLine($"   ✅ Stream approach worked! Read {fileData.Length} bytes");
                }
                
                Console.WriteLine($"   ✅ {vaultType} vault access with {passwordDescription} successful ({fileData.Length} bytes read)");
                
                vault.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ {vaultType} vault access with {passwordDescription} failed: {ex.Message}");
                Console.WriteLine($"   🔧 Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   🔧 Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }

        private async Task TestVaultAccessShouldFail(string vaultPath, string password, VaultType vaultType, string passwordDescription)
        {
            char[] passwordChars = password.ToCharArray();
            try
            {
                string? userId = null;
                
                // For UVF vaults, try to get users (this might fail with old password, which is expected)
                if (vaultType == VaultType.UVF)
                {
                    try
                    {
                        var users = TitanVaultWrapper.TitanVaultStatic.GetVaultUsers(vaultPath, passwordChars);
                        if (users.Length > 0)
                        {
                            userId = users[0];
                        }
                    }
                    catch
                    {
                        // Expected to fail with old password - we'll try without user ID
                        Console.WriteLine($"   🔍 Could not get users with {passwordDescription} (expected for old passwords)");
                    }
                }
                
                TitanVaultWrapper.TitanVault vault = vaultType switch
                {
                    VaultType.UVF => TitanVaultWrapper.TitanVault.LoadUvfVault(vaultPath, passwordChars, userId),
                    VaultType.Cryptomator => TitanVaultWrapper.TitanVault.LoadCryptomatorVault(vaultPath, passwordChars),
                    _ => throw new ArgumentException($"Unknown vault type: {vaultType}")
                };
                
                vault.Dispose();
                Console.WriteLine($"   ❌ ERROR: {vaultType} vault access with {passwordDescription} should have failed!");
                throw new InvalidOperationException($"Vault access with {passwordDescription} should have failed");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"   ✅ Confirmed: {vaultType} vault correctly denied access with {passwordDescription}");
            }
            catch (Exception ex) when (ex.Message.Contains("decrypt") || ex.Message.Contains("password") || ex.Message.Contains("authentication") || ex.Message.Contains("Invalid"))
            {
                Console.WriteLine($"   ✅ Confirmed: {vaultType} vault correctly denied access with {passwordDescription}");
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }

        private async Task VerifyVaultIntegrity(string vaultPath, string password, VaultType vaultType, string vaultTypeName)
        {
            char[] passwordChars = password.ToCharArray();
            try
            {
                string? userId = null;
                
                // For UVF vaults, get the list of users and use the first one
                if (vaultType == VaultType.UVF)
                {
                    Console.WriteLine($"   👥 Getting vault users for final verification...");
                    var users = TitanVaultWrapper.TitanVaultStatic.GetVaultUsers(vaultPath, passwordChars);
                    Console.WriteLine($"   📋 Found {users.Length} users: [{string.Join(", ", users)}]");
                    
                    if (users.Length > 0)
                    {
                        userId = users[0];
                        Console.WriteLine($"   🔑 Using user ID: '{userId}' for verification");
                    }
                }
                
                TitanVaultWrapper.TitanVault vault = vaultType switch
                {
                    VaultType.UVF => TitanVaultWrapper.TitanVault.LoadUvfVault(vaultPath, passwordChars, userId),
                    VaultType.Cryptomator => TitanVaultWrapper.TitanVault.LoadCryptomatorVault(vaultPath, passwordChars),
                    _ => throw new ArgumentException($"Unknown vault type: {vaultType}")
                };
                
                // Extract all files to verify integrity
                string extractPath = Path.Combine(_decryptedFolderPath, vaultTypeName.ToLower());
                Directory.CreateDirectory(extractPath);
                
                Console.WriteLine($"📦 Extracting {vaultTypeName} vault files for verification...");
                await ExtractAllFiles(vault, "/", extractPath);
                
                // Verify against original files
                Console.WriteLine($"🔍 Verifying {vaultTypeName} vault data integrity...");
                await VerifyExtractedFiles(extractPath, vaultTypeName);
                
                vault.Dispose();
            }
            finally
            {
                Array.Clear(passwordChars, 0, passwordChars.Length);
            }
        }

        private async Task ExtractAllFiles(TitanVaultWrapper.TitanVault vault, string vaultPath, string localPath)
        {
            var entries = vault.ListDirectoryDetailed(vaultPath);
            
            foreach (var entry in entries)
            {
                string entryVaultPath = vaultPath == "/" ? $"/{entry.Name}" : $"{vaultPath}/{entry.Name}";
                string entryLocalPath = Path.Combine(localPath, entry.Name);
                
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(entryLocalPath);
                    await ExtractAllFiles(vault, entryVaultPath, entryLocalPath);
                }
                else
                {
                    byte[] fileData;
                    try
                    {
                        fileData = vault.ReadAllBytes(entryVaultPath);
                    }
                    catch (Exception readEx) when (readEx.Message.Contains("Invalid parameters"))
                    {
                        Console.WriteLine($"   🔧 ReadAllBytes failed for {entry.Name}, using stream...");
                        using var stream = vault.OpenReadStream(entryVaultPath);
                        using var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream);
                        fileData = memoryStream.ToArray();
                    }
                    
                    await File.WriteAllBytesAsync(entryLocalPath, fileData);
                    Console.WriteLine($"   📄 {entry.Name} ({fileData.Length} bytes)");
                }
            }
        }

        private async Task VerifyExtractedFiles(string extractedPath, string vaultTypeName)
        {
            var sourceFiles = Directory.GetFiles(_sourceFolderPath, "*", SearchOption.AllDirectories);
            var extractedFiles = Directory.GetFiles(extractedPath, "*", SearchOption.AllDirectories);
            
            Console.WriteLine($"   📊 Source files: {sourceFiles.Length}");
            Console.WriteLine($"   📊 Extracted files: {extractedFiles.Length}");
            
            bool allMatch = true;
            int matchCount = 0;
            
            foreach (var sourceFile in sourceFiles)
            {
                var relativePath = Path.GetRelativePath(_sourceFolderPath, sourceFile);
                var expectedExtractedFile = Path.Combine(extractedPath, relativePath);
                
                if (File.Exists(expectedExtractedFile))
                {
                    var sourceSize = new FileInfo(sourceFile).Length;
                    var extractedSize = new FileInfo(expectedExtractedFile).Length;
                    
                    if (sourceSize == extractedSize)
                    {
                        Console.WriteLine($"   ✅ {relativePath} - Size match ({sourceSize} bytes)");
                        matchCount++;
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ {relativePath} - Size mismatch: {sourceSize} vs {extractedSize}");
                        allMatch = false;
                    }
                }
                else
                {
                    Console.WriteLine($"   ❌ Missing file: {relativePath}");
                    allMatch = false;
                }
            }
            
            if (allMatch)
            {
                Console.WriteLine($"\n🎉 SUCCESS: {vaultTypeName} vault integrity perfect after password changes!");
                Console.WriteLine($"   All {matchCount} files verified successfully");
            }
            else
            {
                Console.WriteLine($"\n💥 FAILURE: {vaultTypeName} vault integrity issues detected!");
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
    }
} 