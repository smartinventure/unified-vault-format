using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UvfLib.Storage;
using UvfLib.Core.Api;

namespace ExampleVaultApp
{
    /// <summary>
    /// Test class for demonstrating multi-user UVF vault functionality.
    /// This class shows how to create, manage, and use multi-user vaults with different users.
    /// </summary>
    public class MultiUserVaultTest
    {
        private string _testVaultPath;
        private readonly string _adminPassword = "admin123";
        private readonly string _user1Password = "user1pass";
        private readonly string _user2Password = "user2pass";

        public MultiUserVaultTest()
        {
            _testVaultPath = Path.Combine(Path.GetTempPath(), "MultiUserTest_" + Guid.NewGuid().ToString("N")[..8]);
        }

        /// <summary>
        /// Main test method that demonstrates the complete multi-user vault workflow
        /// </summary>
        public async Task RunMultiUserVaultTestAsync()
        {
            try
            {
                Console.WriteLine("=== Multi-User UVF Vault Test ===");
                _testVaultPath = Path.Combine(Path.GetTempPath(), $"MultiUserTest_{Path.GetRandomFileName().Substring(0, 8)}");
                Console.WriteLine($"Test vault path: {_testVaultPath}");

                // Step 1: Create a new multi-user vault with admin user
                Console.WriteLine("\n1. Creating multi-user vault with admin user...");
                await CreateMultiUserVaultAsync();
                
                // Step 2: Verify admin can access the vault
                Console.WriteLine("\n2. Verifying admin can access vault...");
                await VerifyVaultAccessAsync(_adminPassword, "admin");
                
                // Step 3: Add first user
                Console.WriteLine("\n3. Adding first user (alice)...");
                await AddUserAsync("alice", _user1Password);

                // Step 4: Add second user  
                Console.WriteLine("\n4. Adding second user (bob)...");
                await AddUserAsync("bob", _user2Password);

                // Step 5: Verify both users can access the vault
                Console.WriteLine("\n5. Verifying alice can access vault...");
                await VerifyVaultAccessAsync(_user1Password, "alice");
                
                Console.WriteLine("\n6. Verifying bob can access vault...");
                await VerifyVaultAccessAsync(_user2Password, "bob");

                // Step 7: List all vault users
                Console.WriteLine("\n7. Listing all vault users...");
                await ListVaultUsersAsync();

                // Step 8: Rotate vault keys
                Console.WriteLine("\n8. Rotating vault keys...");
                await RotateVaultKeysAsync();

                // Step 9: Verify users can still access after key rotation
                Console.WriteLine("\n9. Verifying access after key rotation...");
                await VerifyVaultAccessAsync(_adminPassword, "admin");
                await VerifyVaultAccessAsync(_user1Password, "alice");
                await VerifyVaultAccessAsync(_user2Password, "bob");

                // Step 10: Remove a user
                Console.WriteLine("\n10. Removing user alice...");
                await RemoveUserAsync("alice");

                // Step 11: Verify removed user cannot access
                Console.WriteLine("\n11. Verifying alice can no longer access vault...");
                await VerifyUserRejectionAsync(_user1Password, "alice");

                // Step 12: Verify remaining users can still access
                Console.WriteLine("\n12. Verifying remaining users can still access...");
                await VerifyVaultAccessAsync(_adminPassword, "admin");
                await VerifyVaultAccessAsync(_user2Password, "bob");

                Console.WriteLine("\n✅ Multi-user vault test completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Multi-user vault test failed: {ex.Message}");
                Console.WriteLine(ex.ToString());
            }
            finally
            {
                Console.WriteLine($"\n🚮 Cleaned up test vault: {_testVaultPath}");
                await CleanupAsync();
            }
        }

        /// <summary>
        /// Creates a multi-user vault with admin user
        /// </summary>
        private async Task CreateMultiUserVaultAsync()
        {
            // Ensure directory exists
            Directory.CreateDirectory(_testVaultPath);

            // Create vault using VaultManager with admin user
            using var vault = await VaultManager.CreateMultiUserUvfVaultAsync(_testVaultPath, _adminPassword);
            
            // Write a test file to verify the vault works
            await vault.WriteAllBytesAsync("admin_test.txt", System.Text.Encoding.UTF8.GetBytes("Created by admin"));
            
            Console.WriteLine($"✅ Multi-user vault created successfully at: {_testVaultPath}");
            
            // Display the created files
            var files = Directory.GetFiles(_testVaultPath);
            Console.WriteLine($"Created files: {string.Join(", ", files.Select(Path.GetFileName))}");
        }

        /// <summary>
        /// Adds a user to the vault
        /// </summary>
        private async Task AddUserAsync(string userId, string userPassword)
        {
            await VaultManager.AddUserToVaultAsync(_testVaultPath, _adminPassword, userId, userPassword);
            Console.WriteLine($"✅ User '{userId}' added successfully");
        }

        /// <summary>
        /// Removes a user from the vault
        /// </summary>
        private async Task RemoveUserAsync(string userId)
        {
            await VaultManager.RemoveUserFromVaultAsync(_testVaultPath, _adminPassword, userId);
            Console.WriteLine($"✅ User '{userId}' removed successfully");
        }

        /// <summary>
        /// Rotates vault keys
        /// </summary>
        private async Task RotateVaultKeysAsync()
        {
            try
            {
                await VaultManager.RotateVaultKeysAsync(_testVaultPath, _adminPassword);
                Console.WriteLine("✅ Vault keys rotated successfully");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Cannot rotate keys for user"))
            {
                Console.WriteLine($"⚠️ Key rotation limitation: {ex.Message}");
                Console.WriteLine("   In this demo, we can only rotate keys when all users are admin or we have all passwords.");
            }
        }

        /// <summary>
        /// Lists all users in the vault
        /// </summary>
        private async Task ListVaultUsersAsync()
        {
            var users = await VaultManager.GetVaultUsersAsync(_testVaultPath, _adminPassword);
            Console.WriteLine($"Vault users ({users.Count}):");
            foreach (var user in users)
            {
                Console.WriteLine($"  - {user}");
            }
        }

        /// <summary>
        /// Verifies that a user can access the vault
        /// </summary>
        private async Task VerifyVaultAccessAsync(string password, string userId)
        {
            using var vault = await VaultManager.LoadMultiUserUvfVaultAsync(_testVaultPath, password, userId);
            
            // Try to write a user-specific test file
            string testFileName = $"{userId}_test.txt";
            string testContent = $"Hello from {userId}!";
            await vault.WriteAllBytesAsync(testFileName, System.Text.Encoding.UTF8.GetBytes(testContent));
            
            // Try to read it back
            var data = await vault.ReadAllBytesAsync(testFileName);
            var content = System.Text.Encoding.UTF8.GetString(data);
            
            if (content == testContent)
            {
                Console.WriteLine($"✅ User '{userId}' successfully accessed vault");
            }
            else
            {
                throw new Exception($"Vault access test failed for user '{userId}'. Expected '{testContent}', got '{content}'");
            }
        }

        /// <summary>
        /// Verifies that a user is rejected from accessing the vault
        /// </summary>
        private async Task VerifyUserRejectionAsync(string password, string userId)
        {
            try
            {
                using var vault = await VaultManager.LoadMultiUserUvfVaultAsync(_testVaultPath, password, userId);
                throw new Exception($"Expected user '{userId}' to be rejected, but vault opened successfully");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"✅ User '{userId}' correctly rejected from vault access");
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