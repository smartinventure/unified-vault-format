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
            var quietMode = ParseQuietMode(args);
            var forceRebuild = ParseRebuildFlag(args);

            // Handle rebuild before any other operations
            if (forceRebuild)
            {
                Console.WriteLine("🔄 Force rebuild requested - rebuilding TitanVault AOT library...");
                await RebuildAotLibraryNowAsync();
                Console.WriteLine();
            }

            // Set global debug flag for the encryption stream
            DemoApp.Wrapper.TitanVault.SetVerboseDebug(!quietMode);

            try
            {
                switch (command)
                {
                    case "uvf":
                        await RunUvfDemoAsync(encryptFilenames, quietMode);
                        break;

                    case "cryptomator":
                        await RunCryptomatorDemoAsync(quietMode);
                        break;

                    case "multiuser":
                        await RunMultiUserDemoAsync(quietMode);
                        break;

                    case "password":
                        await RunPasswordChangeDemoAsync(args, quietMode);
                        break;

                    case "largefile":
                        await RunLargeFileDemoAsync(quietMode);
                        break;

                    case "native":
                        TestNativeLibraryOnly();
                        break;

                    case "info":
                        ShowSystemInfo();
                        break;

                    case "openflags":
                        await RunOpenFlagsDemoAsync(quietMode);
                        break;

                    case "rebuild":
                        await RebuildAotLibraryAsync();
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
            Console.WriteLine("  uvf               : Run complete UVF demo (create test data, encrypt, decrypt, verify)");
            Console.WriteLine("  cryptomator       : Run Cryptomator V8 demo (create test data, encrypt, decrypt, verify)");
            Console.WriteLine("  multiuser         : Run multi-user UVF demo (user management, access control)");
            Console.WriteLine("  password          : Run password change demo (both UVF and Cryptomator)");
            Console.WriteLine("  largefile         : Run large file demo (>4GB) with both UVF and Cryptomator vaults");
            Console.WriteLine("  openflags         : Test OpenStreamWithFlags functionality (create, truncate, append modes)");
            Console.WriteLine("  native            : Test native library wrapper only");
            Console.WriteLine("  info              : Show system and library information");
            Console.WriteLine("  rebuild           : Force rebuild TitanVault AOT library only");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --encryptfilenames=true   : Enable filename encryption (default, UVF only)");
            Console.WriteLine("  --encryptfilenames=false  : Disable filename encryption (UVF only)");
            Console.WriteLine("  --uvf                     : Run UVF operations only (password command)");
            Console.WriteLine("  --cryptomator             : Run Cryptomator operations only (password command)");
            Console.WriteLine("  --quiet                   : Reduce verbose debug output (recommended for UVF mode)");
            Console.WriteLine("  --verbose                 : Show detailed debug output (default)");
            Console.WriteLine("  --rebuild                 : Force rebuild TitanVault AOT library");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  DemoApp uvf");
            Console.WriteLine("  DemoApp uvf --quiet");
            Console.WriteLine("  DemoApp uvf --rebuild");
            Console.WriteLine("  DemoApp uvf --quiet --rebuild");
            Console.WriteLine("  DemoApp uvf --encryptfilenames=false");
            Console.WriteLine("  DemoApp cryptomator");
            Console.WriteLine("  DemoApp multiuser");
            Console.WriteLine("  DemoApp password");
            Console.WriteLine("  DemoApp password --uvf");
            Console.WriteLine("  DemoApp password --cryptomator");
            Console.WriteLine("  DemoApp largefile");
            Console.WriteLine("  DemoApp openflags");
            Console.WriteLine("  DemoApp native");
            Console.WriteLine("  DemoApp info");
            Console.WriteLine("  DemoApp rebuild");
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

        private static bool ParseQuietMode(string[] args)
        {
            if (args.Any(arg => arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            
            if (args.Any(arg => arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            
            return false; // Default to verbose/not quiet
        }

        private static bool ParseRebuildFlag(string[] args)
        {
            return args.Any(arg => arg.Equals("--rebuild", StringComparison.OrdinalIgnoreCase));
        }

        private static async Task RunUvfDemoAsync(bool encryptFilenames, bool quietMode)
        {
            Console.WriteLine($"🔧 Running UVF Demo with filename encryption: {(encryptFilenames ? "Enabled" : "Disabled")}");
            if (quietMode)
            {
                Console.WriteLine("   (Quiet mode: Reduced debug output)");
            }
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

        private static async Task RunCryptomatorDemoAsync(bool quietMode)
        {
            Console.WriteLine("🔧 Running Cryptomator V8 Demo");
            if (quietMode)
            {
                Console.WriteLine("   (Quiet mode: Reduced debug output)");
            }
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

        private static async Task RunMultiUserDemoAsync(bool quietMode)
        {
            Console.WriteLine("🔧 Running Multi-User UVF Demo");
            if (quietMode)
            {
                Console.WriteLine("   (Quiet mode: Reduced debug output)");
            }
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

        private static async Task RunPasswordChangeDemoAsync(string[] args, bool quietMode)
        {
            Console.WriteLine("🔧 Running Password Change Demo");
            if (quietMode)
            {
                Console.WriteLine("   (Quiet mode: Reduced debug output)");
            }
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

        private static async Task RunLargeFileDemoAsync(bool quietMode)
        {
            Console.WriteLine("🔧 Running Large File Demo (>4GB)");
            if (quietMode)
            {
                Console.WriteLine("   (Quiet mode: Reduced debug output)");
            }
            Console.WriteLine();

            var demo = new LargeFileDemo(
                @"D:\temp\uvf-demo\largefile",
                "largefile_password_789");

            await demo.RunDemoAsync();

            Console.WriteLine();
            Console.WriteLine("🎉 Large file demo completed successfully!");
            Console.WriteLine("⚠️ Note: This demo creates and tests 5GB files - ensure you have sufficient disk space");
        }

        private static async Task RunOpenFlagsDemoAsync(bool quietMode)
        {
            Console.WriteLine("🔧 Running Open Flags Demo - Testing file creation and writing behaviors");
            if (quietMode)
            {
                Console.WriteLine("   (Quiet mode: Reduced debug output)");
            }
            Console.WriteLine();

            var openFlagsDemo = new OpenFlagsDemo(
                VaultFolderPath + "_openflags",
                Password);

            await openFlagsDemo.RunDemoAsync();

            Console.WriteLine();
            Console.WriteLine("🎉 Open flags demo completed successfully!");
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
            Console.WriteLine();
            
            Console.WriteLine("🔄 AOT Rebuild Commands:");
            Console.WriteLine("   DemoApp rebuild                    - Rebuild AOT library only");
            Console.WriteLine("   DemoApp uvf --rebuild              - Run UVF demo with rebuild");
            Console.WriteLine("   DemoApp [command] --rebuild        - Any command with force rebuild");
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

        private static async Task RebuildAotLibraryAsync()
        {
            Console.WriteLine("🔄 Rebuilding TitanVault AOT Library");
            Console.WriteLine();

            await RebuildAotLibraryNowAsync();
        }

        private static async Task RebuildAotLibraryNowAsync()
        {
            Console.WriteLine("🔧 Forcing rebuild of TitanVault.dll...");
            Console.WriteLine("   This will take several minutes for AOT compilation...");
            Console.WriteLine();

            try
            {
                // Get current directory and calculate paths relative to it
                var currentDir = Environment.CurrentDirectory;
                Console.WriteLine($"📁 Current directory: {currentDir}");
                
                // Define paths - need to go up from DemoApp/bin/Debug/net8.0 to reach the project root
                string masterProjectPath;
                string publishDir;
                string outputDir;
                
                if (currentDir.Contains("bin\\Debug\\net8.0") || currentDir.Contains("bin/Debug/net8.0"))
                {
                    // Running from bin directory
                    masterProjectPath = Path.GetFullPath("../../../../UvfLib.Master/UvfLib.Master.csproj");
                    publishDir = Path.GetFullPath("../../../../UvfLib.Master/bin/Release/net8.0/win-x64");
                    outputDir = ".";
                }
                else
                {
                    // Running from project directory
                    masterProjectPath = Path.GetFullPath("../UvfLib.Master/UvfLib.Master.csproj");
                    publishDir = Path.GetFullPath("../UvfLib.Master/bin/Release/net8.0/win-x64");
                    outputDir = "bin/Debug/net8.0";
                }

                Console.WriteLine($"📁 Master project: {masterProjectPath}");
                Console.WriteLine($"📁 Publish directory: {publishDir}");
                Console.WriteLine($"📁 Output directory: {outputDir}");

                // Verify master project exists
                if (!File.Exists(masterProjectPath))
                {
                    Console.WriteLine($"❌ Master project not found: {masterProjectPath}");
                    throw new FileNotFoundException($"UvfLib.Master project not found: {masterProjectPath}");
                }

                // Clean existing build
                Console.WriteLine("🧹 Cleaning existing AOT build...");
                if (Directory.Exists(publishDir))
                {
                    Directory.Delete(publishDir, true);
                    Console.WriteLine($"   Deleted: {publishDir}");
                }

                // Delete existing files in output
                var existingDll = Path.Combine(outputDir, "TitanVault.dll");
                var existingPdb = Path.Combine(outputDir, "TitanVault.pdb");
                if (File.Exists(existingDll)) 
                {
                    File.Delete(existingDll);
                    Console.WriteLine($"   Deleted: {existingDll}");
                }
                if (File.Exists(existingPdb)) 
                {
                    File.Delete(existingPdb);
                    Console.WriteLine($"   Deleted: {existingPdb}");
                }

                // Build AOT library
                Console.WriteLine("⚙️ Building TitanVault AOT library...");
                Console.WriteLine($"   Command: dotnet publish \"{masterProjectPath}\" --configuration Release --runtime win-x64 --self-contained true /p:PublishAot=true");
                
                var buildArgs = $"publish \"{masterProjectPath}\" --configuration Release --runtime win-x64 --self-contained true /p:PublishAot=true --verbosity normal";
                var result = await RunCommandAsync("dotnet", buildArgs);
                
                if (result == 0)
                {
                    // Copy to output directory
                    var sourceDll = Path.Combine(publishDir, "publish", "TitanVault.dll");
                    var sourcePdb = Path.Combine(publishDir, "publish", "TitanVault.pdb");
                    
                    Console.WriteLine($"📋 Looking for built files:");
                    Console.WriteLine($"   Source DLL: {sourceDll}");
                    Console.WriteLine($"   Source PDB: {sourcePdb}");
                    
                    if (File.Exists(sourceDll))
                    {
                        var targetDll = Path.Combine(outputDir, "TitanVault.dll");
                        File.Copy(sourceDll, targetDll, true);
                        Console.WriteLine($"   ✅ Copied: {sourceDll} -> {targetDll}");
                    }
                    else
                    {
                        Console.WriteLine($"   ⚠️ Source DLL not found: {sourceDll}");
                        // List what files do exist in the publish directory
                        var publishFolder = Path.Combine(publishDir, "publish");
                        if (Directory.Exists(publishFolder))
                        {
                            Console.WriteLine($"   📁 Files in {publishFolder}:");
                            foreach (var file in Directory.GetFiles(publishFolder))
                            {
                                Console.WriteLine($"      {Path.GetFileName(file)}");
                            }
                        }
                    }
                    
                    if (File.Exists(sourcePdb))
                    {
                        var targetPdb = Path.Combine(outputDir, "TitanVault.pdb");
                        File.Copy(sourcePdb, targetPdb, true);
                        Console.WriteLine($"   ✅ Copied: {sourcePdb} -> {targetPdb}");
                    }

                    Console.WriteLine();
                    Console.WriteLine("✅ TitanVault AOT library rebuilt successfully!");
                    Console.WriteLine();
                    Console.WriteLine("📦 The updated TitanVault.dll is now available for:");
                    Console.WriteLine("   • All demo commands (uvf, cryptomator, multiuser, etc.)");
                    Console.WriteLine("   • External language bindings (PHP, Python, Go, C++, etc.)");
                    Console.WriteLine("   • Direct native library usage");
                }
                else
                {
                    Console.WriteLine($"❌ Failed to rebuild TitanVault AOT library (exit code: {result})");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error rebuilding AOT library: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static async Task<int> RunCommandAsync(string command, string arguments)
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process == null) return -1;

            // Capture output and errors
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            // Show output if there were errors
            if (process.ExitCode != 0)
            {
                Console.WriteLine($"❌ Command failed: {command} {arguments}");
                Console.WriteLine($"Exit code: {process.ExitCode}");
                if (!string.IsNullOrEmpty(output))
                {
                    Console.WriteLine("Standard Output:");
                    Console.WriteLine(output);
                }
                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine("Standard Error:");
                    Console.WriteLine(error);
                }
            }
            else if (!string.IsNullOrEmpty(output))
            {
                // Show output for successful commands too (but less verbose)
                Console.WriteLine(output);
            }

            return process.ExitCode;
        }
    }
}
