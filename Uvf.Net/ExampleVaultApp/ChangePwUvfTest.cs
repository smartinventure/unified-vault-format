using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UvfLib.Storage;
using UvfLib.Vault;
using StorageLib.Abstractions;

namespace ExampleVaultApp
{
    /// <summary>
    /// Test class for demonstrating UVF vault password change functionality.
    /// This class helps understand how password changes work and tests the implementation.
    /// </summary>
    public class ChangePwUvfTest
    {
        private string _testVaultPath;
        private readonly string _originalPassword = "uvf-test123";
        private readonly string _newPassword = "uvf-newtest456";
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
        private async Task VerifyVaultAccessAsync(string password, string passwordType)
        {
            // Use auto-detection when loading vault for verification
            using var vault = await VaultManager.LoadUvfVaultAsync(_testVaultPath, password);
            
            // Try to detect and display the actual setting
            try
            {
                string vaultUvfFile = Path.Combine(_testVaultPath, "vault.uvf");
                byte[] vaultFileContent = await File.ReadAllBytesAsync(vaultUvfFile);
                bool detectedEncryptFilenames = VaultHandler.DetectFilenameEncryption(vaultFileContent, password);
                Console.WriteLine($"🔍 Auto-detected filename encryption during {passwordType} password verification: {(detectedEncryptFilenames ? "Enabled" : "Disabled")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Could not detect filename encryption setting: {ex.Message}");
            }
            
            // Try to read the test file
            var data = await vault.ReadAllBytesAsync("test.txt");
            var content = Encoding.UTF8.GetString(data);
            
            if (content == "Hello, UVF World!")
            {
                Console.WriteLine($"✅ UVF vault successfully opened with {passwordType} password");
            }
            else
            {
                throw new Exception($"UVF vault opened but content mismatch. Expected 'Hello, UVF World!', got '{content}'");
            }
        }

        /// <summary>
        /// Changes the vault password using the VaultManager API
        /// </summary>
        private async Task ChangeVaultPasswordAsync()
        {
            Console.WriteLine("Using VaultManager.ChangeUvfPasswordAsync() static method...");
            
            try
            {
                // First, let's analyze the vault file before attempting to change the password
                string vaultFilePath = Path.Combine(_testVaultPath, "vault.uvf");
                if (File.Exists(vaultFilePath))
                {
                    byte[] vaultContent = await File.ReadAllBytesAsync(vaultFilePath);
                    Console.WriteLine($"UVF vault file size: {vaultContent.Length} bytes");
                    await AnalyzeUvfVaultFileAsync(vaultContent, "Before Password Change");
                }
                
                // Use the static method to change the password
                await VaultManager.ChangeUvfPasswordAsync(_testVaultPath, _originalPassword, _newPassword);
                Console.WriteLine("✅ Password changed successfully using VaultManager static API");
                
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
        private async Task VerifyPasswordRejectionAsync(string password)
        {
            try
            {
                // Use auto-detection when testing password rejection
                using var vault = await VaultManager.LoadUvfVaultAsync(_testVaultPath, password);
                throw new Exception("Expected password to be rejected, but UVF vault opened successfully");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"✅ Password correctly rejected: {ex.Message}");
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
    }
} 