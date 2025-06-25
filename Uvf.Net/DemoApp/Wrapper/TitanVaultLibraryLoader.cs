using System.Runtime.InteropServices;
using System.Text;

namespace DemoApp.Wrapper
{
    /// <summary>
    /// Handles loading and testing of the native TitanVault library
    /// </summary>
    public static class TitanVaultLibraryLoader
    {
        private const string LibraryName = "TitanVault.dll";

        /// <summary>
        /// Static constructor to check library availability on first use
        /// </summary>
        static TitanVaultLibraryLoader()
        {
            CheckLibraryAvailability();
        }

        /// <summary>
        /// Check if the native library is available and properly sized
        /// </summary>
        private static void CheckLibraryAvailability()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string libPath = Path.Combine(baseDir, LibraryName);
            
            if (File.Exists(libPath))
            {
                var fileInfo = new FileInfo(libPath);
                
                // Check if this is likely the native AOT version (much larger than managed)
                if (fileInfo.Length > 1_000_000) // > 1MB indicates native AOT
                {
                    Console.WriteLine($"✅ Native AOT library ready: {libPath}");
                    Console.WriteLine($"📦 Size: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0 / 1024.0:F1} MB)");
                    Console.WriteLine($"📅 Modified: {fileInfo.LastWriteTime}");
                }
                else
                {
                    Console.WriteLine($"⚠️ Found managed .NET library instead of native AOT: {libPath}");
                    Console.WriteLine($"📦 Size: {fileInfo.Length:N0} bytes (managed .NET DLL - no native exports)");
                    Console.WriteLine("   This will NOT contain the titan_vault_* C-style exports!");
                    Console.WriteLine("   Make sure to reference the native AOT build output in the project file.");
                }
            }
            else
            {
                Console.WriteLine($"❌ No library found at: {libPath}");
                Console.WriteLine("   Make sure the native AOT library is referenced and copied to output.");
            }
        }

        /// <summary>
        /// Test if the native library can be loaded and basic functions work
        /// </summary>
        public static bool TestNativeLibrary()
        {
            try
            {
                Console.WriteLine("🧪 Testing native TitanVault library...");
                
                // Test 1: Get version
                var versionPtr = TitanVaultNativeMethods.GetVersion();
                if (versionPtr == IntPtr.Zero)
                {
                    Console.WriteLine("❌ Failed to get version - library may not be loaded");
                    return false;
                }
                
                var version = Marshal.PtrToStringUTF8(versionPtr);
                Console.WriteLine($"✅ Version: {version}");
                TitanVaultNativeMethods.FreeString(versionPtr);
                
                // Test 2: Get error (should return "No error" initially)
                var errorPtr = TitanVaultNativeMethods.GetLastError();
                if (errorPtr != IntPtr.Zero)
                {
                    var error = Marshal.PtrToStringUTF8(errorPtr);
                    Console.WriteLine($"✅ Error handling: {error}");
                    TitanVaultNativeMethods.FreeString(errorPtr);
                }
                
                Console.WriteLine("✅ Native library test passed!");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Native library test failed: {ex.Message}");
                Console.WriteLine("   Make sure TitanVault.dll is built with AOT and available in output directory");
                return false;
            }
        }

        /// <summary>
        /// Print detailed information about the loaded library
        /// </summary>
        public static void PrintLibraryInfo()
        {
            Console.WriteLine();
            Console.WriteLine("📋 TitanVault Native Library Information");
            Console.WriteLine("==========================================");
            
            try
            {
                // Get version
                var versionPtr = TitanVaultNativeMethods.GetVersion();
                if (versionPtr != IntPtr.Zero)
                {
                    var version = Marshal.PtrToStringUTF8(versionPtr);
                    Console.WriteLine($"Version: {version}");
                    TitanVaultNativeMethods.FreeString(versionPtr);
                }
                
                // Get file info
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string libPath = Path.Combine(baseDir, LibraryName);
                
                if (File.Exists(libPath))
                {
                    var fileInfo = new FileInfo(libPath);
                    Console.WriteLine($"File Path: {libPath}");
                    Console.WriteLine($"File Size: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)");
                    Console.WriteLine($"Modified: {fileInfo.LastWriteTime}");
                    Console.WriteLine($"Created: {fileInfo.CreationTime}");
                    
                    // Determine build type
                    string buildType = fileInfo.Length > 1_000_000 ? "Native AOT" : "Managed .NET";
                    Console.WriteLine($"Build Type: {buildType}");
                }
                
                Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
                Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
                Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting library info: {ex.Message}");
            }
            
            Console.WriteLine("==========================================");
        }
    }
} 