using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UvfLib.Master;
using StorageLib.Abstractions;

namespace ExampleVaultApp
{
    /// <summary>
    /// Test class to verify OpenStreamWithFlags functionality with managed code
    /// </summary>
    public class OpenFlagsTest
    {
        public static async Task RunTestAsync()
        {
            Console.WriteLine("🧪 Testing OpenStreamWithFlags functionality with managed code...");
            Console.WriteLine();

            var testPath = @"D:\temp\uvf-demo\vault_openflags_managed";
            var password = "test123".ToCharArray();

            // Clean up any existing vault
            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, true);
            }
            Directory.CreateDirectory(testPath);

            try
            {
                // Create a new UVF vault
                using var vault = await VaultManager.CreateUvfVaultAsync(testPath, password, encryptFilenames: true);
                Console.WriteLine("✅ Vault created successfully");

                // Test 1: Create new file with WriteOnly + Create flags
                await TestCreateNewFile(vault);

                // Test 2: Open existing file for truncation
                await TestTruncateFile(vault);

                // Test 3: Open file for appending
                await TestAppendToFile(vault);

                // Test 4: Try to open non-existent file without Create flag (should fail)
                await TestOpenNonExistentFile(vault);

                // Test 5: Open file for read-write access
                await TestReadWriteAccess(vault);

                Console.WriteLine();
                Console.WriteLine("✅ All managed code tests completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test failed: {ex.Message}");
                throw;
            }
        }

        private static async Task TestCreateNewFile(VaultManager vault)
        {
            Console.WriteLine("🧪 Test 1: Create new file with WriteOnly + Create flags");

            try
            {
                var flags = OpenFlags.WriteOnly | OpenFlags.Create;
                using var stream = await vault.OpenAsync("/test_create.txt", flags);

                var data = Encoding.UTF8.GetBytes("Hello, this is a new file created with OpenFlags!");
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();

                Console.WriteLine("   ✅ Successfully created new file with Create flag");
                Console.WriteLine($"   📝 Wrote {data.Length} bytes to /test_create.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to create new file: {ex.Message}");
                throw;
            }
        }

        private static async Task TestTruncateFile(VaultManager vault)
        {
            Console.WriteLine("🧪 Test 2: Open existing file for truncation");

            try
            {
                // First, verify the file has content
                var originalContent = await vault.ReadAllTextAsync("/test_create.txt");
                Console.WriteLine($"   📖 Original content: '{originalContent}'");

                // Now truncate it
                var flags = OpenFlags.WriteOnly | OpenFlags.Truncate;
                using var stream = await vault.OpenAsync("/test_create.txt", flags);

                var newData = Encoding.UTF8.GetBytes("Truncated!");
                await stream.WriteAsync(newData, 0, newData.Length);
                await stream.FlushAsync();

                Console.WriteLine("   ✅ Successfully truncated existing file");
                Console.WriteLine($"   📝 New content: '{Encoding.UTF8.GetString(newData)}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to truncate file: {ex.Message}");
                throw;
            }
        }

        private static async Task TestAppendToFile(VaultManager vault)
        {
            Console.WriteLine("🧪 Test 3: Open file for appending");

            try
            {
                var flags = OpenFlags.WriteOnly | OpenFlags.Append;
                using (var stream = await vault.OpenAsync("/test_create.txt", flags))
                {
                    var appendData = Encoding.UTF8.GetBytes(" [APPENDED TEXT]");
                    await stream.WriteAsync(appendData, 0, appendData.Length);
                    await stream.FlushAsync();

                    Console.WriteLine("   ✅ Successfully appended to file");
                    Console.WriteLine($"   📝 Appended: '{Encoding.UTF8.GetString(appendData)}'");
                } // Stream is properly disposed here

                // Now read back the full content to verify
                var fullContent = await vault.ReadAllTextAsync("/test_create.txt");
                Console.WriteLine($"   📖 Full file content: '{fullContent}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to append to file: {ex.Message}");
                throw;
            }
        }

        private static async Task TestOpenNonExistentFile(VaultManager vault)
        {
            Console.WriteLine("🧪 Test 4: Try to open non-existent file without Create flag (should fail)");

            try
            {
                var flags = OpenFlags.WriteOnly; // No Create flag
                using var stream = await vault.OpenAsync("/nonexistent.txt", flags);

                Console.WriteLine("   ❌ Unexpected success - should have failed without Create flag!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("   ✅ Correctly failed to open non-existent file without Create flag");
                Console.WriteLine($"   📝 Error: {ex.Message}");
            }
        }

        private static async Task TestReadWriteAccess(VaultManager vault)
        {
            Console.WriteLine("🧪 Test 5: Open file for read-write access");

            try
            {
                var flags = OpenFlags.ReadWrite | OpenFlags.Create;
                using var stream = await vault.OpenAsync("/readwrite_test.txt", flags);

                // Write some data
                var writeData = Encoding.UTF8.GetBytes("ReadWrite test data");
                await stream.WriteAsync(writeData, 0, writeData.Length);
                await stream.FlushAsync();

                // Seek to beginning and read it back
                stream.Seek(0, SeekOrigin.Begin);
                var readBuffer = new byte[writeData.Length];
                var bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);

                var readContent = Encoding.UTF8.GetString(readBuffer, 0, bytesRead);

                Console.WriteLine("   ✅ Successfully opened file for read-write access");
                Console.WriteLine($"   📝 Wrote: '{Encoding.UTF8.GetString(writeData)}'");
                Console.WriteLine($"   📖 Read back: '{readContent}'");
                Console.WriteLine($"   🔍 Match: {readContent == Encoding.UTF8.GetString(writeData)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed read-write test: {ex.Message}");
                throw;
            }
        }
    }
} 