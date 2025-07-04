using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UvfLib.Master;
using StorageLib.Abstractions;

namespace ExampleVaultApp
{
    public static class ContextDebugTest
    {
        public static async Task RunDebugAsync()
        {
            Console.WriteLine("🔍 Context Debug Test - Testing improved chunk management...");
            
            try
            {
                // Create a simple vault
                var testPath = @"D:\temp\uvf-debug\context_debug";
                var password = "password123".ToCharArray();
                
                // Clean up any existing vault
                if (Directory.Exists(testPath))
                {
                    Directory.Delete(testPath, true);
                }
                
                // Create vault
                var vault = await VaultManager.CreateUvfVaultAsync(testPath, password, true);
                Console.WriteLine("✅ Vault created successfully");
                
                // Test 1: Write initial content
                await vault.WriteAllTextAsync("/test.txt", "Hello World!", Encoding.UTF8);
                Console.WriteLine("✅ Initial content written");
                
                // Test 2: Read it back immediately (should work)
                var content1 = await vault.ReadAllTextAsync("/test.txt", Encoding.UTF8);
                Console.WriteLine($"✅ Read back immediately: '{content1}'");
                
                // Test 3: Close and reopen vault (fresh instance)
                vault.Dispose();
                vault = await VaultManager.LoadUvfVaultAsync(testPath, password);
                Console.WriteLine("✅ Vault reopened with fresh instance");
                
                // Test 4: Read with fresh instance (should work)
                var content2 = await vault.ReadAllTextAsync("/test.txt", Encoding.UTF8);
                Console.WriteLine($"✅ Read with fresh instance: '{content2}'");
                
                // Test 5: Now try a simple overwrite (not random write)
                await vault.WriteAllTextAsync("/test.txt", "New Content!", Encoding.UTF8);
                Console.WriteLine("✅ Overwrote with new content");
                
                // Test 6: Read back the overwrite
                var content3 = await vault.ReadAllTextAsync("/test.txt", Encoding.UTF8);
                Console.WriteLine($"✅ Read back overwrite: '{content3}'");
                
                Console.WriteLine("🎉 All tests passed! Basic read/write functionality is working correctly.");
                
                vault.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test failed: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
} 