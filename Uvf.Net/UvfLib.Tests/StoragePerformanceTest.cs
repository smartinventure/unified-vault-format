using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Connectors;
using StorageLib.Abstractions;

namespace UvfLib.Tests
{
    [TestClass]
    public class StoragePerformanceTest
    {
        private const long OneMegabyte = 1024 * 1024;
        private const long OneGigabyte = OneMegabyte * 1024;
        
        [TestMethod]
        public async Task TestFileWritePerformance_DirectVsLocalStorage()
        {
            Console.WriteLine("=== Storage Performance Test ===");
            Console.WriteLine($"Testing 1GB file write performance");
            
            // Get temp directory
            string tempDir = Path.GetTempPath();
            string testDir = Path.Combine(tempDir, "UvfStorageTest");
            Directory.CreateDirectory(testDir);
            
            try
            {
                // Create 1GB test data in memory
                Console.WriteLine("📦 Creating 1GB test data in memory...");
                byte[] testData = CreateTestData(OneGigabyte);
                Console.WriteLine($"✅ Created {testData.Length:N0} bytes ({testData.Length / (double)OneMegabyte:F1} MB) of test data");
                
                // Test 1: Direct file write
                await TestDirectFileWrite(testDir, testData);
                
                // Test 2: LocalStorage write (chunked)
                await TestLocalStorageWrite(testDir, testData);
                
                // Test 3: LocalStorage write (single write)
                //await TestLocalStorageWriteSingle(testDir, testData);
                
                // Test 4: LocalStorage using ReadFileAsync equivalent (if available)
                //await TestLocalStorageWriteOptimized(testDir, testData);
                
                Console.WriteLine("\n🎯 Performance test completed successfully!");
            }
            finally
            {
                // Cleanup
                try
                {
                    if (Directory.Exists(testDir))
                    {
                        Directory.Delete(testDir, true);
                        Console.WriteLine("🧹 Cleaned up test directory");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Warning: Could not clean up test directory: {ex.Message}");
                }
            }
        }
        
        private static byte[] CreateTestData(long sizeBytes)
        {
            var data = new byte[sizeBytes];
            var random = new Random(42); // Use fixed seed for reproducible results
            
            // Fill with pseudo-random data in chunks to avoid memory pressure
            const int chunkSize = 1024 * 1024; // 1MB chunks
            for (long i = 0; i < sizeBytes; i += chunkSize)
            {
                int currentChunkSize = (int)Math.Min(chunkSize, sizeBytes - i);
                var chunk = new byte[currentChunkSize];
                random.NextBytes(chunk);
                Array.Copy(chunk, 0, data, i, currentChunkSize);
                
                // Report progress for large files
                if ((i / chunkSize) % 100 == 0)
                {
                    double progressPercent = (i / (double)sizeBytes) * 100;
                    Console.WriteLine($"   Progress: {progressPercent:F1}% ({i / OneMegabyte:N0} MB)");
                }
            }
            
            return data;
        }
        
        private static async Task TestDirectFileWrite(string testDir, byte[] testData)
        {
            Console.WriteLine("\n1️⃣ Testing Direct File Write Performance");
            
            string directFilePath = Path.Combine(testDir, "direct_test_1gb.dat");
            
            // Ensure file doesn't exist
            if (File.Exists(directFilePath))
            {
                File.Delete(directFilePath);
            }
            
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                await File.WriteAllBytesAsync(directFilePath, testData);
                stopwatch.Stop();
                
                // Verify file was written
                var fileInfo = new FileInfo(directFilePath);
                long writtenBytes = fileInfo.Length;
                
                // Calculate performance metrics
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                double mbPerSecond = (writtenBytes / (double)OneMegabyte) / elapsedSeconds;
                double gbPerSecond = mbPerSecond / 1024.0;
                
                Console.WriteLine($"   ✅ Direct write completed successfully");
                Console.WriteLine($"   📊 Time: {stopwatch.Elapsed:mm\\:ss\\.fff}");
                Console.WriteLine($"   📊 Bytes written: {writtenBytes:N0} ({writtenBytes / (double)OneMegabyte:F1} MB)");
                Console.WriteLine($"   🚀 Speed: {mbPerSecond:F2} MB/s ({gbPerSecond:F3} GB/s)");
                
                // Cleanup
                File.Delete(directFilePath);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"   ❌ Direct write failed: {ex.Message}");
                throw;
            }
        }
        
        private static async Task TestLocalStorageWrite(string testDir, byte[] testData)
        {
            Console.WriteLine("\n2️⃣ Testing LocalStorage Write Performance");
            
            var localStorage = new LocalStorage();
            
            try
            {
                // Initialize LocalStorage
                await localStorage.InitializeAsync("file://", testDir);
                
                string storageFilePath = "/localstorage_test_1gb.dat";
                
                var stopwatch = Stopwatch.StartNew();
                
                // Open file for writing
                var fileHandle = await localStorage.OpenAsync(storageFilePath, 
                    OpenFlags.Create | OpenFlags.WriteOnly | OpenFlags.Truncate);
                
                try
                {
                    // Write data in chunks to use the WriteAsync method properly
                    const int chunkSize = 64 * 1024; // 64KB chunks for better performance
                    long totalBytesWritten = 0;
                    
                    for (int offset = 0; offset < testData.Length; offset += chunkSize)
                    {
                        int currentChunkSize = Math.Min(chunkSize, testData.Length - offset);
                        
                        // Allocate unmanaged memory for this chunk
                        IntPtr unmanagedBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(currentChunkSize);
                        try
                        {
                            // Copy chunk to unmanaged memory
                            System.Runtime.InteropServices.Marshal.Copy(testData, offset, unmanagedBuffer, currentChunkSize);
                            
                            // Write chunk
                            await localStorage.WriteAsync(storageFilePath, fileHandle, totalBytesWritten, currentChunkSize, unmanagedBuffer);
                            totalBytesWritten += currentChunkSize;
                        }
                        finally
                        {
                            System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedBuffer);
                        }
                        
                        // Report progress for large writes
                        if (offset % (256 * 1024 * 1024) == 0) // Every 256MB
                        {
                            double progressPercent = (totalBytesWritten / (double)testData.Length) * 100;
                            Console.WriteLine($"   Progress: {progressPercent:F1}% ({totalBytesWritten / OneMegabyte:N0} MB)");
                        }
                    }
                    
                    stopwatch.Stop();
                    
                    // Verify file was written
                    string realFilePath = Path.Combine(testDir, "localstorage_test_1gb.dat");
                    var fileInfo = new FileInfo(realFilePath);
                    long writtenBytes = fileInfo.Length;
                    
                    // Calculate performance metrics
                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    double mbPerSecond = (writtenBytes / (double)OneMegabyte) / elapsedSeconds;
                    double gbPerSecond = mbPerSecond / 1024.0;
                    
                    Console.WriteLine($"   ✅ LocalStorage write completed successfully");
                    Console.WriteLine($"   📊 Time: {stopwatch.Elapsed:mm\\:ss\\.fff}");
                    Console.WriteLine($"   📊 Bytes written: {writtenBytes:N0} ({writtenBytes / (double)OneMegabyte:F1} MB)");
                    Console.WriteLine($"   🚀 Speed: {mbPerSecond:F2} MB/s ({gbPerSecond:F3} GB/s)");
                    
                    // Verify data integrity
                    Assert.AreEqual(testData.Length, writtenBytes, "Written file size should match test data size");
                }
                finally
                {
                    await localStorage.CloseAsync(storageFilePath, fileHandle);
                }
                
                // Cleanup
                await localStorage.DeleteAsync(storageFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ LocalStorage write failed: {ex.Message}");
                throw;
            }
            finally
            {
                localStorage.Dispose();
            }
        }
        
        private static async Task TestLocalStorageWriteSingle(string testDir, byte[] testData)
        {
            Console.WriteLine("\n3️⃣ Testing LocalStorage Write Performance (WriteAllBytesAsync)");
            
            var localStorage = new LocalStorage();
            
            try
            {
                // Initialize LocalStorage
                await localStorage.InitializeAsync("file://", testDir);
                
                string storageFilePath = "/localstorage_single_test_1gb.dat";
                
                var stopwatch = Stopwatch.StartNew();
                
                // Use WriteAllBytesAsync equivalent - but LocalStorage might not have this
                // Let's try to write the whole file at once using a single WriteAsync call
                var fileHandle = await localStorage.OpenAsync(storageFilePath, 
                    OpenFlags.Create | OpenFlags.WriteOnly | OpenFlags.Truncate);
                
                try
                {
                    // Allocate unmanaged memory for the entire file
                    IntPtr unmanagedBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(testData.Length);
                    try
                    {
                        // Copy entire data to unmanaged memory
                        System.Runtime.InteropServices.Marshal.Copy(testData, 0, unmanagedBuffer, testData.Length);
                        
                        // Write entire file in one go
                        await localStorage.WriteAsync(storageFilePath, fileHandle, 0, testData.Length, unmanagedBuffer);
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedBuffer);
                    }
                    
                    stopwatch.Stop();
                    
                    // Verify file was written
                    string realFilePath = Path.Combine(testDir, "localstorage_single_test_1gb.dat");
                    var fileInfo = new FileInfo(realFilePath);
                    long writtenBytes = fileInfo.Length;
                    
                    // Calculate performance metrics
                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    double mbPerSecond = (writtenBytes / (double)OneMegabyte) / elapsedSeconds;
                    double gbPerSecond = mbPerSecond / 1024.0;
                    
                    Console.WriteLine($"   ✅ LocalStorage single write completed successfully");
                    Console.WriteLine($"   📊 Time: {stopwatch.Elapsed:mm\\:ss\\.fff}");
                    Console.WriteLine($"   📊 Bytes written: {writtenBytes:N0} ({writtenBytes / (double)OneMegabyte:F1} MB)");
                    Console.WriteLine($"   🚀 Speed: {mbPerSecond:F2} MB/s ({gbPerSecond:F3} GB/s)");
                    
                    // Verify data integrity
                    Assert.AreEqual(testData.Length, writtenBytes, "Written file size should match test data size");
                }
                finally
                {
                    await localStorage.CloseAsync(storageFilePath, fileHandle);
                }
                
                // Cleanup
                await localStorage.DeleteAsync(storageFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ LocalStorage single write failed: {ex.Message}");
                throw;
            }
            finally
            {
                localStorage.Dispose();
            }
        }
        
        private static async Task TestLocalStorageWriteOptimized(string testDir, byte[] testData)
        {
            Console.WriteLine("\n4️⃣ Testing LocalStorage Write Performance (Larger Chunks)");
            
            var localStorage = new LocalStorage();
            
            try
            {
                // Initialize LocalStorage
                await localStorage.InitializeAsync("file://", testDir);
                
                string storageFilePath = "/localstorage_optimized_test_1gb.dat";
                
                var stopwatch = Stopwatch.StartNew();
                
                // Open file for writing
                var fileHandle = await localStorage.OpenAsync(storageFilePath, 
                    OpenFlags.Create | OpenFlags.WriteOnly | OpenFlags.Truncate);
                
                try
                {
                    // Write data in larger chunks to reduce overhead
                    const int chunkSize = 256 * 1024 * 1024; // 256MB chunks for minimal overhead
                    long totalBytesWritten = 0;
                    
                    for (int offset = 0; offset < testData.Length; offset += chunkSize)
                    {
                        int currentChunkSize = Math.Min(chunkSize, testData.Length - offset);
                        
                        // Allocate unmanaged memory for this chunk
                        IntPtr unmanagedBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(currentChunkSize);
                        try
                        {
                            // Copy chunk to unmanaged memory
                            System.Runtime.InteropServices.Marshal.Copy(testData, offset, unmanagedBuffer, currentChunkSize);
                            
                            // Write chunk
                            await localStorage.WriteAsync(storageFilePath, fileHandle, totalBytesWritten, currentChunkSize, unmanagedBuffer);
                            totalBytesWritten += currentChunkSize;
                        }
                        finally
                        {
                            System.Runtime.InteropServices.Marshal.FreeHGlobal(unmanagedBuffer);
                        }
                        
                        // Report progress
                        double progressPercent = (totalBytesWritten / (double)testData.Length) * 100;
                        Console.WriteLine($"   Progress: {progressPercent:F1}% ({totalBytesWritten / OneMegabyte:N0} MB)");
                    }
                    
                    stopwatch.Stop();
                    
                    // Verify file was written
                    string realFilePath = Path.Combine(testDir, "localstorage_optimized_test_1gb.dat");
                    var fileInfo = new FileInfo(realFilePath);
                    long writtenBytes = fileInfo.Length;
                    
                    // Calculate performance metrics
                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    double mbPerSecond = (writtenBytes / (double)OneMegabyte) / elapsedSeconds;
                    double gbPerSecond = mbPerSecond / 1024.0;
                    
                    Console.WriteLine($"   ✅ LocalStorage optimized write completed successfully");
                    Console.WriteLine($"   📊 Time: {stopwatch.Elapsed:mm\\:ss\\.fff}");
                    Console.WriteLine($"   📊 Bytes written: {writtenBytes:N0} ({writtenBytes / (double)OneMegabyte:F1} MB)");
                    Console.WriteLine($"   🚀 Speed: {mbPerSecond:F2} MB/s ({gbPerSecond:F3} GB/s)");
                    
                    // Verify data integrity
                    Assert.AreEqual(testData.Length, writtenBytes, "Written file size should match test data size");
                }
                finally
                {
                    await localStorage.CloseAsync(storageFilePath, fileHandle);
                }
                
                // Cleanup
                await localStorage.DeleteAsync(storageFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ LocalStorage optimized write failed: {ex.Message}");
                throw;
            }
            finally
            {
                localStorage.Dispose();
            }
        }
    }
} 