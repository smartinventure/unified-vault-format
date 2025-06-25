using System.Runtime.InteropServices;

namespace ExampleVaultApp
{
    /// <summary>
    /// Tests TitanVault native exports using GetProcAddress for dynamic loading
    /// This approach works better with AOT-compiled .NET libraries
    /// </summary>
    public static class TitanVaultExportTester
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        // Function pointer delegates for our exports
        private delegate IntPtr GetVersionDelegate();
        private delegate IntPtr GetLastErrorDelegate();
        private unsafe delegate int DetectVaultFormatDelegate(byte* pathPtr, int pathLength);
        private unsafe delegate void FreeStringDelegate(IntPtr ptr);

        /// <summary>
        /// Test the TitanVault native exports using dynamic loading
        /// </summary>
        public static bool TestTitanVaultExports()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string libraryPath = Path.Combine(baseDir, "TitanVault.dll");

            if (!File.Exists(libraryPath))
            {
                Console.WriteLine($"❌ Library not found: {libraryPath}");
                return false;
            }

            Console.WriteLine($"🔍 Testing TitanVault native exports in: {libraryPath}");

            IntPtr hModule = LoadLibrary(libraryPath);
            if (hModule == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Console.WriteLine($"❌ Failed to load library. Error: {error}");
                return false;
            }

            try
            {
                Console.WriteLine("✅ Library loaded successfully");

                // Test titan_vault_get_version export
                IntPtr getVersionPtr = GetProcAddress(hModule, "titan_vault_get_version");
                if (getVersionPtr != IntPtr.Zero)
                {
                    Console.WriteLine("✅ Found export: titan_vault_get_version");
                    
                    try
                    {
                        var getVersion = Marshal.GetDelegateForFunctionPointer<GetVersionDelegate>(getVersionPtr);
                        IntPtr versionPtr = getVersion();
                        
                        if (versionPtr != IntPtr.Zero)
                        {
                            string version = Marshal.PtrToStringUTF8(versionPtr) ?? "Unknown";
                            Console.WriteLine($"✅ Version: {version}");
                            
                            // Free the memory allocated by the native function
                            IntPtr freeStringPtr = GetProcAddress(hModule, "titan_vault_free_string");
                            if (freeStringPtr != IntPtr.Zero)
                            {
                                var freeString = Marshal.GetDelegateForFunctionPointer<FreeStringDelegate>(freeStringPtr);
                                freeString(versionPtr);
                                Console.WriteLine("✅ Memory freed successfully");
                            }
                        }
                        else
                        {
                            Console.WriteLine("❌ titan_vault_get_version returned null");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error calling titan_vault_get_version: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("❌ Export not found: titan_vault_get_version");
                }

                // Test titan_vault_get_last_error export
                IntPtr getLastErrorPtr = GetProcAddress(hModule, "titan_vault_get_last_error");
                if (getLastErrorPtr != IntPtr.Zero)
                {
                    Console.WriteLine("✅ Found export: titan_vault_get_last_error");
                    
                    try
                    {
                        var getLastError = Marshal.GetDelegateForFunctionPointer<GetLastErrorDelegate>(getLastErrorPtr);
                        IntPtr errorPtr = getLastError();
                        
                        if (errorPtr != IntPtr.Zero)
                        {
                            string error = Marshal.PtrToStringUTF8(errorPtr) ?? "No error";
                            Console.WriteLine($"✅ Last error: {error}");
                            
                            // Free the memory
                            IntPtr freeStringPtr = GetProcAddress(hModule, "titan_vault_free_string");
                            if (freeStringPtr != IntPtr.Zero)
                            {
                                var freeString = Marshal.GetDelegateForFunctionPointer<FreeStringDelegate>(freeStringPtr);
                                freeString(errorPtr);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error calling titan_vault_get_last_error: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("❌ Export not found: titan_vault_get_last_error");
                }

                // Test titan_vault_detect_vault_format export
                IntPtr detectFormatPtr = GetProcAddress(hModule, "titan_vault_detect_vault_format");
                if (detectFormatPtr != IntPtr.Zero)
                {
                    Console.WriteLine("✅ Found export: titan_vault_detect_vault_format");
                    
                    try
                    {
                        var detectFormat = Marshal.GetDelegateForFunctionPointer<DetectVaultFormatDelegate>(detectFormatPtr);
                        
                        // Test with current directory (should fail but not crash)
                        string testPath = Environment.CurrentDirectory;
                        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(testPath);
                        
                        unsafe
                        {
                            fixed (byte* pathPtr = pathBytes)
                            {
                                int result = detectFormat(pathPtr, pathBytes.Length);
                                Console.WriteLine($"✅ Vault format detection test for '{testPath}': {result}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error calling titan_vault_detect_vault_format: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("❌ Export not found: titan_vault_detect_vault_format");
                }

                return true;
            }
            finally
            {
                FreeLibrary(hModule);
                Console.WriteLine("✅ Library unloaded");
            }
        }
    }
} 