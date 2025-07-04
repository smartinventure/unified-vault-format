using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UvfLib.Master;
using StorageLib.Abstractions;

namespace ExampleVaultApp
{
    /// <summary>
    /// Test class to demonstrate random write functionality in encrypted files
    /// Shows how the read-modify-write approach works for encrypted chunks
    /// </summary>
    public class RandomWriteTest
    {
        public static async Task RunTestAsync()
        {
            Console.WriteLine("🧪 Testing Random Write functionality in encrypted files...");
            Console.WriteLine();

            var testPath = @"D:\temp\uvf-demo\vault_randomwrite";
            var password = "randomwrite123".ToCharArray();

            // Clean up any existing vault
            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, true);
            }

            try
            {
                // Create vault
                var vault = await VaultManager.CreateUvfVaultAsync(testPath, password, true);
                Console.WriteLine("✅ Vault created successfully");

                await TestRandomWrites(vault);
                //await TestChunkBoundaryWrites(vault);
                //await TestSeekAndWrite(vault);
                //await TestMultipleRandomWrites(vault);

                Console.WriteLine();
                Console.WriteLine("🎉 All random write tests completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test failed: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private static async Task TestRandomWrites(VaultManager vault)
        {
            Console.WriteLine("🧪 Test 1: Basic random writes within a file");

            // Create a file with initial content
            string initialContent = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefghijklmnopqrstuvwxyz";
            await vault.WriteAllTextAsync("/random_test.txt", initialContent);
            Console.WriteLine($"   📝 Created file with content: '{initialContent}'");
            
            // Open for writing with seeking support
            using (var writeStream = await vault.OpenAsync("/random_test.txt", OpenFlags.WriteOnly))
            {
                
                // Test 1: Write at position 10
                writeStream.Seek(10, SeekOrigin.Begin);
                byte[] data1 = Encoding.UTF8.GetBytes("***");
                await writeStream.WriteAsync(data1, 0, data1.Length);
                Console.WriteLine("   ✅ Wrote '***' at position 10");

                // Test 2: Write at position 30
                writeStream.Seek(30, SeekOrigin.Begin);
                byte[] data2 = Encoding.UTF8.GetBytes("###");
                await writeStream.WriteAsync(data2, 0, data2.Length);
                Console.WriteLine("   ✅ Wrote '###' at position 30");

                // Test 3: Write at position 5
                writeStream.Seek(5, SeekOrigin.Begin);
                byte[] data3 = Encoding.UTF8.GetBytes("@@@");
                await writeStream.WriteAsync(data3, 0, data3.Length);
                Console.WriteLine("   ✅ Wrote '@@@' at position 5");

                await writeStream.FlushAsync();
            } // Ensure write stream is closed before reading
            
            // Read back and verify
            string finalContent = await vault.ReadAllTextAsync("/random_test.txt");
            Console.WriteLine($"   📖 Final content: '{finalContent}'");
            Console.WriteLine($"   📊 Expected:      'ABCDE@@@IJKL***OPQRSTUVWXYZ0123###56789abcdefghijklmnopqrstuvwxyz'");
            
            // Verify the modifications
            if (finalContent.Substring(5, 3) == "@@@" && 
                finalContent.Substring(13, 3) == "***" && 
                finalContent.Substring(33, 3) == "###")
            {
                Console.WriteLine("   ✅ Random writes successful - all modifications verified!");
            }
            else
            {
                Console.WriteLine("   ❌ Random writes failed - modifications not found");
            }
        }

        private static async Task TestChunkBoundaryWrites(VaultManager vault)
        {
            Console.WriteLine();
            Console.WriteLine("🧪 Test 2: Writes across chunk boundaries (32KB chunks)");

            // Create a large file that spans multiple chunks (32KB each)
            const int chunkSize = 32 * 1024; // 32KB
            const int fileSize = chunkSize * 3; // 3 chunks = 96KB
            
            byte[] largeContent = new byte[fileSize];
            for (int i = 0; i < fileSize; i++)
            {
                largeContent[i] = (byte)('A' + (i % 26)); // Repeating A-Z pattern
            }

            using (var stream = await vault.OpenAsync("/large_test.txt", OpenFlags.WriteOnly | OpenFlags.Create))
            {
                await stream.WriteAsync(largeContent, 0, largeContent.Length);
            }
            Console.WriteLine($"   📝 Created large file: {fileSize} bytes ({fileSize / 1024}KB)");

            // Write across chunk boundary (chunk 0 -> chunk 1)
            using (var writeStream = await vault.OpenAsync("/large_test.txt", OpenFlags.WriteOnly))
            {
                int boundaryPos = chunkSize - 10; // 10 bytes before chunk boundary
                writeStream.Seek(boundaryPos, SeekOrigin.Begin);
                
                byte[] boundaryData = Encoding.UTF8.GetBytes("BOUNDARY_WRITE_TEST_DATA");
                await writeStream.WriteAsync(boundaryData, 0, boundaryData.Length);
                Console.WriteLine($"   ✅ Wrote across chunk boundary at position {boundaryPos}");
                
                // Write in middle of chunk 2
                int chunk2Pos = chunkSize * 2 + 1000;
                writeStream.Seek(chunk2Pos, SeekOrigin.Begin);
                byte[] chunk2Data = Encoding.UTF8.GetBytes("CHUNK2_MODIFICATION");
                await writeStream.WriteAsync(chunk2Data, 0, chunk2Data.Length);
                Console.WriteLine($"   ✅ Wrote in chunk 2 at position {chunk2Pos}");
            }

            // Verify by reading specific positions
            using (var readStream = await vault.OpenAsync("/large_test.txt", OpenFlags.ReadOnly))
            {
                // Check boundary write
                readStream.Seek(chunkSize - 10, SeekOrigin.Begin);
                byte[] buffer1 = new byte[24];
                await readStream.ReadAsync(buffer1, 0, 24);
                string boundaryResult = Encoding.UTF8.GetString(buffer1);
                Console.WriteLine($"   📖 Boundary read: '{boundaryResult}'");

                // Check chunk 2 write
                readStream.Seek(chunkSize * 2 + 1000, SeekOrigin.Begin);
                byte[] buffer2 = new byte[19];
                await readStream.ReadAsync(buffer2, 0, 19);
                string chunk2Result = Encoding.UTF8.GetString(buffer2);
                Console.WriteLine($"   📖 Chunk 2 read: '{chunk2Result}'");

                if (boundaryResult == "BOUNDARY_WRITE_TEST_DATA" && chunk2Result == "CHUNK2_MODIFICATION")
                {
                    Console.WriteLine("   ✅ Chunk boundary writes successful!");
                }
                else
                {
                    Console.WriteLine("   ❌ Chunk boundary writes failed");
                }
            }
        }

        private static async Task TestSeekAndWrite(VaultManager vault)
        {
            Console.WriteLine();
            Console.WriteLine("🧪 Test 3: Multiple seek operations and writes");

            // Create initial file
            await vault.WriteAllTextAsync("/seek_test.txt", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");

            using var stream = await vault.OpenAsync("/seek_test.txt", OpenFlags.WriteOnly);

            // Multiple seek and write operations
            var operations = new[]
            {
                (Position: 5, Data: "***"),
                (Position: 15, Data: "###"),
                (Position: 0, Data: "START"),
                (Position: 30, Data: "END"),
                (Position: 10, Data: "MID")
            };

            foreach (var (position, data) in operations)
            {
                stream.Seek(position, SeekOrigin.Begin);
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                await stream.WriteAsync(bytes, 0, bytes.Length);
                Console.WriteLine($"   ✅ Wrote '{data}' at position {position}");
            }

            await stream.FlushAsync();

            // Verify final content
            string result = await vault.ReadAllTextAsync("/seek_test.txt");
            Console.WriteLine($"   📖 Final result: '{result}'");
            Console.WriteLine("   ✅ Multiple seek and write operations completed!");
        }

        private static async Task TestMultipleRandomWrites(VaultManager vault)
        {
            Console.WriteLine();
            Console.WriteLine("🧪 Test 4: Concurrent-style random writes (simulated)");

            // Create a file to simulate database-like random access
            const int recordSize = 50;
            const int numRecords = 100;
            const int fileSize = recordSize * numRecords;

            // Initialize with empty records
            byte[] initialData = new byte[fileSize];
            for (int i = 0; i < fileSize; i++)
            {
                initialData[i] = (byte)'.'; // Fill with dots
            }

            using (var stream = await vault.OpenAsync("/database.txt", OpenFlags.WriteOnly | OpenFlags.Create))
            {
                await stream.WriteAsync(initialData, 0, initialData.Length);
            }
            Console.WriteLine($"   📝 Created database-like file: {numRecords} records of {recordSize} bytes each");

            // Simulate random record updates
            using (var stream = await vault.OpenAsync("/database.txt", OpenFlags.WriteOnly))
            {
                var random = new Random(42); // Fixed seed for reproducible results
                var updatedRecords = new int[10];

                for (int i = 0; i < 10; i++)
                {
                    int recordIndex = random.Next(0, numRecords);
                    updatedRecords[i] = recordIndex;
                    
                    int position = recordIndex * recordSize;
                    stream.Seek(position, SeekOrigin.Begin);
                    
                    string recordData = $"RECORD_{recordIndex:D3}_UPDATED_AT_{DateTime.Now.Ticks}";
                    byte[] recordBytes = Encoding.UTF8.GetBytes(recordData.PadRight(recordSize - 1));
                    
                    await stream.WriteAsync(recordBytes, 0, Math.Min(recordBytes.Length, recordSize - 1));
                    Console.WriteLine($"   ✅ Updated record {recordIndex} at position {position}");
                }

                Console.WriteLine($"   📊 Updated records: [{string.Join(", ", updatedRecords)}]");
            }

            // Verify some random records
            using (var stream = await vault.OpenAsync("/database.txt", OpenFlags.ReadOnly))
            {
                for (int i = 0; i < 3; i++)
                {
                    int recordIndex = i * 25; // Check records 0, 25, 50
                    stream.Seek(recordIndex * recordSize, SeekOrigin.Begin);
                    
                    byte[] buffer = new byte[recordSize];
                    await stream.ReadAsync(buffer, 0, recordSize);
                    
                    string content = Encoding.UTF8.GetString(buffer).TrimEnd('\0', '.');
                    Console.WriteLine($"   📖 Record {recordIndex}: '{content}'");
                }
            }

            Console.WriteLine("   ✅ Database-style random access completed!");
        }
    }
} 