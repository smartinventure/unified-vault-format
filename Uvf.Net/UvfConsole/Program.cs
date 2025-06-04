using UvfLib;
using UvfLib.VaultHelpers;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UvfConsole
{
    /// <summary>
    /// Vault format selection for command line operations
    /// </summary>
    public enum VaultFormat
    {
        UVF,           // Universal Vault Format (default)
        CryptomatorV8  // Legacy Cryptomator V8 format
    }

    public class Program
    {
        // Configuration
        private const string SourceFolderPath = @"D:\temp\uvf\EncryptionTestSource";
        private const string VaultFolderPath = @"D:\temp\uvf\EncryptionTestVault";
        //private const string VaultFolderPath = @"D:\cyptomatortest\tester\tester";
        private const string DecryptedFolderPath = @"D:\temp\uvf\EncryptionTestDecrypted";
        private const string Password = "your-super-secret-password";
        private const bool OutputTreeInfo = false;

        private static Stopwatch _overallStopwatch = new Stopwatch();
        private static long _totalBytesProcessedOverall = 0;

        // Nested class for test run verification
        private class FileVerificationInfo
        {
            public string RelativePath { get; }
            public string SourceHash { get; } // Null for directories
            public long SourceSize { get; } // File size in bytes, 0 for directories
            public bool IsDirectory { get; }
            public bool ExistsInDecrypted { get; set; }
            public string DecryptedHash { get; set; } // Null for directories, or if not found/not a file
            public bool HashesMatch => !IsDirectory && SourceHash != null && DecryptedHash != null && SourceHash == DecryptedHash && !SourceHash.StartsWith("ERROR") && !DecryptedHash.StartsWith("ERROR");
            public bool TypeMismatch { get; set; } // e.g. source is file, decrypted is dir
            public bool SourceHashError => SourceHash != null && SourceHash.StartsWith("ERROR");
            public bool DecryptedHashError => DecryptedHash != null && DecryptedHash.StartsWith("ERROR");


            public FileVerificationInfo(string relativePath, bool isDirectory, string sourceHash = null, long sourceSize = 0)
            {
                RelativePath = relativePath;
                IsDirectory = isDirectory;
                SourceHash = sourceHash;
                SourceSize = sourceSize;
                ExistsInDecrypted = false;
                DecryptedHash = null;
                TypeMismatch = false;
            }
        }

        /// <summary>
        /// Gets the appropriate vault filename based on the format
        /// </summary>
        private static string GetVaultFileName(VaultFormat format)
        {
            return format switch
            {
                VaultFormat.UVF => "vault.uvf",
                VaultFormat.CryptomatorV8 => "masterkey.cryptomator",
                _ => "vault.uvf"
            };
        }

        /// <summary>
        /// Parses command line arguments to determine vault format
        /// </summary>
        private static VaultFormat ParseVaultFormat(string[] args)
        {
            if (args.Contains("--cryptomator"))
                return VaultFormat.CryptomatorV8;
            
            // Default to UVF (even if --uvf is explicitly specified or neither is specified)
            return VaultFormat.UVF;
        }

        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: UvfConsole <encrypt|decrypt|testrun> [--uvf|--cryptomator]");
                Console.WriteLine("  --uvf        : Use Universal Vault Format (default)");
                Console.WriteLine("  --cryptomator: Use Cryptomator V8 format for legacy compatibility");
                Console.WriteLine("  encrypt      : Encrypt files from source to vault");
                Console.WriteLine("  decrypt      : Decrypt files from vault to target");
                Console.WriteLine("  testrun      : Full round-trip test (encrypt then decrypt with verification)");
                return;
            }

            string mode = args[0].ToLowerInvariant();
            VaultFormat vaultFormat = ParseVaultFormat(args);
            
            Console.WriteLine($"Mode: {mode}");
            Console.WriteLine($"Vault Format: {vaultFormat}");
            
            Directory.CreateDirectory(VaultFolderPath);

            string vaultFileName = GetVaultFileName(vaultFormat);
            string vaultFilePath = Path.Combine(VaultFolderPath, vaultFileName);
            byte[] vaultFileContent;

            if (mode == "encrypt")
            {
                vaultFileContent = HandleEncryptMode(vaultFilePath, vaultFormat);
                if (vaultFileContent == null) return;
                ProcessVault(mode, vaultFileContent, vaultFormat);
            }
            else if (mode == "decrypt")
            {
                vaultFileContent = HandleDecryptMode(vaultFilePath, vaultFormat);
                if (vaultFileContent == null) return;
                ProcessVault(mode, vaultFileContent, vaultFormat);
            }
            else if (mode == "testrun")
            {
                HandleTestRunMode(vaultFilePath, vaultFormat);
            }
            else
            {
                Console.WriteLine("Invalid mode. Use 'encrypt', 'decrypt', or 'testrun'.");
                Console.WriteLine("Usage: UvfConsole <encrypt|decrypt|testrun> [--uvf|--cryptomator]");
                return;
            }
        }

        private static byte[] HandleEncryptMode(string vaultFilePath, VaultFormat vaultFormat)
        {
            if (!Directory.Exists(SourceFolderPath))
            {
                Console.Error.WriteLine($"ERROR: Source folder not found at {SourceFolderPath}. Cannot encrypt.");
                return null;
            }

            if (OutputTreeInfo)
            {
                LogDirectoryTreeStructure(SourceFolderPath, "Source Directory Structure (Pre-Encryption):");
            }

            Console.WriteLine($"Starting encryption: {SourceFolderPath} -> {VaultFolderPath}");

            if (File.Exists(vaultFilePath))
            {
                Console.WriteLine($"Vault file found at: {vaultFilePath}. Loading existing vault.");
                return File.ReadAllBytes(vaultFilePath);
            }

            Console.WriteLine($"Vault file not found. Creating new {vaultFormat} vault at: {vaultFilePath}");
            
            if (vaultFormat == VaultFormat.CryptomatorV8)
            {
                // For Cryptomator V8, create both masterkey.cryptomator and vault.cryptomator
                string vaultDirectory = Path.GetDirectoryName(vaultFilePath) ?? throw new InvalidOperationException("Invalid vault file path");
                Vault.CreateNewCryptomatorV8VaultComplete(vaultDirectory, Password);
                Console.WriteLine($"New {vaultFormat} vault files created (masterkey.cryptomator and vault.cryptomator).");
                return File.ReadAllBytes(vaultFilePath);
            }
            else
            {
                // For UVF format, create single file
                byte[] vaultFileContent = Vault.CreateNewUvfVaultFileContent(Password);
                File.WriteAllBytes(vaultFilePath, vaultFileContent);
                Console.WriteLine($"New {vaultFormat} vault file created.");
                return vaultFileContent;
            }
        }

        private static byte[] HandleDecryptMode(string vaultFilePath, VaultFormat vaultFormat)
        {
            Console.WriteLine($"Starting decryption: {VaultFolderPath} -> {DecryptedFolderPath}");
            
            if (!File.Exists(vaultFilePath))
            {
                Console.Error.WriteLine($"ERROR: Vault file not found at {vaultFilePath}. Cannot decrypt.");
                return null;
            }

            Directory.CreateDirectory(DecryptedFolderPath);

            if (OutputTreeInfo)
            {
                LogDirectoryTreeStructure(VaultFolderPath, "Vault Directory Structure (Pre-Decryption):");
            }

            return File.ReadAllBytes(vaultFilePath);
        }

        private static void HandleTestRunMode(string vaultFilePath, VaultFormat vaultFormat)
        {
            Console.WriteLine($"===== Starting Test Run (Format: {vaultFormat}) =====");

            CleanupTestDirectories();

            Console.WriteLine("\n--- Test Run: Analyzing Source Directory ---");
            var sourceItems = new Dictionary<string, FileVerificationInfo>();
            if (Directory.Exists(SourceFolderPath))
            {
                CollectAndHashSourceItems(SourceFolderPath, SourceFolderPath, sourceItems);
                Console.WriteLine($"Collected {sourceItems.Count} items (files and directories) from source.");
            }
            else
            {
                Console.WriteLine($"Source folder {SourceFolderPath} not found. Test run will proceed with empty source.");
            }
            Console.WriteLine("--- Test Run: Source Analysis Complete ---");


            // Encryption Phase
            Console.WriteLine($"\n--- Test Run: Encryption Phase ({vaultFormat}) ---");
            if (!Directory.Exists(SourceFolderPath))
            {
                Console.WriteLine($"Source folder {SourceFolderPath} does not exist. Skipping encryption phase.");
            }
            else
            {
                if (File.Exists(vaultFilePath)) File.Delete(vaultFilePath); // Ensure fresh vault file for test
                
                byte[] vaultFileContentEnc;
                if (vaultFormat == VaultFormat.CryptomatorV8)
                {
                    // For Cryptomator V8, create both masterkey.cryptomator and vault.cryptomator
                    string vaultDirectory = Path.GetDirectoryName(vaultFilePath) ?? throw new InvalidOperationException("Invalid vault file path");
                    
                    // Clean up any existing vault files
                    if (File.Exists(Path.Combine(vaultDirectory, "vault.cryptomator")))
                        File.Delete(Path.Combine(vaultDirectory, "vault.cryptomator"));
                    
                    Vault.CreateNewCryptomatorV8VaultComplete(vaultDirectory, Password);
                    vaultFileContentEnc = File.ReadAllBytes(vaultFilePath);
                    Console.WriteLine($"New {vaultFormat} vault files created for test run encryption (masterkey.cryptomator and vault.cryptomator).");
                }
                else
                {
                    // For UVF format, create single file
                    vaultFileContentEnc = Vault.CreateNewUvfVaultFileContent(Password);
                    File.WriteAllBytes(vaultFilePath, vaultFileContentEnc);
                    Console.WriteLine($"New {vaultFormat} vault file created for test run encryption.");
                }

                _totalBytesProcessedOverall = 0;
                _overallStopwatch.Restart();

                using (Vault vault = LoadVault(vaultFileContentEnc, Password, vaultFormat))
                {
                    DirectoryMetadata rootMetadataEnc = vault.GetRootDirectoryMetadata();
                    string rootDirPhysicalPathEnc = Path.Combine(VaultFolderPath, vault.GetRootDirectoryPath());
                    Directory.CreateDirectory(rootDirPhysicalPathEnc);

                    _totalBytesProcessedOverall = ProcessDirectory(vault, SourceFolderPath, rootMetadataEnc, rootDirPhysicalPathEnc);
                }
                _overallStopwatch.Stop();
                PrintSpeed("Test Run Encrypted", _totalBytesProcessedOverall, _overallStopwatch.Elapsed);
            }
            Console.WriteLine("--- Test Run: Encryption Phase Complete ---");

            // Decryption Phase
            Console.WriteLine("\n--- Test Run: Decryption Phase ---");
            if (!File.Exists(vaultFilePath))
            {
                 Console.WriteLine($"Vault file {vaultFilePath} does not exist (likely because source was empty or encryption failed). Skipping decryption phase.");
            }
            else
            {
                byte[] vaultFileContentDec = File.ReadAllBytes(vaultFilePath);

                _totalBytesProcessedOverall = 0;
                _overallStopwatch.Restart();

                using (Vault vault = LoadVault(vaultFileContentDec, Password, vaultFormat))
                {
                    string rootDirPhysicalPathDec = Path.Combine(VaultFolderPath, vault.GetRootDirectoryPath());
                    string rootDirUvfPathDec = Path.Combine(rootDirPhysicalPathDec, vault.GetDirectoryMetadataFilename());

                    if (!vault.IsCryptomatorV8() && !File.Exists(rootDirUvfPathDec))
                    {
                        Console.Error.WriteLine($"ERROR in TestRun: Root {vault.GetDirectoryMetadataFilename()} not found at {rootDirUvfPathDec} after encryption phase. Decryption may fail or be incomplete.");
                    }
                    else
                    {
                        // Handle root directory metadata properly for different vault formats
                        DirectoryMetadata rootMetadataDec;
                        if (vault.IsCryptomatorV8())
                        {
                            // For Cryptomator v8, use programmatically generated root metadata
                            rootMetadataDec = vault.GetRootDirectoryMetadata();
                        }
                        else
                        {
                            // For UVF format, load from metadata file
                            byte[] rootDirBytesDec = File.ReadAllBytes(rootDirUvfPathDec);
                            rootMetadataDec = vault.DecryptDirectoryMetadata(rootDirBytesDec);
                        }
                        
                        _totalBytesProcessedOverall = DecryptDirectory(vault, rootMetadataDec, rootDirPhysicalPathDec, DecryptedFolderPath);
                    }
                }
                _overallStopwatch.Stop();
                PrintSpeed("Test Run Decrypted", _totalBytesProcessedOverall, _overallStopwatch.Elapsed);
            }
            Console.WriteLine("--- Test Run: Decryption Phase Complete ---");

            // Verification Phase
            Console.WriteLine("\n--- Test Run: Verification Phase ---");
            var unexpectedDecryptedItems = new List<string>();
            if (Directory.Exists(DecryptedFolderPath))
            {
                 VerifyDecryptedItems(DecryptedFolderPath, DecryptedFolderPath, sourceItems, unexpectedDecryptedItems);
            }
            else
            {
                Console.WriteLine($"Decrypted folder {DecryptedFolderPath} not found. Skipping verification of decrypted items.");
            }
            Console.WriteLine("--- Test Run: Verification Phase Complete ---");

            PrintTestRunSummary(sourceItems, unexpectedDecryptedItems, SourceFolderPath, DecryptedFolderPath);
            Console.WriteLine("===== Test Run Complete =====");
        }

        /// <summary>
        /// Loads a vault using the appropriate method based on the vault format
        /// </summary>
        private static Vault LoadVault(byte[] vaultFileContent, string password, VaultFormat vaultFormat)
        {
            return vaultFormat switch
            {
                VaultFormat.UVF => Vault.LoadUvfVault(vaultFileContent, password),
                VaultFormat.CryptomatorV8 => Vault.LoadCryptomatorV8Vault(vaultFileContent, password),
                _ => Vault.LoadUvfVault(vaultFileContent, password)
            };
        }

        private static void ProcessVault(string mode, byte[] vaultFileContent, VaultFormat vaultFormat)
        {
            Console.WriteLine($"Loading {vaultFormat} vault...");
            using (Vault vault = LoadVault(vaultFileContent, Password, vaultFormat))
            {
                Console.WriteLine("Vault loaded successfully.");

                if (mode == "encrypt")
                {
                    // Handle encryption mode
                    DirectoryMetadata rootMetadata;
                    string rootDirPhysicalPath = Path.Combine(VaultFolderPath, vault.GetRootDirectoryPath());
                    string rootDirUvfPath = Path.Combine(rootDirPhysicalPath, vault.GetDirectoryMetadataFilename());

                    Directory.CreateDirectory(rootDirPhysicalPath);

                    DirectoryMetadata rootMetadataEnc = HandleRootMetadataForEncryption(vault, rootDirUvfPath);
                    _totalBytesProcessedOverall = ProcessDirectory(vault, SourceFolderPath, rootMetadataEnc, rootDirPhysicalPath);
                }
                else if (mode == "decrypt")
                {
                    // Handle decryption mode using the new approach
                    Directory.CreateDirectory(DecryptedFolderPath);
                    ProcessVault(vault, CancellationToken.None);
                }
            }
        }

        private static DirectoryMetadata HandleRootMetadataForEncryption(Vault vault, string rootDirUvfPath)
        {
            if (File.Exists(rootDirUvfPath))
            {
                try
                {
                    Console.WriteLine($"Loading existing root dir.uvf from: {rootDirUvfPath}");
                    byte[] rootDirBytes = File.ReadAllBytes(rootDirUvfPath);
                    var metadata = vault.DecryptDirectoryMetadata(rootDirBytes);
                    Console.WriteLine("Successfully loaded and decrypted existing root metadata.");
                    return metadata;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not load or decrypt existing root dir.uvf ({ex.Message}). Initializing fresh root metadata.");
                    return vault.GetRootDirectoryMetadata();
                }
            }

            Console.WriteLine("Initializing fresh root metadata (no existing root dir.uvf found for encrypt mode).");
            return vault.GetRootDirectoryMetadata();
        }

        private static long ProcessDirectory(Vault vault, string sourceDir, DirectoryMetadata currentDirMetadata, string currentDirPhysicalVaultPath)
        {
            Console.WriteLine($"Processing directory: {sourceDir} -> {currentDirPhysicalVaultPath}");
            long bytesProcessedInThisCall = 0;

            // Save the current directory's metadata (except for Cryptomator v8 root directory)
            bool isRootDirectory = currentDirMetadata.Equals(vault.GetRootDirectoryMetadata());
            bool shouldSaveMetadata = !(vault.IsCryptomatorV8() && isRootDirectory);
            
            if (shouldSaveMetadata)
            {
                byte[] encryptedMetadata = vault.EncryptDirectoryMetadata(currentDirMetadata);
                string dirMetadataPath = Path.Combine(currentDirPhysicalVaultPath, vault.GetDirectoryMetadataFilename());
                File.WriteAllBytes(dirMetadataPath, encryptedMetadata);
            }
            else
            {
                Console.WriteLine($"  Skipping metadata save for Cryptomator v8 root directory");
            }

            // Process all files in the current directory
            foreach (string sourceFilePath in Directory.GetFiles(sourceDir))
            {
                string plainName = Path.GetFileName(sourceFilePath);
                long sourceFileSize = new FileInfo(sourceFilePath).Length;
                long expectedEncryptedSize = Vault.CalculateExpectedEncryptedSize(sourceFileSize);

                Console.WriteLine($"  Processing file: {plainName} ({sourceFileSize:N0} bytes, expected encrypted: {expectedEncryptedSize:N0} bytes)");

                // Get encrypted name and create physical path for the encrypted file
                string encryptedName = vault.EncryptFilename(plainName, currentDirMetadata);
                string targetEncryptedFilePath = Path.Combine(currentDirPhysicalVaultPath, encryptedName);

                bool encryptThisFile = true;
                if (File.Exists(targetEncryptedFilePath))
                {
                    long existingFileSize = new FileInfo(targetEncryptedFilePath).Length;
                    Console.WriteLine($"    File already exists with size {existingFileSize} bytes");
                    
                    if (existingFileSize == expectedEncryptedSize)
                    {
                        Console.WriteLine("    Skipping file as it appears to be already encrypted correctly");
                        encryptThisFile = false;
                        bytesProcessedInThisCall += sourceFileSize; // Count it anyway for progress
                    }
                    else
                    {
                        Console.WriteLine($"    Existing file size ({existingFileSize}) doesn't match expected encrypted size ({expectedEncryptedSize}), re-encrypting");
                    }
                }

                if (encryptThisFile)
                {
                    try
                    {
                        using (FileStream sourceStream = File.OpenRead(sourceFilePath))
                        using (FileStream targetStream = File.Create(targetEncryptedFilePath))
                        using (Stream encryptingStream = vault.GetEncryptingStream(targetStream))
                        {
                            sourceStream.CopyTo(encryptingStream);
                        }
                        bytesProcessedInThisCall += sourceFileSize;
                        
                        // Verify the actual encrypted size matches expected
                        long actualEncryptedSize = new FileInfo(targetEncryptedFilePath).Length;
                        if (actualEncryptedSize != expectedEncryptedSize)
                        {
                            Console.WriteLine($"  WARNING: Actual encrypted size ({actualEncryptedSize:N0}) differs from expected ({expectedEncryptedSize:N0})");
                        }
                        else
                        {
                            Console.WriteLine($"  Encrypted successfully. Size verification passed.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"    ERROR encrypting file {plainName}: {ex.Message}");
                    }
                }
            }

            // Process subdirectories
            foreach (string sourceSubDirPath in Directory.GetDirectories(sourceDir))
            {
                string plainSubDirName = Path.GetFileName(sourceSubDirPath);
                Console.WriteLine($"  Processing subdirectory: {plainSubDirName}");

                // Create new metadata for the subdirectory
                DirectoryMetadata subDirMetadata = vault.CreateNewDirectoryMetadata();
                string encryptedSubDirName = vault.EncryptFilename(plainSubDirName, currentDirMetadata);
                
                // Create the physical path for the encrypted subdirectory
                string subDirPhysicalVaultPath = Path.Combine(currentDirPhysicalVaultPath, encryptedSubDirName);
                string subDirMetadataPath = Path.Combine(subDirPhysicalVaultPath, vault.GetDirectoryMetadataFilename());

                bool processSubDir = true;
                DirectoryMetadata existingSubDirMetadata = null;

                // Check if subdirectory already exists with valid metadata
                if (Directory.Exists(subDirPhysicalVaultPath) && File.Exists(subDirMetadataPath))
                {
                    try
                    {
                        Console.WriteLine($"    Subdirectory already exists, checking metadata...");
                        byte[] existingMetadataBytes = File.ReadAllBytes(subDirMetadataPath);
                        existingSubDirMetadata = vault.DecryptDirectoryMetadata(existingMetadataBytes);
                        
                        // If we can successfully decrypt the metadata, we can reuse this directory
                        Console.WriteLine($"    Reusing existing subdirectory structure (DirId: {existingSubDirMetadata.DirId})");
                        subDirMetadata = existingSubDirMetadata;
                        processSubDir = false;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    Cannot reuse existing subdirectory: {ex.Message}");
                        // We'll create a new directory structure
                        processSubDir = true;
                    }
                }

                if (processSubDir)
                {
                    Console.WriteLine($"    Creating new subdirectory structure");
                    Directory.CreateDirectory(subDirPhysicalVaultPath);
                }

                // Recursively process the subdirectory
                bytesProcessedInThisCall += ProcessDirectory(vault, sourceSubDirPath, subDirMetadata, subDirPhysicalVaultPath);
            }

            return bytesProcessedInThisCall;
        }

        private static long DecryptDirectory(Vault vault, DirectoryMetadata currentDirectoryMetadata, string currentDirPhysicalVaultPath, string targetDecryptedPath)
        {
            //Console.WriteLine($"  DEBUG: DecryptDirectory START - CurrentDirPhysicalPath: {currentDirPhysicalVaultPath}, TargetDecryptedPath: {targetDecryptedPath}, CurrentDirMetadata (DirId: {currentDirectoryMetadata.DirId}, SeedId: {currentDirectoryMetadata.SeedId})");
            Console.WriteLine($"Decrypting directory from: {currentDirPhysicalVaultPath} -> to: {targetDecryptedPath} (DirId: {currentDirectoryMetadata.DirId})");
            
            // Add detailed logging to understand what's happening
            if (!Directory.Exists(currentDirPhysicalVaultPath))
            {
                Console.WriteLine($"❌ ERROR: Directory does not exist: {currentDirPhysicalVaultPath}");
                return 0;
            }
            
            var allFiles = Directory.GetFiles(currentDirPhysicalVaultPath);
            var allDirs = Directory.GetDirectories(currentDirPhysicalVaultPath);
            Console.WriteLine($"  Found {allFiles.Length} files and {allDirs.Length} subdirectories in {currentDirPhysicalVaultPath}");
            
            if (allFiles.Length > 0)
            {
                Console.WriteLine($"  Files found:");
                foreach (var file in allFiles)
                {
                    Console.WriteLine($"    - {Path.GetFileName(file)}");
                }
            }
            
            if (allDirs.Length > 0)
            {
                Console.WriteLine($"  Subdirectories found:");
                foreach (var dir in allDirs)
                {
                    Console.WriteLine($"    - {Path.GetFileName(dir)}");
                }
            }
            
            Directory.CreateDirectory(targetDecryptedPath);

            long bytesProcessedInThisCall = 0;
            string metadataFilename = vault.GetDirectoryMetadataFilename();
            string rootMetadataFilename = vault.IsCryptomatorV8() ? "dirid.c9r" : "dir.uvf";
            Console.WriteLine($"  Looking for metadata filename: {metadataFilename}");

            // Process all encrypted files in the current directory
            foreach (string encryptedFilePath in Directory.GetFiles(currentDirPhysicalVaultPath))
            {
                string encryptedName = Path.GetFileName(encryptedFilePath);
                Console.WriteLine($"  Processing file: {encryptedName}");
                
                // Skip metadata files (both current directory and root directory metadata)
                if (encryptedName == metadataFilename || encryptedName == rootMetadataFilename) 
                {
                    Console.WriteLine($"    Skipping metadata file: {encryptedName}");
                    continue; // Skip metadata file
                }

                try
                {
                    Console.WriteLine($"    Attempting to decrypt filename: {encryptedName}");
                    string decryptedName = vault.DecryptFilename(encryptedName, currentDirectoryMetadata);
                    string targetDecryptedFilePath = Path.Combine(targetDecryptedPath, decryptedName);
                    Console.WriteLine($"    Decrypted filename: {encryptedName} -> {decryptedName}");

                    long encryptedFileSize = new FileInfo(encryptedFilePath).Length;
                    long expectedDecryptedSize = Vault.CalculateExpectedDecryptedSize(encryptedFileSize);

                    Console.WriteLine($"\nSize Analysis for {encryptedName} -> {decryptedName}:");
                    Console.WriteLine($"  Encrypted size: {encryptedFileSize:N0} bytes");
                    Console.WriteLine($"  Expected decrypted size: {expectedDecryptedSize:N0} bytes");

                    bool decryptThisFile = true;
                    if (File.Exists(targetDecryptedFilePath))
                    {
                        long existingFileSize = new FileInfo(targetDecryptedFilePath).Length;
                        Console.WriteLine($"  File already exists with size {existingFileSize:N0} bytes");

                        if (existingFileSize == expectedDecryptedSize)
                        {
                            Console.WriteLine("  Skipping file as it appears to be already decrypted correctly");
                            decryptThisFile = false;
                            bytesProcessedInThisCall += expectedDecryptedSize; // Count it anyway for progress
                        }
                        else
                        {
                            Console.WriteLine($"  Existing file size ({existingFileSize:N0}) doesn't match expected decrypted size ({expectedDecryptedSize:N0}), re-decrypting");
                        }
                    }

                    if (decryptThisFile)
                    {
                        Console.WriteLine($"    Starting decryption of {encryptedName}...");
                        using (FileStream sourceStream = File.OpenRead(encryptedFilePath))
                        using (Stream decryptingStream = vault.GetDecryptingStream(sourceStream))
                        using (FileStream targetStream = File.Create(targetDecryptedFilePath))
                        {
                            decryptingStream.CopyTo(targetStream);
                        }

                        // Verify the actual decrypted size matches expected
                        long actualDecryptedSize = new FileInfo(targetDecryptedFilePath).Length;
                        if (actualDecryptedSize != expectedDecryptedSize)
                        {
                            Console.WriteLine($"  WARNING: Actual decrypted size ({actualDecryptedSize:N0}) differs from expected ({expectedDecryptedSize:N0})");
                        }
                        else
                        {
                            Console.WriteLine($"  Decrypted successfully. Size verification passed.");
                        }

                        bytesProcessedInThisCall += actualDecryptedSize;
                        Console.WriteLine($"    ✅ Successfully decrypted {decryptedName}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"    ❌ ERROR processing item '{encryptedName}': {ex.Message}");
                }
            }

            // Process all encrypted subdirectories
            foreach (string encryptedSubDirPath in Directory.GetDirectories(currentDirPhysicalVaultPath))
            {
                string encryptedSubDirName = Path.GetFileName(encryptedSubDirPath);
                Console.WriteLine($"  Processing subdirectory: {encryptedSubDirName}");

                try
                {
                    Console.WriteLine($"    Attempting to decrypt subdirectory name: {encryptedSubDirName}");
                    string decryptedSubDirName = vault.DecryptFilename(encryptedSubDirName, currentDirectoryMetadata);
                    string targetDecryptedSubDirPath = Path.Combine(targetDecryptedPath, decryptedSubDirName);
                    Console.WriteLine($"    Decrypted subdirectory name: {encryptedSubDirName} -> {decryptedSubDirName}");

                    Console.WriteLine($"  Processing encrypted subdirectory: {encryptedSubDirName} -> {decryptedSubDirName}");

                    // Load and decrypt the subdirectory's metadata
                    // Use the specific metadata filename for this subdirectory context
                    string expectedSubDirMetadataFilename = vault.IsCryptomatorV8() ? "dir.c9r" : "dir.uvf";
                    string subDirMetadataPath = Path.Combine(encryptedSubDirPath, expectedSubDirMetadataFilename);
                    if (!File.Exists(subDirMetadataPath))
                    {
                        Console.Error.WriteLine($"    ERROR: Missing {expectedSubDirMetadataFilename} in subdirectory: {encryptedSubDirPath}");
                        continue;
                    }

                    Console.WriteLine($"    Loading metadata from: {subDirMetadataPath}");
                    byte[] encryptedMetadata = File.ReadAllBytes(subDirMetadataPath);
                    DirectoryMetadata subDirMetadata = vault.DecryptDirectoryMetadata(encryptedMetadata);

                    // For Cryptomator v8, the actual content is in a different directory calculated from the directory ID
                    string actualContentPath;
                    if (vault.IsCryptomatorV8())
                    {
                        // Calculate the path where the actual directory content is stored
                        actualContentPath = Path.Combine(VaultFolderPath, vault.GetDirectoryPath(subDirMetadata));
                        Console.WriteLine($"    Cryptomator v8: Actual content path: {actualContentPath}");
                        Console.WriteLine($"    Directory ID from metadata: {subDirMetadata.DirId}");
                        
                        if (!Directory.Exists(actualContentPath))
                        {
                            Console.Error.WriteLine($"    ERROR: Actual content directory not found: {actualContentPath}");
                            continue;
                        }
                    }
                    else
                    {
                        // For UVF format, content is in the same directory as the metadata
                        actualContentPath = encryptedSubDirPath;
                    }

                    // Check if subdirectory is already decrypted with correct structure
                    bool decryptSubDir = true;
                    if (Directory.Exists(targetDecryptedSubDirPath))
                    {
                        Console.WriteLine($"    Subdirectory already exists at target location");
                        // We can't verify the content directly like with files, but we can check if it's a valid directory
                        if (Directory.GetFiles(targetDecryptedSubDirPath).Length > 0 || Directory.GetDirectories(targetDecryptedSubDirPath).Length > 0)
                        {
                            Console.WriteLine($"    Subdirectory appears to be already populated, will verify contents");
                            decryptSubDir = false;
                        }
                    }

                    if (decryptSubDir)
                    {
                        Directory.CreateDirectory(targetDecryptedSubDirPath);
                    }

                    // Recursively decrypt the subdirectory using the actual content path
                    Console.WriteLine($"    Recursively processing subdirectory from: {actualContentPath} -> {targetDecryptedSubDirPath}");
                    bytesProcessedInThisCall += DecryptDirectory(vault, subDirMetadata, actualContentPath, targetDecryptedSubDirPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"    ❌ ERROR processing encrypted subdirectory '{encryptedSubDirName}': {ex.Message}");
                }
            }

            Console.WriteLine($"  Finished processing directory. Total bytes: {bytesProcessedInThisCall:N0}");
            return bytesProcessedInThisCall;
        }

        private static void PrintSpeed(string operationLabel, long totalBytes, TimeSpan elapsed)
        {
            Console.WriteLine($"{operationLabel} {totalBytes} bytes.");
            if (elapsed.TotalSeconds > 0 && totalBytes > 0)
            {
                double megabytes = totalBytes / (1024.0 * 1024.0);
                double speed = megabytes / elapsed.TotalSeconds;
                Console.WriteLine($"Speed: {speed:F2} MB/s ({elapsed.TotalMilliseconds:F0} ms)");
            }
            else if (totalBytes == 0)
            {
                Console.WriteLine("No data processed to calculate speed.");
            }
            else
            {
                Console.WriteLine($"Time elapsed: {elapsed.TotalMilliseconds:F0} ms (too fast to calculate meaningful speed for small data or speed calculation not applicable).");
            }
        }

        private static void LogDirectoryTreeStructure(string rootPath, string description)
        {
            Console.WriteLine($"\n--- {description} ---");
            if (!Directory.Exists(rootPath) && !File.Exists(rootPath))
            {
                Console.WriteLine($"Path does not exist: {rootPath}");
                Console.WriteLine("--- End of Structure ---");
                Console.WriteLine();
                return;
            }
            
            Console.WriteLine(rootPath);
            LogDirectoryTreeRecursive(rootPath, "", true);
            Console.WriteLine("--- End of Structure ---");
            Console.WriteLine();
        }

        private static void LogDirectoryTreeRecursive(string currentPath, string indent, bool isLast)
        {
            if (!Directory.Exists(currentPath))
            {
                return;
            }

            var entries = Directory.GetFileSystemEntries(currentPath)
                                 .OrderBy(e => e)
                                 .ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                string entry = entries[i];
                bool lastEntry = (i == entries.Count - 1);
                string marker = lastEntry ? "└───" : "├───";
                string entryName = Path.GetFileName(entry);

                Console.WriteLine($"{indent}{marker}{entryName}");

                if (Directory.Exists(entry))
                {
                    string newIndent = indent + (lastEntry ? "    " : "│   ");
                    LogDirectoryTreeRecursive(entry, newIndent, true);
                }
            }
        }

        // --- Helper methods for TestRun mode ---

        private static void CleanupTestDirectories()
        {
            Console.WriteLine($"Cleaning up test directories: {VaultFolderPath} and {DecryptedFolderPath}");

            try
            {
                if (Directory.Exists(VaultFolderPath))
                {
                    Directory.Delete(VaultFolderPath, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not fully delete vault folder {VaultFolderPath}: {ex.Message}");
            }
            Directory.CreateDirectory(VaultFolderPath);

            try
            {
                if (Directory.Exists(DecryptedFolderPath))
                {
                    Directory.Delete(DecryptedFolderPath, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not fully delete decrypted folder {DecryptedFolderPath}: {ex.Message}");
            }
            Directory.CreateDirectory(DecryptedFolderPath);
            Console.WriteLine("Cleanup complete.");
        }

        private static string GetRelativePath(string fullPath, string basePath)
        {
            if (string.IsNullOrEmpty(basePath)) return fullPath;

            // Normalize paths - remove trailing separators and ensure consistent case
            string normalizedBasePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedFullPath = Path.GetFullPath(fullPath);

            // If the full path equals the base path exactly, this is the root item (return empty string)
            if (normalizedFullPath.Equals(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            // Add separator to base path for proper prefix matching
            string basePathWithSeparator = normalizedBasePath + Path.DirectorySeparatorChar;

            if (normalizedFullPath.StartsWith(basePathWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFullPath.Substring(basePathWithSeparator.Length);
            }
            
            // If no match, return the full path as-is (shouldn't happen in normal cases)
            return fullPath; 
        }

        private static void CollectAndHashSourceItems(string currentItemPath, string rootBasePath, Dictionary<string, FileVerificationInfo> items)
        {
            // Process current item (could be the initial rootBasePath itself or a subdirectory)
            if (Directory.Exists(currentItemPath))
            {
                string dirRelativePath = GetRelativePath(currentItemPath, rootBasePath);
                if (!string.IsNullOrEmpty(dirRelativePath) && !items.ContainsKey(dirRelativePath))
                {
                    items.Add(dirRelativePath, new FileVerificationInfo(dirRelativePath, true, null, 0));
                }

                // Process files in current directory
                foreach (string filePath in Directory.GetFiles(currentItemPath))
                {
                    string fileRelativePath = GetRelativePath(filePath, rootBasePath);
                    string hash = null;
                    long fileSize = 0;
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        fileSize = fileInfo.Length;
                        // Use the same consistent hash calculation method
                        hash = CalculateFileHashConsistently(filePath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR hashing source file {filePath}: {ex.Message}");
                        hash = $"ERROR_HASHING: {ex.Message}";
                    }
                    if (!items.ContainsKey(fileRelativePath)) // Should not happen if logic is correct, but safeguard
                    {
                         items.Add(fileRelativePath, new FileVerificationInfo(fileRelativePath, false, hash, fileSize));
                    }
                }

                // Process subdirectories recursively
                foreach (string subDirPath in Directory.GetDirectories(currentItemPath))
                {
                    CollectAndHashSourceItems(subDirPath, rootBasePath, items);
                }
            }
            else if (File.Exists(currentItemPath))
            {
                string fileRelativePath = GetRelativePath(currentItemPath, rootBasePath);
                try
                {
                    var fileInfo = new FileInfo(currentItemPath);
                    long fileSize = fileInfo.Length;
                    string hash = CalculateFileHashConsistently(currentItemPath);
                    items.Add(fileRelativePath, new FileVerificationInfo(fileRelativePath, false, hash, fileSize));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR hashing source file {currentItemPath}: {ex.Message}");
                    items.Add(fileRelativePath, new FileVerificationInfo(fileRelativePath, false, $"ERROR_HASHING: {ex.Message}", 0));
                }
            }
            // If currentItemPath is a file (should not happen with initial call), it's an error in calling logic.
            // This function is designed to be called with a directory path.
        }
        
        private static void VerifyDecryptedItems(string currentItemPath, string rootDecryptedPath, Dictionary<string, FileVerificationInfo> sourceItems, List<string> unexpectedItems)
        {
            if (Directory.Exists(currentItemPath))
            {
                string dirRelativePath = GetRelativePath(currentItemPath, rootDecryptedPath);

                if (!string.IsNullOrEmpty(dirRelativePath)) // Don't process the root decrypted folder itself as an item to verify
                {
                    if (sourceItems.TryGetValue(dirRelativePath, out var dirInfo))
                    {
                        if (dirInfo.IsDirectory)
                        {
                            dirInfo.ExistsInDecrypted = true;
                        }
                        else // Source was file, decrypted is dir
                        {
                            dirInfo.ExistsInDecrypted = true; // It exists, but...
                            dirInfo.TypeMismatch = true;
                        }
                    }
                    else
                    {
                        unexpectedItems.Add($"Unexpected directory: {currentItemPath}");
                    }
                }

                // Process files
                foreach (string filePath in Directory.GetFiles(currentItemPath))
                {
                    string fileRelativePath = GetRelativePath(filePath, rootDecryptedPath);
                    string hash = null;
                    
                    // Get file info for size comparison
                    var decryptedFileInfo = new FileInfo(filePath);
                    long fileSize = decryptedFileInfo.Length;
                    
                    try
                    {
                        // Use the same file reading method as used for source files
                        hash = CalculateFileHashConsistently(filePath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR hashing decrypted file {filePath}: {ex.Message}");
                        hash = $"ERROR_HASHING: {ex.Message}";
                    }

                    if (sourceItems.TryGetValue(fileRelativePath, out var fileInfo))
                    {
                        if (!fileInfo.IsDirectory) // Source was file, decrypted is also file
                        {
                            fileInfo.ExistsInDecrypted = true;
                            fileInfo.DecryptedHash = hash;
                            
                            // Display hash comparison prominently
                            Console.WriteLine($"Hash Analysis for {Path.GetFileName(filePath)}:");
                            Console.WriteLine($"  Source hash:    {fileInfo.SourceHash}");
                            Console.WriteLine($"  Decrypted hash: {hash}");
                            Console.WriteLine($"  Hash match: {fileInfo.HashesMatch}");
                            
                            if (!fileInfo.HashesMatch && !fileInfo.SourceHashError && !fileInfo.DecryptedHashError)
                            {
                                Console.WriteLine($"❌ Hash mismatch detected for {fileRelativePath}!");
                                
                                if (File.Exists(filePath))
                                {
                                    // Additional detailed analysis for hash mismatches
                                    Console.WriteLine($"   File sizes: Source={fileInfo.SourceSize:N0}, Decrypted={fileSize:N0}");
                                    
                                    if (fileInfo.SourceSize == fileSize)
                                    {
                                        Console.WriteLine($"   Sizes match but hashes differ - detailed analysis:");
                                        
                                        // Get the source file path for comparison  
                                        string sourceFilePath = Path.Combine(Path.GetDirectoryName(rootDecryptedPath.TrimEnd('\\', '/')) ?? "", "EncryptionTestSource", fileRelativePath);
                                        if (File.Exists(sourceFilePath))
                                        {
                                            CompareFileSegments(sourceFilePath, filePath);
                                        }
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"Hash mismatch: {fileRelativePath}");
                                }
                            }
                            else if (fileInfo.HashesMatch)
                            {
                                Console.WriteLine($"✅ {Path.GetFileName(filePath)} verified successfully");
                            }
                        }
                        else // Source was dir, decrypted is file
                        {
                            fileInfo.ExistsInDecrypted = true; // It exists, but...
                            fileInfo.TypeMismatch = true;
                            fileInfo.DecryptedHash = hash; // Store hash anyway if needed for debug
                        }
                    }
                    else
                    {
                        unexpectedItems.Add($"Unexpected file: {filePath} (Hash: {hash})");
                    }
                }

                // Process subdirectories
                foreach (string subDirPath in Directory.GetDirectories(currentItemPath))
                {
                    VerifyDecryptedItems(subDirPath, rootDecryptedPath, sourceItems, unexpectedItems);
                }
            }
        }

        /// <summary>
        /// Calculates file hash using a consistent method for both source and decrypted files
        /// </summary>
        private static string CalculateFileHashConsistently(string filePath)
        {
            // Force any pending file operations to complete by waiting for exclusive access
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // Immediately close and reopen with shared access for actual reading
            }
            
            // Now read the file for hashing with the same method used elsewhere
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return FastHash.GetHash(fs);
            }
        }

        /// <summary>
        /// Compares the first and last segments of two files to help diagnose corruption
        /// </summary>
        private static void CompareFileSegments(string sourceFilePath, string decryptedFilePath)
        {
            const int segmentSize = 1024; // Compare first and last 1KB
            
            try
            {
                using (var sourceFs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var decryptedFs = new FileStream(decryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (sourceFs.Length != decryptedFs.Length)
                    {
                        Console.WriteLine($"   ❌ File sizes differ: Source={sourceFs.Length:N0}, Decrypted={decryptedFs.Length:N0}");
                        return;
                    }
                    
                    // Compare first segment
                    byte[] sourceStart = new byte[segmentSize];
                    byte[] decryptedStart = new byte[segmentSize];
                    
                    int sourceRead = sourceFs.Read(sourceStart, 0, segmentSize);
                    int decryptedRead = decryptedFs.Read(decryptedStart, 0, segmentSize);
                    
                    bool startMatches = sourceRead == decryptedRead && sourceStart.Take(sourceRead).SequenceEqual(decryptedStart.Take(decryptedRead));
                    Console.WriteLine($"   First {segmentSize} bytes match: {startMatches}");
                    
                    // Compare last segment (if file is large enough)
                    if (sourceFs.Length > segmentSize)
                    {
                        sourceFs.Seek(-segmentSize, SeekOrigin.End);
                        decryptedFs.Seek(-segmentSize, SeekOrigin.End);
                        
                        byte[] sourceEnd = new byte[segmentSize];
                        byte[] decryptedEnd = new byte[segmentSize];
                        
                        sourceRead = sourceFs.Read(sourceEnd, 0, segmentSize);
                        decryptedRead = decryptedFs.Read(decryptedEnd, 0, segmentSize);
                        
                        bool endMatches = sourceRead == decryptedRead && sourceEnd.Take(sourceRead).SequenceEqual(decryptedEnd.Take(decryptedRead));
                        Console.WriteLine($"   Last {segmentSize} bytes match: {endMatches}");
                        
                        // If segments match but hashes don't, it might be a middle corruption
                        if (startMatches && endMatches)
                        {
                            Console.WriteLine($"   🤔 Start and end match, possible middle corruption or hash calculation issue");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error comparing file segments: {ex.Message}");
            }
        }

        private static void PrintTestRunSummary(Dictionary<string, FileVerificationInfo> sourceItems, List<string> unexpectedItems, string sourceBasePath, string decryptedBasePath)
        {
            Console.WriteLine("\n--- Test Run Summary ---");
            bool allGood = true;
            int missingCount = 0;
            int hashMismatchCount = 0;
            int typeMismatchCount = 0;
            int sourceHashErrorCount = 0;
            int decryptedHashErrorCount = 0;

            if (!Directory.Exists(sourceBasePath) && sourceItems.Any())
            {
                 Console.WriteLine($"WARNING: Source path {sourceBasePath} does not exist, but source items were somehow collected. This is unexpected.");
            }
            if (!Directory.Exists(decryptedBasePath) && (sourceItems.Any(si => si.Value.ExistsInDecrypted) || unexpectedItems.Any() ))
            {
                 Console.WriteLine($"WARNING: Decrypted path {decryptedBasePath} does not exist, but decrypted items were somehow processed. This is unexpected.");
            }

            foreach (var entry in sourceItems.OrderBy(kvp => kvp.Key))
            {
                var info = entry.Value;
                string itemType = info.IsDirectory ? "Directory" : "File";

                if (info.SourceHashError && !info.IsDirectory)
                {
                    Console.WriteLine($"SOURCE HASH ERROR: [{info.RelativePath}] - {info.SourceHash}");
                    allGood = false; // Hashing error is a failure
                    sourceHashErrorCount++;
                }

                if (!info.ExistsInDecrypted)
                {
                    Console.WriteLine($"MISSING in decrypted: [{info.RelativePath}] (Type: {itemType})");
                    allGood = false;
                    missingCount++;
                }
                else // Item exists in decrypted
                {
                    if (info.TypeMismatch)
                    {
                        Console.WriteLine($"TYPE MISMATCH: [{info.RelativePath}] Source is {itemType}, decrypted is {(info.IsDirectory ? "File" : "Directory")}.");
                        allGood = false;
                        typeMismatchCount++;
                    }
                    
                    if (!info.IsDirectory) // It's a file and it exists
                    {
                        if(info.DecryptedHashError)
                        {
                            Console.WriteLine($"DECRYPTED HASH ERROR: [{info.RelativePath}] - {info.DecryptedHash}");
                            allGood = false;
                            decryptedHashErrorCount++;
                        }
                        else if (!info.HashesMatch && !info.SourceHashError) // Only check hash if no errors in hashing
                        {
                            Console.WriteLine($"HASH MISMATCH: [{info.RelativePath}]");
                            Console.WriteLine($"  Source Hash: {info.SourceHash}");
                            Console.WriteLine($"  Decrypted Hash: {info.DecryptedHash}");
                            allGood = false;
                            hashMismatchCount++;
                        }
                    }
                }
            }

            if (unexpectedItems.Any())
            {
                allGood = false;
                Console.WriteLine($"\nUnexpected items found in decrypted folder ({decryptedBasePath}):");
                foreach (var unexpectedPath in unexpectedItems.OrderBy(p => p))
                {
                    Console.WriteLine($"  - {unexpectedPath}");
                }
            }

            Console.WriteLine("\n--- Overall Result ---");
            if (allGood)
            {
                Console.WriteLine("SUCCESS: All source items exist in decrypted folder with matching content and types (or source/decrypted was empty as expected).");
            }
            else
            {
                Console.WriteLine("FAILURE: Discrepancies found.");
                if (missingCount > 0) Console.WriteLine($"  Missing items in decrypted: {missingCount}");
                if (hashMismatchCount > 0) Console.WriteLine($"  Hash mismatches: {hashMismatchCount}");
                if (typeMismatchCount > 0) Console.WriteLine($"  Type mismatches: {typeMismatchCount}");
                if (unexpectedItems.Count > 0) Console.WriteLine($"  Unexpected items in decrypted: {unexpectedItems.Count}");
                if (sourceHashErrorCount > 0) Console.WriteLine($"  Errors hashing source files: {sourceHashErrorCount}");
                if (decryptedHashErrorCount > 0) Console.WriteLine($"  Errors hashing decrypted files: {decryptedHashErrorCount}");
            }
            Console.WriteLine($"Source Path: {sourceBasePath}");
            Console.WriteLine($"Decrypted Path: {decryptedBasePath}");
            Console.WriteLine("--- End of Test Run Summary ---");
        }

        private static void ProcessVault(Vault vault, CancellationToken cancellationToken)
        {
            Console.WriteLine("Using programmatically generated root metadata for Cryptomator v8 decryption");

            if (vault.IsCryptomatorV8())
            {
                // For Cryptomator v8, we need to find the actual root directory
                ProcessCryptomatorV8Vault(vault, cancellationToken);
            }
            else
            {
                // For UVF vaults, use the standard approach
                ProcessStandardVault(vault, cancellationToken);
            }
        }

        private static void ProcessCryptomatorV8Vault(Vault vault, CancellationToken cancellationToken)
        {
            Console.WriteLine("\n--- Cryptomator v8 Direct Root Access ---");

            // Calculate root directory path directly - this is deterministic and always correct
            var rootMetadata = vault.GetRootDirectoryMetadata();
            var rootPath = vault.GetDirectoryPath(rootMetadata);
            var fullRootPath = Path.Combine(VaultFolderPath, rootPath);
            
            Console.WriteLine($"Calculated root path: {rootPath}");
            Console.WriteLine($"Full root path: {fullRootPath}");
            
            // Verify the calculated root directory exists (sanity check)
            if (!Directory.Exists(fullRootPath))
            {
                throw new InvalidOperationException($"Calculated root directory not found at: {fullRootPath}");
            }
            
            Console.WriteLine($"✅ Root directory exists and is ready for decryption");
            
            // Start decryption directly from the calculated root
            Console.WriteLine($"Decrypting from calculated root: {fullRootPath} -> {DecryptedFolderPath}");
            DecryptDirectory(vault, rootMetadata, fullRootPath, DecryptedFolderPath);
        }

        private static void ProcessStandardVault(Vault vault, CancellationToken cancellationToken)
        {
            // Get the root directory metadata and path
            var rootMetadata = vault.GetRootDirectoryMetadata();
            var rootDirectoryPath = Path.Combine(VaultFolderPath, vault.GetDirectoryPath(rootMetadata));

            Console.WriteLine($"Decrypting from calculated root: {rootDirectoryPath} -> {DecryptedFolderPath}");
            DecryptDirectory(vault, rootMetadata, rootDirectoryPath, DecryptedFolderPath);
        }

    }
} 