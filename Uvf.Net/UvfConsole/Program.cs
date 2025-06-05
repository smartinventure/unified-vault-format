using UvfLib;
using UvfLib.VaultHelpers;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
                Console.WriteLine("Usage: UvfConsole <encrypt|decrypt|testrun|analyze-jwt|test-our-jwt|test-signature|test-mackey|test-jwt-compare|test-own-validation|test-mac-derivation|test-concatenated-key|decrypt-real-dirid> [--uvf|--cryptomator]");
                Console.WriteLine("  --uvf        : Use Universal Vault Format (default)");
                Console.WriteLine("  --cryptomator: Use Cryptomator V8 format for legacy compatibility");
                Console.WriteLine("  encrypt      : Encrypt files from source to vault");
                Console.WriteLine("  decrypt      : Decrypt files from vault to target");
                Console.WriteLine("  testrun      : Full round-trip test (encrypt then decrypt with verification)");
                Console.WriteLine("  analyze-jwt  : Analyze real Cryptomator JWT signature process");
                Console.WriteLine("  test-our-jwt : Test our generated JWT");
                Console.WriteLine("  test-signature: Test signature process");
                Console.WriteLine("  test-mackey  : Test MAC key comparison");
                Console.WriteLine("  test-jwt-compare: Test comprehensive JWT comparison");
                Console.WriteLine("  test-own-validation: Test own vault validation");
                Console.WriteLine("  test-mac-derivation: Test MAC key derivation comparison");
                Console.WriteLine("  test-concatenated-key: Test concatenated key validation");
                Console.WriteLine("  decrypt-real-dirid: Decrypt real Cryptomator dirid.c9r file");
                return;
            }

            string mode = args[0].ToLowerInvariant();
            VaultFormat vaultFormat = ParseVaultFormat(args);
            
            Console.WriteLine($"Mode: {mode}");
            
            if (mode == "analyze-jwt")
            {
                AnalyzeRealCryptomatorJWT();
                return;
            }
            
            if (mode == "test-our-jwt")
            {
                TestOurGeneratedJWT();
                return;
            }
            
            if (mode == "test-signature")
            {
                TestSignatureProcess();
                return;
            }
            
            if (mode == "test-mackey")
            {
                TestMacKeyComparison();
                return;
            }
            
            if (mode == "test-jwt-compare")
            {
                TestJWTComparison();
                return;
            }
            
            if (mode == "test-own-validation")
            {
                TestOwnVaultValidation();
                return;
            }
            
            if (mode == "test-mac-derivation")
            {
                TestMacKeyDerivation();
                return;
            }
            
            if (mode == "test-concatenated-key")
            {
                TestConcatenatedKeyValidation();
                return;
            }
            
            if (mode == "decrypt-real-dirid")
            {
                DecryptRealDiridFile();
                return;
            }
            
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

        private static long ProcessDirectory(Vault vault, string sourceDir, DirectoryMetadata currentDirMetadata, string currentDirPhysicalVaultPath, string metadataPath = null)
        {
            // dirMetadataStorePath is where metadata *about* currentDir (like its dir.uvf) would be stored.
            // For UVF, this is relevant. For Cryptomator, dirid.c9r is in the content path.
            string dirMetadataStorePath = metadataPath ?? currentDirPhysicalVaultPath;
            
            Console.WriteLine($"Processing directory: {sourceDir} -> {currentDirPhysicalVaultPath}");
            if (metadataPath != null && metadataPath != currentDirPhysicalVaultPath) // Only log if metadataPath is distinct and provided
            {
                Console.WriteLine($"  (Metadata about this directory, if any, would be in: {dirMetadataStorePath})");
            }
            long bytesProcessedInThisCall = 0;

            // Save the current directory's metadata (dir.uvf for UVF format)
            // This is skipped for Cryptomator v8 root, and Cryptomator v8 doesn't use dir.c9r in this way.
            bool isRootDirectory = currentDirMetadata.Equals(vault.GetRootDirectoryMetadata());
            
            if (!vault.IsCryptomatorV8()) // Only for UVF format
            {
                byte[] encryptedMetadata = vault.EncryptDirectoryMetadata(currentDirMetadata);
                string dirMetadataFilePath = Path.Combine(dirMetadataStorePath, vault.GetDirectoryMetadataFilename());
                File.WriteAllBytes(dirMetadataFilePath, encryptedMetadata);
                Console.WriteLine($"  UVF Metadata ({vault.GetDirectoryMetadataFilename()}) saved to: {dirMetadataFilePath}");
            }
            else if (isRootDirectory) // For Cryptomator V8 root
            {
                Console.WriteLine($"  Skipping UVF-style metadata file save for Cryptomator v8 root directory.");
            }
            // For Cryptomator V8 subdirectories, there's no dir.c9r equivalent to dir.uvf.
            // The necessary dirid.c9r (containing the directory's own ID) is handled next.

            // For Cryptomator v8, EACH directory (root or subdir) needs a dirid.c9r file in its *content* directory,
            // containing its own encrypted DirID.
            if (vault.IsCryptomatorV8())
            {
                // The content path for currentDirMetadata is currentDirPhysicalVaultPath.
                string diridFilePath = Path.Combine(currentDirPhysicalVaultPath, "dirid.c9r"); 
                
                using (FileStream diridStream = File.Create(diridFilePath))
                using (Stream encryptingStream = vault.GetEncryptingStream(diridStream))
                {
                    string actualDirIdToEncrypt;
                    if (isRootDirectory) 
                    {
                        actualDirIdToEncrypt = ""; // Root directory ID is empty string for Cryptomator
                        Console.WriteLine($"  Preparing to write empty DirID for root's dirid.c9r.");
                    }
                    else
                    {
                        actualDirIdToEncrypt = currentDirMetadata.DirId;
                        if (string.IsNullOrEmpty(actualDirIdToEncrypt))
                        {
                            // This is generally unexpected for a non-root Cryptomator directory if DirIds are always generated UUIDs.
                            Console.WriteLine($"  Warning: DirId for Cryptomator non-root directory {sourceDir} is null or empty. Using empty string for its dirid.c9r content.");
                            actualDirIdToEncrypt = ""; 
                        }
                    }
                    
                    byte[] dirIdBytes = System.Text.Encoding.ASCII.GetBytes(actualDirIdToEncrypt);
                    encryptingStream.Write(dirIdBytes, 0, dirIdBytes.Length);
                    // Ensure stream is flushed and closed properly by the using statement to finalize encryption.
                }
                // Log file size after stream is closed and file is written
                long writtenDiridSize = -1;
                try { writtenDiridSize = new FileInfo(diridFilePath).Length; } catch {}
                Console.WriteLine($"  Cryptomator dirid.c9r for DirId '{currentDirMetadata.DirId}' (content: '{ (isRootDirectory ? "" : currentDirMetadata.DirId) }') saved to: {diridFilePath} (Size: {writtenDiridSize} bytes)");
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
                
                bool processSubDir = true;
                DirectoryMetadata existingSubDirMetadata = null;

                // For both UVF and Cryptomator V8: encryptedSubDirName should be a DIRECTORY
                // The encrypted directory name becomes the actual directory name in the filesystem
                string subDirPhysicalVaultPath = Path.Combine(currentDirPhysicalVaultPath, encryptedSubDirName);

                if (vault.IsCryptomatorV8())
                {
                    // For Cryptomator V8: Create an actual directory with the encrypted name
                    // Check if the encrypted directory already exists
                    if (Directory.Exists(subDirPhysicalVaultPath))
                    {
                        Console.WriteLine($"    Encrypted directory already exists: {encryptedSubDirName}");
                        processSubDir = false; // We can reuse the existing structure
                        
                        // TODO: In a complete implementation, we might want to verify the dirid.c9r content
                        // For now, we'll assume it's correct and proceed
                    }

                    if (processSubDir)
                    {
                        Console.WriteLine($"    Creating encrypted directory: {encryptedSubDirName}");
                        Directory.CreateDirectory(subDirPhysicalVaultPath);
                        Console.WriteLine($"    Encrypted directory created successfully");
                    }
                    
                    // Create dir.c9r file in the reference directory pointing to the content directory
                    // This tells Cryptomator where to find the actual subdirectory content
                    string dirC9rFilePath = Path.Combine(subDirPhysicalVaultPath, "dir.c9r");
                    if (!File.Exists(dirC9rFilePath))
                    {
                        Console.WriteLine($"    Creating dir.c9r file pointing to content directory");
                        using (FileStream dirC9rStream = File.Create(dirC9rFilePath))
                        using (Stream encryptingStream = vault.GetEncryptingStream(dirC9rStream))
                        {
                            // Write the subdirectory's DirId, which points to where the content is stored
                            byte[] dirIdBytes = System.Text.Encoding.ASCII.GetBytes(subDirMetadata.DirId);
                            encryptingStream.Write(dirIdBytes, 0, dirIdBytes.Length);
                        }
                        Console.WriteLine($"    dir.c9r file created successfully");
                    }
                }
                else
                {
                    // For UVF format: keep the existing behavior (create directory for metadata container)
                    string subDirMetadataPath = Path.Combine(subDirPhysicalVaultPath, vault.GetDirectoryMetadataFilename());

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
                        Console.WriteLine($"    Creating new UVF subdirectory structure");
                        Directory.CreateDirectory(subDirPhysicalVaultPath);
                    }
                }

                // For both UVF and Cryptomator v8, determine the actual content path where files should be stored
                string actualContentPath;
                if (vault.IsCryptomatorV8())
                {
                    // For Cryptomator V8: Content is stored in separate directory calculated from DirId (like UVF)
                    // The encrypted directory name becomes a "reference" directory containing only dirid.c9r
                    actualContentPath = Path.Combine(VaultFolderPath, vault.GetDirectoryPath(subDirMetadata));
                    Console.WriteLine($"    Cryptomator v8: Content will be stored in: {actualContentPath}");
                    Console.WriteLine($"    Directory ID from metadata: {subDirMetadata.DirId}");
                    
                    // During encryption, create the content directory
                    Directory.CreateDirectory(actualContentPath);
                }
                else
                {
                    // For UVF format, use separate content directory calculated from subdirectory's dirId
                    actualContentPath = Path.Combine(VaultFolderPath, vault.GetDirectoryPath(subDirMetadata));
                    Console.WriteLine($"    UVF: Content will be stored in: {actualContentPath}");
                    Console.WriteLine($"    Directory ID from metadata: {subDirMetadata.DirId}");
                    
                    // During encryption, create the content directory
                    Directory.CreateDirectory(actualContentPath);
                }

                // Recursively process the subdirectory using the correct content path
                // For both UVF and Cryptomator V8: subdirectory content goes to separate directory calculated from DirId
                // The encrypted directory name (subDirPhysicalVaultPath) contains only metadata
                bytesProcessedInThisCall += ProcessDirectory(vault, sourceSubDirPath, subDirMetadata, actualContentPath, subDirPhysicalVaultPath);
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

                // For Cryptomator v8, only process files with .c9r extension
                if (vault.IsCryptomatorV8() && !encryptedName.EndsWith(".c9r"))
                {
                    Console.WriteLine($"    Skipping non-encrypted file: {encryptedName}");
                    continue;
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

                // For Cryptomator v8, only process directories with .c9r extension
                if (vault.IsCryptomatorV8() && !encryptedSubDirName.EndsWith(".c9r"))
                {
                    Console.WriteLine($"    Skipping non-encrypted directory: {encryptedSubDirName}");
                    continue;
                }

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

                    // For Cryptomator v8, determine the actual content path where files should be stored
                    string actualContentPath;
                    if (vault.IsCryptomatorV8())
                    {
                        // Calculate the path where the actual directory content should be stored
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
                        // For UVF format, also use separate content directory calculated from subdirectory's dirId
                        actualContentPath = Path.Combine(VaultFolderPath, vault.GetDirectoryPath(subDirMetadata));
                        Console.WriteLine($"    UVF: Actual content path: {actualContentPath}");
                        Console.WriteLine($"    Directory ID from metadata: {subDirMetadata.DirId}");
                        
                        if (!Directory.Exists(actualContentPath))
                        {
                            Console.Error.WriteLine($"    ERROR: Actual content directory not found: {actualContentPath}");
                            continue;
                        }
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

        /// <summary>
        /// Analyzes a real Cryptomator JWT to understand the signature process
        /// </summary>
        private static void AnalyzeRealCryptomatorJWT()
        {
            Console.WriteLine("===== Analyzing Real Cryptomator JWT =====");
            
            // Real JWT from Cryptomator
            string realJWT = "eyJraWQiOiJtYXN0ZXJrZXlmaWxlOm1hc3RlcmtleS5jcnlwdG9tYXRvciIsImFsZyI6IkhTMjU2IiwidHlwIjoiSldUIn0.eyJqdGkiOiI5MzZkNWRkMy1hM2VlLTQwYzQtOWEwZi1lOWNkMGRhOGE5MTIiLCJmb3JtYXQiOjgsImNpcGhlckNvbWJvIjoiU0lWX0dDTSIsInNob3J0ZW5pbmdUaHJlc2hvbGQiOjIyMH0.AdkqHRjU13p3egKQqDsOM9GTO8ICSkv8_AECtpixhfA";
            
            // Real MAC key from masterkey.cryptomator (base64)
            string realMacKeyBase64 = "Im8tmsDJrOnAzEb9clltg1DoJ848IKRrY1II3i+cJgtgiIjQOvoQ0A==";
            
            var parts = realJWT.Split('.');
            if (parts.Length != 3)
            {
                Console.WriteLine("❌ Invalid JWT format");
                return;
            }
            
            string header = parts[0];
            string payload = parts[1]; 
            string expectedSignature = parts[2];
            
            try
            {
                // Decode header and payload
                var headerDecoded = UvfLib.Core.Common.Base64Url.DecodeToString(header);
                var payloadDecoded = UvfLib.Core.Common.Base64Url.DecodeToString(payload);
                
                Console.WriteLine($"📋 Real Header: {headerDecoded}");
                Console.WriteLine($"📋 Real Payload: {payloadDecoded}");
                Console.WriteLine($"📋 Expected Signature: {expectedSignature}");
                
                // Test our JSON generation with the same JTI
                var testPayload = new
                {
                    jti = "936d5dd3-a3ee-40c4-9a0f-e9cd0da8a912", // Use same JTI as real one
                    format = 8,
                    cipherCombo = "SIV_GCM",
                    shorteningThreshold = 220
                };
                
                var jsonOptions = new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = false  // Ensures compact format without spaces
                };
                string ourPayloadJson = System.Text.Json.JsonSerializer.Serialize(testPayload, jsonOptions);
                
                Console.WriteLine($"🔍 Our Payload JSON: {ourPayloadJson}");
                Console.WriteLine($"✅ Payloads Match: {payloadDecoded == ourPayloadJson}");
                
                if (payloadDecoded != ourPayloadJson)
                {
                    Console.WriteLine("❌ PAYLOAD MISMATCH ANALYSIS:");
                    Console.WriteLine($"   Real:  [{payloadDecoded}]");
                    Console.WriteLine($"   Ours:  [{ourPayloadJson}]");
                    
                    // Character-by-character comparison
                    int maxLen = Math.Max(payloadDecoded.Length, ourPayloadJson.Length);
                    for (int i = 0; i < maxLen; i++)
                    {
                        char realChar = i < payloadDecoded.Length ? payloadDecoded[i] : '?';
                        char ourChar = i < ourPayloadJson.Length ? ourPayloadJson[i] : '?';
                        if (realChar != ourChar)
                        {
                            Console.WriteLine($"   Diff at position {i}: Real='{realChar}'({(int)realChar}), Ours='{ourChar}'({(int)ourChar})");
                            break;
                        }
                    }
                }
                
                // Get MAC key bytes
                byte[] macKeyBytes = Convert.FromBase64String(realMacKeyBase64);
                Console.WriteLine($"🔑 MAC Key Length: {macKeyBytes.Length} bytes");
                
                // Create signing input
                string signingInput = $"{header}.{payload}";
                byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);
                
                Console.WriteLine($"📝 Signing Input: {signingInput}");
                Console.WriteLine($"📝 Signing Input Length: {signingInputBytes.Length} bytes");
                
                // Calculate HMAC-SHA256
                byte[] calculatedSignatureBytes;
                using (var hmac = new HMACSHA256(macKeyBytes))
                {
                    calculatedSignatureBytes = hmac.ComputeHash(signingInputBytes);
                }
                
                // Convert to Base64URL
                string calculatedSignature = UvfLib.Core.Common.Base64Url.Encode(calculatedSignatureBytes);
                
                Console.WriteLine($"🔐 Calculated Signature: {calculatedSignature}");
                Console.WriteLine($"🔐 Expected Signature:   {expectedSignature}");
                Console.WriteLine($"✅ Signatures Match: {calculatedSignature == expectedSignature}");
                
                if (calculatedSignature != expectedSignature)
                {
                    Console.WriteLine("❌ SIGNATURE MISMATCH ANALYSIS:");
                    Console.WriteLine($"   - Expected bytes: {Convert.ToHexString(UvfLib.Core.Common.Base64Url.Decode(expectedSignature))}");
                    Console.WriteLine($"   - Calculated bytes: {Convert.ToHexString(calculatedSignatureBytes)}");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error analyzing JWT: {ex.Message}");
            }
            
            Console.WriteLine("===== Analysis Complete =====");
        }

        private static void TestOurGeneratedJWT()
        {
            Console.WriteLine("===== Testing Our Generated JWT =====");
            
            // Our generated JWT from the vault file
            string ourJWT = "eyJraWQiOiJtYXN0ZXJrZXlmaWxlOm1hc3RlcmtleS5jcnlwdG9tYXRvciIsImFsZyI6IkhTMjU2IiwidHlwIjoiSldUIn0.eyJqdGkiOiI1ZGQ1MTNiOC04ZTExLTQwY2UtOWNmNC02YTVkYmU2MDAzYzUiLCJmb3JtYXQiOjgsImNpcGhlckNvbWJvIjoiU0lWX0dDTSIsInNob3J0ZW5pbmdUaHJlc2hvbGQiOjIyMH0.xuZKgunCgXY8WV0BW1NadEJZk2H8BFzfHVGykA1pjkM";
            
            var parts = ourJWT.Split('.');
            if (parts.Length != 3)
            {
                Console.WriteLine("❌ Invalid JWT format");
                return;
            }
            
            try
            {
                // Decode header and payload
                var headerDecoded = UvfLib.Core.Common.Base64Url.DecodeToString(parts[0]);
                var payloadDecoded = UvfLib.Core.Common.Base64Url.DecodeToString(parts[1]);
                
                Console.WriteLine($"📋 Our Header: {headerDecoded}");
                Console.WriteLine($"📋 Our Payload: {payloadDecoded}");
                Console.WriteLine($"📋 Our Signature: {parts[2]}");
                
                // Check if our JSON is compact (no spaces after colons/commas)
                bool isCompact = !payloadDecoded.Contains(": ") && !payloadDecoded.Contains(", ");
                Console.WriteLine($"✅ Compact JSON Format: {isCompact}");
                
                if (!isCompact)
                {
                    Console.WriteLine("❌ Our JSON still has spaces! This will cause signature mismatch.");
                }
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error analyzing our JWT: {ex.Message}");
            }
            
            Console.WriteLine("===== Our JWT Analysis Complete =====");
        }

        private static void TestSignatureProcess()
        {
            Console.WriteLine("===== Testing Our Vault Signature Process =====");
            
            // Test with our own created vault files
            string masterkeyPath = @"D:\temp\uvf\EncryptionTestVault\masterkey.cryptomator";
            string vaultConfigPath = @"D:\temp\uvf\EncryptionTestVault\vault.cryptomator";
            
            if (!File.Exists(masterkeyPath))
            {
                Console.WriteLine($"❌ Masterkey file not found: {masterkeyPath}");
                return;
            }
            
            if (!File.Exists(vaultConfigPath))
            {
                Console.WriteLine($"❌ Vault config file not found: {vaultConfigPath}");
                return;
            }
            
            try
            {
                // Read our masterkey file
                string masterkeyJson = File.ReadAllText(masterkeyPath);
                Console.WriteLine($"📋 Our Masterkey JSON: {masterkeyJson}");
                
                // Parse to get MAC key
                using (JsonDocument doc = JsonDocument.Parse(masterkeyJson))
                {
                    if (doc.RootElement.TryGetProperty("hmacMasterKey", out JsonElement macKeyElement))
                    {
                        string macKeyBase64 = macKeyElement.GetString();
                        Console.WriteLine($"🔑 Our MAC Key (Base64): {macKeyBase64}");
                        
                        byte[] macKeyBytes = Convert.FromBase64String(macKeyBase64!);
                        Console.WriteLine($"🔑 Our MAC Key Length: {macKeyBytes.Length} bytes");
                        
                        // Read our generated vault config
                        string ourJWT = File.ReadAllText(vaultConfigPath);
                        Console.WriteLine($"📋 Our Generated JWT: {ourJWT}");
                        
                        var parts = ourJWT.Split('.');
                        if (parts.Length != 3)
                        {
                            Console.WriteLine("❌ Invalid JWT format");
                            return;
                        }
                        
                        string header = parts[0];
                        string payload = parts[1]; 
                        string actualSignature = parts[2];
                        
                        // Decode header and payload
                        var headerDecoded = UvfLib.Core.Common.Base64Url.DecodeToString(header);
                        var payloadDecoded = UvfLib.Core.Common.Base64Url.DecodeToString(payload);
                        
                        Console.WriteLine($"📋 Our Header: {headerDecoded}");
                        Console.WriteLine($"📋 Our Payload: {payloadDecoded}");
                        Console.WriteLine($"📋 Our Signature: {actualSignature}");
                        
                        // Verify signature by recalculating it
                        string signingInput = $"{header}.{payload}";
                        byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);
                        
                        byte[] calculatedSignatureBytes;
                        using (var hmac = new HMACSHA256(macKeyBytes))
                        {
                            calculatedSignatureBytes = hmac.ComputeHash(signingInputBytes);
                        }
                        
                        string calculatedSignature = UvfLib.Core.Common.Base64Url.Encode(calculatedSignatureBytes);
                        
                        Console.WriteLine($"🔐 Recalculated Signature: {calculatedSignature}");
                        Console.WriteLine($"🔐 Actual Signature:      {actualSignature}");
                        Console.WriteLine($"✅ Our JWT Self-Validates: {calculatedSignature == actualSignature}");
                        
                        if (calculatedSignature != actualSignature)
                        {
                            Console.WriteLine("❌ OUR JWT DOESN'T SELF-VALIDATE!");
                            Console.WriteLine("This indicates a bug in our JWT creation process.");
                        }
                        else
                        {
                            Console.WriteLine("✅ Our JWT self-validates correctly.");
                            Console.WriteLine("The issue must be with Cryptomator's validation logic or compatibility.");
                            
                            // Try loading this masterkey with our own library to see if that works
                            Console.WriteLine("\n🧪 Testing with our own library...");
                            try
                            {
                                byte[] masterkeyBytes = File.ReadAllBytes(masterkeyPath);
                                using (var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, Password))
                                {
                                    Console.WriteLine("✅ Our library can load the masterkey successfully.");
                                    // Try creating root metadata to see if vault works
                                    var rootMetadata = vault.GetRootDirectoryMetadata();
                                    Console.WriteLine($"✅ Root metadata accessible: DirId = {rootMetadata.DirId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Our library can't load the masterkey: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ hmacMasterKey not found in masterkey file");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing signature process: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("===== Signature Process Test Complete =====");
        }

        private static void TestMacKeyComparison()
        {
            Console.WriteLine("===== Testing MAC Key Comparison =====");
            
            string masterkeyPath = @"D:\temp\uvf\EncryptionTestVault\masterkey.cryptomator";
            
            if (!File.Exists(masterkeyPath))
            {
                Console.WriteLine($"❌ Masterkey file not found: {masterkeyPath}");
                return;
            }
            
            try
            {
                // 1. Get MAC key from JSON file
                string masterkeyJson = File.ReadAllText(masterkeyPath);
                byte[] macKeyFromJson = null;
                
                using (JsonDocument doc = JsonDocument.Parse(masterkeyJson))
                {
                    if (doc.RootElement.TryGetProperty("hmacMasterKey", out JsonElement macKeyElement))
                    {
                        string macKeyBase64 = macKeyElement.GetString();
                        macKeyFromJson = Convert.FromBase64String(macKeyBase64!);
                        Console.WriteLine($"🔑 MAC Key from JSON: {Convert.ToHexString(macKeyFromJson)}");
                        Console.WriteLine($"🔑 MAC Key from JSON Length: {macKeyFromJson.Length} bytes");
                    }
                    else
                    {
                        Console.WriteLine("❌ hmacMasterKey not found in masterkey file");
                        return;
                    }
                }
                
                // 2. Get MAC key from vault instance
                byte[] masterkeyBytes = File.ReadAllBytes(masterkeyPath);
                using (var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, Password))
                {
                    Console.WriteLine("✅ Vault loaded successfully");
                    
                    // Access the private field to get the perpetual masterkey
                    var vaultType = typeof(Vault);
                    var perpetualMasterkeyField = vaultType.GetField("_perpetualMasterkey", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (perpetualMasterkeyField != null)
                    {
                        var perpetualMasterkey = perpetualMasterkeyField.GetValue(vault);
                        if (perpetualMasterkey != null)
                        {
                            // Get the MAC key using reflection
                            var perpetualMasterkeyType = perpetualMasterkey.GetType();
                            var getMacKeyMethod = perpetualMasterkeyType.GetMethod("GetMacKey");
                            
                            if (getMacKeyMethod != null)
                            {
                                using (var macKeySecret = (IDisposable)getMacKeyMethod.Invoke(perpetualMasterkey, null)!)
                                {
                                    var getEncodedMethod = macKeySecret.GetType().GetMethod("GetEncoded");
                                    if (getEncodedMethod != null)
                                    {
                                        byte[] macKeyFromVault = (byte[])getEncodedMethod.Invoke(macKeySecret, null)!;
                                        Console.WriteLine($"🔑 MAC Key from Vault: {Convert.ToHexString(macKeyFromVault)}");
                                        Console.WriteLine($"🔑 MAC Key from Vault Length: {macKeyFromVault.Length} bytes");
                                        
                                        // Compare the keys
                                        bool keysMatch = macKeyFromJson.SequenceEqual(macKeyFromVault);
                                        Console.WriteLine($"✅ MAC Keys Match: {keysMatch}");
                                        
                                        if (!keysMatch)
                                        {
                                            Console.WriteLine("❌ MAC KEYS ARE DIFFERENT!");
                                            Console.WriteLine("This explains why the JWT signature doesn't validate.");
                                            Console.WriteLine("The JWT is signed with one key but validated with another.");
                                        }
                                        else
                                        {
                                            Console.WriteLine("✅ MAC keys are identical.");
                                            Console.WriteLine("The JWT signature issue must be elsewhere.");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing MAC key comparison: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("===== MAC Key Comparison Test Complete =====");
        }

        private static void TestJWTComparison()
        {
            Console.WriteLine("===== Testing Comprehensive JWT Comparison =====");
            
            // Real JWT from Cryptomator
            string realJWT = "eyJraWQiOiJtYXN0ZXJrZXlmaWxlOm1hc3RlcmtleS5jcnlwdG9tYXRvciIsImFsZyI6IkhTMjU2IiwidHlwIjoiSldUIn0.eyJqdGkiOiI5MzZkNWRkMy1hM2VlLTQwYzQtOWEwZi1lOWNkMGRhOGE5MTIiLCJmb3JtYXQiOjgsImNpcGhlckNvbWJvIjoiU0lWX0dDTSIsInNob3J0ZW5pbmdUaHJlc2hvbGQiOjIyMH0.AdkqHRjU13p3egKQqDsOM9GTO8ICSkv8_AECtpixhfA";
            
            // Read our generated JWT from the vault file
            string vaultConfigPath = @"D:\temp\uvf\EncryptionTestVault\vault.cryptomator";
            
            if (!File.Exists(vaultConfigPath))
            {
                Console.WriteLine($"❌ Vault config file not found: {vaultConfigPath}");
                return;
            }
            
            try
            {
                string ourJWT = File.ReadAllText(vaultConfigPath).Trim();
                
                Console.WriteLine($"📋 Real JWT: {realJWT}");
                Console.WriteLine($"📋 Our JWT:  {ourJWT}");
                
                var realParts = realJWT.Split('.');
                var ourParts = ourJWT.Split('.');
                
                if (realParts.Length != 3 || ourParts.Length != 3)
                {
                    Console.WriteLine("❌ Invalid JWT format");
                    return;
                }
                
                // Compare headers
                bool headerMatch = realParts[0] == ourParts[0];
                Console.WriteLine($"🔍 JWT Header Match: {headerMatch}");
                
                if (!headerMatch)
                {
                    var realHeader = UvfLib.Core.Common.Base64Url.DecodeToString(realParts[0]);
                    var ourHeader = UvfLib.Core.Common.Base64Url.DecodeToString(ourParts[0]);
                    Console.WriteLine($"   Real Header: {realHeader}");
                    Console.WriteLine($"   Our Header:  {ourHeader}");
                }
                
                // Compare payloads (structure, not JTI which is random)
                var realPayload = UvfLib.Core.Common.Base64Url.DecodeToString(realParts[1]);
                var ourPayload = UvfLib.Core.Common.Base64Url.DecodeToString(ourParts[1]);
                
                Console.WriteLine($"📋 Real Payload: {realPayload}");
                Console.WriteLine($"📋 Our Payload:  {ourPayload}");
                
                // Parse both payloads to compare structure
                using (JsonDocument realDoc = JsonDocument.Parse(realPayload))
                using (JsonDocument ourDoc = JsonDocument.Parse(ourPayload))
                {
                    bool formatMatch = realDoc.RootElement.GetProperty("format").GetInt32() == ourDoc.RootElement.GetProperty("format").GetInt32();
                    bool cipherComboMatch = realDoc.RootElement.GetProperty("cipherCombo").GetString() == ourDoc.RootElement.GetProperty("cipherCombo").GetString();
                    bool thresholdMatch = realDoc.RootElement.GetProperty("shorteningThreshold").GetInt32() == ourDoc.RootElement.GetProperty("shorteningThreshold").GetInt32();
                    
                    Console.WriteLine($"✅ Format Match: {formatMatch}");
                    Console.WriteLine($"✅ CipherCombo Match: {cipherComboMatch}");
                    Console.WriteLine($"✅ ShorteningThreshold Match: {thresholdMatch}");
                    
                    bool structureMatch = formatMatch && cipherComboMatch && thresholdMatch;
                    Console.WriteLine($"✅ Payload Structure Match: {structureMatch}");
                    
                    if (structureMatch && headerMatch)
                    {
                        Console.WriteLine("✅ JWT structure is identical to real Cryptomator!");
                        Console.WriteLine("The issue might be elsewhere (e.g., masterkey format).");
                    }
                    else
                    {
                        Console.WriteLine("❌ JWT structure differences found.");
                    }
                }
                
                // Test our JWT signature with the real MAC key
                Console.WriteLine("\n🧪 Testing our JWT with real MAC key...");
                string realMacKeyBase64 = "Im8tmsDJrOnAzEb9clltg1DoJ848IKRrY1II3i+cJgtgiIjQOvoQ0A==";
                byte[] realMacKey = Convert.FromBase64String(realMacKeyBase64);
                
                string signingInput = $"{ourParts[0]}.{ourParts[1]}";
                byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);
                
                byte[] expectedSignatureBytes;
                using (var hmac = new HMACSHA256(realMacKey))
                {
                    expectedSignatureBytes = hmac.ComputeHash(signingInputBytes);
                }
                
                string expectedSignature = UvfLib.Core.Common.Base64Url.Encode(expectedSignatureBytes);
                Console.WriteLine($"🔐 Our JWT with Real MAC Key: {expectedSignature}");
                Console.WriteLine($"🔐 Our Actual Signature:     {ourParts[2]}");
                Console.WriteLine($"✅ Would Validate with Real Key: {expectedSignature == ourParts[2]}");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing JWT comparison: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("===== JWT Comparison Test Complete =====");
        }

        private static void TestOwnVaultValidation()
        {
            Console.WriteLine("===== Testing Own Vault Validation =====");
            
            // Test with our own created vault files
            string masterkeyPath = @"D:\temp\uvf\EncryptionTestVault\masterkey.cryptomator";
            string vaultConfigPath = @"D:\temp\uvf\EncryptionTestVault\vault.cryptomator";
            
            if (!File.Exists(masterkeyPath))
            {
                Console.WriteLine($"❌ Masterkey file not found: {masterkeyPath}");
                return;
            }
            
            if (!File.Exists(vaultConfigPath))
            {
                Console.WriteLine($"❌ Vault config file not found: {vaultConfigPath}");
                return;
            }
            
            try
            {
                // Read our masterkey file
                string masterkeyJson = File.ReadAllText(masterkeyPath);
                Console.WriteLine($"📋 Our Masterkey JSON: {masterkeyJson}");
                
                // Parse to get MAC key
                using (JsonDocument doc = JsonDocument.Parse(masterkeyJson))
                {
                    if (doc.RootElement.TryGetProperty("hmacMasterKey", out JsonElement macKeyElement))
                    {
                        string macKeyBase64 = macKeyElement.GetString();
                        Console.WriteLine($"🔑 Our MAC Key (Base64): {macKeyBase64}");
                        
                        byte[] macKeyBytes = Convert.FromBase64String(macKeyBase64!);
                        Console.WriteLine($"🔑 Our MAC Key Length: {macKeyBytes.Length} bytes");
                        
                        // Read our generated vault config
                        string ourJWT = File.ReadAllText(vaultConfigPath);
                        Console.WriteLine($"📋 Our Generated JWT: {ourJWT}");
                        
                        var parts = ourJWT.Split('.');
                        if (parts.Length != 3)
                        {
                            Console.WriteLine("❌ Invalid JWT format");
                            return;
                        }
                        
                        string header = parts[0];
                        string payload = parts[1]; 
                        string actualSignature = parts[2];
                        
                        // Decode header and payload
                        var headerDecoded = UvfLib.Core.Common.Base64Url.DecodeToString(header);
                        var payloadDecoded = UvfLib.Core.Common.Base64Url.DecodeToString(payload);
                        
                        Console.WriteLine($"📋 Our Header: {headerDecoded}");
                        Console.WriteLine($"📋 Our Payload: {payloadDecoded}");
                        Console.WriteLine($"📋 Our Signature: {actualSignature}");
                        
                        // Verify signature by recalculating it
                        string signingInput = $"{header}.{payload}";
                        byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);
                        
                        byte[] calculatedSignatureBytes;
                        using (var hmac = new HMACSHA256(macKeyBytes))
                        {
                            calculatedSignatureBytes = hmac.ComputeHash(signingInputBytes);
                        }
                        
                        string calculatedSignature = UvfLib.Core.Common.Base64Url.Encode(calculatedSignatureBytes);
                        
                        Console.WriteLine($"🔐 Recalculated Signature: {calculatedSignature}");
                        Console.WriteLine($"🔐 Actual Signature:      {actualSignature}");
                        Console.WriteLine($"✅ Our JWT Self-Validates: {calculatedSignature == actualSignature}");
                        
                        if (calculatedSignature != actualSignature)
                        {
                            Console.WriteLine("❌ OUR JWT DOESN'T SELF-VALIDATE!");
                            Console.WriteLine("This indicates a bug in our JWT creation process.");
                        }
                        else
                        {
                            Console.WriteLine("✅ Our JWT self-validates correctly.");
                            Console.WriteLine("The issue must be with Cryptomator's validation logic or compatibility.");
                            
                            // Try loading this masterkey with our own library to see if that works
                            Console.WriteLine("\n🧪 Testing with our own library...");
                            try
                            {
                                byte[] masterkeyBytes = File.ReadAllBytes(masterkeyPath);
                                using (var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, Password))
                                {
                                    Console.WriteLine("✅ Our library can load the masterkey successfully.");
                                    // Try creating root metadata to see if vault works
                                    var rootMetadata = vault.GetRootDirectoryMetadata();
                                    Console.WriteLine($"✅ Root metadata accessible: DirId = {rootMetadata.DirId}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Our library can't load the masterkey: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ hmacMasterKey not found in masterkey file");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing own vault validation: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("===== Own Vault Validation Test Complete =====");
        }

        private static void TestMacKeyDerivation()
        {
            Console.WriteLine("===== Testing MAC Key Derivation Comparison =====");
            
            string masterkeyPath = @"D:\temp\uvf\EncryptionTestVault\masterkey.cryptomator";
            
            if (!File.Exists(masterkeyPath))
            {
                Console.WriteLine($"❌ Masterkey file not found: {masterkeyPath}");
                return;
            }
            
            try
            {
                // 1. Get MAC key from JSON file (what we use for JWT signing)
                string masterkeyJson = File.ReadAllText(masterkeyPath);
                byte[] macKeyFromJson = null;
                
                using (JsonDocument doc = JsonDocument.Parse(masterkeyJson))
                {
                    if (doc.RootElement.TryGetProperty("hmacMasterKey", out JsonElement macKeyElement))
                    {
                        string macKeyBase64 = macKeyElement.GetString();
                        macKeyFromJson = Convert.FromBase64String(macKeyBase64!);
                        Console.WriteLine($"🔑 MAC Key from JSON: {Convert.ToHexString(macKeyFromJson)}");
                        Console.WriteLine($"🔑 MAC Key from JSON Length: {macKeyFromJson.Length} bytes");
                    }
                    else
                    {
                        Console.WriteLine("❌ hmacMasterKey not found in masterkey file");
                        return;
                    }
                }
                
                // 2. Get MAC key by unlocking the masterkey (what Cryptomator would derive)
                byte[] masterkeyBytes = File.ReadAllBytes(masterkeyPath);
                using (var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, Password))
                {
                    Console.WriteLine("✅ Vault loaded successfully");
                    
                    // Access the perpetual masterkey using reflection to get the derived MAC key
                    var vaultType = typeof(Vault);
                    var perpetualMasterkeyField = vaultType.GetField("_perpetualMasterkey", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (perpetualMasterkeyField != null)
                    {
                        var perpetualMasterkey = perpetualMasterkeyField.GetValue(vault);
                        if (perpetualMasterkey != null)
                        {
                            // Get the MAC key using reflection
                            var perpetualMasterkeyType = perpetualMasterkey.GetType();
                            var getMacKeyMethod = perpetualMasterkeyType.GetMethod("GetMacKey");
                            
                            if (getMacKeyMethod != null)
                            {
                                using (var macKeySecret = (IDisposable)getMacKeyMethod.Invoke(perpetualMasterkey, null)!)
                                {
                                    var getEncodedMethod = macKeySecret.GetType().GetMethod("GetEncoded");
                                    if (getEncodedMethod != null)
                                    {
                                        byte[] macKeyFromUnlock = (byte[])getEncodedMethod.Invoke(macKeySecret, null)!;
                                        Console.WriteLine($"🔑 MAC Key from Unlock: {Convert.ToHexString(macKeyFromUnlock)}");
                                        Console.WriteLine($"🔑 MAC Key from Unlock Length: {macKeyFromUnlock.Length} bytes");
                                        
                                        // Compare the keys
                                        bool keysMatch = macKeyFromJson.SequenceEqual(macKeyFromUnlock);
                                        Console.WriteLine($"✅ MAC Keys Match: {keysMatch}");
                                        
                                        if (!keysMatch)
                                        {
                                            Console.WriteLine("❌ MAC KEYS ARE DIFFERENT!");
                                            Console.WriteLine("This explains why Cryptomator can't validate our JWT.");
                                            Console.WriteLine("We're signing with the JSON key, but Cryptomator validates with the derived key.");
                                            
                                            // Test signing with the derived key
                                            Console.WriteLine("\n🧪 Testing JWT with derived MAC key...");
                                            string vaultConfigPath = @"D:\temp\uvf\EncryptionTestVault\vault.cryptomator";
                                            if (File.Exists(vaultConfigPath))
                                            {
                                                string ourJWT = File.ReadAllText(vaultConfigPath);
                                                var parts = ourJWT.Split('.');
                                                if (parts.Length == 3)
                                                {
                                                    string signingInput = $"{parts[0]}.{parts[1]}";
                                                    byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);
                                                    
                                                    byte[] correctSignatureBytes;
                                                    using (var hmac = new HMACSHA256(macKeyFromUnlock))
                                                    {
                                                        correctSignatureBytes = hmac.ComputeHash(signingInputBytes);
                                                    }
                                                    
                                                    string correctSignature = UvfLib.Core.Common.Base64Url.Encode(correctSignatureBytes);
                                                    Console.WriteLine($"🔐 Correct signature with derived key: {correctSignature}");
                                                    Console.WriteLine($"🔐 Our current signature:              {parts[2]}");
                                                    Console.WriteLine($"✅ Would validate: {correctSignature == parts[2]}");
                                                }
                                            }
                                        }
                                        else
                                        {
                                            Console.WriteLine("✅ MAC keys are identical.");
                                            Console.WriteLine("The issue must be elsewhere in the JWT validation process.");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing MAC key derivation: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("===== MAC Key Derivation Test Complete =====");
        }

        private static void TestConcatenatedKeyValidation()
        {
            Console.WriteLine("===== Testing Concatenated Key Validation =====");
            
            string masterkeyPath = @"D:\temp\uvf\EncryptionTestVault\masterkey.cryptomator";
            string vaultConfigPath = @"D:\temp\uvf\EncryptionTestVault\vault.cryptomator";
            
            if (!File.Exists(masterkeyPath) || !File.Exists(vaultConfigPath))
            {
                Console.WriteLine($"❌ Required files not found");
                return;
            }
            
            try
            {
                // Read our JWT
                string ourJWT = File.ReadAllText(vaultConfigPath);
                var parts = ourJWT.Split('.');
                if (parts.Length != 3)
                {
                    Console.WriteLine("❌ Invalid JWT format");
                    return;
                }
                
                Console.WriteLine($"📋 Our JWT: {ourJWT}");
                
                // Read masterkey and extract parameters
                string masterkeyJson = File.ReadAllText(masterkeyPath);
                using (JsonDocument doc = JsonDocument.Parse(masterkeyJson))
                {
                    if (!doc.RootElement.TryGetProperty("primaryMasterKey", out JsonElement encKeyElement) ||
                        !doc.RootElement.TryGetProperty("hmacMasterKey", out JsonElement macKeyElement) ||
                        !doc.RootElement.TryGetProperty("scryptSalt", out JsonElement saltElement) ||
                        !doc.RootElement.TryGetProperty("scryptCostParam", out JsonElement costElement) ||
                        !doc.RootElement.TryGetProperty("scryptBlockSize", out JsonElement blockSizeElement))
                    {
                        Console.WriteLine("❌ Required masterkey fields not found");
                        return;
                    }

                    byte[] wrappedEncKey = Convert.FromBase64String(encKeyElement.GetString()!);
                    byte[] wrappedMacKey = Convert.FromBase64String(macKeyElement.GetString()!);
                    byte[] salt = Convert.FromBase64String(saltElement.GetString()!);
                    int costParam = costElement.GetInt32();
                    int blockSize = blockSizeElement.GetInt32();

                    Console.WriteLine($"🔑 Wrapped Enc Key Length: {wrappedEncKey.Length} bytes");
                    Console.WriteLine($"🔑 Wrapped MAC Key Length: {wrappedMacKey.Length} bytes");

                    // Derive KEK using scrypt
                    byte[] kek = Org.BouncyCastle.Crypto.Generators.SCrypt.Generate(
                        Encoding.UTF8.GetBytes(Password), 
                        salt, 
                        costParam, 
                        blockSize, 
                        1, // parallelism = 1 for Cryptomator
                        32 // KEK length = 32 bytes
                    );

                    try
                    {
                        // Unwrap both keys
                        byte[] rawEncKey = UvfLib.Core.Common.AesKeyWrap.Unwrap(kek, wrappedEncKey);
                        byte[] rawMacKey = UvfLib.Core.Common.AesKeyWrap.Unwrap(kek, wrappedMacKey);

                        Console.WriteLine($"🔑 Raw Enc Key Length: {rawEncKey.Length} bytes");
                        Console.WriteLine($"🔑 Raw MAC Key Length: {rawMacKey.Length} bytes");
                        Console.WriteLine($"🔑 Raw Enc Key: {Convert.ToHexString(rawEncKey)}");
                        Console.WriteLine($"🔑 Raw MAC Key: {Convert.ToHexString(rawMacKey)}");

                        // Concatenate as per Cryptomator specification: encryption key + MAC key
                        byte[] concatenatedKey = new byte[rawEncKey.Length + rawMacKey.Length];
                        Buffer.BlockCopy(rawEncKey, 0, concatenatedKey, 0, rawEncKey.Length);
                        Buffer.BlockCopy(rawMacKey, 0, concatenatedKey, rawEncKey.Length, rawMacKey.Length);

                        Console.WriteLine($"🔑 Concatenated Key Length: {concatenatedKey.Length} bytes");
                        Console.WriteLine($"🔑 Concatenated Key: {Convert.ToHexString(concatenatedKey)}");

                        // Test JWT validation with concatenated key
                        string signingInput = $"{parts[0]}.{parts[1]}";
                        byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);
                        
                        byte[] expectedSignatureBytes;
                        using (var hmac = new HMACSHA256(concatenatedKey))
                        {
                            expectedSignatureBytes = hmac.ComputeHash(signingInputBytes);
                        }
                        
                        string expectedSignature = UvfLib.Core.Common.Base64Url.Encode(expectedSignatureBytes);
                        Console.WriteLine($"🔐 Expected signature with concatenated key: {expectedSignature}");
                        Console.WriteLine($"🔐 Our actual signature:                   {parts[2]}");
                        Console.WriteLine($"✅ JWT validates with concatenated key: {expectedSignature == parts[2]}");

                        // Clear sensitive data
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(rawEncKey);
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(rawMacKey);
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(concatenatedKey);
                    }
                    finally
                    {
                        System.Security.Cryptography.CryptographicOperations.ZeroMemory(kek);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing concatenated key validation: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("===== Concatenated Key Validation Test Complete =====");
        }

        private static void DecryptRealDiridFile()
        {
            Console.WriteLine("===== Decrypting Real Cryptomator dirid.c9r File =====");
            
            // Real Cryptomator vault paths
            string realMasterkeyPath = @"D:\cyptomatortest\martintest\masterkey.cryptomator";
            string realDiridPath = @"D:\cyptomatortest\martintest\d\JT\THXHOONCL2NPW5ELQF3MQTGG764ZCY\dirid.c9r";
            string realPassword = "your-super-secret-password"; // Assuming same password
            
            if (!File.Exists(realMasterkeyPath))
            {
                Console.WriteLine($"❌ Real masterkey file not found: {realMasterkeyPath}");
                return;
            }
            
            if (!File.Exists(realDiridPath))
            {
                Console.WriteLine($"❌ Real dirid.c9r file not found: {realDiridPath}");
                return;
            }
            
            try
            {
                Console.WriteLine($"📋 Real masterkey path: {realMasterkeyPath}");
                Console.WriteLine($"📋 Real dirid.c9r path: {realDiridPath}");
                
                // Check file size first
                var fileInfo = new FileInfo(realDiridPath);
                Console.WriteLine($"📏 Real dirid.c9r file size: {fileInfo.Length} bytes");
                
                // Load the real Cryptomator vault
                byte[] realMasterkeyBytes = File.ReadAllBytes(realMasterkeyPath);
                using (var vault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, realPassword))
                {
                    Console.WriteLine("✅ Real vault loaded successfully");
                    
                    // Read the encrypted dirid.c9r file
                    byte[] encryptedDiridBytes = File.ReadAllBytes(realDiridPath);
                    Console.WriteLine($"📄 Read {encryptedDiridBytes.Length} bytes from dirid.c9r");
                    
                    // Decrypt the file content
                    using (var encryptedStream = new MemoryStream(encryptedDiridBytes))
                    using (var decryptingStream = vault.GetDecryptingStream(encryptedStream))
                    using (var decryptedStream = new MemoryStream())
                    {
                        decryptingStream.CopyTo(decryptedStream);
                        byte[] decryptedContent = decryptedStream.ToArray();
                        
                        Console.WriteLine($"🔓 Decrypted content length: {decryptedContent.Length} bytes");
                        
                        if (decryptedContent.Length == 0)
                        {
                            Console.WriteLine("🔍 Decrypted content is EMPTY (0 bytes)");
                        }
                        else
                        {
                            Console.WriteLine($"🔍 Decrypted content (hex): {Convert.ToHexString(decryptedContent)}");
                            
                            // Try to interpret as ASCII string
                            try
                            {
                                string contentAsAscii = System.Text.Encoding.ASCII.GetString(decryptedContent);
                                Console.WriteLine($"🔍 Decrypted content (ASCII): '{contentAsAscii}'");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Cannot interpret as ASCII: {ex.Message}");
                            }
                            
                            // Try to interpret as UTF8 string
                            try
                            {
                                string contentAsUtf8 = System.Text.Encoding.UTF8.GetString(decryptedContent);
                                Console.WriteLine($"🔍 Decrypted content (UTF8): '{contentAsUtf8}'");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Cannot interpret as UTF8: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error decrypting real dirid.c9r file: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("===== Real dirid.c9r Decryption Complete =====");
            
            // Also test our own dirid.c9r file for comparison
            Console.WriteLine("\n===== Testing Our Own dirid.c9r File =====");
            string ourMasterkeyPath = @"D:\temp\uvf\EncryptionTestVault\masterkey.cryptomator";
            
            // Find any dirid.c9r file in our vault
            string[] ourDiridFiles = Directory.GetFiles(@"D:\temp\uvf\EncryptionTestVault", "dirid.c9r", SearchOption.AllDirectories);
            
            if (File.Exists(ourMasterkeyPath) && ourDiridFiles.Length > 0)
            {
                try
                {
                    string ourDiridPath = ourDiridFiles[0]; // Use the first found file
                    var ourFileInfo = new FileInfo(ourDiridPath);
                    Console.WriteLine($"📏 Our dirid.c9r file size: {ourFileInfo.Length} bytes");
                    Console.WriteLine($"📋 Testing file: {ourDiridPath}");
                    
                    byte[] ourMasterkeyBytes = File.ReadAllBytes(ourMasterkeyPath);
                    using (var ourVault = Vault.LoadCryptomatorV8Vault(ourMasterkeyBytes, realPassword))
                    {
                        byte[] ourEncryptedDiridBytes = File.ReadAllBytes(ourDiridPath);
                        using (var encryptedStream = new MemoryStream(ourEncryptedDiridBytes))
                        using (var decryptingStream = ourVault.GetDecryptingStream(encryptedStream))
                        using (var decryptedStream = new MemoryStream())
                        {
                            decryptingStream.CopyTo(decryptedStream);
                            byte[] ourDecryptedContent = decryptedStream.ToArray();
                            
                            Console.WriteLine($"🔓 Our decrypted content length: {ourDecryptedContent.Length} bytes");
                            if (ourDecryptedContent.Length > 0)
                            {
                                Console.WriteLine($"🔍 Our decrypted content (hex): {Convert.ToHexString(ourDecryptedContent)}");
                            }
                            else
                            {
                                Console.WriteLine("🔍 Our decrypted content is EMPTY (0 bytes)");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error testing our dirid.c9r: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("❌ Our vault files not found for comparison");
            }
        }
    }
} 