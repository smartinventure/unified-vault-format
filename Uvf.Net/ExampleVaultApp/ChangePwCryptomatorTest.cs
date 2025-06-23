using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UvfLib.Master;
using StorageLib.Abstractions;

namespace ExampleVaultApp
{
    /// <summary>
    /// Test class for demonstrating Cryptomator vault password change functionality.
    /// This class helps understand how password changes work and tests the implementation.
    /// </summary>
    public class ChangePwCryptomatorTest
    {
        private string _testVaultPath;
        private readonly string _originalPassword = "test123";
        private readonly string _newPassword = "newtest456";

        public ChangePwCryptomatorTest()
        {
            _testVaultPath = Path.Combine(Path.GetTempPath(), "ChangePwTest_" + Guid.NewGuid().ToString("N")[..8]);
        }

        /// <summary>
        /// Main test method that demonstrates the complete password change process
        /// </summary>
        public async Task RunPasswordChangeTestAsync()
        {
            try
            {
                Console.WriteLine("=== Cryptomator Password Change Test ===");
                _testVaultPath = Path.Combine(Path.GetTempPath(), $"ChangePwTest_{Path.GetRandomFileName().Substring(0, 8)}");
                Console.WriteLine($"Test vault path: {_testVaultPath}");

                // Step 1: Create a new vault with original password
                Console.WriteLine("\n1. Creating vault with original password...");
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
                
                Console.WriteLine("\n✅ Password change test completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Password change test failed: {ex.Message}");
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
            using var vault = await VaultManager.CreateCryptomatorVaultAsync(_testVaultPath, _originalPassword);
            
            // Write a test file to verify the vault works
            await vault.WriteAllBytesAsync("test.txt", Encoding.UTF8.GetBytes("Hello, World!"));
            
            Console.WriteLine($"✅ Vault created successfully at: {_testVaultPath}");
            
            // Display the created files
            var files = Directory.GetFiles(_testVaultPath);
            Console.WriteLine($"Created files: {string.Join(", ", files.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Verifies that the vault can be opened with the given password
        /// </summary>
        private async Task VerifyVaultAccessAsync(string password, string passwordType)
        {
            using var vault = await VaultManager.LoadCryptomatorVaultAsync(_testVaultPath, password);
            
            // Try to read the test file
            var data = await vault.ReadAllBytesAsync("test.txt");
            var content = Encoding.UTF8.GetString(data);
            
            if (content == "Hello, World!")
            {
                Console.WriteLine($"✅ Vault successfully opened with {passwordType} password");
            }
            else
            {
                throw new Exception($"Vault opened but content mismatch. Expected 'Hello, World!', got '{content}'");
            }
        }

        /// <summary>
        /// Changes the vault password using the VaultManager API
        /// </summary>
        private async Task ChangeVaultPasswordAsync()
        {
            Console.WriteLine("Using VaultManager.ChangeVaultPasswordAsync() static method...");
            
            try
            {
                // First, let's analyze the masterkey file before attempting to change the password
                string masterkeyPath = Path.Combine(_testVaultPath, "masterkey.cryptomator");
                if (File.Exists(masterkeyPath))
                {
                    byte[] masterkeyContent = await File.ReadAllBytesAsync(masterkeyPath);
                    Console.WriteLine($"Masterkey file size: {masterkeyContent.Length} bytes");
                    await AnalyzeMasterkeyFileAsync(masterkeyContent, "Before Password Change");
                }
                
                // Use the static method to change the password
                await VaultManager.ChangeCryptomatorVaultPasswordAsync(_testVaultPath, _originalPassword, _newPassword);
                Console.WriteLine("✅ Password changed successfully using VaultManager static API");
                
                // Analyze the masterkey file after the change
                if (File.Exists(masterkeyPath))
                {
                    byte[] masterkeyContent = await File.ReadAllBytesAsync(masterkeyPath);
                    Console.WriteLine($"Masterkey file size after change: {masterkeyContent.Length} bytes");
                    await AnalyzeMasterkeyFileAsync(masterkeyContent, "After Password Change");
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
        private async Task VerifyPasswordRejectionAsync(string password)
        {
            try
            {
                using var vault = await VaultManager.LoadCryptomatorVaultAsync(_testVaultPath, password);
                throw new Exception("Expected password to be rejected, but vault opened successfully");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"✅ Password correctly rejected: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyzes and displays the structure of a masterkey file
        /// </summary>
        private async Task AnalyzeMasterkeyFileAsync(byte[] masterkeyContent, string label)
        {
            try
            {
                string jsonContent = Encoding.UTF8.GetString(masterkeyContent);
                Console.WriteLine($"\n--- {label} Masterkey File Analysis ---");
                
                // Parse as JSON to analyze structure
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                
                Console.WriteLine($"Version: {root.GetProperty("version").GetInt32()}");
                Console.WriteLine($"Scrypt Cost Param: {root.GetProperty("scryptCostParam").GetInt32()}");
                Console.WriteLine($"Scrypt Block Size: {root.GetProperty("scryptBlockSize").GetInt32()}");
                
                if (root.TryGetProperty("scryptSalt", out var saltProp))
                {
                    string saltBase64 = saltProp.GetString() ?? "";
                    Console.WriteLine($"Scrypt Salt: {saltBase64[..Math.Min(20, saltBase64.Length)]}... ({saltBase64.Length} chars)");
                }
                
                if (root.TryGetProperty("primaryMasterKey", out var encKeyProp))
                {
                    string encKeyBase64 = encKeyProp.GetString() ?? "";
                    Console.WriteLine($"Primary Master Key: {encKeyBase64[..Math.Min(20, encKeyBase64.Length)]}... ({encKeyBase64.Length} chars)");
                }
                
                if (root.TryGetProperty("hmacMasterKey", out var macKeyProp))
                {
                    string macKeyBase64 = macKeyProp.GetString() ?? "";
                    Console.WriteLine($"HMAC Master Key: {macKeyBase64[..Math.Min(20, macKeyBase64.Length)]}... ({macKeyBase64.Length} chars)");
                }
                
                Console.WriteLine($"--- End {label} Analysis ---\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Could not analyze {label.ToLower()} masterkey file: {ex.Message}");
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
    }
} 