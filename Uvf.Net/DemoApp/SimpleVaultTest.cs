using System;
using System.IO;
using DemoApp.Wrapper;

namespace DemoApp
{
    public static class SimpleVaultTest
    {
        public static void TestUvfVaultCreationAndLoading()
        {
            string testVaultPath = @"D:\temp\uvf-simple-test";
            char[] testPassword = "TestPassword123".ToCharArray();
            
            try
            {
                Console.WriteLine("🔧 Simple UVF Vault Test");
                Console.WriteLine($"Test vault path: {testVaultPath}");
                
                // Clean up any existing vault
                if (Directory.Exists(testVaultPath))
                {
                    Directory.Delete(testVaultPath, true);
                    Console.WriteLine("✅ Cleaned existing test vault");
                }
                
                Directory.CreateDirectory(testVaultPath);
                Console.WriteLine("✅ Created test vault directory");
                
                // Step 1: Create UVF vault
                Console.WriteLine("1️⃣ Creating UVF vault...");
                var vault = TitanVault.CreateUvfVault(testVaultPath, testPassword, encryptFilenames: true);
                Console.WriteLine("✅ UVF vault created successfully!");
                
                // Step 2: Use the vault
                Console.WriteLine("2️⃣ Testing vault operations...");
                vault.WriteAllText("/test.txt", "Hello UVF World!");
                string content = vault.ReadAllText("/test.txt");
                Console.WriteLine($"✅ File content: {content}");
                
                // Step 3: Close vault
                vault.Dispose();
                Console.WriteLine("✅ Vault closed");
                
                // Step 4: Reopen vault
                Console.WriteLine("3️⃣ Reopening vault...");
                using var vault2 = TitanVault.LoadUvfVault(testVaultPath, testPassword, "admin");
                string content2 = vault2.ReadAllText("/test.txt");
                Console.WriteLine($"✅ Reopened vault, content: {content2}");
                
                Console.WriteLine("🎉 Simple UVF vault test completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Simple vault test failed: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
            finally
            {
                Array.Clear(testPassword, 0, testPassword.Length);
            }
        }
    }
} 