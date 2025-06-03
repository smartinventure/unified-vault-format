using UvfLib;
using UvfLib.Api;
using UvfLib.V3;
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
        //private const string VaultFolderPath = @"D:\temp\uvf\EncryptionTestVault";
        private const string VaultFolderPath = @"D:\cyptomatortest\tester\tester";
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
                Console.WriteLine("Usage: UvfConsole <encrypt|decrypt|testrun|minimal|largefile> [--uvf|--cryptomator]");
                Console.WriteLine("  --uvf        : Use Universal Vault Format (default)");
                Console.WriteLine("  --cryptomator: Use Cryptomator V8 format for legacy compatibility");
                Console.WriteLine("  minimal      : Run minimal chunk-level debugging test");
                Console.WriteLine("  largefile    : Test only the large NDPI file for corruption debugging");
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
            else if (mode == "minimal")
            {
                HandleMinimalTestMode(vaultFilePath, vaultFormat);
            }
            else if (mode == "largefile")
            {
                HandleLargeFileTestMode(vaultFilePath, vaultFormat);
            }
            else
            {
                Console.WriteLine("Invalid mode. Use 'encrypt', 'decrypt', 'testrun', 'minimal', or 'largefile'.");
                Console.WriteLine("Usage: UvfConsole <encrypt|decrypt|testrun|minimal|largefile> [--uvf|--cryptomator]");
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

        private static DirectoryMetadata HandleRootMetadataForDecryption(Vault vault, string rootDirUvfPath)
        {
            try
            {
                // For Cryptomator v8, the root directory doesn't have a dir.c9r file
                if (vault.IsCryptomatorV8())
                {
                    // Root directory metadata is generated programmatically for Cryptomator v8
                    return vault.GetRootDirectoryMetadata();
                }

                // For UVF format, load from dir.uvf file
                if (File.Exists(rootDirUvfPath))
                {
                    Console.WriteLine($"Loading existing root dir.uvf from: {rootDirUvfPath}");
                    byte[] encryptedRootMetadata = File.ReadAllBytes(rootDirUvfPath);
                    return vault.DecryptDirectoryMetadata(encryptedRootMetadata);
                }
                else
                {
                    Console.Error.WriteLine($"ERROR: Root dir.uvf not found at {rootDirUvfPath}. Cannot decrypt.");
                    throw new FileNotFoundException($"Root directory metadata file not found: {rootDirUvfPath}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FATAL: Could not load or decrypt root dir.uvf for decryption ({ex.Message}). Cannot proceed.");
                throw;
            }
        }

        private static long CalculateExpectedEncryptedSize(long sourceFileSize)
        {
            // Calculate how many complete chunks we'll need
            long completeChunks = sourceFileSize / Constants.PAYLOAD_SIZE;
            
            // Calculate the size of the final partial chunk (if any)
            long remainingBytes = sourceFileSize % Constants.PAYLOAD_SIZE;
            
            // Each chunk (including the final partial one if it exists) needs GCM_NONCE_SIZE + GCM_TAG_SIZE overhead
            long totalChunks = remainingBytes > 0 ? completeChunks + 1 : completeChunks;
            long totalOverhead = totalChunks * (Constants.GCM_NONCE_SIZE + Constants.GCM_TAG_SIZE);
            
            // Add the file header size (from FileHeaderImpl)
            // Magic bytes (4) + Seed ID (4) + Nonce (12) + Content Key (32) + Tag (16) = 68 bytes
            long headerSize = 68;

            // Total size = file header + source file size + total chunk overhead
            long expectedSize = headerSize + sourceFileSize + totalOverhead;

            Console.WriteLine($"\nDebug - Expected Size Calculation:");
            Console.WriteLine($"  Source size: {sourceFileSize:N0} bytes");
            Console.WriteLine($"  Complete chunks: {completeChunks:N0}");
            Console.WriteLine($"  Remaining bytes: {remainingBytes:N0}");
            Console.WriteLine($"  Total chunks: {totalChunks:N0}");
            Console.WriteLine($"  Per-chunk overhead: {Constants.GCM_NONCE_SIZE + Constants.GCM_TAG_SIZE} bytes");
            Console.WriteLine($"  Total chunk overhead: {totalOverhead:N0} bytes");
            Console.WriteLine($"  File header size: {headerSize} bytes");
            Console.WriteLine($"  Expected encrypted size: {expectedSize:N0} bytes\n");

            return expectedSize;
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
                long expectedEncryptedSize = CalculateExpectedEncryptedSize(sourceFileSize);

                Console.WriteLine($"  Processing file: {plainName} ({sourceFileSize} bytes, expected encrypted size: {expectedEncryptedSize} bytes)");

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
                        long calculatedEncryptedSize = Vault.CalculateExpectedEncryptedSize(sourceFileSize);
                        Console.WriteLine($"\nSize Analysis for {plainName}:");
                        Console.WriteLine($"  Source size: {sourceFileSize:N0} bytes");
                        Console.WriteLine($"  Expected encrypted size: {calculatedEncryptedSize:N0} bytes");

                        using (FileStream sourceStream = File.OpenRead(sourceFilePath))
                        using (FileStream targetStream = File.Create(targetEncryptedFilePath))
                        using (Stream encryptingStream = vault.GetEncryptingStream(targetStream))
                        {
                            sourceStream.CopyTo(encryptingStream);
                        }
                        bytesProcessedInThisCall += sourceFileSize;
                        
                        // Verify the actual encrypted size matches expected
                        long actualEncryptedSize = new FileInfo(targetEncryptedFilePath).Length;
                        if (actualEncryptedSize != calculatedEncryptedSize)
                        {
                            Console.WriteLine($"  WARNING: Actual encrypted size ({actualEncryptedSize:N0}) differs from expected ({calculatedEncryptedSize:N0})");
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

        private static void HandleMinimalTestMode(string vaultFilePath, VaultFormat vaultFormat)
        {
            Console.WriteLine($"===== Starting Minimal Chunk Test (Format: {vaultFormat}) =====");
            
            // Create a dedicated folder for minimal testing
            string minimalTestSource = @"D:\temp\uvf\MinimalTestSource";
            string minimalTestVault = @"D:\temp\uvf\MinimalTestVault";
            string minimalTestDecrypted = @"D:\temp\uvf\MinimalTestDecrypted";
            
            // Clean up vault and decrypted directories (but keep source since it has the real file)
            if (Directory.Exists(minimalTestVault)) Directory.Delete(minimalTestVault, true);
            if (Directory.Exists(minimalTestDecrypted)) Directory.Delete(minimalTestDecrypted, true);
            
            Directory.CreateDirectory(minimalTestVault);
            Directory.CreateDirectory(minimalTestDecrypted);
            
            Console.WriteLine("Prepared test directories for minimal testing");
            
            // Check what files exist in the source directory
            Console.WriteLine("\n--- Files in MinimalTestSource ---");
            if (Directory.Exists(minimalTestSource))
            {
                var files = Directory.GetFiles(minimalTestSource);
                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    Console.WriteLine($"  {Path.GetFileName(file)}: {info.Length:N0} bytes");
                }
            }
            else
            {
                Console.WriteLine("MinimalTestSource directory not found, creating it...");
                Directory.CreateDirectory(minimalTestSource);
            }
            
            // Create test files with specific chunk boundaries
            // Chunk size is 32768 bytes (32KB)
            const int CHUNK_SIZE = 32768;
            
            var testCases = new[]
            {
                new { Name = "exactly_1_chunk.bin", Size = CHUNK_SIZE, Description = "Exactly 1 chunk (32KB)" },
                new { Name = "exactly_2_chunks.bin", Size = CHUNK_SIZE * 2, Description = "Exactly 2 chunks (64KB)" },
                new { Name = "exactly_3_chunks.bin", Size = CHUNK_SIZE * 3, Description = "Exactly 3 chunks (96KB)" },
                new { Name = "partial_2nd_chunk.bin", Size = CHUNK_SIZE + 1000, Description = "1 full + 1000 bytes" },
                new { Name = "partial_3rd_chunk.bin", Size = CHUNK_SIZE * 2 + 1500, Description = "2 full + 1500 bytes" },
                new { Name = "large_partial.bin", Size = CHUNK_SIZE * 10 + 12345, Description = "10 full + 12345 bytes" }
            };
            
            Console.WriteLine("\n--- Creating/Verifying Test Files ---");
            foreach (var testCase in testCases)
            {
                string testFilePath = Path.Combine(minimalTestSource, testCase.Name);
                
                if (File.Exists(testFilePath))
                {
                    var existingInfo = new FileInfo(testFilePath);
                    if (existingInfo.Length == testCase.Size)
                    {
                        Console.WriteLine($"Using existing {testCase.Name}: {testCase.Description} ({testCase.Size:N0} bytes)");
                        continue;
                    }
                }
                
                Console.WriteLine($"Creating {testCase.Name}: {testCase.Description} ({testCase.Size:N0} bytes)");
                
                // Create and write the file
                using (var fs = File.Create(testFilePath))
                {
                    // Fill with predictable data pattern for easy verification
                    byte[] pattern = new byte[1024];
                    for (int i = 0; i < pattern.Length; i++)
                    {
                        pattern[i] = (byte)(i % 256);
                    }
                    
                    int bytesWritten = 0;
                    while (bytesWritten < testCase.Size)
                    {
                        int bytesToWrite = Math.Min(pattern.Length, testCase.Size - bytesWritten);
                        fs.Write(pattern, 0, bytesToWrite);
                        bytesWritten += bytesToWrite;
                    }
                } // File is properly closed here
            }
            
            // Create vault for testing
            Console.WriteLine("\n--- Creating Vault ---");
            if (File.Exists(vaultFilePath)) File.Delete(vaultFilePath);
            
            if (vaultFormat == VaultFormat.CryptomatorV8)
            {
                // For Cryptomator V8, create both masterkey.cryptomator and vault.cryptomator
                string vaultDirectory = Path.GetDirectoryName(vaultFilePath) ?? throw new InvalidOperationException("Invalid vault file path");
                Vault.CreateNewCryptomatorV8VaultComplete(vaultDirectory, Password);
                Console.WriteLine($"Vault created: {vaultFilePath} (plus vault.cryptomator)");
            }
            else
            {
                // For UVF format, create single file
                byte[] vaultFileContent = Vault.CreateNewUvfVaultFileContent(Password);
                File.WriteAllBytes(vaultFilePath, vaultFileContent);
                Console.WriteLine($"Vault created: {vaultFilePath}");
            }
            
            byte[] loadedVaultFileContent = File.ReadAllBytes(vaultFilePath);
            
            // Get all files to test (including the existing large file)
            var allTestFiles = Directory.GetFiles(minimalTestSource).Select(f => Path.GetFileName(f)).ToArray();
            
            // Test each file through encryption/decryption
            Console.WriteLine("\n--- Testing Encryption/Decryption ---");
            
            using (Vault vault = LoadVault(loadedVaultFileContent, Password, vaultFormat))
            {
                DirectoryMetadata rootMetadata = vault.GetRootDirectoryMetadata();
                string vaultRootPath = Path.Combine(minimalTestVault, vault.GetRootDirectoryPath());
                Directory.CreateDirectory(vaultRootPath);
                
                // Save root metadata
                byte[] encryptedRootMetadata = vault.EncryptDirectoryMetadata(rootMetadata);
                File.WriteAllBytes(Path.Combine(vaultRootPath, vault.GetDirectoryMetadataFilename()), encryptedRootMetadata);
                
                foreach (var testFileName in allTestFiles)
                {
                    var testFileInfo = new FileInfo(Path.Combine(minimalTestSource, testFileName));
                    
                    Console.WriteLine($"\n=== Testing {testFileName} ===");
                    Console.WriteLine($"File size: {testFileInfo.Length:N0} bytes");
                    
                    string sourceFile = Path.Combine(minimalTestSource, testFileName);
                    
                    // For very large files (>100MB), let's calculate expected chunk info
                    if (testFileInfo.Length > 100_000_000)
                    {
                        long completeChunks = testFileInfo.Length / CHUNK_SIZE;
                        long remainingBytes = testFileInfo.Length % CHUNK_SIZE;
                        Console.WriteLine($"Chunk analysis: {completeChunks:N0} complete chunks + {remainingBytes:N0} remaining bytes");
                    }
                    
                    // Encrypt file
                    Console.WriteLine($"Encrypting {testFileName}...");
                    string encryptedName = vault.EncryptFilename(testFileName, rootMetadata);
                    string encryptedFilePath = Path.Combine(vaultRootPath, encryptedName);
                    
                    var encryptStopwatch = Stopwatch.StartNew();
                    using (var sourceStream = File.OpenRead(sourceFile))
                    using (var targetStream = File.Create(encryptedFilePath))
                    using (var encryptingStream = vault.GetEncryptingStream(targetStream))
                    {
                        sourceStream.CopyTo(encryptingStream);
                    }
                    encryptStopwatch.Stop();
                    
                    long encryptedSize = new FileInfo(encryptedFilePath).Length;
                    long expectedSize = Vault.CalculateExpectedEncryptedSize(testFileInfo.Length);
                    Console.WriteLine($"  Encrypted size: {encryptedSize:N0} bytes (expected: {expectedSize:N0})");
                    Console.WriteLine($"  Size match: {encryptedSize == expectedSize}");
                    Console.WriteLine($"  Encrypt time: {encryptStopwatch.ElapsedMilliseconds:N0} ms");
                    
                    // Decrypt file
                    Console.WriteLine($"Decrypting {testFileName}...");
                    string decryptedFilePath = Path.Combine(minimalTestDecrypted, testFileName);
                    
                    var decryptStopwatch = Stopwatch.StartNew();
                    using (var encryptedStream = File.OpenRead(encryptedFilePath))
                    using (var decryptingStream = vault.GetDecryptingStream(encryptedStream))
                    using (var targetStream = File.Create(decryptedFilePath))
                    {
                        decryptingStream.CopyTo(targetStream);
                    }
                    decryptStopwatch.Stop();
                    
                    // Verify results
                    var decryptedInfo = new FileInfo(decryptedFilePath);
                    
                    Console.WriteLine($"  Original size:  {testFileInfo.Length:N0} bytes");
                    Console.WriteLine($"  Decrypted size: {decryptedInfo.Length:N0} bytes");
                    Console.WriteLine($"  Size match: {testFileInfo.Length == decryptedInfo.Length}");
                    Console.WriteLine($"  Decrypt time: {decryptStopwatch.ElapsedMilliseconds:N0} ms");
                    
                    // Hash comparison
                    Console.WriteLine("  Computing hashes...");
                    var hashStopwatch = Stopwatch.StartNew();
                    string originalHash, decryptedHash;
                    using (var fs = File.OpenRead(sourceFile)) originalHash = FastHash.GetHash(fs);
                    using (var fs = File.OpenRead(decryptedFilePath)) decryptedHash = FastHash.GetHash(fs);
                    hashStopwatch.Stop();
                    
                    bool hashMatch = originalHash == decryptedHash;
                    Console.WriteLine($"  Original hash:  {originalHash}");
                    Console.WriteLine($"  Decrypted hash: {decryptedHash}");
                    Console.WriteLine($"  Hash match: {hashMatch}");
                    Console.WriteLine($"  Hash time: {hashStopwatch.ElapsedMilliseconds:N0} ms");
                    
                    if (!hashMatch)
                    {
                        Console.WriteLine($"  ❌ CORRUPTION DETECTED in {testFileName}!");
                        
                        // Detailed analysis for corrupted files
                        Console.WriteLine($"  Performing byte-by-byte analysis...");
                        AnalyzeCorruption(sourceFile, decryptedFilePath, (int)Math.Min(testFileInfo.Length, int.MaxValue));
                    }
                    else
                    {
                        Console.WriteLine($"  ✅ {testFileName} processed correctly");
                    }
                }
            }
            
            Console.WriteLine("\n===== Minimal Chunk Test Complete =====");
        }

        private static void AnalyzeCorruption(string originalPath, string decryptedPath, int fileSize)
        {
            using var originalFs = File.OpenRead(originalPath);
            using var decryptedFs = File.OpenRead(decryptedPath);
            
            // Check file sizes
            if (originalFs.Length != decryptedFs.Length)
            {
                Console.WriteLine($"    Size mismatch: original={originalFs.Length}, decrypted={decryptedFs.Length}");
                return;
            }
            
            // Find first difference
            byte[] originalBuffer = new byte[4096];
            byte[] decryptedBuffer = new byte[4096];
            long position = 0;
            long firstDifference = -1;
            
            while (position < originalFs.Length)
            {
                int originalRead = originalFs.Read(originalBuffer, 0, originalBuffer.Length);
                int decryptedRead = decryptedFs.Read(decryptedBuffer, 0, decryptedBuffer.Length);
                
                if (originalRead != decryptedRead)
                {
                    Console.WriteLine($"    Read size mismatch at position {position}");
                    break;
                }
                
                for (int i = 0; i < originalRead; i++)
                {
                    if (originalBuffer[i] != decryptedBuffer[i])
                    {
                        if (firstDifference == -1)
                        {
                            firstDifference = position + i;
                            Console.WriteLine($"    First difference at byte {firstDifference}");
                            Console.WriteLine($"    Original: 0x{originalBuffer[i]:X2}, Decrypted: 0x{decryptedBuffer[i]:X2}");
                            
                            // Check if it's at chunk boundary
                            long chunkNum = firstDifference / 32768;
                            long offsetInChunk = firstDifference % 32768;
                            Console.WriteLine($"    Position in chunks: chunk {chunkNum}, offset {offsetInChunk}");
                        }
                    }
                }
                
                position += originalRead;
            }
            
            if (firstDifference == -1)
            {
                Console.WriteLine($"    No byte differences found (hash algorithm issue?)");
            }
        }

        private static void HandleLargeFileTestMode(string vaultFilePath, VaultFormat vaultFormat)
        {
            Console.WriteLine($"===== Testing Large NDPI File (Format: {vaultFormat}) =====");
            
            // Use the same test directories
            string minimalTestSource = @"D:\temp\uvf\MinimalTestSource";
            string minimalTestVault = @"D:\temp\uvf\MinimalTestVault";
            string minimalTestDecrypted = @"D:\temp\uvf\MinimalTestDecrypted";
            
            // Clean up vault and decrypted directories
            if (Directory.Exists(minimalTestVault)) Directory.Delete(minimalTestVault, true);
            if (Directory.Exists(minimalTestDecrypted)) Directory.Delete(minimalTestDecrypted, true);
            
            Directory.CreateDirectory(minimalTestVault);
            Directory.CreateDirectory(minimalTestDecrypted);
            
            // Find the NDPI file
            string ndpiFile = null;
            if (Directory.Exists(minimalTestSource))
            {
                var ndpiFiles = Directory.GetFiles(minimalTestSource, "*.ndpi");
                if (ndpiFiles.Length > 0)
                {
                    ndpiFile = ndpiFiles[0];
                    Console.WriteLine($"Found NDPI file: {Path.GetFileName(ndpiFile)}");
                }
            }
            
            if (ndpiFile == null)
            {
                Console.WriteLine($"❌ No NDPI file found in {minimalTestSource}");
                Console.WriteLine("Please ensure the 2GB NDPI file is copied to the MinimalTestSource directory.");
                return;
            }
            
            var fileInfo = new FileInfo(ndpiFile);
            Console.WriteLine($"File size: {fileInfo.Length:N0} bytes ({fileInfo.Length / (1024.0 * 1024.0 * 1024.0):F2} GB)");
            
            // Calculate chunk information
            const int CHUNK_SIZE = 32768;
            long completeChunks = fileInfo.Length / CHUNK_SIZE;
            long remainingBytes = fileInfo.Length % CHUNK_SIZE;
            Console.WriteLine($"Chunk analysis: {completeChunks:N0} complete chunks + {remainingBytes:N0} remaining bytes");
            
            // Create vault
            Console.WriteLine("\n--- Creating Vault ---");
            if (File.Exists(vaultFilePath)) File.Delete(vaultFilePath);
            
            if (vaultFormat == VaultFormat.CryptomatorV8)
            {
                // For Cryptomator V8, create both masterkey.cryptomator and vault.cryptomator
                string vaultDirectory = Path.GetDirectoryName(vaultFilePath) ?? throw new InvalidOperationException("Invalid vault file path");
                Vault.CreateNewCryptomatorV8VaultComplete(vaultDirectory, Password);
                Console.WriteLine($"Vault created: {vaultFilePath} (plus vault.cryptomator)");
            }
            else
            {
                // For UVF format, create single file
                byte[] vaultFileContent = Vault.CreateNewUvfVaultFileContent(Password);
                File.WriteAllBytes(vaultFilePath, vaultFileContent);
                Console.WriteLine($"Vault created: {vaultFilePath}");
            }
            
            byte[] loadedVaultFileContent = File.ReadAllBytes(vaultFilePath);
            
            using (Vault vault = LoadVault(loadedVaultFileContent, Password, vaultFormat))
            {
                DirectoryMetadata rootMetadata = vault.GetRootDirectoryMetadata();
                string vaultRootPath = Path.Combine(minimalTestVault, vault.GetRootDirectoryPath());
                Directory.CreateDirectory(vaultRootPath);
                
                // Save root metadata
                byte[] encryptedRootMetadata = vault.EncryptDirectoryMetadata(rootMetadata);
                File.WriteAllBytes(Path.Combine(vaultRootPath, vault.GetDirectoryMetadataFilename()), encryptedRootMetadata);
                
                string fileName = Path.GetFileName(ndpiFile);
                Console.WriteLine($"\n=== Testing {fileName} ===");
                
                // Encrypt file
                Console.WriteLine($"Encrypting {fileName}...");
                string encryptedName = vault.EncryptFilename(fileName, rootMetadata);
                string encryptedFilePath = Path.Combine(vaultRootPath, encryptedName);
                
                var encryptStopwatch = Stopwatch.StartNew();
                using (var sourceStream = File.OpenRead(ndpiFile))
                using (var targetStream = File.Create(encryptedFilePath))
                using (var encryptingStream = vault.GetEncryptingStream(targetStream))
                {
                    sourceStream.CopyTo(encryptingStream);
                }
                encryptStopwatch.Stop();
                
                long encryptedSize = new FileInfo(encryptedFilePath).Length;
                long expectedSize = Vault.CalculateExpectedEncryptedSize(fileInfo.Length);
                Console.WriteLine($"  Encrypted size: {encryptedSize:N0} bytes (expected: {expectedSize:N0})");
                Console.WriteLine($"  Size match: {encryptedSize == expectedSize}");
                Console.WriteLine($"  Encrypt time: {encryptStopwatch.ElapsedMilliseconds:N0} ms ({encryptStopwatch.Elapsed.TotalSeconds:F1} seconds)");
                
                // Decrypt file
                Console.WriteLine($"Decrypting {fileName}...");
                string decryptedFilePath = Path.Combine(minimalTestDecrypted, fileName);
                
                var decryptStopwatch = Stopwatch.StartNew();
                using (var encryptedStream = File.OpenRead(encryptedFilePath))
                using (var decryptingStream = vault.GetDecryptingStream(encryptedStream))
                using (var targetStream = File.Create(decryptedFilePath))
                {
                    decryptingStream.CopyTo(targetStream);
                }
                decryptStopwatch.Stop();
                
                // Verify results
                var decryptedInfo = new FileInfo(decryptedFilePath);
                
                Console.WriteLine($"  Original size:  {fileInfo.Length:N0} bytes");
                Console.WriteLine($"  Decrypted size: {decryptedInfo.Length:N0} bytes");
                Console.WriteLine($"  Size match: {fileInfo.Length == decryptedInfo.Length}");
                Console.WriteLine($"  Decrypt time: {decryptStopwatch.ElapsedMilliseconds:N0} ms ({decryptStopwatch.Elapsed.TotalSeconds:F1} seconds)");
                
                // Hash comparison
                Console.WriteLine("  Computing hashes...");
                var hashStopwatch = Stopwatch.StartNew();
                string originalHash, decryptedHash;
                using (var fs = File.OpenRead(ndpiFile)) originalHash = FastHash.GetHash(fs);
                using (var fs = File.OpenRead(decryptedFilePath)) decryptedHash = FastHash.GetHash(fs);
                hashStopwatch.Stop();
                
                bool hashMatch = originalHash == decryptedHash;
                Console.WriteLine($"  Original hash:  {originalHash}");
                Console.WriteLine($"  Decrypted hash: {decryptedHash}");
                Console.WriteLine($"  Hash match: {hashMatch}");
                Console.WriteLine($"  Hash time: {hashStopwatch.ElapsedMilliseconds:N0} ms ({hashStopwatch.Elapsed.TotalSeconds:F1} seconds)");
                
                if (!hashMatch)
                {
                    Console.WriteLine($"  ❌ CORRUPTION DETECTED in {fileName}!");
                    
                    // Enhanced analysis for the large file
                    Console.WriteLine($"  Performing detailed corruption analysis...");
                    
                    try
                    {
                        using var originalFs = new FileStream(ndpiFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var decryptedFs = new FileStream(decryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        
                        Console.WriteLine($"  File sizes: Original={originalFs.Length:N0}, Decrypted={decryptedFs.Length:N0}");
                        
                        if (originalFs.Length == decryptedFs.Length)
                        {
                            // Check multiple segments for corruption pattern
                            var segmentPositions = new[]
                            {
                                0L, // Start
                                fileInfo.Length / 4, // 25%
                                fileInfo.Length / 2, // 50%
                                3 * fileInfo.Length / 4, // 75%
                                Math.Max(0, fileInfo.Length - 4096) // Near end
                            };
                            
                            foreach (var position in segmentPositions)
                            {
                                originalFs.Seek(position, SeekOrigin.Begin);
                                decryptedFs.Seek(position, SeekOrigin.Begin);
                                
                                byte[] originalSegment = new byte[4096];
                                byte[] decryptedSegment = new byte[4096];
                                
                                int originalRead = originalFs.Read(originalSegment, 0, 4096);
                                int decryptedRead = decryptedFs.Read(decryptedSegment, 0, 4096);
                                
                                bool segmentMatch = originalRead == decryptedRead && 
                                                  originalSegment.Take(originalRead).SequenceEqual(decryptedSegment.Take(decryptedRead));
                                
                                double percentPos = (double)position / fileInfo.Length * 100;
                                Console.WriteLine($"  Position {percentPos:F1}% ({position:N0}): {(segmentMatch ? "MATCH" : "MISMATCH")}");
                                
                                if (!segmentMatch && originalRead == decryptedRead)
                                {
                                    // Find first differing byte
                                    for (int i = 0; i < originalRead; i++)
                                    {
                                        if (originalSegment[i] != decryptedSegment[i])
                                        {
                                            Console.WriteLine($"    First difference at offset {i}: 0x{originalSegment[i]:X2} vs 0x{decryptedSegment[i]:X2}");
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ❌ Error during corruption analysis: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"  ✅ {fileName} processed correctly - NO CORRUPTION DETECTED!");
                }
            }
            
            Console.WriteLine("\n===== Large File Test Complete =====");
        }

        /// <summary>
        /// Scans the entire vault structure and decrypts all directories that contain dirid.c9r files.
        /// This is used for Cryptomator vaults where files might be scattered across multiple directories.
        /// </summary>
        private static long ScanAndDecryptAllDirectories(Vault vault, string vaultBasePath, string targetDecryptedPath)
        {
            long totalBytesProcessed = 0;
            string metadataFilename = vault.GetDirectoryMetadataFilename();
            
            // Find all directories that contain metadata files (these are encrypted directories)
            var encryptedDirectories = FindAllEncryptedDirectories(vaultBasePath, metadataFilename);
            
            Console.WriteLine($"Found {encryptedDirectories.Count} encrypted directories in vault:");
            foreach (var encryptedDir in encryptedDirectories)
            {
                Console.WriteLine($"  - {encryptedDir}");
            }
            
            // Process each encrypted directory
            foreach (var encryptedDirPath in encryptedDirectories)
            {
                try
                {
                    Console.WriteLine($"\n--- Processing encrypted directory: {encryptedDirPath} ---");
                    
                    // Load and decrypt the directory's metadata
                    string metadataPath = Path.Combine(encryptedDirPath, metadataFilename);
                    byte[] encryptedMetadata = File.ReadAllBytes(metadataPath);
                    DirectoryMetadata dirMetadata = vault.DecryptDirectoryMetadata(encryptedMetadata);
                    
                    Console.WriteLine($"Directory metadata loaded successfully (DirId: {dirMetadata.DirId})");
                    
                    // Create a subdirectory in the target path for this encrypted directory
                    string dirName = Path.GetFileName(encryptedDirPath);
                    string targetSubDir = Path.Combine(targetDecryptedPath, $"DecryptedDir_{dirName}");
                    Directory.CreateDirectory(targetSubDir);
                    
                    // Decrypt this directory
                    long bytesFromThisDir = DecryptDirectory(vault, dirMetadata, encryptedDirPath, targetSubDir);
                    totalBytesProcessed += bytesFromThisDir;
                    
                    Console.WriteLine($"Processed {bytesFromThisDir:N0} bytes from {encryptedDirPath}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"❌ ERROR processing encrypted directory {encryptedDirPath}: {ex.Message}");
                }
            }
            
            return totalBytesProcessed;
        }
        
        /// <summary>
        /// Recursively finds all directories that contain encrypted directory metadata files.
        /// </summary>
        private static List<string> FindAllEncryptedDirectories(string basePath, string metadataFilename)
        {
            var encryptedDirs = new List<string>();
            
            try
            {
                Console.WriteLine($"Scanning for directories containing '{metadataFilename}' in: {basePath}");
                
                // Check all subdirectories recursively
                var allDirectories = Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories);
                Console.WriteLine($"Found {allDirectories.Length} total directories to check");
                
                foreach (string dirPath in allDirectories)
                {
                    string metadataPath = Path.Combine(dirPath, metadataFilename);
                    Console.WriteLine($"  Checking: {dirPath}");
                    Console.WriteLine($"    Looking for: {metadataPath}");
                    
                    if (File.Exists(metadataPath))
                    {
                        Console.WriteLine($"    ✅ Found metadata file! Adding directory: {dirPath}");
                        encryptedDirs.Add(dirPath);
                    }
                    else
                    {
                        Console.WriteLine($"    ❌ No metadata file found");
                    }
                }
                
                Console.WriteLine($"Total encrypted directories found: {encryptedDirs.Count}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ ERROR scanning for encrypted directories in {basePath}: {ex.Message}");
            }
            
            return encryptedDirs;
        }

        private static void TestManualDecryption(Vault vault, DirectoryMetadata rootMetadata, string testDir)
        {
            Console.WriteLine($"Testing manual decryption of directory: {testDir}");
            
            // Decrypt the directory using the root metadata
            string decryptedDir = Path.Combine(testDir, "DecryptedDir");
            Directory.CreateDirectory(decryptedDir);
            
            long bytesProcessed = DecryptDirectory(vault, rootMetadata, testDir, decryptedDir);
            Console.WriteLine($"Processed {bytesProcessed:N0} bytes from {testDir}");
            
            // Verify the decrypted directory
            Console.WriteLine($"Verifying decrypted directory: {decryptedDir}");
            if (Directory.Exists(decryptedDir))
            {
                var files = Directory.GetFiles(decryptedDir);
                if (files.Length > 0)
                {
                    Console.WriteLine($"Decrypted files found: {files.Length}");
                    foreach (var file in files)
                    {
                        Console.WriteLine($"  - {Path.GetFileName(file)}");
                    }
                }
                else
                {
                    Console.WriteLine("Decrypted directory is empty.");
                }
            }
            else
            {
                Console.WriteLine("Decrypted directory does not exist.");
            }
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

        private static List<string> FindAllDirectories(string basePath)
        {
            var directories = new List<string>();
            
            if (!Directory.Exists(basePath))
                return directories;

            // Recursively find all directories under d/
            foreach (var subDir in Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories))
            {
                // Check if this directory contains a metadata file
                var metadataFiles = Directory.GetFiles(subDir, "*.c9r").Where(f => Path.GetFileName(f) == "dirid.c9r");
                if (metadataFiles.Any())
                {
                    directories.Add(subDir);
                }
            }

            return directories;
        }

        private static string? FindRootDirectoryByKnownFiles(Vault vault, List<string> directories, string[] knownFiles)
        {
            var rootMetadata = vault.GetRootDirectoryMetadata();
            Console.WriteLine($"Testing {directories.Count} directories to find root by decrypting with root metadata...");
            
            foreach (var directory in directories)
            {
                try
                {
                    Console.WriteLine($"Testing directory: {Path.GetFileName(directory)}");
                    
                    // Get all encrypted files in this directory
                    var encryptedFiles = Directory.GetFiles(directory, "*.c9r")
                        .Where(f => !f.EndsWith(vault.GetDirectoryMetadataFilename()))
                        .ToArray();
                    
                    Console.WriteLine($"  Found {encryptedFiles.Length} encrypted files to test");
                    
                    // Check if this is the exact calculated root directory (precise match)
                    var metadataPath = Path.Combine(directory, vault.GetDirectoryMetadataFilename());
                    var calculatedRootPath = vault.GetDirectoryPath(rootMetadata);
                    // Normalize paths for comparison
                    var normalizedCalculatedPath = calculatedRootPath.Replace('/', Path.DirectorySeparatorChar);
                    var normalizedActualPath = directory.Replace(VaultFolderPath, "").TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    bool isExactRootMatch = normalizedCalculatedPath.Equals(normalizedActualPath, StringComparison.OrdinalIgnoreCase);
                    
                    DirectoryMetadata directoryMetadata;
                    
                    if (isExactRootMatch && vault.IsCryptomatorV8())
                    {
                        // For the exact root directory in Cryptomator v8, use programmatically generated metadata
                        Console.WriteLine($"  This is the exact calculated root directory - using programmatic root metadata");
                        directoryMetadata = rootMetadata;
                    }
                    else if (File.Exists(metadataPath))
                    {
                        // For subdirectories, load metadata from file
                        try
                        {
                            var metadataBytes = File.ReadAllBytes(metadataPath);
                            directoryMetadata = vault.DecryptDirectoryMetadata(metadataBytes);
                            Console.WriteLine($"  Loaded directory metadata from file (this is a subdirectory)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  Failed to decrypt directory metadata: {ex.Message}");
                            continue;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  No metadata file found in directory");
                        continue;
                    }
                    
                    // Try to decrypt filenames using this directory's metadata
                    foreach (var encryptedFile in encryptedFiles)
                    {
                        try
                        {
                            var encryptedFilename = Path.GetFileName(encryptedFile);
                            Console.WriteLine($"  Testing decryption of: {encryptedFilename}");
                            
                            var decryptedFilename = vault.DecryptFilename(encryptedFilename, directoryMetadata);
                            Console.WriteLine($"    SUCCESS: {encryptedFilename} -> {decryptedFilename}");
                            
                            if (knownFiles.Contains(decryptedFilename))
                            {
                                Console.WriteLine($"    MATCH! Found known file: {decryptedFilename}");
                                Console.WriteLine($"Root directory identified: {directory}");
                                return directory;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"    Failed to decrypt {Path.GetFileName(encryptedFile)}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ERROR testing directory {directory}: {ex.Message}");
                }
            }
            
            Console.WriteLine("No root directory found by known files");
            return null;
        }

        /// <summary>
        /// Tests the v3 hash algorithm (HMAC-SHA256) to see if it produces the correct directory hashes
        /// </summary>
        private static void TestV3HashAlgorithm(Vault vault)
        {
            try
            {
                Console.WriteLine("Testing Cryptomator backup directory ID system (dirid.c9r)...");
                
                // First, let's decode the JWT from vault.cryptomator
                string vaultJwtPath = Path.Combine(VaultFolderPath, "vault.cryptomator");
                if (File.Exists(vaultJwtPath))
                {
                    string jwt = File.ReadAllText(vaultJwtPath);
                    Console.WriteLine($"JWT found: {jwt.Substring(0, Math.Min(50, jwt.Length))}...");
                    
                    // Decode the JWT payload (middle part)
                    var jwtParts = jwt.Split('.');
                    if (jwtParts.Length == 3)
                    {
                        string payload = jwtParts[1];
                        // Add padding if needed for base64url
                        while (payload.Length % 4 != 0)
                            payload += "=";
                        
                        try
                        {
                            byte[] payloadBytes = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/'));
                            string payloadJson = Encoding.UTF8.GetString(payloadBytes);
                            Console.WriteLine($"JWT Payload: {payloadJson}");
                            
                            // Parse the JSON to see the format version
                            if (payloadJson.Contains("\"format\":8"))
                            {
                                Console.WriteLine("✅ Confirmed: This is Cryptomator format 8");
                            }
                            if (payloadJson.Contains("\"cipherCombo\":\"SIV_GCM\""))
                            {
                                Console.WriteLine("✅ Confirmed: This is SIV_GCM cipher combo (v8)");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Failed to decode JWT payload: {ex.Message}");
                        }
                    }
                }
                
                Console.WriteLine("\n=== BACKUP DIRECTORY ID ANALYSIS ===");
                Console.WriteLine("Key insight from official docs:");
                Console.WriteLine("- dirid.c9r contains the PARENT directory ID, not current directory ID");
                Console.WriteLine("- Root directory has no metadata file (empty string ID)");
                Console.WriteLine("- We need to work backwards from child to parent");
                
                // Test the backup directory ID hypothesis
                var allDirectories = FindAllDirectories(Path.Combine(VaultFolderPath, "d"));
                Console.WriteLine($"\nFound {allDirectories.Count} directories with dirid.c9r files");
                
                // Read the dirid.c9r files to see what parent IDs they contain
                foreach (var dir in allDirectories)
                {
                    var dirName = Path.GetFileName(dir);
                    var diridPath = Path.Combine(dir, "dirid.c9r");
                    
                    if (File.Exists(diridPath))
                    {
                        try
                        {
                            Console.WriteLine($"\nAnalyzing directory: {dirName}");
                            
                            // Read and decrypt the dirid.c9r file (it's encrypted like a regular file)
                            byte[] encryptedData = File.ReadAllBytes(diridPath);
                            Console.WriteLine($"  dirid.c9r size: {encryptedData.Length} bytes");
                            
                            // According to docs, dirid.c9r contains the parent directory ID
                            // We need to decrypt it as a regular file to get the parent ID
                            
                            // For now, let's see if we can identify which directory might be root
                            // by checking if any directory's calculated hash matches our known directories
                            
                            var files = Directory.GetFiles(dir, "*.c9r").Where(f => !f.EndsWith("dirid.c9r")).ToArray();
                            Console.WriteLine($"  Files in directory: {files.Length}");
                            
                            if (files.Length == 4) // Root should have 4 files
                            {
                                Console.WriteLine($"  → This could be ROOT directory (4 files)");
                                
                                // Test if this directory matches the root hash calculation
                                var rootMetadata = vault.GetRootDirectoryMetadata();
                                var calculatedRootPath = vault.GetDirectoryPath(rootMetadata);
                                
                                if (calculatedRootPath.EndsWith(dirName))
                                {
                                    Console.WriteLine($"  ✅ MATCH! This directory matches calculated root path");
                                }
                                else
                                {
                                    Console.WriteLine($"  ❌ No match with calculated root path");
                                    Console.WriteLine($"     Calculated: {calculatedRootPath}");
                                    Console.WriteLine($"     Actual: d/{dir.Substring(dir.LastIndexOf('\\') + 1)}");
                                }
                            }
                            else if (files.Length == 3)
                            {
                                Console.WriteLine($"  → This could be SUBFOLDER (3 files)");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  ❌ Error analyzing {dirName}: {ex.Message}");
                        }
                    }
                }
                
                Console.WriteLine("\n=== ROOT DIRECTORY IDENTIFICATION STRATEGY ===");
                Console.WriteLine("Based on official docs and backup directory ID system:");
                Console.WriteLine("1. Root directory has empty string ID (empty byte array in UTF-8)");
                Console.WriteLine("2. Root directory has no dir.c9r or dirid.c9r file");
                Console.WriteLine("3. Root directory is calculated using: base32(sha1(aesSiv('', null, keys)))");
                Console.WriteLine("4. Our algorithm should be correct, but implementation might have subtle differences");
                
                // Let's test our root calculation step by step
                TestRootDirectoryCalculationStepByStep(vault);
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in algorithm analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Tests the root directory calculation step by step to identify where our algorithm differs
        /// </summary>
        private static void TestRootDirectoryCalculationStepByStep(Vault vault)
        {
            Console.WriteLine("\n=== STEP-BY-STEP ROOT CALCULATION DEBUG ===");
            
            try
            {
                // Step 1: Root directory ID is empty string
                string rootDirId = ""; // Empty string as per official docs
                byte[] rootDirIdBytes = Encoding.UTF8.GetBytes(rootDirId);
                Console.WriteLine($"Step 1 - Root directory ID: '{rootDirId}' (length: {rootDirIdBytes.Length} bytes)");
                
                // We need access to the actual masterkey to test this properly
                // For now, let's document what the calculation should be
                Console.WriteLine("\nStep 2 - AES-SIV encryption:");
                Console.WriteLine("  Input: empty byte array (UTF-8 of empty string)");
                Console.WriteLine("  Associated data: null");
                Console.WriteLine("  Keys: encryptionMasterKey + macMasterKey");
                
                Console.WriteLine("\nStep 3 - SHA-1 hash:");
                Console.WriteLine("  Input: AES-SIV encrypted result from step 2");
                Console.WriteLine("  Output: 20-byte SHA-1 hash");
                
                Console.WriteLine("\nStep 4 - Base32 encoding:");
                Console.WriteLine("  Input: 20-byte SHA-1 hash from step 3");
                Console.WriteLine("  Output: 32-character Base32 string");
                
                Console.WriteLine("\nStep 5 - Directory path construction:");
                Console.WriteLine("  Format: d/[first2chars]/[remaining30chars]");
                
                Console.WriteLine("\n🔍 COMPARISON:");
                Console.WriteLine($"  Our calculation:  d/VT/4LAU6OWKNCOOKXTMRWW3E7V35JDCBP (old v2)");
                Console.WriteLine($"  Our calculation:  d/AR/6ZC34NDMUCRKBBCGJHUIH6DMK2RIZA (new v3)");
                Console.WriteLine($"  Actual directory: d/V4/LQRSXGFPTO6ERYLTNDPKKYHWNE2T4E");
                
                Console.WriteLine("\n💡 HYPOTHESIS:");
                Console.WriteLine("The difference suggests either:");
                Console.WriteLine("1. Our AES-SIV implementation produces different results than Java");
                Console.WriteLine("2. The key derivation from masterkey is different");
                Console.WriteLine("3. There's a subtle difference in how empty string is processed");
                Console.WriteLine("4. Cryptomator v8 uses a different variant of the algorithm");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in step-by-step calculation: {ex.Message}");
            }
        }
    }
} 