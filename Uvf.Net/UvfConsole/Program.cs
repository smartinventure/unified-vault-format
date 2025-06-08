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
        //private const string VaultFolderPath = @"D:\temp\uvf\IdenticalTestVault2";
        //private const string VaultFolderPath = @"D:\cyptomatortest\martintest";
        private const string VaultFolderPath = @"D:\temp\uvf\EncryptionTestVault";

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
            Console.WriteLine("Hello!!!");
            if (args.Length == 0)
            {
                Console.WriteLine("UVF.NET - Universal Vault Format Library and Console Tool");
                Console.WriteLine();
                Console.WriteLine("Usage: UvfConsole <command> [--cryptomator]");
                Console.WriteLine();
                Console.WriteLine("Commands:");
                Console.WriteLine("  encrypt       : Encrypt files from source to vault");
                Console.WriteLine("  decrypt       : Decrypt files from vault to target");
                Console.WriteLine("  testrun       : Full encrypt + decrypt + verify cycle");
                Console.WriteLine("  analyze-jwt   : Analyze real Cryptomator JWT");
                Console.WriteLine("  test-our-jwt  : Test our generated JWT");
                Console.WriteLine("  test-signature: Test signature process");
                Console.WriteLine("  test-mackey   : Test MAC key comparison");
                Console.WriteLine("  test-jwt-compare: Test JWT comparison");
                Console.WriteLine("  test-own-validation: Test own vault validation");
                Console.WriteLine("  test-mac-derivation: Test MAC key derivation");
                Console.WriteLine("  test-concatenated-key: Test concatenated key validation");
                Console.WriteLine("  decrypt-real-dirid: Decrypt real Cryptomator dirid.c9r file");
                Console.WriteLine("  test-our-dirid: Test our own dirid.c9r file");
                Console.WriteLine("  test-dirid-consistency: Test dirid.c9r backup consistency");
                Console.WriteLine("  test-dirid-mapping: Show what each dirid.c9r points to");
                Console.WriteLine("  test-dir-c9r-traversal: Analyze all dir.c9r files and traversal");
                Console.WriteLine("  test-identical-vault: Create vault with real Cryptomator's exact parameters");
                Console.WriteLine("  test-mac-key: Test MAC key extraction and JWT signing");
                Console.WriteLine("  vault-compare: Compare all files between real vault and our vault with checksums");
                Console.WriteLine("  debugpath     : DEBUG: Manual UUID to path conversion testing");
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

            if (mode == "test-our-dirid")
            {
                TestOurDiridFile();
                return;
            }

            if (mode == "test-dirid-consistency")
            {
                TestDiridConsistency();
                return;
            }

            if (mode == "test-parent-uuid")
            {
                TestParentUuidMapping();
                return;
            }

            if (mode == "test-dirid-mapping")
            {
                TestDiridMapping();
                return;
            }

            if (mode == "test-dir-c9r-traversal")
            {
                TestDirC9rTraversal();
                return;
            }

            if (mode == "test-identical-vault")
            {
                IdenticalVaultCreator.CreateIdenticalVault();
                return;
            }

            if (mode == "test-mac-key")
            {
                TestMacKeyExtraction();
                return;
            }

            if (mode == "vault-compare")
            {
                TestVaultFileComparison();
                return;
            }

            if (mode == "debugpath")
            {
                DebugUuidToPathConversion();
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
                Console.WriteLine("Invalid mode. Use one of the commands listed above.");
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
                        // For CryptomatorV8 subdirectories: dirid.c9r should contain raw UUID string (36 bytes)
                        // Not the Base64-encoded version (which would be ~48 bytes)
                        // This ensures 132-byte encrypted file instead of 144 bytes
                        if (string.IsNullOrEmpty(currentDirMetadata.DirId))
                        {
                            Console.WriteLine($"  Warning: DirId for Cryptomator non-root directory {sourceDir} is null or empty. Using empty string for its dirid.c9r content.");
                            actualDirIdToEncrypt = "";
                        }
                        else
                        {
                            // Decode Base64 DirId to get raw UUID string
                            byte[] decodedDirIdBytes = Convert.FromBase64String(currentDirMetadata.DirId);
                            actualDirIdToEncrypt = System.Text.Encoding.ASCII.GetString(decodedDirIdBytes);
                            Console.WriteLine($"  ENCRYPTION DEBUG: Converting Base64 DirId to raw UUID for dirid.c9r");
                            Console.WriteLine($"    Original DirId (Base64): {currentDirMetadata.DirId}");
                            Console.WriteLine($"    Raw UUID for dirid.c9r: {actualDirIdToEncrypt}");
                            Console.WriteLine($"    Length: {actualDirIdToEncrypt.Length} characters");
                        }
                    }

                    byte[] dirIdBytes = System.Text.Encoding.ASCII.GetBytes(actualDirIdToEncrypt);
                    encryptingStream.Write(dirIdBytes, 0, dirIdBytes.Length);
                    // Ensure stream is flushed and closed properly by the using statement to finalize encryption.
                }
                // Log file size after stream is closed and file is written
                long writtenDiridSize = -1;
                try { writtenDiridSize = new FileInfo(diridFilePath).Length; } catch { }
                Console.WriteLine($"  Cryptomator dirid.c9r for DirId '{currentDirMetadata.DirId}' (content: '{(isRootDirectory ? "" : currentDirMetadata.DirId)}') saved to: {diridFilePath} (Size: {writtenDiridSize} bytes)");
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

                    // Creating dir.c9r file pointing to content directory
                    // CRITICAL: dir.c9r must be PLAINTEXT (not encrypted) according to Cryptomator devs
                    // Convert Base64 DirId back to raw UUID string for plaintext storage
                    byte[] decodedDirIdBytes = Convert.FromBase64String(subDirMetadata.DirId);
                    string rawUuidString = System.Text.Encoding.ASCII.GetString(decodedDirIdBytes);

                    // CORRECTED: dir.c9r should be INSIDE the encrypted subdirectory directory
                    // This file contains the UUID pointing to the actual content location
                    string dirFilePath = Path.Combine(subDirPhysicalVaultPath, "dir.c9r");
                    File.WriteAllText(dirFilePath, rawUuidString); // Write as plaintext, 36 bytes
                    Console.WriteLine($"    ENCRYPTION DEBUG: Writing to dir.c9r (FIXED LOCATION)");
                    Console.WriteLine($"      Original DirId (Base64): {subDirMetadata.DirId}");
                    Console.WriteLine($"      Raw UUID String: '{rawUuidString}'");
                    Console.WriteLine($"      dir.c9r file path (PARENT DIR): {dirFilePath}");
                    Console.WriteLine($"      dir.c9r content: '{rawUuidString}' ({rawUuidString.Length} bytes)");
                    Console.WriteLine($"      Pointer: {Path.GetFileName(currentDirPhysicalVaultPath)} -> {encryptedSubDirName}");

                    // Show UUID → Directory path mapping during encryption
                    string expectedContentPath = vault.GetCryptomatorV8DirectoryPathByUuid(rawUuidString);
                    string actualContentPathUsed = vault.GetCryptomatorV8DirectoryPathByUuid(System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(subDirMetadata.DirId)));
                    Console.WriteLine($"    ENCRYPTION: UUID → DIRECTORY PATH MAPPING:");
                    Console.WriteLine($"      UUID in dir.c9r: '{rawUuidString}'");
                    Console.WriteLine($"      Calculated content path: '{expectedContentPath}'");
                    Console.WriteLine($"      Actual content path used: '{actualContentPathUsed}'");
                    Console.WriteLine($"      Paths match: {expectedContentPath == actualContentPathUsed}");

                    // Verify immediately by reading back
                    string readBackContent = File.ReadAllText(dirFilePath);
                    Console.WriteLine($"      Verification - Read back from dir.c9r: '{readBackContent}'");
                    Console.WriteLine($"      UUID Match: {rawUuidString == readBackContent}");
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
                    Console.WriteLine($"    DEBUG: Content path calculated from DirId: {subDirMetadata.DirId}");

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

                    DirectoryMetadata subDirMetadata;
                    string actualContentPath;

                    if (vault.IsCryptomatorV8())
                    {
                        // CRITICAL: For Cryptomator V8, dir.c9r is PLAINTEXT (not encrypted)
                        // Read it directly as plaintext to get the raw UUID string
                        string subDirId = File.ReadAllText(subDirMetadataPath).Trim();

                        Console.WriteLine($"    DECRYPTION DEBUG: Reading from dir.c9r");
                        Console.WriteLine($"      dir.c9r file path: {subDirMetadataPath}");
                        Console.WriteLine($"      Raw content from dir.c9r: '{subDirId}'");
                        Console.WriteLine($"      Length: {subDirId.Length} characters");

                        // Convert to Base64 to show what DirectoryMetadata will have
                        byte[] uuidBytesForMetadata = System.Text.Encoding.ASCII.GetBytes(subDirId);
                        string expectedBase64DirId = Convert.ToBase64String(uuidBytesForMetadata);
                        Console.WriteLine($"      Will convert to Base64 for DirectoryMetadata: {expectedBase64DirId}");

                        // For CryptomatorV8, calculate the content directory path using the proper UvfLib method
                        actualContentPath = Path.Combine(VaultFolderPath, vault.GetCryptomatorV8DirectoryPathByUuid(subDirId));

                        Console.WriteLine($"    DECRYPTION DEBUG: Content directory calculation");
                        Console.WriteLine($"      Content directory path: {actualContentPath}");
                        Console.WriteLine($"      Directory exists: {Directory.Exists(actualContentPath)}");

                        // Final UUID consistency check
                        Console.WriteLine($"    FINAL UUID CONSISTENCY CHECK:");
                        Console.WriteLine($"      UUID from dir.c9r: '{subDirId}'");
                        Console.WriteLine($"      Length: {subDirId.Length}");
                        Console.WriteLine($"      Expected format: XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX (36 chars)");
                        Console.WriteLine($"      Format looks correct: {subDirId.Length == 36 && subDirId.Contains("-")}");

                        // Show the relationship between UUID and directory path
                        string dirPathCalculation = vault.GetCryptomatorV8DirectoryPathByUuid(subDirId);
                        Console.WriteLine($"    UUID → DIRECTORY PATH MAPPING:");
                        Console.WriteLine($"      Input UUID: '{subDirId}'");
                        Console.WriteLine($"      Calculated path: '{dirPathCalculation}'");
                        Console.WriteLine($"      Full path: {actualContentPath}");
                        Console.WriteLine($"      Path format: d/XX/YYYYYYYYYYYYYYYYYYYYYYYY");

                        // Extract the components to verify structure
                        if (dirPathCalculation.StartsWith("d/") && dirPathCalculation.Length > 3)
                        {
                            string pathPart = dirPathCalculation.Substring(2); // Remove "d/"
                            if (pathPart.Length >= 3 && pathPart[2] == '/')
                            {
                                string prefix = pathPart.Substring(0, 2);
                                string suffix = pathPart.Substring(3);
                                Console.WriteLine($"      Path breakdown: d/{prefix}/{suffix}");
                                Console.WriteLine($"      Prefix length: {prefix.Length} (should be 2)");
                                Console.WriteLine($"      Suffix length: {suffix.Length} (should be 30)");
                                Console.WriteLine($"      Structure valid: {prefix.Length == 2 && suffix.Length == 30}");
                            }
                        }

                        if (!Directory.Exists(actualContentPath))
                        {
                            Console.Error.WriteLine($"    ERROR: Content directory not found: {actualContentPath}");
                            continue;
                        }

                        // For CryptomatorV8, create DirectoryMetadata with the correct UUID for authentication
                        // Use the new public method to create proper DirectoryMetadata from UUID string
                        subDirMetadata = vault.CreateCryptomatorV8DirectoryMetadataFromUuid(subDirId);
                        Console.WriteLine($"    DECRYPTION DEBUG: Created DirectoryMetadata");
                        Console.WriteLine($"      Input UUID string: '{subDirId}'");
                        Console.WriteLine($"      Resulting DirId (Base64): {subDirMetadata.DirId}");
                        Console.WriteLine($"      Expected vs Actual Base64: {expectedBase64DirId == subDirMetadata.DirId}");
                    }
                    else
                    {
                        // UVF format: Load and decrypt the subdirectory's metadata
                        Console.WriteLine($"    Loading UVF metadata from: {subDirMetadataPath}");
                        byte[] subDirMetadataBytes = File.ReadAllBytes(subDirMetadataPath);
                        subDirMetadata = vault.DecryptDirectoryMetadata(subDirMetadataBytes);

                        // For UVF format, use separate content directory calculated from subdirectory's dirId
                        actualContentPath = Path.Combine(VaultFolderPath, vault.GetDirectoryPath(subDirMetadata));
                        Console.WriteLine($"    UVF: Content will be stored in: {actualContentPath}");
                        Console.WriteLine($"    Directory ID from metadata: {subDirMetadata.DirId}");

                        if (!Directory.Exists(actualContentPath))
                        {
                            Console.Error.WriteLine($"    ERROR: Content directory not found: {actualContentPath}");
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
            if (!Directory.Exists(decryptedBasePath) && (sourceItems.Any(si => si.Value.ExistsInDecrypted) || unexpectedItems.Any()))
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
                        if (info.DecryptedHashError)
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
            string realSubDiridPath = @"D:\cyptomatortest\martintest\d\V6\66TMDQNTYH3ILNYN4ER4YYVWNZIL2F\dirid.c9r";
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
                // Load the real Cryptomator vault
                byte[] realMasterkeyBytes = File.ReadAllBytes(realMasterkeyPath);
                using (var vault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, realPassword))
                {
                    Console.WriteLine("✅ Real vault loaded successfully");

                    // Test root directory dirid.c9r
                    Console.WriteLine($"\n📋 Real ROOT directory dirid.c9r: {realDiridPath}");
                    var fileInfo = new FileInfo(realDiridPath);
                    Console.WriteLine($"📏 Real dirid.c9r file size: {fileInfo.Length} bytes");

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

                    // Test subdirectory dirid.c9r if it exists
                    if (File.Exists(realSubDiridPath))
                    {
                        Console.WriteLine($"\n📋 Real SUBDIRECTORY dirid.c9r: {realSubDiridPath}");
                        var subFileInfo = new FileInfo(realSubDiridPath);
                        Console.WriteLine($"📏 Real sub-dirid.c9r file size: {subFileInfo.Length} bytes");

                        byte[] encryptedSubDiridBytes = File.ReadAllBytes(realSubDiridPath);
                        Console.WriteLine($"📄 Read {encryptedSubDiridBytes.Length} bytes from sub-dirid.c9r");

                        using (var encryptedStream = new MemoryStream(encryptedSubDiridBytes))
                        using (var decryptingStream = vault.GetDecryptingStream(encryptedStream))
                        using (var decryptedStream = new MemoryStream())
                        {
                            decryptingStream.CopyTo(decryptedStream);
                            byte[] decryptedSubContent = decryptedStream.ToArray();

                            Console.WriteLine($"🔓 Sub-dirid decrypted content length: {decryptedSubContent.Length} bytes");

                            if (decryptedSubContent.Length == 0)
                            {
                                Console.WriteLine("🔍 Sub-dirid content is EMPTY (0 bytes)");
                            }
                            else
                            {
                                Console.WriteLine($"🔍 Sub-dirid content (hex): {Convert.ToHexString(decryptedSubContent)}");

                                try
                                {
                                    string subContentAsAscii = System.Text.Encoding.ASCII.GetString(decryptedSubContent);
                                    Console.WriteLine($"🔍 Sub-dirid content (ASCII): '{subContentAsAscii}'");

                                    // If it looks like a UUID, show what path it should map to
                                    if (subContentAsAscii.Length == 36 && subContentAsAscii.Contains('-'))
                                    {
                                        Console.WriteLine($"🔍 Looks like UUID: {subContentAsAscii}");
                                        try
                                        {
                                            string expectedPath = vault.GetCryptomatorV8DirectoryPathByUuid(subContentAsAscii);
                                            Console.WriteLine($"🔍 Expected content path: {expectedPath}");

                                            string fullExpectedPath = Path.Combine(@"D:\cyptomatortest\martintest", expectedPath);
                                            bool pathExists = Directory.Exists(fullExpectedPath);
                                            Console.WriteLine($"🔍 Expected path exists: {pathExists} ({fullExpectedPath})");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"❌ Error calculating path from UUID: {ex.Message}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"❌ Cannot interpret sub-dirid as ASCII: {ex.Message}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"\n❌ Real subdirectory dirid.c9r not found: {realSubDiridPath}");
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

        private static void TestOurDiridFile()
        {
            Console.WriteLine("===== Testing Our Own dirid.c9r Files =====");

            string ourMasterkeyPath = @"D:\temp\uvf\EncryptionTestVault\masterkey.cryptomator";
            string ourPassword = "your-super-secret-password";

            if (!File.Exists(ourMasterkeyPath))
            {
                Console.WriteLine($"❌ Our masterkey file not found: {ourMasterkeyPath}");
                return;
            }

            // Find all dirid.c9r files in our vault
            string[] ourDiridFiles = Directory.GetFiles(@"D:\temp\uvf\EncryptionTestVault", "dirid.c9r", SearchOption.AllDirectories);

            if (ourDiridFiles.Length == 0)
            {
                Console.WriteLine("❌ No dirid.c9r files found in our vault");
                return;
            }

            try
            {
                byte[] ourMasterkeyBytes = File.ReadAllBytes(ourMasterkeyPath);
                using (var ourVault = Vault.LoadCryptomatorV8Vault(ourMasterkeyBytes, ourPassword))
                {
                    Console.WriteLine("✅ Our vault loaded successfully");

                    foreach (string diridPath in ourDiridFiles)
                    {
                        Console.WriteLine($"\n📋 Testing dirid.c9r: {diridPath}");

                        var fileInfo = new FileInfo(diridPath);
                        Console.WriteLine($"📏 File size: {fileInfo.Length} bytes");

                        byte[] encryptedBytes = File.ReadAllBytes(diridPath);
                        using (var encryptedStream = new MemoryStream(encryptedBytes))
                        using (var decryptingStream = ourVault.GetDecryptingStream(encryptedStream))
                        using (var decryptedStream = new MemoryStream())
                        {
                            decryptingStream.CopyTo(decryptedStream);
                            byte[] decryptedContent = decryptedStream.ToArray();

                            Console.WriteLine($"🔓 Decrypted content length: {decryptedContent.Length} bytes");

                            if (decryptedContent.Length == 0)
                            {
                                Console.WriteLine("🔍 Decrypted content is EMPTY (0 bytes) - this is expected for root directory");
                            }
                            else
                            {
                                Console.WriteLine($"🔍 Decrypted content (hex): {Convert.ToHexString(decryptedContent)}");

                                try
                                {
                                    string contentAsAscii = System.Text.Encoding.ASCII.GetString(decryptedContent);
                                    Console.WriteLine($"🔍 Decrypted content (ASCII): '{contentAsAscii}'");

                                    // If it looks like a UUID, calculate what path it should point to
                                    if (contentAsAscii.Length == 36 && contentAsAscii.Contains('-'))
                                    {
                                        Console.WriteLine($"🔍 Looks like UUID: {contentAsAscii}");
                                        try
                                        {
                                            string expectedPath = ourVault.GetCryptomatorV8DirectoryPathByUuid(contentAsAscii);
                                            Console.WriteLine($"🔍 Expected content path: {expectedPath}");

                                            string fullExpectedPath = Path.Combine(@"D:\temp\uvf\EncryptionTestVault", expectedPath);
                                            bool pathExists = Directory.Exists(fullExpectedPath);
                                            Console.WriteLine($"🔍 Expected path exists: {pathExists} ({fullExpectedPath})");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"❌ Error calculating path from UUID: {ex.Message}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"❌ Cannot interpret as ASCII: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing our dirid.c9r files: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\n===== Our dirid.c9r Test Complete =====");
        }

        static void TestRealVaultUuidMapping()
        {
            // Test the UUID from real Cryptomator vault
            string realVaultUuid = "5255bfb8-615a-4c7b-9415-577f94386f98";
            string expectedPath = "d/V6/66TMDQNTYH3ILNYN4ER4YYVWNZIL2F";

            Console.WriteLine("=== Real Vault UUID Mapping Test ===");
            Console.WriteLine($"Real vault UUID: {realVaultUuid}");
            Console.WriteLine($"Expected path: {expectedPath}");

            // Load the REAL Cryptomator vault instead of creating a temporary one
            string realMasterkeyPath = @"D:\cyptomatortest\martintest\masterkey.cryptomator";
            string realPassword = "your-super-secret-password";

            if (!File.Exists(realMasterkeyPath))
            {
                Console.WriteLine($"❌ Real masterkey file not found: {realMasterkeyPath}");
                return;
            }

            try
            {
                // Load the REAL Cryptomator vault with its REAL keys
                byte[] realMasterkeyBytes = File.ReadAllBytes(realMasterkeyPath);
                using var realVault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, realPassword);

                // Verify this is actually a CryptomatorV8 vault
                if (!realVault.IsCryptomatorV8())
                {
                    Console.WriteLine("❌ ERROR: Failed to load real CryptomatorV8 vault");
                    return;
                }

                Console.WriteLine("✅ Successfully loaded REAL Cryptomator vault");

                string calculatedPath = realVault.GetCryptomatorV8DirectoryPathByUuid(realVaultUuid);
                Console.WriteLine($"Calculated path: {calculatedPath}");
                Console.WriteLine($"Paths match: {calculatedPath == expectedPath}");

                if (calculatedPath != expectedPath)
                {
                    Console.WriteLine("❌ MISMATCH! This reveals the UUID calculation issue.");
                    Console.WriteLine($"Expected: {expectedPath}");
                    Console.WriteLine($"Got:      {calculatedPath}");
                    Console.WriteLine("This explains the Cryptomator compatibility problem.");
                }
                else
                {
                    Console.WriteLine("✅ Perfect match! UUID calculation is correct with real keys.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing with real vault: {ex.Message}");
            }
        }

        static void TestCryptomatorAlgorithmStepByStep()
        {
            Console.WriteLine("=== Step-by-Step Cryptomator Algorithm Test ===");

            string realUuid = "5255bfb8-615a-4c7b-9415-577f94386f98";
            string expectedPath = "d/V6/66TMDQNTYH3ILNYN4ER4YYVWNZIL2F";

            Console.WriteLine($"Testing UUID: {realUuid}");
            Console.WriteLine($"Expected path: {expectedPath}");
            Console.WriteLine();

            // Load the REAL Cryptomator vault instead of creating a temporary one
            string realMasterkeyPath = @"D:\cyptomatortest\martintest\masterkey.cryptomator";
            string realPassword = "your-super-secret-password";

            if (!File.Exists(realMasterkeyPath))
            {
                Console.WriteLine($"❌ Real masterkey file not found: {realMasterkeyPath}");
                return;
            }

            try
            {
                // Load the REAL Cryptomator vault with its REAL keys
                byte[] realMasterkeyBytes = File.ReadAllBytes(realMasterkeyPath);
                using var realVault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, realPassword);

                Console.WriteLine("✅ Successfully loaded REAL Cryptomator vault");
                Console.WriteLine();

                Console.WriteLine("Step 1: Convert UUID to bytes");
                byte[] uuidBytes = System.Text.Encoding.UTF8.GetBytes(realUuid);
                Console.WriteLine($"  UUID bytes length: {uuidBytes.Length}");
                Console.WriteLine($"  UUID bytes (hex): {Convert.ToHexString(uuidBytes)}");
                Console.WriteLine();

                // Test our implementation using the REAL vault keys
                Console.WriteLine("Step 2: Using our implementation with REAL vault keys");
                string ourResult = realVault.GetCryptomatorV8DirectoryPathByUuid(realUuid);
                Console.WriteLine($"  Our result: {ourResult}");
                Console.WriteLine($"  Matches expected: {ourResult == expectedPath}");
                Console.WriteLine();

                if (ourResult != expectedPath)
                {
                    Console.WriteLine("Step 3: Manual algorithm analysis");
                    Console.WriteLine("Our algorithm differs from real Cryptomator even with real keys.");
                    Console.WriteLine("Need to reverse engineer the exact steps they use.");
                    Console.WriteLine();

                    // Extract just the path part for comparison
                    string ourHash = ourResult.Replace("d/", "");
                    string expectedHash = expectedPath.Replace("d/", "");

                    Console.WriteLine($"Our hash:      {ourHash}");
                    Console.WriteLine($"Expected hash: {expectedHash}");
                    Console.WriteLine();

                    // Compare the prefix and suffix
                    string ourPrefix = ourHash.Substring(0, 2);
                    string ourSuffix = ourHash.Substring(3);
                    string expectedPrefix = expectedHash.Substring(0, 2);
                    string expectedSuffix = expectedHash.Substring(3);

                    Console.WriteLine($"Prefix: '{ourPrefix}' vs '{expectedPrefix}' - Match: {ourPrefix == expectedPrefix}");
                    Console.WriteLine($"Suffix: '{ourSuffix}' vs '{expectedSuffix}' - Match: {ourSuffix == expectedSuffix}");
                    Console.WriteLine();

                    Console.WriteLine("Step 4: Algorithm difference analysis");
                    Console.WriteLine("The issue is likely in one of these steps:");
                    Console.WriteLine("1. AES-SIV encryption of UUID (key order, AD parameter)");
                    Console.WriteLine("2. SHA1 hashing of encrypted bytes");
                    Console.WriteLine("3. Base32 encoding (alphabet, padding)");
                    Console.WriteLine("4. String splitting (first 2 chars vs last 30 chars)");
                }
                else
                {
                    Console.WriteLine("✅ Perfect match! Our algorithm works correctly with real keys.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }

        static void ReverseEngineerCryptomatorHash()
        {
            Console.WriteLine("=== Reverse Engineering Real Cryptomator Hash ===");

            string expectedBase32 = "V666TMDQNTYH3ILNYN4ER4YYVWNZIL2F";
            Console.WriteLine($"Expected Base32 hash: {expectedBase32}");
            Console.WriteLine($"Length: {expectedBase32.Length}");

            try
            {
                // Decode the Base32 to get the SHA1 hash bytes
                byte[] sha1HashBytes = DecodeBase32GoogleGuava(expectedBase32);
                Console.WriteLine($"Decoded SHA1 hash: {Convert.ToHexString(sha1HashBytes)}");
                Console.WriteLine($"SHA1 hash length: {sha1HashBytes.Length} bytes");

                if (sha1HashBytes.Length == 20) // SHA1 produces 20 bytes
                {
                    Console.WriteLine("✅ Valid SHA1 hash length");
                    Console.WriteLine();
                    Console.WriteLine("This means the real Cryptomator:");
                    Console.WriteLine("1. Does some encryption/processing of the UUID");
                    Console.WriteLine("2. SHA1 hashes the result");
                    Console.WriteLine("3. Base32 encodes the SHA1 hash");
                    Console.WriteLine();
                    Console.WriteLine("The question is: what exactly is step 1?");
                    Console.WriteLine("It might not be AES-SIV at all!");
                }
                else
                {
                    Console.WriteLine($"❌ Unexpected hash length: {sha1HashBytes.Length} bytes");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error decoding Base32: {ex.Message}");
            }
        }

        static byte[] DecodeBase32GoogleGuava(string input)
        {
            const string BASE32_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

            if (string.IsNullOrEmpty(input))
                return Array.Empty<byte>();

            var result = new List<byte>();
            int buffer = 0;
            int bitsLeft = 0;

            foreach (char c in input.ToUpperInvariant())
            {
                int value = BASE32_ALPHABET.IndexOf(c);
                if (value == -1)
                    throw new ArgumentException($"Invalid Base32 character: {c}");

                buffer = (buffer << 5) | value;
                bitsLeft += 5;

                if (bitsLeft >= 8)
                {
                    result.Add((byte)(buffer >> (bitsLeft - 8)));
                    bitsLeft -= 8;
                }
            }

            return result.ToArray();
        }

        static void BruteForceTestCryptomatorAlgorithm()
        {
            Console.WriteLine("=== Brute Force Test: Finding Correct Algorithm ===");

            string testUuid = "5255bfb8-615a-4c7b-9415-577f94386f98";
            string expectedSha1 = "AFBDE9B0706CF07DA16DC37848F318AD9B942F45";

            Console.WriteLine($"Testing UUID: {testUuid}");
            Console.WriteLine($"Expected SHA1: {expectedSha1}");
            Console.WriteLine();

            try
            {
                // Test 1: Direct SHA1 of UUID string
                Console.WriteLine("Test 1: Direct SHA1 of UUID string");
                TestDirectSha1(testUuid, expectedSha1);
                Console.WriteLine();

                // Test 2: SHA1 of UUID bytes (UTF8)
                Console.WriteLine("Test 2: SHA1 of UUID bytes (UTF8)");
                TestSha1OfBytes(testUuid, System.Text.Encoding.UTF8, expectedSha1);
                Console.WriteLine();

                // Test 3: SHA1 of UUID bytes (ASCII)
                Console.WriteLine("Test 3: SHA1 of UUID bytes (ASCII)");
                TestSha1OfBytes(testUuid, System.Text.Encoding.ASCII, expectedSha1);
                Console.WriteLine();

                // Test 4: Try different input formats
                Console.WriteLine("Test 4: Different UUID formats");
                TestDifferentUuidFormats(testUuid, expectedSha1);
                Console.WriteLine();

                // Test 5: Try our current AES-SIV approach but with different parameters
                Console.WriteLine("Test 5: AES-SIV with different parameters");
                TestAesSivVariations(testUuid, expectedSha1);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static void TestDirectSha1(string input, string expectedSha1)
        {
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha1.ComputeHash(inputBytes);
            string actualSha1 = Convert.ToHexString(hashBytes);

            Console.WriteLine($"  Input: '{input}'");
            Console.WriteLine($"  SHA1: {actualSha1}");
            Console.WriteLine($"  Match: {actualSha1 == expectedSha1} {(actualSha1 == expectedSha1 ? "✅" : "❌")}");
        }

        static void TestSha1OfBytes(string input, System.Text.Encoding encoding, string expectedSha1)
        {
            using var sha1 = System.Security.Cryptography.SHA1.Create();
            byte[] inputBytes = encoding.GetBytes(input);
            byte[] hashBytes = sha1.ComputeHash(inputBytes);
            string actualSha1 = Convert.ToHexString(hashBytes);

            Console.WriteLine($"  Input: '{input}' ({encoding.EncodingName})");
            Console.WriteLine($"  SHA1: {actualSha1}");
            Console.WriteLine($"  Match: {actualSha1 == expectedSha1} {(actualSha1 == expectedSha1 ? "✅" : "❌")}");
        }

        static void TestDifferentUuidFormats(string baseUuid, string expectedSha1)
        {
            string[] variations = {
                baseUuid,                                    // Original
                baseUuid.Replace("-", ""),                   // No dashes
                baseUuid.ToUpperInvariant(),                 // Uppercase
                baseUuid.ToUpperInvariant().Replace("-", ""), // Uppercase no dashes
                "{" + baseUuid + "}",                        // With braces
                baseUuid.Replace("-", "").ToUpperInvariant() // Uppercase hex
            };

            using var sha1 = System.Security.Cryptography.SHA1.Create();

            foreach (string variation in variations)
            {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(variation);
                byte[] hashBytes = sha1.ComputeHash(inputBytes);
                string actualSha1 = Convert.ToHexString(hashBytes);

                Console.WriteLine($"  Format: '{variation}'");
                Console.WriteLine($"  SHA1: {actualSha1}");
                Console.WriteLine($"  Match: {actualSha1 == expectedSha1} {(actualSha1 == expectedSha1 ? "✅" : "❌")}");
                Console.WriteLine();
            }
        }

        static void TestAesSivVariations(string testUuid, string expectedSha1)
        {
            Console.WriteLine("  Testing AES-SIV + SHA1 variations...");
            Console.WriteLine("  (This requires a vault to get keys - skipping for now)");
            Console.WriteLine("  Our current AES-SIV approach produces different results");
            Console.WriteLine("  The issue might be in key derivation or associated data");
        }

        static void ManualCryptomatorSpecTest()
        {
            Console.WriteLine("=== Manual Implementation of Cryptomator Specification ===");

            string testUuid = "5255bfb8-615a-4c7b-9415-577f94386f98";
            string expectedPath = "d/V6/66TMDQNTYH3ILNYN4ER4YYVWNZIL2F";
            string expectedSha1 = "AFBDE9B0706CF07DA16DC37848F318AD9B942F45";

            Console.WriteLine($"UUID: {testUuid}");
            Console.WriteLine($"Expected path: {expectedPath}");
            Console.WriteLine($"Expected SHA1: {expectedSha1}");
            Console.WriteLine();

            Console.WriteLine("According to Cryptomator specification:");
            Console.WriteLine("1. aesSiv(dirId, null, encryptionMasterKey, macMasterKey)");
            Console.WriteLine("2. sha1(encrypted_result)");
            Console.WriteLine("3. base32(sha1_hash)");
            Console.WriteLine("4. d/XX/YYYYYYYY (first 2 chars / next 30 chars)");
            Console.WriteLine();

            // Load the REAL Cryptomator vault instead of creating a temporary one
            string realMasterkeyPath = @"D:\cyptomatortest\martintest\masterkey.cryptomator";
            string realPassword = "your-super-secret-password";

            if (!File.Exists(realMasterkeyPath))
            {
                Console.WriteLine($"❌ Real masterkey file not found: {realMasterkeyPath}");
                return;
            }

            try
            {
                // Load the REAL Cryptomator vault with its REAL keys
                byte[] realMasterkeyBytes = File.ReadAllBytes(realMasterkeyPath);
                using var realVault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, realPassword);

                Console.WriteLine("Step 1: Our implementation result using REAL vault keys");
                string ourPath = realVault.GetCryptomatorV8DirectoryPathByUuid(testUuid);
                Console.WriteLine($"Our path: {ourPath}");
                Console.WriteLine($"Matches: {ourPath == expectedPath}");
                Console.WriteLine();

                if (ourPath != expectedPath)
                {
                    Console.WriteLine("Step 2: Analysis of the difference");
                    string ourHash = ourPath.Replace("d/", "");
                    string expectedHash = expectedPath.Replace("d/", "");

                    Console.WriteLine($"Our hash:      {ourHash}");
                    Console.WriteLine($"Expected hash: {expectedHash}");
                    Console.WriteLine();

                    Console.WriteLine("Step 3: Reverse engineer the working hash");
                    // Since we know the expected hash, let's decode it to see the SHA1
                    try
                    {
                        byte[] expectedSha1Bytes = DecodeBase32GoogleGuava(expectedHash.Replace("/", ""));
                        string decodedSha1 = Convert.ToHexString(expectedSha1Bytes);
                        Console.WriteLine($"Decoded SHA1 from expected hash: {decodedSha1}");
                        Console.WriteLine($"Matches expected SHA1: {decodedSha1 == expectedSha1}");
                        Console.WriteLine();

                        if (decodedSha1 == expectedSha1)
                        {
                            Console.WriteLine("✅ Confirmed: Expected hash correctly decodes to expected SHA1");
                            Console.WriteLine("❌ Problem: Our AES-SIV encryption produces different input to SHA1");
                            Console.WriteLine("   Even with the REAL vault keys, our result differs.");
                            Console.WriteLine();
                            Console.WriteLine("Possible issues:");
                            Console.WriteLine("- Different AES-SIV key derivation");
                            Console.WriteLine("- Different associated data (not null)");
                            Console.WriteLine("- Different input format (not UTF8 string)");
                            Console.WriteLine("- Completely different encryption algorithm");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error decoding hash: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("✅ Perfect match! Our implementation works correctly with real keys.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }

        /// <summary>
        /// DEBUG METHOD: Manual experimentation with UUID to path conversion
        /// TODO: Remove this method when debugging is complete
        /// </summary>
        static void DebugUuidToPathConversion()
        {
            Console.WriteLine("=== DEBUG: Manual UUID to Path Conversion ===");
            Console.WriteLine("🚧 This is a temporary debugging method");
            Console.WriteLine();

            // Known values from real Cryptomator vault
            string testUuid = "5255bfb8-615a-4c7b-9415-577f94386f98";
            string expectedPath = "d/V6/66TMDQNTYH3ILNYN4ER4YYVWNZIL2F";
            string expectedBase32Hash = "V666TMDQNTYH3ILNYN4ER4YYVWNZIL2F";
            string expectedSha1 = "AFBDE9B0706CF07DA16DC37848F318AD9B942F45";

            Console.WriteLine($"🎯 Target UUID: {testUuid}");
            Console.WriteLine($"🎯 Expected path: {expectedPath}");
            Console.WriteLine($"🎯 Expected Base32 hash: {expectedBase32Hash}");
            Console.WriteLine($"🎯 Expected SHA1: {expectedSha1}");
            Console.WriteLine();

            // Load the REAL Cryptomator vault instead of creating a temporary one
            string realMasterkeyPath = @"D:\cyptomatortest\martintest\masterkey.cryptomator";
            string realPassword = "your-super-secret-password";

            if (!File.Exists(realMasterkeyPath))
            {
                Console.WriteLine($"❌ Real masterkey file not found: {realMasterkeyPath}");
                return;
            }

            try
            {
                // Step 1: Show our implementation result using REAL vault keys
                Console.WriteLine("📊 Step 1: Our Implementation Using REAL Vault Keys");
                byte[] realMasterkeyBytes = File.ReadAllBytes(realMasterkeyPath);
                using var realVault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, realPassword);

                Console.WriteLine("✅ Successfully loaded REAL Cryptomator vault");

                string ourCurrentPath = realVault.GetCryptomatorV8DirectoryPathByUuid(testUuid);
                Console.WriteLine($"   Our result: {ourCurrentPath}");
                Console.WriteLine($"   Expected:   {expectedPath}");
                Console.WriteLine($"   ✅ Match: {ourCurrentPath == expectedPath}");
                Console.WriteLine();

                // Step 2: Analyze the expected hash
                Console.WriteLine("🔍 Step 2: Analyze Expected Hash");
                try
                {
                    byte[] decodedSha1 = DecodeBase32GoogleGuava(expectedBase32Hash);
                    string sha1Hex = Convert.ToHexString(decodedSha1);
                    Console.WriteLine($"   Decoded SHA1: {sha1Hex}");
                    Console.WriteLine($"   Length: {decodedSha1.Length} bytes");
                    Console.WriteLine($"   ✅ Matches expected: {sha1Hex == expectedSha1}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Error decoding: {ex.Message}");
                }
                Console.WriteLine();

                // Step 3: Manual experimentation area
                Console.WriteLine("🧪 Step 3: Manual Experimentation Area");
                Console.WriteLine("   This is where you can add your own debugging code:");
                Console.WriteLine();

                // TODO: Add your experimental code here
                Console.WriteLine("   // Example experiments you could try:");
                Console.WriteLine("   // 1. Try different AES-SIV key combinations");
                Console.WriteLine("   // 2. Try different input formats (bytes vs string)");
                Console.WriteLine("   // 3. Try different associated data parameters");
                Console.WriteLine("   // 4. Try completely different encryption methods");
                Console.WriteLine("   // 5. Try to work backwards from the known SHA1");
                Console.WriteLine();

                // Step 4: Working backwards experiment
                Console.WriteLine("🔄 Step 4: Working Backwards from Known SHA1");
                Console.WriteLine("   We know the final SHA1 hash should be:");
                Console.WriteLine($"   {expectedSha1}");
                Console.WriteLine("   This means the AES-SIV encrypted bytes should hash to this value.");
                Console.WriteLine("   The question is: what are those encrypted bytes?");
                Console.WriteLine();

                if (ourCurrentPath == expectedPath)
                {
                    Console.WriteLine("🎉 SUCCESS: Our algorithm now works with real keys!");
                    Console.WriteLine("   The issue was using wrong keys, not wrong algorithm.");
                }
                else
                {
                    Console.WriteLine("🔍 STILL INVESTIGATING: Even with real keys, result differs.");
                    Console.WriteLine("   This indicates a fundamental algorithm difference.");
                }
                Console.WriteLine();

                // You can add more experimental code here
                Console.WriteLine("💡 Next steps for debugging:");
                Console.WriteLine("   1. Contact Cryptomator developers for AES-SIV parameter clarification");
                Console.WriteLine("   2. Try different key derivation methods");
                Console.WriteLine("   3. Test with null vs empty associated data");
                Console.WriteLine("   4. Verify input format (UTF8 string vs raw bytes)");
                Console.WriteLine();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in debugging: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("🚧 End of debugging method");
        }

        private static void TestDiridConsistency()
        {
            Console.WriteLine("===== Testing dirid.c9r Consistency (Backup Directory IDs) =====");
            Console.WriteLine("This tests if dirid.c9r files correctly contain parent directory IDs");
            Console.WriteLine();

            // Test both vaults
            Console.WriteLine("🔍 REAL CRYPTOMATOR VAULT:");
            TestVaultDiridConsistency(@"D:\cyptomatortest\martintest", "your-super-secret-password");

            Console.WriteLine("\n🔍 OUR VAULT:");
            TestVaultDiridConsistency(@"D:\temp\uvf\EncryptionTestVault", "your-super-secret-password");

            Console.WriteLine("\n===== dirid.c9r Consistency Test Complete =====");
        }

        private static void TestParentUuidMapping()
        {
            Console.WriteLine("===== Testing Parent UUID Mapping =====");

            // Real Cryptomator vault test
            Console.WriteLine("🔍 REAL CRYPTOMATOR VAULT:");
            Console.WriteLine("Subdirectory dirid.c9r contains UUID: 5255bfb8-615a-4c7b-9415-577f94386f98");
            Console.WriteLine("Expected to map to root: d/JT/THXHOONCL2NPW5ELQF3MQTGG764ZCY");

            try
            {
                byte[] realMasterkeyBytes = File.ReadAllBytes(@"D:\cyptomatortest\martintest\masterkey.cryptomator");
                using var realVault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, "your-super-secret-password");

                string calculatedPath = realVault.GetCryptomatorV8DirectoryPathByUuid("5255bfb8-615a-4c7b-9415-577f94386f98");
                Console.WriteLine($"Calculated path: {calculatedPath}");
                Console.WriteLine($"✅ Real vault consistency: {calculatedPath == "d/JT/THXHOONCL2NPW5ELQF3MQTGG764ZCY"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }

            Console.WriteLine();

            // Our vault test
            Console.WriteLine("🔍 OUR VAULT:");
            Console.WriteLine("Subdirectory dirid.c9r contains UUID: 4cf27cc8-ed7e-45af-ad7b-f6cf46e1079d");
            Console.WriteLine("Expected to map to root: d/5O/VFGSIRT2CEZBBXJU6PCWPIPQMQ7HI3");

            try
            {
                byte[] ourMasterkeyBytes = File.ReadAllBytes(@"D:\temp\uvf\EncryptionTestVault\masterkey.cryptomator");
                using var ourVault = Vault.LoadCryptomatorV8Vault(ourMasterkeyBytes, "your-super-secret-password");

                string calculatedPath = ourVault.GetCryptomatorV8DirectoryPathByUuid("4cf27cc8-ed7e-45af-ad7b-f6cf46e1079d");
                Console.WriteLine($"Calculated path: {calculatedPath}");
                Console.WriteLine($"✅ Our vault consistency: {calculatedPath == "d/5O/VFGSIRT2CEZBBXJU6PCWPIPQMQ7HI3"}");

                if (calculatedPath != "d/5O/VFGSIRT2CEZBBXJU6PCWPIPQMQ7HI3")
                {
                    Console.WriteLine("🎯 FOUND THE ISSUE: Our subdirectory's dirid.c9r contains wrong parent UUID!");
                    Console.WriteLine("This means our dirid.c9r creation logic is broken!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }

            Console.WriteLine("\n===== Parent UUID Mapping Test Complete =====");
        }

        private static void TestVaultDiridConsistency(string vaultPath, string password)
        {
            Console.WriteLine($"Testing vault: {vaultPath}");

            string masterkeyPath = Path.Combine(vaultPath, "masterkey.cryptomator");
            if (!File.Exists(masterkeyPath))
            {
                Console.WriteLine($"❌ Masterkey not found: {masterkeyPath}");
                return;
            }

            try
            {
                byte[] masterkeyBytes = File.ReadAllBytes(masterkeyPath);
                using var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, password);

                Console.WriteLine("✅ Vault loaded successfully");

                // Find all dirid.c9r files
                string[] diridFiles = Directory.GetFiles(vaultPath, "dirid.c9r", SearchOption.AllDirectories);
                Console.WriteLine($"📋 Found {diridFiles.Length} dirid.c9r files");

                foreach (string diridPath in diridFiles)
                {
                    Console.WriteLine($"\n📄 Testing: {diridPath}");

                    // Get the directory containing this dirid.c9r
                    string containingDir = Path.GetDirectoryName(diridPath);
                    string relativePath = Path.GetRelativePath(vaultPath, containingDir);
                    Console.WriteLine($"   Directory: {relativePath}");

                    // Check if this is the root directory
                    bool isRoot = relativePath.Split(Path.DirectorySeparatorChar).Length == 3; // d/XX/YYYYYY
                    Console.WriteLine($"   Is root directory: {isRoot}");

                    // Decrypt the dirid.c9r file
                    var fileInfo = new FileInfo(diridPath);
                    Console.WriteLine($"   File size: {fileInfo.Length} bytes");

                    byte[] encryptedBytes = File.ReadAllBytes(diridPath);
                    using (var encryptedStream = new MemoryStream(encryptedBytes))
                    using (var decryptingStream = vault.GetDecryptingStream(encryptedStream))
                    using (var decryptedStream = new MemoryStream())
                    {
                        decryptingStream.CopyTo(decryptedStream);
                        byte[] decryptedContent = decryptedStream.ToArray();

                        Console.WriteLine($"   Decrypted length: {decryptedContent.Length} bytes");

                        if (decryptedContent.Length == 0)
                        {
                            Console.WriteLine($"   Content: EMPTY (expected for root directory)");
                            if (!isRoot)
                            {
                                Console.WriteLine($"   ❌ ERROR: Non-root directory has empty dirid.c9r!");
                            }
                        }
                        else
                        {
                            string parentUuid = System.Text.Encoding.ASCII.GetString(decryptedContent);
                            Console.WriteLine($"   Content: '{parentUuid}' (parent directory UUID)");

                            if (isRoot)
                            {
                                Console.WriteLine($"   ❌ ERROR: Root directory should have empty dirid.c9r!");
                            }
                            else
                            {
                                // Calculate what path this parent UUID should map to
                                try
                                {
                                    string expectedParentPath = vault.GetCryptomatorV8DirectoryPathByUuid(parentUuid);
                                    Console.WriteLine($"   Expected parent path: {expectedParentPath}");

                                    // Check if this parent path actually exists
                                    string fullParentPath = Path.Combine(vaultPath, expectedParentPath);
                                    bool parentExists = Directory.Exists(fullParentPath);
                                    Console.WriteLine($"   Parent exists: {parentExists}");

                                    if (parentExists)
                                    {
                                        // Verify logical parent-child relationship
                                        string actualParent = Path.GetDirectoryName(relativePath);
                                        string expectedParent = expectedParentPath.Replace('/', Path.DirectorySeparatorChar);

                                        Console.WriteLine($"   Actual parent: {actualParent}");
                                        Console.WriteLine($"   Expected parent: {expectedParent}");
                                        Console.WriteLine($"   ✅ Parent relationship: {actualParent == expectedParent}");

                                        if (actualParent != expectedParent)
                                        {
                                            Console.WriteLine($"   ❌ MISMATCH: dirid.c9r points to wrong parent!");
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine($"   ❌ ERROR: Parent directory doesn't exist!");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"   ❌ ERROR calculating parent path: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error testing vault: {ex.Message}");
            }
        }

        private static void TestDiridMapping()
        {
            Console.WriteLine("===== Testing dirid.c9r Mapping Analysis =====");
            Console.WriteLine("This shows what each dirid.c9r contains and where it points to");
            Console.WriteLine();

            // Test both vaults
            Console.WriteLine("🔍 REAL CRYPTOMATOR VAULT:");
            AnalyzeDiridMapping(@"D:\cyptomatortest\martintest", "your-super-secret-password");

            Console.WriteLine("\n🔍 OUR VAULT:");
            AnalyzeDiridMapping(@"D:\temp\uvf\EncryptionTestVault", "your-super-secret-password");

            Console.WriteLine("\n===== dirid.c9r Mapping Analysis Complete =====");
        }

        private static void AnalyzeDiridMapping(string vaultPath, string password)
        {
            Console.WriteLine($"Analyzing vault: {vaultPath}");

            string masterkeyPath = Path.Combine(vaultPath, "masterkey.cryptomator");
            if (!File.Exists(masterkeyPath))
            {
                Console.WriteLine($"❌ Masterkey not found: {masterkeyPath}");
                return;
            }

            try
            {
                byte[] masterkeyBytes = File.ReadAllBytes(masterkeyPath);
                using var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, password);

                // Find all dirid.c9r files
                string[] diridFiles = Directory.GetFiles(vaultPath, "dirid.c9r", SearchOption.AllDirectories);
                Console.WriteLine($"Found {diridFiles.Length} dirid.c9r files:");

                foreach (string diridPath in diridFiles)
                {
                    // Get the directory containing this dirid.c9r
                    string containingDir = Path.GetDirectoryName(diridPath);
                    string relativePath = Path.GetRelativePath(vaultPath, containingDir);

                    Console.WriteLine($"\n📍 dirid.c9r Location: {relativePath}");

                    // Decrypt the dirid.c9r file
                    byte[] encryptedBytes = File.ReadAllBytes(diridPath);
                    using (var encryptedStream = new MemoryStream(encryptedBytes))
                    using (var decryptingStream = vault.GetDecryptingStream(encryptedStream))
                    using (var decryptedStream = new MemoryStream())
                    {
                        decryptingStream.CopyTo(decryptedStream);
                        byte[] decryptedContent = decryptedStream.ToArray();

                        if (decryptedContent.Length == 0)
                        {
                            Console.WriteLine($"   📄 Content: EMPTY");
                            Console.WriteLine($"   🎯 Points to: ROOT (empty UUID)");
                            Console.WriteLine($"   ✅ Correct: This IS the root directory");
                        }
                        else
                        {
                            string containedUuid = System.Text.Encoding.ASCII.GetString(decryptedContent);
                            Console.WriteLine($"   📄 Content: '{containedUuid}'");

                            try
                            {
                                string targetPath = vault.GetCryptomatorV8DirectoryPathByUuid(containedUuid);
                                Console.WriteLine($"   🎯 Points to: {targetPath}");

                                // Check if it points to itself or somewhere else
                                string normalizedRelativePath = relativePath.Replace('\\', '/');
                                if (targetPath == normalizedRelativePath)
                                {
                                    Console.WriteLine($"   ✅ Points to: ITSELF (backup of own UUID)");
                                }
                                else
                                {
                                    Console.WriteLine($"   ❓ Points to: DIFFERENT directory");
                                    Console.WriteLine($"      Expected (self): {normalizedRelativePath}");
                                    Console.WriteLine($"      Actually points to: {targetPath}");

                                    // Check if that directory exists
                                    string fullTargetPath = Path.Combine(vaultPath, targetPath);
                                    bool targetExists = Directory.Exists(fullTargetPath);
                                    Console.WriteLine($"      Target exists: {targetExists}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"   ❌ ERROR calculating target path: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error analyzing vault: {ex.Message}");
            }
        }

        private static void TestDirC9rTraversal()
        {
            Console.WriteLine("===== Testing dir.c9r Directory Traversal =====");
            Console.WriteLine("This analyzes all dir.c9r files and shows what directories they point to");
            Console.WriteLine();

            // Test both vaults
            Console.WriteLine("🔍 REAL CRYPTOMATOR VAULT:");
            AnalyzeDirC9rTraversal(@"D:\cyptomatortest\martintest2", "your-super-secret-password");

            Console.WriteLine("\n🔍 OUR VAULT:");
            AnalyzeDirC9rTraversal(@"D:\temp\uvf\IdenticalTestVault2", "your-super-secret-password");

            Console.WriteLine("\n===== dir.c9r Directory Traversal Analysis Complete =====");
        }

        private static void AnalyzeDirC9rTraversal(string vaultPath, string password)
        {
            Console.WriteLine($"Analyzing vault: {vaultPath}");

            string masterkeyPath = Path.Combine(vaultPath, "masterkey.cryptomator");
            if (!File.Exists(masterkeyPath))
            {
                Console.WriteLine($"❌ Masterkey not found: {masterkeyPath}");
                return;
            }

            try
            {
                byte[] masterkeyBytes = File.ReadAllBytes(masterkeyPath);
                using var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, password);

                // Find all dir.c9r files
                string[] dirC9rFiles = Directory.GetFiles(vaultPath, "dir.c9r", SearchOption.AllDirectories);
                Console.WriteLine($"Found {dirC9rFiles.Length} dir.c9r files:");

                foreach (string dirC9rPath in dirC9rFiles)
                {
                    // Get the directory containing this dir.c9r
                    string containingDir = Path.GetDirectoryName(dirC9rPath);
                    string relativePath = Path.GetRelativePath(vaultPath, containingDir);

                    Console.WriteLine($"\n📍 dir.c9r Location: {relativePath}");

                    try
                    {
                        // Read the dir.c9r file (it's unencrypted UTF8)
                        string uuidContent = File.ReadAllText(dirC9rPath, System.Text.Encoding.UTF8).Trim();
                        Console.WriteLine($"   📄 Content: '{uuidContent}' (UUID)");
                        Console.WriteLine($"   📏 Length: {uuidContent.Length} characters");

                        if (string.IsNullOrEmpty(uuidContent))
                        {
                            Console.WriteLine($"   ❌ ERROR: Empty dir.c9r file!");
                            continue;
                        }

                        // Calculate what directory this UUID points to
                        string targetPath = vault.GetCryptomatorV8DirectoryPathByUuid(uuidContent);
                        Console.WriteLine($"   🎯 Points to: {targetPath}");

                        // Check if the target directory exists
                        string fullTargetPath = Path.Combine(vaultPath, targetPath);
                        bool targetExists = Directory.Exists(fullTargetPath);
                        Console.WriteLine($"   📂 Target exists: {targetExists}");

                        if (targetExists)
                        {
                            // Show what's in the target directory
                            string[] filesInTarget = Directory.GetFiles(fullTargetPath);
                            string[] dirsInTarget = Directory.GetDirectories(fullTargetPath);
                            Console.WriteLine($"   📋 Target contains: {filesInTarget.Length} files, {dirsInTarget.Length} subdirs");

                            // Check for essential files
                            bool hasDialridC9r = File.Exists(Path.Combine(fullTargetPath, "dirid.c9r"));
                            Console.WriteLine($"   🔧 Has dirid.c9r: {hasDialridC9r}");

                            // Show a few file examples
                            if (filesInTarget.Length > 0)
                            {
                                Console.WriteLine($"   📝 Sample files:");
                                for (int i = 0; i < Math.Min(3, filesInTarget.Length); i++)
                                {
                                    string fileName = Path.GetFileName(filesInTarget[i]);
                                    var fileInfo = new FileInfo(filesInTarget[i]);
                                    Console.WriteLine($"      - {fileName} ({fileInfo.Length} bytes)");
                                }
                                if (filesInTarget.Length > 3)
                                {
                                    Console.WriteLine($"      ... and {filesInTarget.Length - 3} more files");
                                }
                            }

                            // Show subdirectory examples
                            if (dirsInTarget.Length > 0)
                            {
                                Console.WriteLine($"   📁 Sample subdirs:");
                                for (int i = 0; i < Math.Min(3, dirsInTarget.Length); i++)
                                {
                                    string dirName = Path.GetFileName(dirsInTarget[i]);
                                    Console.WriteLine($"      - {dirName}");
                                }
                                if (dirsInTarget.Length > 3)
                                {
                                    Console.WriteLine($"      ... and {dirsInTarget.Length - 3} more subdirs");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"   ❌ ERROR: Target directory doesn't exist!");
                            Console.WriteLine($"      This breaks directory traversal!");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ❌ ERROR processing dir.c9r: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error analyzing vault: {ex.Message}");
            }
        }







        private static void CreateProperVaultCryptomator(string realVaultPath, string testVaultPath, string password)
        {
            Console.WriteLine("🔧 Creating properly signed vault.cryptomator file...");

            // Read the real vault.cryptomator to get the payload structure
            string realVaultConfigPath = Path.Combine(realVaultPath, "vault.cryptomator");
            string realJWT = File.ReadAllText(realVaultConfigPath).Trim();
            Console.WriteLine($"📄 Real JWT: {realJWT}");

            var jwtParts = realJWT.Split('.');
            if (jwtParts.Length != 3)
            {
                throw new InvalidOperationException("Invalid JWT format in real vault");
            }

            // Decode the real payload to understand the structure
            string realPayloadJson = UvfLib.Core.Common.Base64Url.DecodeToString(jwtParts[1]);
            Console.WriteLine($"📋 Real payload: {realPayloadJson}");

            // Parse the payload to extract settings
            using (JsonDocument doc = JsonDocument.Parse(realPayloadJson))
            {
                var root = doc.RootElement;
                int format = root.GetProperty("format").GetInt32();
                string cipherCombo = root.GetProperty("cipherCombo").GetString();
                int shorteningThreshold = root.GetProperty("shorteningThreshold").GetInt32();

                Console.WriteLine($"📊 Settings: format={format}, cipherCombo={cipherCombo}, threshold={shorteningThreshold}");

                // Generate a new JTI (UUID)
                string newJti = Guid.NewGuid().ToString();
                Console.WriteLine($"🆔 New JTI: {newJti}");

                // Create our own payload with the same settings but new JTI
                var ourPayload = new
                {
                    jti = newJti,
                    format = format,
                    cipherCombo = cipherCombo,
                    shorteningThreshold = shorteningThreshold
                };

                var jsonOptions = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = false  // Compact format
                };
                string ourPayloadJson = System.Text.Json.JsonSerializer.Serialize(ourPayload, jsonOptions);
                Console.WriteLine($"📋 Our payload: {ourPayloadJson}");

                // Create header (same as real Cryptomator)
                var header = new
                {
                    kid = "masterkeyfile:masterkey.cryptomator",
                    alg = "HS256",
                    typ = "JWT"
                };
                string headerJson = System.Text.Json.JsonSerializer.Serialize(header, jsonOptions);
                string headerBase64 = UvfLib.Core.Common.Base64Url.Encode(Encoding.UTF8.GetBytes(headerJson));
                string payloadBase64 = UvfLib.Core.Common.Base64Url.Encode(Encoding.UTF8.GetBytes(ourPayloadJson));

                Console.WriteLine($"🔤 Header: {headerJson}");
                Console.WriteLine($"🔤 Header (Base64): {headerBase64}");
                Console.WriteLine($"🔤 Payload (Base64): {payloadBase64}");

                // Load our masterkey to get the MAC key for signing
                string ourMasterkeyPath = Path.Combine(testVaultPath, "masterkey.cryptomator");
                byte[] masterkeyBytes = File.ReadAllBytes(ourMasterkeyPath);

                using (var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, password))
                {
                    // Get MAC key for signing
                    var vaultType = typeof(Vault);
                    var perpetualMasterkeyField = vaultType.GetField("_perpetualMasterkey",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (perpetualMasterkeyField != null)
                    {
                        var perpetualMasterkey = perpetualMasterkeyField.GetValue(vault);
                        if (perpetualMasterkey != null)
                        {
                            var perpetualMasterkeyType = perpetualMasterkey.GetType();
                            var getMacKeyMethod = perpetualMasterkeyType.GetMethod("GetMacKey");

                            if (getMacKeyMethod != null)
                            {
                                using (var macKeySecret = (IDisposable)getMacKeyMethod.Invoke(perpetualMasterkey, null)!)
                                {
                                    var getEncodedMethod = macKeySecret.GetType().GetMethod("GetEncoded");
                                    if (getEncodedMethod != null)
                                    {
                                        byte[] macKeyBytes = (byte[])getEncodedMethod.Invoke(macKeySecret, null)!;
                                        Console.WriteLine($"🔑 MAC Key length: {macKeyBytes.Length} bytes");

                                        // Sign the JWT
                                        string signingInput = $"{headerBase64}.{payloadBase64}";
                                        byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);

                                        byte[] signatureBytes;
                                        using (var hmac = new HMACSHA256(macKeyBytes))
                                        {
                                            signatureBytes = hmac.ComputeHash(signingInputBytes);
                                        }

                                        string signature = UvfLib.Core.Common.Base64Url.Encode(signatureBytes);
                                        Console.WriteLine($"🔐 Signature: {signature}");

                                        // Create final JWT
                                        string finalJWT = $"{headerBase64}.{payloadBase64}.{signature}";
                                        Console.WriteLine($"📄 Final JWT: {finalJWT}");

                                        // Write to our vault WITHOUT BOM (important for Java compatibility)
                                        string ourVaultConfigPath = Path.Combine(testVaultPath, "vault.cryptomator");
                                        byte[] jwtBytes = new UTF8Encoding(false).GetBytes(finalJWT); // false = no BOM
                                        File.WriteAllBytes(ourVaultConfigPath, jwtBytes);

                                        long fileSize = new FileInfo(ourVaultConfigPath).Length;
                                        Console.WriteLine($"✅ Created properly signed vault.cryptomator: {fileSize} bytes");

                                        return;
                                    }
                                }
                            }
                        }
                    }
                }

                throw new InvalidOperationException("Could not extract MAC key from masterkey");
            }
        }

        private static void TestMacKeyExtraction()
        {
            Console.WriteLine("===== Testing MAC Key Extraction =====");

            string realVaultPath = @"D:\cyptomatortest\martintest2";
            string realMasterkeyPath = Path.Combine(realVaultPath, "masterkey.cryptomator");
            string realVaultConfigPath = Path.Combine(realVaultPath, "vault.cryptomator");
            string password = "your-super-secret-password";

            try
            {
                // Load the real masterkey
                byte[] masterkeyBytes = File.ReadAllBytes(realMasterkeyPath);
                Console.WriteLine($"📄 Masterkey size: {masterkeyBytes.Length} bytes");

                using (var vault = Vault.LoadCryptomatorV8Vault(masterkeyBytes, password))
                {
                    Console.WriteLine("✅ Vault loaded successfully");

                    // Extract MAC key using reflection (same as our previous method)
                    var vaultType = typeof(Vault);
                    var perpetualMasterkeyField = vaultType.GetField("_perpetualMasterkey",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (perpetualMasterkeyField != null)
                    {
                        var perpetualMasterkey = perpetualMasterkeyField.GetValue(vault);
                        if (perpetualMasterkey != null)
                        {
                            var perpetualMasterkeyType = perpetualMasterkey.GetType();
                            var getMacKeyMethod = perpetualMasterkeyType.GetMethod("GetMacKey");

                            if (getMacKeyMethod != null)
                            {
                                using (var macKeySecret = (IDisposable)getMacKeyMethod.Invoke(perpetualMasterkey, null)!)
                                {
                                    var getEncodedMethod = macKeySecret.GetType().GetMethod("GetEncoded");
                                    if (getEncodedMethod != null)
                                    {
                                        byte[] macKeyBytes = (byte[])getEncodedMethod.Invoke(macKeySecret, null)!;
                                        Console.WriteLine($"🔑 Extracted MAC Key: {macKeyBytes.Length} bytes");
                                        Console.WriteLine($"🔑 MAC Key (hex): {Convert.ToHexString(macKeyBytes)}");

                                        // Test signing with this key
                                        TestJWTSigning(macKeyBytes, realVaultConfigPath);
                                        return;
                                    }
                                }
                            }
                        }
                    }

                    Console.WriteLine("❌ Could not extract MAC key using reflection");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
            }
        }

        private static void TestJWTSigning(byte[] macKeyBytes, string realVaultConfigPath)
        {
            Console.WriteLine("\n🔧 Testing JWT Signing...");

            // Read the real JWT to compare
            string realJWT = File.ReadAllText(realVaultConfigPath).Trim();
            Console.WriteLine($"📄 Real JWT: {realJWT}");

            var jwtParts = realJWT.Split('.');
            if (jwtParts.Length != 3)
            {
                Console.WriteLine("❌ Invalid JWT format");
                return;
            }

            // Test: can we verify the real JWT signature with our extracted key?
            string realSigningInput = $"{jwtParts[0]}.{jwtParts[1]}";
            byte[] realSigningInputBytes = Encoding.UTF8.GetBytes(realSigningInput);

            byte[] expectedSignatureBytes;
            using (var hmac = new HMACSHA256(macKeyBytes))
            {
                expectedSignatureBytes = hmac.ComputeHash(realSigningInputBytes);
            }

            string expectedSignature = UvfLib.Core.Common.Base64Url.Encode(expectedSignatureBytes);
            string actualSignature = jwtParts[2];

            Console.WriteLine($"🔐 Expected signature: {expectedSignature}");
            Console.WriteLine($"🔐 Actual signature:   {actualSignature}");
            Console.WriteLine($"✅ Signatures match: {expectedSignature == actualSignature}");

            if (expectedSignature == actualSignature)
            {
                Console.WriteLine("🎉 SUCCESS: We can correctly sign JWTs with this MAC key!");

                // Now test creating our own JWT
                CreateOurOwnJWT(macKeyBytes, jwtParts[1]); // Use same payload
            }
            else
            {
                Console.WriteLine("❌ FAILED: MAC key extraction or signing process is incorrect");

                // Debug the signing process
                Console.WriteLine($"\n🔍 Debugging signature mismatch:");
                Console.WriteLine($"   MAC key length: {macKeyBytes.Length}");
                Console.WriteLine($"   Signing input: '{realSigningInput}'");
                Console.WriteLine($"   Signing input bytes: {realSigningInputBytes.Length}");
                Console.WriteLine($"   Expected sig bytes: {expectedSignatureBytes.Length}");
            }
        }

        private static void CreateOurOwnJWT(byte[] macKeyBytes, string realPayloadBase64)
        {
            Console.WriteLine("\n🔧 Creating our own JWT...");

            // Decode the real payload to understand the structure
            string realPayloadJson = UvfLib.Core.Common.Base64Url.DecodeToString(realPayloadBase64);
            Console.WriteLine($"📋 Real payload: {realPayloadJson}");

            // Parse the payload to extract settings
            using (JsonDocument doc = JsonDocument.Parse(realPayloadJson))
            {
                var root = doc.RootElement;
                int format = root.GetProperty("format").GetInt32();
                string cipherCombo = root.GetProperty("cipherCombo").GetString();
                int shorteningThreshold = root.GetProperty("shorteningThreshold").GetInt32();

                // Generate a new JTI (UUID) for our vault
                string newJti = Guid.NewGuid().ToString();
                Console.WriteLine($"🆔 Our JTI: {newJti}");

                // Create our payload with the same settings but new JTI
                var ourPayload = new
                {
                    jti = newJti,
                    format = format,
                    cipherCombo = cipherCombo,
                    shorteningThreshold = shorteningThreshold
                };

                var jsonOptions = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = false  // Compact format
                };
                string ourPayloadJson = System.Text.Json.JsonSerializer.Serialize(ourPayload, jsonOptions);
                Console.WriteLine($"📋 Our payload: {ourPayloadJson}");

                // Create header (same as real Cryptomator)
                var header = new
                {
                    kid = "masterkeyfile:masterkey.cryptomator",
                    alg = "HS256",
                    typ = "JWT"
                };
                string headerJson = System.Text.Json.JsonSerializer.Serialize(header, jsonOptions);
                string headerBase64 = UvfLib.Core.Common.Base64Url.Encode(Encoding.UTF8.GetBytes(headerJson));
                string payloadBase64 = UvfLib.Core.Common.Base64Url.Encode(Encoding.UTF8.GetBytes(ourPayloadJson));

                // Sign our JWT
                string signingInput = $"{headerBase64}.{payloadBase64}";
                byte[] signingInputBytes = Encoding.UTF8.GetBytes(signingInput);

                byte[] signatureBytes;
                using (var hmac = new HMACSHA256(macKeyBytes))
                {
                    signatureBytes = hmac.ComputeHash(signingInputBytes);
                }

                string signature = UvfLib.Core.Common.Base64Url.Encode(signatureBytes);
                string finalJWT = $"{headerBase64}.{payloadBase64}.{signature}";

                Console.WriteLine($"🔐 Our signature: {signature}");
                Console.WriteLine($"📄 Our final JWT: {finalJWT}");

                // Test: write this to a temp file and see if it works
                string testPath = Path.Combine(Path.GetTempPath(), "test_vault.cryptomator");
                byte[] jwtBytes = new UTF8Encoding(false).GetBytes(finalJWT); // No BOM
                File.WriteAllBytes(testPath, jwtBytes);

                Console.WriteLine($"💾 Saved test JWT to: {testPath}");
                Console.WriteLine($"📏 File size: {jwtBytes.Length} bytes");
                Console.WriteLine("🎯 This JWT should work with real Cryptomator!");
            }
        }

        private static void TestVaultFileComparison()
        {
            Console.WriteLine("===== Vault File Comparison =====");
            Console.WriteLine("Comparing all files between real vault and our vault with checksums");
            Console.WriteLine();

            string realVaultPath = @"D:\cyptomatortest\martintest2";
            string ourVaultPath = @"D:\temp\uvf\IdenticalTestVault2";

            try
            {
                // Get all files from both vaults
                var realFiles = GetAllVaultFiles(realVaultPath, "REAL VAULT");
                var ourFiles = GetAllVaultFiles(ourVaultPath, "OUR VAULT");

                // Compare the file lists
                CompareVaultFiles(realFiles, ourFiles);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during vault comparison: {ex.Message}");
            }

            Console.WriteLine("\n===== Vault File Comparison Complete =====");
        }

        private static Dictionary<string, VaultFileInfo> GetAllVaultFiles(string vaultPath, string vaultName)
        {
            Console.WriteLine($"\n🔍 Analyzing {vaultName}: {vaultPath}");

            var files = new Dictionary<string, VaultFileInfo>();

            if (!Directory.Exists(vaultPath))
            {
                Console.WriteLine($"❌ Vault directory not found: {vaultPath}");
                return files;
            }

            // Get all files recursively
            string[] allFiles = Directory.GetFiles(vaultPath, "*", SearchOption.AllDirectories);
            Console.WriteLine($"📊 Found {allFiles.Length} total files");

            foreach (string filePath in allFiles)
            {
                try
                {
                    string relativePath = Path.GetRelativePath(vaultPath, filePath);
                    var fileInfo = new FileInfo(filePath);

                    // Calculate MD5 checksum
                    string md5Hash = CalculateFileMD5(filePath);

                    var vaultFileInfo = new VaultFileInfo
                    {
                        RelativePath = relativePath,
                        FullPath = filePath,
                        Size = fileInfo.Length,
                        MD5Hash = md5Hash,
                        LastModified = fileInfo.LastWriteTime
                    };

                    files[relativePath] = vaultFileInfo;

                    // Show progress for large files
                    if (fileInfo.Length > 1024)
                    {
                        Console.WriteLine($"   📄 {relativePath} ({fileInfo.Length} bytes) - MD5: {md5Hash}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Error processing {filePath}: {ex.Message}");
                }
            }

            Console.WriteLine($"✅ {vaultName}: {files.Count} files processed");
            return files;
        }

        private static void CompareVaultFiles(Dictionary<string, VaultFileInfo> realFiles, Dictionary<string, VaultFileInfo> ourFiles)
        {
            Console.WriteLine($"\n📊 COMPARISON RESULTS:");
            Console.WriteLine($"   Real vault: {realFiles.Count} files");
            Console.WriteLine($"   Our vault:  {ourFiles.Count} files");

            // Files that exist in both vaults
            var matchingFiles = new List<string>();
            var identicalFiles = new List<string>();
            var differentFiles = new List<string>();

            // Files only in real vault
            var onlyInReal = new List<string>();

            // Files only in our vault  
            var onlyInOurs = new List<string>();

            // Check files in real vault
            foreach (var kvp in realFiles)
            {
                string relativePath = kvp.Key;
                var realFile = kvp.Value;

                if (ourFiles.ContainsKey(relativePath))
                {
                    matchingFiles.Add(relativePath);
                    var ourFile = ourFiles[relativePath];

                    if (realFile.MD5Hash == ourFile.MD5Hash && realFile.Size == ourFile.Size)
                    {
                        identicalFiles.Add(relativePath);
                    }
                    else
                    {
                        differentFiles.Add(relativePath);
                        Console.WriteLine($"\n❌ DIFFERENT: {relativePath}");
                        Console.WriteLine($"   Real: {realFile.Size} bytes, MD5: {realFile.MD5Hash}");
                        Console.WriteLine($"   Ours: {ourFile.Size} bytes, MD5: {ourFile.MD5Hash}");
                    }
                }
                else
                {
                    onlyInReal.Add(relativePath);
                }
            }

            // Check files only in our vault
            foreach (var kvp in ourFiles)
            {
                string relativePath = kvp.Key;
                if (!realFiles.ContainsKey(relativePath))
                {
                    onlyInOurs.Add(relativePath);
                }
            }

            // Summary
            Console.WriteLine($"\n📈 SUMMARY:");
            Console.WriteLine($"   ✅ Identical files: {identicalFiles.Count}");
            Console.WriteLine($"   ❌ Different files: {differentFiles.Count}");
            Console.WriteLine($"   📁 Only in real:    {onlyInReal.Count}");
            Console.WriteLine($"   📁 Only in ours:    {onlyInOurs.Count}");

            // Show files only in real vault
            if (onlyInReal.Count > 0)
            {
                Console.WriteLine($"\n📁 FILES ONLY IN REAL VAULT ({onlyInReal.Count}):");
                foreach (string file in onlyInReal.Take(10))
                {
                    var realFile = realFiles[file];
                    Console.WriteLine($"   - {file} ({realFile.Size} bytes)");
                }
                if (onlyInReal.Count > 10)
                {
                    Console.WriteLine($"   ... and {onlyInReal.Count - 10} more files");
                }
            }

            // Show files only in our vault
            if (onlyInOurs.Count > 0)
            {
                Console.WriteLine($"\n📁 FILES ONLY IN OUR VAULT ({onlyInOurs.Count}):");
                foreach (string file in onlyInOurs.Take(10))
                {
                    var ourFile = ourFiles[file];
                    Console.WriteLine($"   - {file} ({ourFile.Size} bytes)");
                }
                if (onlyInOurs.Count > 10)
                {
                    Console.WriteLine($"   ... and {onlyInOurs.Count - 10} more files");
                }
            }

            // Show structural differences
            Console.WriteLine($"\n🏗️ STRUCTURAL ANALYSIS:");

            // Count by file extension
            var realExtensions = realFiles.Keys.Select(f => Path.GetExtension(f).ToLower()).GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());
            var ourExtensions = ourFiles.Keys.Select(f => Path.GetExtension(f).ToLower()).GroupBy(e => e).ToDictionary(g => g.Key, g => g.Count());

            var allExtensions = realExtensions.Keys.Union(ourExtensions.Keys).OrderBy(e => e);

            Console.WriteLine($"   File type comparison:");
            foreach (string ext in allExtensions)
            {
                int realCount = realExtensions.GetValueOrDefault(ext, 0);
                int ourCount = ourExtensions.GetValueOrDefault(ext, 0);
                string status = realCount == ourCount ? "✅" : "❌";
                Console.WriteLine($"   {status} {ext}: Real={realCount}, Ours={ourCount}");
            }
        }

        private static string CalculateFileMD5(string filePath)
        {
            try
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                using var stream = File.OpenRead(filePath);
                byte[] hashBytes = md5.ComputeHash(stream);
                return Convert.ToHexString(hashBytes);
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }

        private class VaultFileInfo
        {
            public string RelativePath { get; set; } = "";
            public string FullPath { get; set; } = "";
            public long Size { get; set; }
            public string MD5Hash { get; set; } = "";
            public DateTime LastModified { get; set; }
        }
    }
}