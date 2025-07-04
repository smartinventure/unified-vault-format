using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DemoApp.Wrapper;

namespace DemoApp
{
    /// <summary>
    /// Demonstrates the OpenStreamWithFlags functionality with various flag combinations
    /// Shows how developers can control file creation, truncation, and appending behavior
    /// </summary>
    public class OpenFlagsDemo
    {
        private readonly string _vaultPath;
        private readonly string _password;

        public OpenFlagsDemo(string vaultPath, string password)
        {
            _vaultPath = vaultPath ?? throw new ArgumentNullException(nameof(vaultPath));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        public async Task RunDemoAsync()
        {
            Console.WriteLine("🔧 Setting up vault for open flags demo...");
            
            // Clean up any existing vault
            if (Directory.Exists(_vaultPath))
            {
                Directory.Delete(_vaultPath, true);
            }
            Directory.CreateDirectory(_vaultPath);

            // Create a new UVF vault
            using var vault = TitanVault.CreateUvfVault(_vaultPath, _password.ToCharArray(), encryptFilenames: true);
            Console.WriteLine("✅ Vault created successfully");
            Console.WriteLine();

            // Test 1: Create new file (should succeed)
            await TestCreateNewFile(vault);
            Console.WriteLine();

            // Test 2: Try to create file exclusively (should fail - file exists)
            await TestCreateExclusiveFile(vault);
            Console.WriteLine();

            // Test 3: Open existing file for truncation
            await TestTruncateExistingFile(vault);
            Console.WriteLine();

            // Test 4: Open file for appending
            await TestAppendToFile(vault);
            Console.WriteLine();

            // Test 5: Try to open non-existent file without Create flag (should fail)
            await TestOpenNonExistentFile(vault);
            Console.WriteLine();

            // Test 6: Create file with Create + Exclusive flags (should succeed for new file)
            await TestCreateExclusiveNewFile(vault);
            Console.WriteLine();

            // Test 7: Open file for read-write access
            await TestReadWriteAccess(vault);
            Console.WriteLine();

            Console.WriteLine("📋 Summary of Open Flags Functionality:");
            Console.WriteLine("   ✅ Create flag: Creates file if it doesn't exist");
            Console.WriteLine("   ✅ Exclusive flag: Fails if file already exists (when used with Create)");
            Console.WriteLine("   ✅ Truncate flag: Truncates existing file to zero length");
            Console.WriteLine("   ✅ Append flag: Opens file for appending at the end");
            Console.WriteLine("   ✅ ReadWrite flag: Allows both reading and writing");
            Console.WriteLine("   ✅ Error handling: Proper error messages for invalid operations");
        }

        private async Task TestCreateNewFile(TitanVault vault)
        {
            Console.WriteLine("🧪 Test 1: Create new file with WriteOnly + Create flags");
            
            try
            {
                var flags = TitanVaultUtils.OpenFlags.WriteOnly | TitanVaultUtils.OpenFlags.Create;
                using var stream = vault.OpenStreamWithFlags("/test_create.txt", flags);
                
                var data = Encoding.UTF8.GetBytes("Hello, this is a new file created with OpenFlags!");
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
                
                Console.WriteLine("   ✅ Successfully created new file with Create flag");
                Console.WriteLine($"   📝 Wrote {data.Length} bytes to /test_create.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to create new file: {ex.Message}");
            }
        }

        private async Task TestCreateExclusiveFile(TitanVault vault)
        {
            Console.WriteLine("🧪 Test 2: Try to create file exclusively (should fail - file exists)");
            
            try
            {
                var flags = TitanVaultUtils.OpenFlags.WriteOnly | TitanVaultUtils.OpenFlags.Create | TitanVaultUtils.OpenFlags.Exclusive;
                using var stream = vault.OpenStreamWithFlags("/test_create.txt", flags);
                
                Console.WriteLine("   ❌ Unexpected success - Exclusive flag should have failed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("   ✅ Correctly failed with Exclusive flag on existing file");
                Console.WriteLine($"   📝 Error: {ex.Message}");
            }
        }

        private async Task TestTruncateExistingFile(TitanVault vault)
        {
            Console.WriteLine("🧪 Test 3: Open existing file for truncation");
            
            try
            {
                // First, verify the file has content
                var originalContent = await ReadFileContent(vault, "/test_create.txt");
                Console.WriteLine($"   📖 Original file size: {originalContent.Length} bytes");
                
                // Now truncate it
                var flags = TitanVaultUtils.OpenFlags.WriteOnly | TitanVaultUtils.OpenFlags.Truncate;
                using var stream = vault.OpenStreamWithFlags("/test_create.txt", flags);
                
                var newData = Encoding.UTF8.GetBytes("Truncated!");
                await stream.WriteAsync(newData, 0, newData.Length);
                await stream.FlushAsync();
                
                Console.WriteLine("   ✅ Successfully truncated existing file");
                Console.WriteLine($"   📝 New content: '{Encoding.UTF8.GetString(newData)}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to truncate file: {ex.Message}");
            }
        }

        private async Task TestAppendToFile(TitanVault vault)
        {
            Console.WriteLine("🧪 Test 4: Open file for appending");
            
            try
            {
                var flags = TitanVaultUtils.OpenFlags.WriteOnly | TitanVaultUtils.OpenFlags.Append;
                using var stream = vault.OpenStreamWithFlags("/test_create.txt", flags);
                
                var appendData = Encoding.UTF8.GetBytes(" [APPENDED TEXT]");
                await stream.WriteAsync(appendData, 0, appendData.Length);
                await stream.FlushAsync();
                
                Console.WriteLine("   ✅ Successfully appended to file");
                Console.WriteLine($"   📝 Appended: '{Encoding.UTF8.GetString(appendData)}'");
                
                // Read back the full content to verify
                var fullContent = await ReadFileContent(vault, "/test_create.txt");
                Console.WriteLine($"   📖 Full file content: '{fullContent}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to append to file: {ex.Message}");
            }
        }

        private async Task TestOpenNonExistentFile(TitanVault vault)
        {
            Console.WriteLine("🧪 Test 5: Try to open non-existent file without Create flag (should fail)");
            
            try
            {
                var flags = TitanVaultUtils.OpenFlags.WriteOnly; // No Create flag
                using var stream = vault.OpenStreamWithFlags("/nonexistent.txt", flags);
                
                Console.WriteLine("   ❌ Unexpected success - should have failed without Create flag!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("   ✅ Correctly failed to open non-existent file without Create flag");
                Console.WriteLine($"   📝 Error: {ex.Message}");
            }
        }

        private async Task TestCreateExclusiveNewFile(TitanVault vault)
        {
            Console.WriteLine("🧪 Test 6: Create file with Create + Exclusive flags (should succeed for new file)");
            
            try
            {
                var flags = TitanVaultUtils.OpenFlags.WriteOnly | TitanVaultUtils.OpenFlags.Create | TitanVaultUtils.OpenFlags.Exclusive;
                using var stream = vault.OpenStreamWithFlags("/exclusive_new.txt", flags);
                
                var data = Encoding.UTF8.GetBytes("This file was created with Create + Exclusive flags!");
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
                
                Console.WriteLine("   ✅ Successfully created new file with Create + Exclusive flags");
                Console.WriteLine($"   📝 Wrote {data.Length} bytes to /exclusive_new.txt");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed to create file with Create + Exclusive: {ex.Message}");
            }
        }

        private async Task TestReadWriteAccess(TitanVault vault)
        {
            Console.WriteLine("🧪 Test 7: Open file for read-write access");
            
            try
            {
                var flags = TitanVaultUtils.OpenFlags.ReadWrite | TitanVaultUtils.OpenFlags.Create;
                using var stream = vault.OpenStreamWithFlags("/readwrite_test.txt", flags);
                
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
            }
        }

        private async Task<string> ReadFileContent(TitanVault vault, string filePath)
        {
            try
            {
                using var stream = vault.OpenReadStream(filePath);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }
            catch
            {
                return "[Failed to read]";
            }
        }
    }
} 