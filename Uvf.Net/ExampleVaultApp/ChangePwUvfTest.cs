using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UvfLib.Master;
using UvfLib.Vault;
using StorageLib.Abstractions;
using ExampleVaultApp.Wrapper;

namespace ExampleVaultApp
{
    /// <summary>
    /// Test class for demonstrating UVF vault password change functionality.
    /// This class helps understand how password changes work and tests the implementation.
    /// </summary>
    public class ChangePwUvfTest
    {
        private string _testVaultPath;
        private readonly char[] _originalPassword = "uvf-test123".ToCharArray();
        private readonly char[] _newPassword = "uvf-newtest456".ToCharArray();
        private readonly bool _encryptFilenames;

        public ChangePwUvfTest(bool encryptFilenames)
        {
            _testVaultPath = Path.Combine(Path.GetTempPath(), "ChangePwUvfTest_" + Guid.NewGuid().ToString("N")[..8]);
            _encryptFilenames = encryptFilenames;
        }

        /// <summary>
        /// Main test method that demonstrates the complete password change process
        /// </summary>
        public async Task RunPasswordChangeTestAsync()
        {
            try
            {
                Console.WriteLine("=== UVF Password Change Test ===");
                _testVaultPath = Path.Combine(Path.GetTempPath(), $"ChangePwUvfTest_{Path.GetRandomFileName().Substring(0, 8)}");
                Console.WriteLine($"Test vault path: {_testVaultPath}");

                // Step 1: Create a new vault with original password
                Console.WriteLine("\n1. Creating UVF vault with original password...");
                await CreateTestVaultAsync();
                
                // Step 2: Verify we can open with original password
                Console.WriteLine("\n2. Verifying vault opens with original password...");
                await VerifyVaultAccessAsync(_originalPassword, "original");
                
                // Step 3: Change password
                Console.WriteLine("\n3. Changing vault password...");
                await ChangeVaultPasswordAsync();

                // Step 4: Verify old password no longer works
                Console.WriteLine("\n4. Verifying old password no longer works...");
                await VerifyPasswordRejectionAsync(_originalPassword);

                // Step 5: Verify new password works
                Console.WriteLine("\n5. Verifying vault opens with new password...");
                await VerifyVaultAccessAsync(_newPassword, "new");
                
                Console.WriteLine("\n✅ UVF password change test completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ UVF password change test failed: {ex.Message}");
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                Console.WriteLine($"\n🚮 Cleaned up test vault: {_testVaultPath}");
                await CleanupAsync();
            }
        }

        /// <summary>
        /// Creates a test vault with the original password
        /// </summary>
        private async Task CreateTestVaultAsync()
        {
            // Ensure directory exists
            Directory.CreateDirectory(_testVaultPath);

            // Create vault using VaultManager
            using var vault = await VaultManager.CreateUvfVaultAsync(_testVaultPath, _originalPassword, _encryptFilenames);
            
            // Write a test file to verify the vault works
            await vault.WriteAllBytesAsync("test.txt", Encoding.UTF8.GetBytes("Hello, UVF World!"));
            
            Console.WriteLine($"✅ UVF vault created successfully at: {_testVaultPath}");
            
            // Display the created files
            var files = Directory.GetFiles(_testVaultPath);
            Console.WriteLine($"Created files: {string.Join(", ", files.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Verifies that the vault can be opened with the given password
        /// </summary>
        private async Task VerifyVaultAccessAsync(char[] passwordChars, string passwordType)
        {
            Console.WriteLine($"🔍 DEBUG: Verifying vault access with {passwordType} password: '{new string(passwordChars)}'");
            Console.WriteLine($"🔍 DEBUG: Password array length: {passwordChars.Length}");
            Console.WriteLine($"🔍 DEBUG: Vault path: {_testVaultPath}");
            
            try
            {
                // Use auto-detection when loading vault for verification
                Console.WriteLine($"🔍 DEBUG: About to call VaultManager.LoadUvfVaultAsync()...");
                using var vault = await VaultManager.LoadUvfVaultAsync(_testVaultPath, passwordChars);
                Console.WriteLine($"🔍 DEBUG: VaultManager.LoadUvfVaultAsync() succeeded!");
                
                // Try to detect and display the actual setting
                try
                {
                    string vaultUvfFile = Path.Combine(_testVaultPath, "vault.uvf");
                    byte[] vaultFileContent = await File.ReadAllBytesAsync(vaultUvfFile);
                    // Convert char[] to byte[] for VaultHandler
                    byte[] passwordBytes = Encoding.UTF8.GetBytes(passwordChars);
                    bool detectedEncryptFilenames = VaultHandler.DetectFilenameEncryption(vaultFileContent, passwordBytes);
                    Console.WriteLine($"🔍 Auto-detected filename encryption during {passwordType} password verification: {(detectedEncryptFilenames ? "Enabled" : "Disabled")}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Could not detect filename encryption setting: {ex.Message}");
                }
                
                Console.WriteLine($"🔍 DEBUG: About to read test file 'test.txt'...");
                // Try to read the test file
                var data = await vault.ReadAllBytesAsync("test.txt");
                var content = Encoding.UTF8.GetString(data);
                Console.WriteLine($"🔍 DEBUG: Read {data.Length} bytes from test.txt");
                
                if (content == "Hello, UVF World!")
                {
                    Console.WriteLine($"✅ UVF vault successfully opened with {passwordType} password");
                }
                else
                {
                    throw new Exception($"UVF vault opened but content mismatch. Expected 'Hello, UVF World!', got '{content}'");
                }
            }
            catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("AES Key Wrap integrity check failed"))
            {
                Console.WriteLine($"❌ CRITICAL: AES Key Wrap Error during vault access with {passwordType} password!");
                Console.WriteLine($"🔍 DEBUG: This should NOT happen with the correct password!");
                Console.WriteLine($"🔍 DEBUG: Exception: {ex.Message}");
                Console.WriteLine($"🔍 DEBUG: Stack trace: {ex.StackTrace}");
                Console.WriteLine($"🔍 DEBUG: Password used: '{new string(passwordChars)}'");
                Console.WriteLine($"🔍 DEBUG: Password array length: {passwordChars.Length}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to verify vault access with {passwordType} password: {ex.Message}");
                Console.WriteLine($"🔍 DEBUG: Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"🔍 DEBUG: Inner exception: {ex.InnerException.Message}");
                    Console.WriteLine($"🔍 DEBUG: Inner exception type: {ex.InnerException.GetType().Name}");
                }
                throw;
            }
        }

        /// <summary>
        /// Changes the vault password using the VaultManager API
        /// </summary>
        private async Task ChangeVaultPasswordAsync()
        {
            Console.WriteLine("Using VaultManager.ChangeUvfAdminPasswordAsync() static method...");
            
            try
            {
                // Debug: Show password state before change
                Console.WriteLine($"🔍 DEBUG: Original password: '{new string(_originalPassword)}'");
                Console.WriteLine($"🔍 DEBUG: New password: '{new string(_newPassword)}'");
                Console.WriteLine($"🔍 DEBUG: Original password array length: {_originalPassword.Length}");
                Console.WriteLine($"🔍 DEBUG: New password array length: {_newPassword.Length}");
                
                // First, let's analyze the vault file before attempting to change the password
                string vaultFilePath = Path.Combine(_testVaultPath, "vault.uvf");
                if (File.Exists(vaultFilePath))
                {
                    byte[] vaultContent = await File.ReadAllBytesAsync(vaultFilePath);
                    Console.WriteLine($"UVF vault file size: {vaultContent.Length} bytes");
                    await AnalyzeUvfVaultFileAsync(vaultContent, "Before Password Change");
                }
                
                // Create separate copies to avoid modifying the original arrays
                char[] oldPasswordChars = new char[_originalPassword.Length];
                char[] newPasswordChars = new char[_newPassword.Length];
                Array.Copy(_originalPassword, oldPasswordChars, _originalPassword.Length);
                Array.Copy(_newPassword, newPasswordChars, _newPassword.Length);
                
                Console.WriteLine($"🔍 DEBUG: About to call ChangeUvfAdminPasswordAsync with old='{new string(oldPasswordChars)}' new='{new string(newPasswordChars)}'");
                
                try
                {
                    // Use the static method to change the password
                    await VaultManager.ChangeUvfAdminPasswordAsync(_testVaultPath, oldPasswordChars, newPasswordChars);
                    Console.WriteLine("✅ Password changed successfully using VaultManager static API");
                }
                catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("AES Key Wrap integrity check failed"))
                {
                    Console.WriteLine($"❌ CAUGHT AES Key Wrap Error during password change: {ex.Message}");
                    Console.WriteLine($"🔍 Stack Trace: {ex.StackTrace}");
                    Console.WriteLine($"🔍 Old password at error time: '{new string(oldPasswordChars)}'");
                    Console.WriteLine($"🔍 New password at error time: '{new string(newPasswordChars)}'");
                    Console.WriteLine($"🔍 DEBUG: This suggests the old password is incorrect or the vault structure has changed");
                    throw; // Re-throw to see full error
                }
                finally
                {
                    // Clear password arrays for security
                    Array.Clear(oldPasswordChars, 0, oldPasswordChars.Length);
                    Array.Clear(newPasswordChars, 0, newPasswordChars.Length);
                }
                
                // Debug: Verify original arrays are still intact
                Console.WriteLine($"🔍 DEBUG: After password change - Original password still: '{new string(_originalPassword)}'");
                Console.WriteLine($"🔍 DEBUG: After password change - New password still: '{new string(_newPassword)}'");
                
                // Analyze the vault file after the change
                if (File.Exists(vaultFilePath))
                {
                    byte[] vaultContent = await File.ReadAllBytesAsync(vaultFilePath);
                    Console.WriteLine($"UVF vault file size after change: {vaultContent.Length} bytes");
                    await AnalyzeUvfVaultFileAsync(vaultContent, "After Password Change");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Password change failed: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    Console.WriteLine($"Inner exception type: {ex.InnerException.GetType().Name}");
                }
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Verifies that the given password is rejected
        /// </summary>
        private async Task VerifyPasswordRejectionAsync(char[] passwordChars)
        {
            Console.WriteLine($"🔍 DEBUG: Testing password rejection with password: '{new string(passwordChars)}'");
            Console.WriteLine($"🔍 DEBUG: Password array length: {passwordChars.Length}");
            
            try
            {
                Console.WriteLine($"🔍 DEBUG: About to call VaultManager.LoadUvfVaultAsync() expecting it to fail...");
                // Use auto-detection when testing password rejection
                using var vault = await VaultManager.LoadUvfVaultAsync(_testVaultPath, passwordChars);
                Console.WriteLine($"❌ UNEXPECTED: Password was NOT rejected - vault opened successfully!");
                throw new Exception("Expected password to be rejected, but UVF vault opened successfully");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"✅ Password correctly rejected (UnauthorizedAccessException): {ex.Message}");
            }
            catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("AES Key Wrap integrity check failed"))
            {
                Console.WriteLine($"✅ Password correctly rejected (AES Key Wrap Error): {ex.Message}");
                Console.WriteLine($"🔍 DEBUG: This is the expected behavior - wrong password causes AES Key Wrap failure");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔍 DEBUG: Exception type: {ex.GetType().Name}");
                Console.WriteLine($"🔍 DEBUG: Exception message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"🔍 DEBUG: Inner exception: {ex.InnerException.Message}");
                    Console.WriteLine($"🔍 DEBUG: Inner exception type: {ex.InnerException.GetType().Name}");
                }
                // Any exception during password verification should be treated as rejection (which is good)
                Console.WriteLine($"✅ Password correctly rejected (Exception): {ex.Message}");
            }
        }

        /// <summary>
        /// Analyzes and displays the structure of a UVF vault file
        /// </summary>
        private async Task AnalyzeUvfVaultFileAsync(byte[] vaultContent, string label)
        {
            try
            {
                Console.WriteLine($"\n--- {label} UVF Vault File Analysis ---");
                
                // UVF files are binary, so we can't parse them as JSON like Cryptomator
                // Instead, we'll analyze the basic structure
                Console.WriteLine($"File size: {vaultContent.Length} bytes");
                
                // Check for UVF magic bytes at the beginning
                if (vaultContent.Length >= 4)
                {
                    var magicBytes = vaultContent.Take(4).ToArray();
                    string magicString = string.Join(" ", magicBytes.Select(b => $"0x{b:X2}"));
                    Console.WriteLine($"Magic bytes: {magicString}");
                    
                    // Check if it matches expected UVF magic bytes (u, v, f, 0x00)
                    if (magicBytes[0] == (byte)'u' && magicBytes[1] == (byte)'v' && 
                        magicBytes[2] == (byte)'f' && magicBytes[3] == 0x00)
                    {
                        Console.WriteLine("✅ Valid UVF magic bytes detected");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ UVF magic bytes not found or invalid");
                    }
                }
                
                // Show first few bytes for analysis
                if (vaultContent.Length > 0)
                {
                    int previewLength = Math.Min(32, vaultContent.Length);
                    string hexPreview = string.Join(" ", vaultContent.Take(previewLength).Select(b => $"{b:X2}"));
                    Console.WriteLine($"First {previewLength} bytes (hex): {hexPreview}");
                }
                
                Console.WriteLine($"--- End {label} Analysis ---\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Could not analyze {label.ToLower()} UVF vault file: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up test files
        /// </summary>
        private async Task CleanupAsync()
        {
            try
            {
                if (Directory.Exists(_testVaultPath))
                {
                    Directory.Delete(_testVaultPath, true);
                    Console.WriteLine($"✅ Cleaned up test vault: {_testVaultPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Warning: Could not clean up test vault: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs multiple test scenarios to try to reproduce the AES Key Wrap error
        /// </summary>
        public async Task RunMultiplePasswordChangeScenarios()
        {
            Console.WriteLine("🧪 Running Multiple Password Change Scenarios to Reproduce AES Key Wrap Error...");
            
            for (int scenario = 1; scenario <= 5; scenario++)
            {
                Console.WriteLine($"\n=== Scenario {scenario} ===");
                try
                {
                    await RunSingleScenario(scenario);
                    Console.WriteLine($"✅ Scenario {scenario} completed successfully");
                }
                catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("AES Key Wrap integrity check failed"))
                {
                    Console.WriteLine($"🎯 FOUND IT! AES Key Wrap Error in Scenario {scenario}");
                    Console.WriteLine($"❌ Error: {ex.Message}");
                    Console.WriteLine($"🔍 Stack Trace: {ex.StackTrace}");
                    throw; // Re-throw to stop execution
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Scenario {scenario} failed with different error: {ex.Message}");
                }
            }
            
            Console.WriteLine("\n🤔 No AES Key Wrap errors found in any scenario");
        }

        private async Task RunSingleScenario(int scenarioNumber)
        {
            // Create unique test path for this scenario
            string scenarioPath = Path.Combine(Path.GetTempPath(), $"AESKeyWrapTest_Scenario{scenarioNumber}_{Path.GetRandomFileName()}");
            
            try
            {
                switch (scenarioNumber)
                {
                    case 1:
                        await TestScenario1_BasicPasswordChange(scenarioPath);
                        break;
                    case 2:
                        await TestScenario2_QuickSuccessiveChanges(scenarioPath);
                        break;
                    case 3:
                        await TestScenario3_WrongPasswordFirst(scenarioPath);
                        break;
                    case 4:
                        await TestScenario4_EmptyPasswords(scenarioPath);
                        break;
                    case 5:
                        await TestScenario5_SpecialCharacters(scenarioPath);
                        break;
                }
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(scenarioPath))
                {
                    Directory.Delete(scenarioPath, true);
                }
            }
        }

        private async Task TestScenario1_BasicPasswordChange(string testPath)
        {
            Console.WriteLine("Testing basic password change...");
            var vault = await VaultManager.CreateUvfVaultAsync(testPath, "original123".ToCharArray(), true);
            await VaultManager.ChangeUvfAdminPasswordAsync(testPath, "original123".ToCharArray(), "changed456".ToCharArray());
            
            // Verify new password works
            using var verifyVault = await VaultManager.LoadUvfVaultAsync(testPath, "changed456".ToCharArray());
        }

        private async Task TestScenario2_QuickSuccessiveChanges(string testPath)
        {
            Console.WriteLine("Testing quick successive password changes...");
            var vault = await VaultManager.CreateUvfVaultAsync(testPath, "pass1".ToCharArray(), true);
            
            // Rapid password changes
            await VaultManager.ChangeUvfAdminPasswordAsync(testPath, "pass1".ToCharArray(), "pass2".ToCharArray());
            await VaultManager.ChangeUvfAdminPasswordAsync(testPath, "pass2".ToCharArray(), "pass3".ToCharArray());
            await VaultManager.ChangeUvfAdminPasswordAsync(testPath, "pass3".ToCharArray(), "pass4".ToCharArray());
            
            // Verify final password works
            using var verifyVault = await VaultManager.LoadUvfVaultAsync(testPath, "pass4".ToCharArray());
        }

        private async Task TestScenario3_WrongPasswordFirst(string testPath)
        {
            Console.WriteLine("Testing password change with wrong old password first...");
            var vault = await VaultManager.CreateUvfVaultAsync(testPath, "correct123".ToCharArray(), true);
            
            // Try with wrong password first (this should fail)
            try
            {
                await VaultManager.ChangeUvfAdminPasswordAsync(testPath, "wrong123".ToCharArray(), "new123".ToCharArray());
                throw new Exception("Expected password change to fail with wrong old password");
            }
            catch (UnauthorizedAccessException)
            {
                // Expected - wrong password should be rejected
            }
            
            // Now try with correct password
            await VaultManager.ChangeUvfAdminPasswordAsync(testPath, "correct123".ToCharArray(), "new123".ToCharArray());
            
            // Verify new password works
            using var verifyVault = await VaultManager.LoadUvfVaultAsync(testPath, "new123".ToCharArray());
        }

        private async Task TestScenario4_EmptyPasswords(string testPath)
        {
            Console.WriteLine("Testing with empty/minimal passwords...");
            var vault = await VaultManager.CreateUvfVaultAsync(testPath, "a".ToCharArray(), true);
            await VaultManager.ChangeUvfAdminPasswordAsync(testPath, "a".ToCharArray(), "b".ToCharArray());
            
            // Verify new password works
            using var verifyVault = await VaultManager.LoadUvfVaultAsync(testPath, "b".ToCharArray());
        }

        private async Task TestScenario5_SpecialCharacters(string testPath)
        {
            Console.WriteLine("Testing with special characters in passwords...");
            var vault = await VaultManager.CreateUvfVaultAsync(testPath, "päss@wörd#1".ToCharArray(), true);
            await VaultManager.ChangeUvfAdminPasswordAsync(testPath, "päss@wörd#1".ToCharArray(), "ñéw_påsś@2".ToCharArray());
            
            // Verify new password works
            using var verifyVault = await VaultManager.LoadUvfVaultAsync(testPath, "ñéw_påsś@2".ToCharArray());
        }

        /// <summary>
        /// Tests key unwrapping scenarios that might trigger the AES Key Wrap error
        /// </summary>
        public async Task TestKeyUnwrappingScenarios()
        {
            Console.WriteLine("🔐 Testing Key Unwrapping Scenarios to Trigger AES Key Wrap Error...");
            
            string testPath = Path.Combine(Path.GetTempPath(), $"KeyUnwrapTest_{Path.GetRandomFileName()}");
            
            try
            {
                // Create a vault first
                Console.WriteLine("📦 Creating vault for key unwrapping tests...");
                var vault = await VaultManager.CreateUvfVaultAsync(testPath, "test123".ToCharArray(), true);
                vault.Dispose();
                
                // Test 1: Try to open with slightly wrong password (might trigger key unwrap error)
                Console.WriteLine("\n🧪 Test 1: Opening with slightly wrong password...");
                await TestWrongPasswordScenario(testPath, "test123", "test124");
                
                // Test 2: Try to open with completely wrong password
                Console.WriteLine("\n🧪 Test 2: Opening with completely wrong password...");
                await TestWrongPasswordScenario(testPath, "test123", "wrongpassword");
                
                // Test 3: Try to open with empty password
                Console.WriteLine("\n🧪 Test 3: Opening with empty password...");
                await TestWrongPasswordScenario(testPath, "test123", "");
                
                // Test 4: Try to open with null characters
                Console.WriteLine("\n🧪 Test 4: Opening with null characters in password...");
                await TestWrongPasswordScenario(testPath, "test123", "test\0123");
                
                // Test 5: Try concurrent access
                Console.WriteLine("\n🧪 Test 5: Concurrent vault access...");
                await TestConcurrentAccess(testPath, "test123");
                
                // Test 6: Corrupt the vault file slightly and try to open
                Console.WriteLine("\n🧪 Test 6: Opening slightly corrupted vault...");
                await TestCorruptedVault(testPath, "test123");
                
                Console.WriteLine("\n✅ All key unwrapping tests completed");
            }
            finally
            {
                if (Directory.Exists(testPath))
                {
                    Directory.Delete(testPath, true);
                }
            }
        }

        private async Task TestWrongPasswordScenario(string vaultPath, string correctPassword, string wrongPassword)
        {
            try
            {
                using var vault = await VaultManager.LoadUvfVaultAsync(vaultPath, wrongPassword.ToCharArray());
                Console.WriteLine($"⚠️ UNEXPECTED: Wrong password '{wrongPassword}' was accepted!");
            }
            catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("AES Key Wrap integrity check failed"))
            {
                Console.WriteLine($"🎯 FOUND IT! AES Key Wrap Error with password '{wrongPassword}': {ex.Message}");
                Console.WriteLine($"🔍 Stack Trace: {ex.StackTrace}");
                throw; // Re-throw to stop execution and show the error
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✅ Wrong password '{wrongPassword}' correctly rejected: {ex.GetType().Name} - {ex.Message}");
            }
        }

        private async Task TestConcurrentAccess(string vaultPath, string password)
        {
            try
            {
                var tasks = new List<Task>();
                
                // Try to open the same vault multiple times concurrently
                for (int i = 0; i < 5; i++)
                {
                    int taskId = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            using var vault = await VaultManager.LoadUvfVaultAsync(vaultPath, password.ToCharArray());
                            Console.WriteLine($"   Task {taskId}: Vault opened successfully");
                            await Task.Delay(100); // Hold it open briefly
                        }
                        catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("AES Key Wrap integrity check failed"))
                        {
                            Console.WriteLine($"🎯 FOUND IT! AES Key Wrap Error in concurrent task {taskId}: {ex.Message}");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"   Task {taskId}: Error - {ex.GetType().Name}: {ex.Message}");
                        }
                    }));
                }
                
                await Task.WhenAll(tasks);
                Console.WriteLine("✅ Concurrent access test completed");
            }
            catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("AES Key Wrap integrity check failed"))
            {
                Console.WriteLine($"🎯 FOUND IT! AES Key Wrap Error during concurrent access: {ex.Message}");
                throw;
            }
        }

        private async Task TestCorruptedVault(string vaultPath, string password)
        {
            string vaultFile = Path.Combine(vaultPath, "vault.uvf");
            string backupFile = vaultFile + ".backup";
            
            try
            {
                // Backup the original vault file
                File.Copy(vaultFile, backupFile);
                
                // Read the vault file
                byte[] vaultData = await File.ReadAllBytesAsync(vaultFile);
                Console.WriteLine($"   Original vault file size: {vaultData.Length} bytes");
                
                // Corrupt a few bytes in the middle (not the header)
                if (vaultData.Length > 100)
                {
                    vaultData[50] ^= 0xFF; // Flip all bits in one byte
                    vaultData[51] ^= 0xAA; // Flip some bits in another byte
                    
                    await File.WriteAllBytesAsync(vaultFile, vaultData);
                    Console.WriteLine("   Corrupted 2 bytes in vault file");
                    
                    // Try to open the corrupted vault
                    using var vault = await VaultManager.LoadUvfVaultAsync(vaultPath, password.ToCharArray());
                    Console.WriteLine("⚠️ UNEXPECTED: Corrupted vault was opened successfully!");
                }
            }
            catch (System.Security.Cryptography.CryptographicException ex) when (ex.Message.Contains("AES Key Wrap integrity check failed"))
            {
                Console.WriteLine($"🎯 FOUND IT! AES Key Wrap Error with corrupted vault: {ex.Message}");
                Console.WriteLine($"🔍 Stack Trace: {ex.StackTrace}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✅ Corrupted vault correctly rejected: {ex.GetType().Name} - {ex.Message}");
            }
            finally
            {
                // Restore the original vault file
                if (File.Exists(backupFile))
                {
                    File.Copy(backupFile, vaultFile, true);
                    File.Delete(backupFile);
                }
            }
        }

        /// <summary>
        /// Native-style password change test using TitanVaultNativeMethods directly
        /// This reproduces the exact same ReadAllBytes issue as in PasswordChangeDemo.cs
        /// </summary>
        public async Task RunNativeStylePasswordChangeTestAsync()
        {
            Console.WriteLine("🔧 === Native-Style Password Change Test ===");
            Console.WriteLine("🔧 This test uses TitanVaultNativeMethods directly to reproduce ReadAllBytes issues");
            
            string testPath = Path.Combine(Path.GetTempPath(), $"NativeStyleTest_{Path.GetRandomFileName()}");
            
            try
            {
                Console.WriteLine($"🔧 Test vault path: {testPath}");
                
                // Step 1: Create vault using native methods
                Console.WriteLine("\n1️⃣ Creating UVF vault using native methods...");
                await CreateVaultNativeStyle(testPath, "original123", true);
                
                // Step 2: Test reading files using native ReadFile method (this should reproduce the -1 error)
                Console.WriteLine("\n2️⃣ Testing native ReadFile method (may reproduce -1 error)...");
                await TestNativeReadFile(testPath, "original123");
                
                // Step 3: Change password using native methods
                Console.WriteLine("\n3️⃣ Changing password using native methods...");
                await ChangePasswordNativeStyle(testPath, "original123", "newpass456");
                
                // Step 4: Test reading with new password
                Console.WriteLine("\n4️⃣ Testing native ReadFile with new password...");
                await TestNativeReadFile(testPath, "newpass456");
                
                // Step 5: Verify old password fails
                Console.WriteLine("\n5️⃣ Verifying old password fails...");
                await TestNativeReadFileShouldFail(testPath, "original123");
                
                Console.WriteLine("\n✅ Native-style password change test completed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Native-style test failed: {ex.Message}");
                Console.WriteLine($"🔍 Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"🔍 Inner exception: {ex.InnerException.Message}");
                }
                Console.WriteLine($"🔍 Stack trace: {ex.StackTrace}");
            }
            finally
            {
                if (Directory.Exists(testPath))
                {
                    Directory.Delete(testPath, true);
                    Console.WriteLine($"🧹 Cleaned up: {testPath}");
                }
            }
        }

        /// <summary>
        /// Creates a vault using native methods and populates it with test data
        /// </summary>
        private async Task CreateVaultNativeStyle(string vaultPath, string password, bool encryptFilenames)
        {
            Console.WriteLine($"🔧 Creating vault with password: '{password}', encrypt filenames: {encryptFilenames}");
            
            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                
                fixed (byte* vaultPathPtr = vaultPathBytes)
                fixed (byte* passwordPtr = passwordBytes)
                {
                    // Create UVF vault
                    Console.WriteLine("🔧 Calling TitanVaultNativeMethods.CreateUvfVault...");
                    var result = TitanVaultNativeMethods.CreateUvfVault(
                        vaultPathPtr, vaultPathBytes.Length,
                        passwordPtr, passwordBytes.Length,
                        encryptFilenames ? 1 : 0,
                        TitanVaultUtils.KdfMethod.PBKDF2,
                        64000);
                    
                    Console.WriteLine($"🔧 CreateUvfVault result: {result}");
                    
                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        string error = TitanVaultUtils.GetLastErrorString();
                        throw new InvalidOperationException($"Failed to create UVF vault: {error} (code: {result})");
                    }
                    
                    Console.WriteLine("✅ Vault created successfully");
                }
                
                // Now load the vault and add test data
                Console.WriteLine("🔧 Loading vault to add test data...");
                IntPtr vaultHandle = IntPtr.Zero;
                
                fixed (byte* vaultPathPtr = vaultPathBytes)
                fixed (byte* passwordPtr = passwordBytes)
                {
                    vaultHandle = TitanVaultNativeMethods.LoadUvfVault(
                        vaultPathPtr, vaultPathBytes.Length,
                        passwordPtr, passwordBytes.Length,
                        null, 0); // No specific user ID
                    
                    if (vaultHandle == IntPtr.Zero)
                    {
                        string error = TitanVaultUtils.GetLastErrorString();
                        throw new InvalidOperationException($"Failed to load UVF vault: {error}");
                    }
                    
                    Console.WriteLine($"✅ Vault loaded successfully, handle: {vaultHandle}");
                }
                
                try
                {
                    // Write test data
                    Console.WriteLine("📄 Writing test data to vault...");
                    WriteTestDataNative(vaultHandle);
                    Console.WriteLine("✅ Test data written successfully");
                }
                finally
                {
                    // Close vault
                    Console.WriteLine("🔧 Closing vault...");
                    var closeResult = TitanVaultNativeMethods.CloseVault(vaultHandle);
                    Console.WriteLine($"🔧 CloseVault result: {closeResult}");
                }
            }
        }

        /// <summary>
        /// Writes test data to the vault using native methods
        /// </summary>
        private void WriteTestDataNative(IntPtr vaultHandle)
        {
            var testFiles = new[]
            {
                ("/test.txt", "Hello, Native UVF World!"),
                ("/password_test.txt", "This file tests native password changes!"),
                ("/data.json", $"{{\"test\": \"native\", \"timestamp\": \"{DateTime.Now:O}\"}}")
            };
            
            unsafe
            {
                foreach (var (filePath, content) in testFiles)
                {
                    Console.WriteLine($"   📄 Writing: {filePath} ({content.Length} chars)");
                    
                    var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                    var contentBytes = Encoding.UTF8.GetBytes(content);
                    
                    fixed (byte* filePathPtr = filePathBytes)
                    fixed (byte* contentPtr = contentBytes)
                    {
                        var result = TitanVaultNativeMethods.WriteFile(
                            vaultHandle,
                            filePathPtr, filePathBytes.Length,
                            contentPtr, contentBytes.Length);
                        
                        if (result != TitanVaultUtils.ReturnCodes.Success)
                        {
                            string error = TitanVaultUtils.GetLastErrorString();
                            throw new InvalidOperationException($"Failed to write file {filePath}: {error} (code: {result})");
                        }
                        
                        Console.WriteLine($"   ✅ {filePath} written successfully");
                    }
                }
            }
        }

        /// <summary>
        /// Tests native ReadFile method - THIS IS WHERE THE -1 ERROR SHOULD OCCUR
        /// This method reproduces the exact same issue as in TitanVault.ReadAllBytes()
        /// </summary>
        private async Task TestNativeReadFile(string vaultPath, string password)
        {
            Console.WriteLine($"🔍 Testing native ReadFile with password: '{password}'");
            
            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                IntPtr vaultHandle = IntPtr.Zero;
                
                // Load vault
                fixed (byte* vaultPathPtr = vaultPathBytes)
                fixed (byte* passwordPtr = passwordBytes)
                {
                    Console.WriteLine("🔧 Loading vault for reading...");
                    vaultHandle = TitanVaultNativeMethods.LoadUvfVault(
                        vaultPathPtr, vaultPathBytes.Length,
                        passwordPtr, passwordBytes.Length,
                        null, 0);
                    
                    if (vaultHandle == IntPtr.Zero)
                    {
                        string error = TitanVaultUtils.GetLastErrorString();
                        throw new InvalidOperationException($"Failed to load vault: {error}");
                    }
                    
                    Console.WriteLine($"✅ Vault loaded, handle: {vaultHandle}");
                }
                
                try
                {
                    // Test reading the test file using the EXACT same logic as TitanVault.ReadAllBytes()
                    string testFile = "/password_test.txt";
                    Console.WriteLine($"🔍 Reading file: {testFile}");
                    
                    var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(testFile);
                    
                    fixed (byte* filePathPtr = filePathBytes)
                    {
                        // First call to get the required buffer size (EXACT same logic as TitanVault.ReadAllBytes)
                        int bufferSize = 0;
                        
                        Console.WriteLine("🔍 DEBUG: First ReadFile call to get buffer size...");
                        Console.WriteLine($"🔍 DEBUG: vaultHandle = {vaultHandle}");
                        Console.WriteLine($"🔍 DEBUG: filePath = '{testFile}'");
                        Console.WriteLine($"🔍 DEBUG: filePathBytes.Length = {filePathBytes.Length}");
                        Console.WriteLine($"🔍 DEBUG: buffer = null (to get size)");
                        Console.WriteLine($"🔍 DEBUG: bufferSize = {bufferSize} (input)");
                        
                        var result = TitanVaultNativeMethods.ReadFile(
                            vaultHandle, 
                            filePathPtr, filePathBytes.Length, 
                            null, &bufferSize);
                        
                        Console.WriteLine($"🔍 DEBUG: First ReadFile result = {result}");
                        Console.WriteLine($"🔍 DEBUG: bufferSize after call = {bufferSize}");
                        Console.WriteLine($"🔍 DEBUG: Expected result = {TitanVaultUtils.ReturnCodes.InsufficientBuffer} (InsufficientBuffer)");
                        
                        if (result == TitanVaultUtils.ReturnCodes.InsufficientBuffer)
                        {
                            Console.WriteLine("✅ First call returned InsufficientBuffer as expected");
                            Console.WriteLine($"📏 Required buffer size: {bufferSize} bytes");
                            
                            // Allocate buffer and read the file (EXACT same logic)
                            var buffer = new byte[bufferSize];
                            
                            fixed (byte* bufferPtr = buffer)
                            {
                                Console.WriteLine("🔍 DEBUG: Second ReadFile call to read actual data...");
                                Console.WriteLine($"🔍 DEBUG: buffer allocated size = {buffer.Length}");
                                Console.WriteLine($"🔍 DEBUG: bufferSize = {bufferSize}");
                                
                                result = TitanVaultNativeMethods.ReadFile(
                                    vaultHandle, 
                                    filePathPtr, filePathBytes.Length, 
                                    bufferPtr, &bufferSize);
                                
                                Console.WriteLine($"🔍 DEBUG: Second ReadFile result = {result}");
                                Console.WriteLine($"🔍 DEBUG: Expected result = {TitanVaultUtils.ReturnCodes.Success} (Success)");
                                
                                if (result == TitanVaultUtils.ReturnCodes.Success)
                                {
                                    string content = Encoding.UTF8.GetString(buffer, 0, bufferSize);
                                    Console.WriteLine($"✅ File read successfully! Content: '{content}' ({bufferSize} bytes)");
                                    return;
                                }
                                else
                                {
                                    string error = TitanVaultUtils.GetLastErrorString();
                                    Console.WriteLine($"❌ SECOND ReadFile call failed with result: {result}");
                                    Console.WriteLine($"❌ Error message: {error}");
                                    Console.WriteLine($"❌ This is the same issue as in TitanVault.ReadAllBytes()!");
                                    
                                    // Try stream fallback (like in PasswordChangeDemo)
                                    Console.WriteLine("🔧 Trying stream fallback approach...");
                                    TryStreamFallback(vaultHandle, testFile);
                                }
                            }
                        }
                        else
                        {
                            string error = TitanVaultUtils.GetLastErrorString();
                            Console.WriteLine($"❌ FIRST ReadFile call failed with result: {result}");
                            Console.WriteLine($"❌ Error message: {error}");
                            Console.WriteLine($"❌ Expected InsufficientBuffer ({TitanVaultUtils.ReturnCodes.InsufficientBuffer}), got {result}");
                            
                            if (result == -1)
                            {
                                Console.WriteLine("🎯 FOUND IT! This is the -1 error from ReadFile that causes 'Invalid parameters'!");
                                Console.WriteLine("🔍 This is the exact same issue as in TitanVault.ReadAllBytes()");
                            }
                        }
                    }
                }
                finally
                {
                    // Close vault
                    if (vaultHandle != IntPtr.Zero)
                    {
                        Console.WriteLine("🔧 Closing vault...");
                        var closeResult = TitanVaultNativeMethods.CloseVault(vaultHandle);
                        Console.WriteLine($"🔧 CloseVault result: {closeResult}");
                    }
                }
            }
        }

        /// <summary>
        /// Tries to read using stream fallback approach
        /// </summary>
        private void TryStreamFallback(IntPtr vaultHandle, string filePath)
        {
            Console.WriteLine($"🔧 Attempting stream fallback for: {filePath}");
            
            unsafe
            {
                var filePathBytes = TitanVaultUtils.StringToUtf8Bytes(filePath);
                
                fixed (byte* filePathPtr = filePathBytes)
                {
                    // Try to open read stream
                    var streamHandle = TitanVaultNativeMethods.OpenReadStream(
                        vaultHandle, 
                        (IntPtr)filePathPtr, 
                        filePathBytes.Length);
                    
                    if (streamHandle == IntPtr.Zero)
                    {
                        string error = TitanVaultUtils.GetLastErrorString();
                        Console.WriteLine($"❌ Stream fallback failed: {error}");
                        return;
                    }
                    
                    Console.WriteLine($"✅ Read stream opened, handle: {streamHandle}");
                    
                    try
                    {
                        // Get stream length
                        var streamLength = TitanVaultNativeMethods.StreamGetLength(streamHandle);
                        Console.WriteLine($"📏 Stream length: {streamLength} bytes");
                        
                        if (streamLength > 0)
                        {
                            // Read data from stream
                            var buffer = new byte[streamLength];
                            
                            fixed (byte* bufferPtr = buffer)
                            {
                                var bytesRead = TitanVaultNativeMethods.StreamRead(
                                    streamHandle, 
                                    (IntPtr)bufferPtr, 
                                    (int)streamLength);
                                
                                if (bytesRead > 0)
                                {
                                    string content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                                    Console.WriteLine($"✅ Stream fallback SUCCESS! Content: '{content}' ({bytesRead} bytes)");
                                }
                                else
                                {
                                    Console.WriteLine($"❌ Stream read failed, bytes read: {bytesRead}");
                                }
                            }
                        }
                    }
                    finally
                    {
                        // Close stream
                        var closeResult = TitanVaultNativeMethods.CloseStream(streamHandle);
                        Console.WriteLine($"🔧 CloseStream result: {closeResult}");
                    }
                }
            }
        }

        /// <summary>
        /// Changes password using native methods
        /// </summary>
        private async Task ChangePasswordNativeStyle(string vaultPath, string oldPassword, string newPassword)
        {
            Console.WriteLine($"🔑 Changing password from '{oldPassword}' to '{newPassword}' using native methods...");
            
            unsafe
            {
                var vaultPathBytes = TitanVaultUtils.StringToUtf8Bytes(vaultPath);
                var oldPasswordBytes = Encoding.UTF8.GetBytes(oldPassword);
                var newPasswordBytes = Encoding.UTF8.GetBytes(newPassword);
                
                fixed (byte* vaultPathPtr = vaultPathBytes)
                fixed (byte* oldPasswordPtr = oldPasswordBytes)
                fixed (byte* newPasswordPtr = newPasswordBytes)
                {
                    Console.WriteLine("🔧 Calling TitanVaultNativeMethods.ChangeUvfAdminPassword...");
                    
                    var result = TitanVaultNativeMethods.ChangeUvfAdminPassword(
                        vaultPathPtr, vaultPathBytes.Length,
                        oldPasswordPtr, oldPasswordBytes.Length,
                        newPasswordPtr, newPasswordBytes.Length);
                    
                    Console.WriteLine($"🔧 ChangeUvfAdminPassword result: {result}");
                    
                    if (result != TitanVaultUtils.ReturnCodes.Success)
                    {
                        string error = TitanVaultUtils.GetLastErrorString();
                        throw new InvalidOperationException($"Failed to change password: {error} (code: {result})");
                    }
                    
                    Console.WriteLine("✅ Password changed successfully using native methods");
                }
            }
        }

        /// <summary>
        /// Tests that ReadFile fails with the old password
        /// </summary>
        private async Task TestNativeReadFileShouldFail(string vaultPath, string password)
        {
            Console.WriteLine($"🚫 Testing that ReadFile fails with old password: '{password}'");
            
            try
            {
                await TestNativeReadFile(vaultPath, password);
                Console.WriteLine("❌ ERROR: ReadFile should have failed with old password!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✅ Old password correctly rejected: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }
} 