using System.Runtime.InteropServices;
using System.Text;
using ExampleVaultApp.Wrapper;

namespace ExampleVaultApp
{
    /// <summary>
    /// Tests UVF vault functionality using C-style wrapper calls (but with managed implementation).
    /// This allows testing the same wrapper logic that the AOT exports use, but with debugging capability.
    /// </summary>
    public class SimpleUvfTestCStyle
    {
        private readonly string _vaultFolderPath;
        private readonly string _password;
        private readonly bool _encryptFilenames;

        public SimpleUvfTestCStyle(string vaultFolderPath, string password, bool encryptFilenames = true)
        {
            _vaultFolderPath = vaultFolderPath ?? throw new ArgumentNullException(nameof(vaultFolderPath));
            _password = password ?? throw new ArgumentNullException(nameof(password));
            _encryptFilenames = encryptFilenames;
        }

        public unsafe Task RunTestAsync()
        {
            Console.WriteLine("🧪 Testing UVF Vault with C-Style Wrapper (Managed Implementation)");
            Console.WriteLine($"🔒 Vault: {_vaultFolderPath}");
            Console.WriteLine($"🔐 Filename Encryption: {(_encryptFilenames ? "Enabled" : "Disabled")}");
            Console.WriteLine();

            try
            {
                // Clean up any existing vault
                if (Directory.Exists(_vaultFolderPath))
                {
                    Directory.Delete(_vaultFolderPath, true);
                }
                Directory.CreateDirectory(_vaultFolderPath);

                // Convert strings to byte arrays for C-style call
                byte[] vaultPathBytes = Encoding.UTF8.GetBytes(_vaultFolderPath);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(_password);

                // Call the managed wrapper using unsafe pointers (same as C-style)
                fixed (byte* vaultPathPtr = vaultPathBytes)
                fixed (byte* passwordPtr = passwordBytes)
                {
                    int result = TitanVaultNativeMethods.CreateUvfVault(
                        vaultPathPtr, vaultPathBytes.Length,
                        passwordPtr, passwordBytes.Length,
                        _encryptFilenames ? 1 : 0,  // encryptFilenames
                        0,                          // kdfMethod (0 = PBKDF2)
                        64000                       // kdfIterations
                    );

                    if (result != 0)
                    {
                        // Get error message using C-style call
                        IntPtr errorPtr = TitanVaultNativeMethods.GetLastError();
                        string errorMessage = errorPtr != IntPtr.Zero 
                            ? Marshal.PtrToStringAnsi(errorPtr) ?? "Unknown error"
                            : "Unknown error";
                        
                        // Free the error string
                        if (errorPtr != IntPtr.Zero)
                        {
                            TitanVaultNativeMethods.FreeString(errorPtr);
                        }
                        
                        throw new Exception($"Failed to create UVF vault. Error code: {result}, Message: {errorMessage}");
                    }
                }

                Console.WriteLine("✅ UVF vault created successfully using C-style wrapper");

                // Verify vault creation
                string vaultFile = Path.Combine(_vaultFolderPath, "vault.uvf");
                if (!File.Exists(vaultFile))
                {
                    throw new Exception("vault.uvf file was not created");
                }

                var fileInfo = new FileInfo(vaultFile);
                Console.WriteLine($"✅ Vault file created: {vaultFile} ({fileInfo.Length} bytes)");
                Console.WriteLine("✅ All C-Style wrapper tests completed successfully!");

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test failed: {ex.Message}");
                throw;
            }
        }
    }
} 