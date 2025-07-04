using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UvfLib.Master;
using StorageLib.Abstractions;

namespace ExampleVaultApp
{
    public static class DebugRandomWrite
    {
        public static async Task RunDebugAsync()
        {
            Console.WriteLine("🔍 Debugging Random Write Issue...");
            
            try
            {
                // Create a simple vault
                var testPath = @"D:\temp\uvf-debug\vault_debug";
                var password = "password123".ToCharArray();
                
                // Clean up any existing vault
                if (Directory.Exists(testPath))
                {
                    Directory.Delete(testPath, true);
                }
                
                var vault = await VaultManager.CreateUvfVaultAsync(testPath, password, true);
                Console.WriteLine("✅ Vault created");

                // Test 1: Write a simple file sequentially
                Console.WriteLine("\n📝 Test 1: Sequential write");
                using (var stream = await vault.OpenAsync("/test.txt", OpenFlags.Create | OpenFlags.WriteOnly))
                {
                    byte[] data = Encoding.UTF8.GetBytes("Hello World");
                    await stream.WriteAsync(data, 0, data.Length);
                }
                Console.WriteLine("✅ Sequential write completed");

                // Try to read it back
                try
                {
                    string content = await vault.ReadAllTextAsync("/test.txt");
                    Console.WriteLine($"✅ Sequential read successful: '{content}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Sequential read failed: {ex.Message}");
                }

                // Test 2: Write the same content with a single random write
                Console.WriteLine("\n📝 Test 2: Single random write at position 0");
                using (var stream = await vault.OpenAsync("/test2.txt", OpenFlags.Create | OpenFlags.WriteOnly))
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    byte[] data = Encoding.UTF8.GetBytes("Hello World");
                    await stream.WriteAsync(data, 0, data.Length);
                }
                Console.WriteLine("✅ Random write at position 0 completed");

                // Try to read it back
                try
                {
                    string content = await vault.ReadAllTextAsync("/test2.txt");
                    Console.WriteLine($"✅ Random read successful: '{content}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Random read failed: {ex.Message}");
                }

                // Test 3: Write, then modify with random write
                Console.WriteLine("\n📝 Test 3: Sequential write + random modification");
                using (var stream = await vault.OpenAsync("/test3.txt", OpenFlags.Create | OpenFlags.WriteOnly))
                {
                    byte[] data = Encoding.UTF8.GetBytes("Hello World");
                    await stream.WriteAsync(data, 0, data.Length);
                }
                Console.WriteLine("✅ Initial sequential write completed");

                // Now modify with random write
                using (var stream = await vault.OpenAsync("/test3.txt", OpenFlags.WriteOnly))
                {
                    stream.Seek(6, SeekOrigin.Begin); // Position after "Hello "
                    byte[] data = Encoding.UTF8.GetBytes("UVF");
                    await stream.WriteAsync(data, 0, data.Length);
                }
                Console.WriteLine("✅ Random modification completed");

                // Try to read it back
                try
                {
                    string content = await vault.ReadAllTextAsync("/test3.txt");
                    Console.WriteLine($"✅ Modified content read: '{content}' (expected: 'Hello UVFrld')");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Modified content read failed: {ex.Message}");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Debug test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
} 