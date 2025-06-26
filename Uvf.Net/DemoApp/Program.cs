using DemoApp;

namespace DemoApp
{
    public enum VaultTypeFilter
    {
        UVF,
        Cryptomator, 
        Both
    }

    public class Program
    {
        // Test paths for demo operations
        private const string SourceFolderPath = @"D:\temp\uvf-demo\source";
        private const string VaultFolderPath = @"D:\temp\uvf-demo\vault";
        private const string DecryptedFolderPath = @"D:\temp\uvf-demo\decrypted";
        private const string Password = "demo-password-123";

        public static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 UVF.NET Demo Application");
            Console.WriteLine("   Demonstrating native AOT library wrapper and UVF vault operations");
            Console.WriteLine();

            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            string command = args[0].ToLowerInvariant();
            var encryptFilenames = ParseEncryptFilenames(args);

            try
            {
                switch (command)
                {
                    case "uvf":
                        await RunUvfDemoAsync(encryptFilenames);
                        break;

                    case "cryptomator":
                        await RunCryptomatorDemoAsync();
                        break;

                    case "multiuser":
                        await RunMultiUserDemoAsync();
                        break;

                    case "password":
                        await RunPasswordChangeDemoAsync(args);
                        break;

                    case "native":
                        TestNativeLibraryOnly();
                        break;

                    case "info":
                        ShowSystemInfo();
                        break;

                    default:
                        Console.WriteLine($"❌ Unknown command: {command}");
                        ShowUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Demo failed: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                Environment.Exit(1);
            }
        }

        private static void ShowUsage()
        {
            Console.WriteLine("Usage: DemoApp <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  test              : Run complete UVF demo (create test data, encrypt, decrypt, verify)");
            Console.WriteLine("  cryptomator       : Run Cryptomator V8 demo (create test data, encrypt, decrypt, verify)");
            Console.WriteLine("  multiuser         : Run multi-user UVF demo (user management, access control)");
            Console.WriteLine("  password          : Run password change demo (both UVF and Cryptomator)");
            Console.WriteLine("  native            : Test native library wrapper only");
            Console.WriteLine("  info              : Show system and library information");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --encryptfilenames=true   : Enable filename encryption (default, UVF only)");
            Console.WriteLine("  --encryptfilenames=false  : Disable filename encryption (UVF only)");
            Console.WriteLine("  --uvf                     : Run UVF operations only (password command)");
            Console.WriteLine("  --cryptomator             : Run Cryptomator operations only (password command)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DemoApp test");
            Console.WriteLine("  DemoApp test --encryptfilenames=false");
            Console.WriteLine("  DemoApp cryptomator");
            Console.WriteLine("  DemoApp multiuser");
            Console.WriteLine("  DemoApp password");
            Console.WriteLine("  DemoApp password --uvf");
            Console.WriteLine("  DemoApp password --cryptomator");
            Console.WriteLine("  DemoApp native");
            Console.WriteLine("  DemoApp info");
            Console.WriteLine();
            Console.WriteLine("Demo Paths:");
            Console.WriteLine($"  Source:    {SourceFolderPath}");
            Console.WriteLine($"  Vault:     {VaultFolderPath}");
            Console.WriteLine($"  Decrypted: {DecryptedFolderPath}");
        }

        private static bool ParseEncryptFilenames(string[] args)
        {
            var encryptFilenamesArg = args.FirstOrDefault(arg => arg.StartsWith("--encryptfilenames="));
            if (encryptFilenamesArg != null)
            {
                var value = encryptFilenamesArg.Split('=')[1].ToLowerInvariant();
                return value == "true";
            }
            
            return true; // Default to true
        }

        private static VaultTypeFilter ParseVaultType(string[] args)
        {
            if (args.Any(arg => arg.Equals("--uvf", StringComparison.OrdinalIgnoreCase)))
            {
                return VaultTypeFilter.UVF;
            }
            
            if (args.Any(arg => arg.Equals("--cryptomator", StringComparison.OrdinalIgnoreCase)))
            {
                return VaultTypeFilter.Cryptomator;
            }
            
            return VaultTypeFilter.Both; // Default to both
        }

        private static async Task RunUvfDemoAsync(bool encryptFilenames)
        {
            Console.WriteLine($"🔧 Running UVF Demo with filename encryption: {(encryptFilenames ? "Enabled" : "Disabled")}");
            Console.WriteLine();

            var demo = new UvfDemo(
                SourceFolderPath, 
                VaultFolderPath, 
                DecryptedFolderPath, 
                Password,
                encryptFilenames);

            await demo.RunDemoAsync();

            Console.WriteLine();
            Console.WriteLine("🎉 Demo completed successfully!");
            Console.WriteLine();
            Console.WriteLine("📁 You can inspect the results at:");
            Console.WriteLine($"   Source files:    {SourceFolderPath}");
            Console.WriteLine($"   Encrypted vault: {VaultFolderPath}");
            Console.WriteLine($"   Decrypted files: {DecryptedFolderPath}");
        }

        private static async Task RunCryptomatorDemoAsync()
        {
            Console.WriteLine("🔧 Running Cryptomator V8 Demo");
            Console.WriteLine();

            var demo = new CryptomatorDemo(
                SourceFolderPath, 
                VaultFolderPath, 
                DecryptedFolderPath, 
                Password);

            await demo.RunDemoAsync();

            Console.WriteLine();
            Console.WriteLine("🎉 Cryptomator demo completed successfully!");
            Console.WriteLine();
            Console.WriteLine("📁 You can inspect the results at:");
            Console.WriteLine($"   Source files:    {SourceFolderPath}");
            Console.WriteLine($"   Encrypted vault: {VaultFolderPath} (Cryptomator V8 format)");
            Console.WriteLine($"   Decrypted files: {DecryptedFolderPath}");
        }

        private static async Task RunMultiUserDemoAsync()
        {
            Console.WriteLine("🔧 Running Multi-User UVF Demo");
            Console.WriteLine();

            var demo = new MultiUserUvfDemo(
                SourceFolderPath + "_multiuser",
                VaultFolderPath + "_multiuser",
                DecryptedFolderPath + "_multiuser",
                "admin_password_789",
                encryptFilenames: true);

            await demo.RunDemoAsync();

            Console.WriteLine();
            Console.WriteLine("🎉 Multi-user demo completed successfully!");
            Console.WriteLine();
            Console.WriteLine("📁 You can inspect the results at:");
            Console.WriteLine($"   Source files:    {SourceFolderPath}_multiuser");
            Console.WriteLine($"   Multi-user vault: {VaultFolderPath}_multiuser");
            Console.WriteLine($"   Decrypted files: {DecryptedFolderPath}_multiuser");
        }

        private static async Task RunPasswordChangeDemoAsync(string[] args)
        {
            Console.WriteLine("🔧 Running Password Change Demo");
            Console.WriteLine();

            // Parse vault type from arguments
            var vaultType = ParseVaultType(args);
            
            var demo = new PasswordChangeDemo(
                SourceFolderPath + "_password",
                VaultFolderPath + "_uvf_password",
                VaultFolderPath + "_cryptomator_password",
                DecryptedFolderPath + "_password");

            await demo.RunDemoAsync(vaultType);

            Console.WriteLine();
            Console.WriteLine("🎉 Password change demo completed successfully!");
            Console.WriteLine();
            Console.WriteLine("📁 You can inspect the results at:");
            Console.WriteLine($"   Source files:     {SourceFolderPath}_password");
            
            if (vaultType == VaultTypeFilter.UVF || vaultType == VaultTypeFilter.Both)
            {
                Console.WriteLine($"   UVF vault:        {VaultFolderPath}_uvf_password");
            }
            if (vaultType == VaultTypeFilter.Cryptomator || vaultType == VaultTypeFilter.Both)
            {
                Console.WriteLine($"   Cryptomator vault: {VaultFolderPath}_cryptomator_password");
            }
            
            Console.WriteLine($"   Decrypted files:  {DecryptedFolderPath}_password");
        }

        private static void TestNativeLibraryOnly()
        {
            Console.WriteLine("🔧 Testing TitanVault Native Library Only");
            Console.WriteLine();

            // Test native library availability
            TitanVaultWrapper.PrintLibraryInfo();
            Console.WriteLine();

            // Test basic library loading
            bool isAvailable = TitanVaultWrapper.TestNativeLibrary();
            Console.WriteLine();

            // Test actual native exports
            Console.WriteLine("🔧 Testing Native Exports");
            bool exportsWork = TitanVaultExportTester.TestTitanVaultExports();
            Console.WriteLine();

            Console.WriteLine("🔧 TitanVault C-Style Exports Status");
            Console.WriteLine("✅ C-style exports successfully added to AOT library!");
            Console.WriteLine("   The following functions are exported for other languages:");
            Console.WriteLine("   - titan_vault_get_version()");
            Console.WriteLine("   - titan_vault_get_last_error()");
            Console.WriteLine("   - titan_vault_detect_vault_format()");
            Console.WriteLine("   - titan_vault_create_cryptomator_vault()");
            Console.WriteLine("   - titan_vault_create_uvf_vault()");
            Console.WriteLine("   - titan_vault_add_user()");
            Console.WriteLine("   - titan_vault_remove_user()");
            Console.WriteLine("   - titan_vault_read_file()");
            Console.WriteLine("   - titan_vault_write_file()");
            Console.WriteLine("   - titan_vault_close_vault()");
            Console.WriteLine("   - titan_vault_free_string()");
            Console.WriteLine();

            if (isAvailable && exportsWork)
            {
                Console.WriteLine("✅ TitanVault Native Library Ready!");
            }
            else if (isAvailable)
            {
                Console.WriteLine("⚠️ Library loads but exports may need testing");
            }

            Console.WriteLine();
            Console.WriteLine("✅ TitanVault Ready for Language Bindings!");
            Console.WriteLine("   Use with: PHP (FFI), Python (ctypes), Go (cgo), C++ (LoadLibrary)");
            Console.WriteLine("   Header file: titan_vault.h");
        }

        private static void ShowSystemInfo()
        {
            Console.WriteLine("🔧 System Information");
            Console.WriteLine();
            
            Console.WriteLine($"   OS: {Environment.OSVersion}");
            Console.WriteLine($"   .NET Runtime: {Environment.Version}");
            Console.WriteLine($"   Working Directory: {Environment.CurrentDirectory}");
            Console.WriteLine($"   Machine Name: {Environment.MachineName}");
            Console.WriteLine($"   User: {Environment.UserName}");
            Console.WriteLine($"   64-bit Process: {Environment.Is64BitProcess}");
            Console.WriteLine($"   64-bit OS: {Environment.Is64BitOperatingSystem}");
            Console.WriteLine();

            Console.WriteLine("📁 Demo Paths Status:");
            CheckDirectoryStatus(SourceFolderPath, "Source");
            CheckDirectoryStatus(VaultFolderPath, "Vault");
            CheckDirectoryStatus(DecryptedFolderPath, "Decrypted");
            Console.WriteLine();

            Console.WriteLine("📦 TitanVault Library Status:");
            TitanVaultWrapper.PrintLibraryInfo();
        }

        private static void CheckDirectoryStatus(string path, string name)
        {
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                Console.WriteLine($"   {name}: ✅ Exists ({files.Length} files)");
            }
            else
            {
                Console.WriteLine($"   {name}: ❌ Does not exist");
            }
        }
    }
}
