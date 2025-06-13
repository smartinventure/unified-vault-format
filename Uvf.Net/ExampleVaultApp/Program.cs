using ExampleVaultApp;

namespace ExampleVaultApp
{
    public class Program
    {
        // Same paths as the old Program.cs for consistency
        private const string SourceFolderPath = @"D:\temp\uvf\EncryptionTestSource";
        private const string VaultFolderPath = @"D:\temp\uvf\EncryptionTestVault";
        private const string DecryptedFolderPath = @"D:\temp\uvf\EncryptionTestDecrypted";
        private const string Password = "your-super-secret-password";

        public static async Task Main(string[] args)
        {
            Console.WriteLine("ExampleVaultApp - Testing UvfLib.Storage Implementation");
            
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: ExampleVaultApp <command> [--cryptomator]");
                Console.WriteLine();
                Console.WriteLine("Commands:");
                Console.WriteLine("  testrun       : Full encrypt + decrypt + verify cycle using UvfLib.Storage");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --cryptomator : Use Cryptomator V8 format (default: UVF)");
                return;
            }

            string command = args[0].ToLowerInvariant();
            bool useCryptomator = args.Contains("--cryptomator");

            if (command == "testrun")
            {
                if (useCryptomator)
                {
                    Console.WriteLine("🔧 Running Cryptomator V8 test using UvfLib.Storage...");
                    var cryptomatorTest = new DirectCryptomatorTest(
                        SourceFolderPath, 
                        VaultFolderPath, 
                        DecryptedFolderPath, 
                        Password);
                    
                    await cryptomatorTest.RunTestAsync();
                }
                else
                {
                    Console.WriteLine("🔧 UVF format test not implemented yet - use --cryptomator for now");
                    Console.WriteLine("   (Will be implemented after Cryptomator test is working)");
                }
            }
            else
            {
                Console.WriteLine($"Unknown command: {command}");
            }
        }
    }
}
