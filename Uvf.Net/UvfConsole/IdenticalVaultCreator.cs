using UvfLib;
using UvfLib.Vault.VaultHelpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UvfConsole
{
    /// <summary>
    /// Handles creation of vaults that are identical to real Cryptomator vaults
    /// for compatibility testing and verification purposes.
    /// </summary>
    public static class IdenticalVaultCreator
    {
        /// <summary>
        /// Creates a vault that mimics the structure and content of a real Cryptomator vault
        /// using the same masterkey and UUIDs for perfect compatibility testing.
        /// </summary>
        public static void CreateIdenticalVault()
        {
            Console.WriteLine("===== Testing Identical Vault Creation =====");
            Console.WriteLine("This creates our vault using real Cryptomator's masterkey and UUIDs");
            Console.WriteLine();

            string realVaultPath = @"D:\cyptomatortest\martintest2";  
            string realMasterkeyPath = Path.Combine(realVaultPath, "masterkey.cryptomator");
            string testVaultPath = @"D:\temp\uvf\IdenticalTestVault2";  
            string password = "your-super-secret-password";

            // Clean up test vault
            if (Directory.Exists(testVaultPath))
            {
                Directory.Delete(testVaultPath, true);
            }
            Directory.CreateDirectory(testVaultPath);

            Console.WriteLine($"📋 Real vault: {realVaultPath}");
            Console.WriteLine($"🔧 Test vault: {testVaultPath}");
            Console.WriteLine();

            try
            {
                // First, analyze the real vault structure to build correct mapping
                Console.WriteLine("🔍 ANALYZING REAL VAULT STRUCTURE:");
                byte[] realMasterkeyBytes = File.ReadAllBytes(realMasterkeyPath);
                using var analyzeVault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, password);

                // Get the correct directory mapping from the real vault
                var directoryMapping = AnalyzeRealVaultStructure(analyzeVault, realVaultPath, password);
                
                Console.WriteLine($"📊 Found {directoryMapping.Count} directory mappings:");
                foreach (var mapping in directoryMapping)
                {
                    Console.WriteLine($"   '{mapping.Value}' → UUID: {mapping.Key}");
                }

                // Copy the real masterkey to our test vault
                string testMasterkeyPath = Path.Combine(testVaultPath, "masterkey.cryptomator");
                File.WriteAllBytes(testMasterkeyPath, realMasterkeyBytes);

                Console.WriteLine("✅ Copied real masterkey to test vault");

                // Load vault with real masterkey
                using var vault = Vault.LoadCryptomatorV8Vault(realMasterkeyBytes, password);
                Console.WriteLine("✅ Loaded vault with real masterkey");

                // Create root directory structure (same as real vault)
                DirectoryMetadata rootMetadata = vault.GetRootDirectoryMetadata();
                string rootDirPath = Path.Combine(testVaultPath, vault.GetRootDirectoryPath());
                Directory.CreateDirectory(rootDirPath);
                Console.WriteLine($"📁 Created root directory: {vault.GetRootDirectoryPath()}");

                // Create root dirid.c9r (empty for root)
                string rootDiridPath = Path.Combine(rootDirPath, "dirid.c9r");
                using (FileStream diridStream = File.Create(rootDiridPath))
                using (Stream encryptingStream = vault.GetEncryptingStream(diridStream))
                {
                    // Write empty content for root
                }
                Console.WriteLine($"✅ Created root dirid.c9r: {new FileInfo(rootDiridPath).Length} bytes");

                // Create subdirectories using the correct mapping
                foreach (var mapping in directoryMapping)
                {
                    string uuid = mapping.Key;
                    string dirName = mapping.Value;
                    
                    Console.WriteLine($"\n🔄 Creating subdirectory '{dirName}' with UUID: {uuid}");

                    // Create DirectoryMetadata with correct UUID
                    DirectoryMetadata subdirMetadata = vault.CreateCryptomatorV8DirectoryMetadataFromUuid(uuid);
                    Console.WriteLine($"📋 Created metadata - DirId: {subdirMetadata.DirId}");

                    // Calculate paths
                    string expectedSubdirContentPath = vault.GetCryptomatorV8DirectoryPathByUuid(uuid);
                    string fullSubdirContentPath = Path.Combine(testVaultPath, expectedSubdirContentPath);
                    Console.WriteLine($"🎯 Expected subdirectory content path: {expectedSubdirContentPath}");

                    // Create subdirectory content directory
                    Directory.CreateDirectory(fullSubdirContentPath);

                    // Create subdirectory dirid.c9r (contains its own UUID)
                    string subdirDiridPath = Path.Combine(fullSubdirContentPath, "dirid.c9r");
                    using (FileStream diridStream = File.Create(subdirDiridPath))
                    using (Stream encryptingStream = vault.GetEncryptingStream(diridStream))
                    {
                        byte[] uuidBytes = System.Text.Encoding.ASCII.GetBytes(uuid);
                        encryptingStream.Write(uuidBytes, 0, uuidBytes.Length);
                    }
                    Console.WriteLine($"✅ Created subdirectory dirid.c9r: {new FileInfo(subdirDiridPath).Length} bytes");

                    // Create encrypted subdirectory name in root
                    string encryptedSubdirName = vault.EncryptFilename(dirName, rootMetadata);
                    string encryptedSubdirPath = Path.Combine(rootDirPath, encryptedSubdirName);
                    Directory.CreateDirectory(encryptedSubdirPath);
                    Console.WriteLine($"📁 Created encrypted subdirectory: {encryptedSubdirName}");

                    // Create dir.c9r file in encrypted subdirectory (pointing to content)
                    string dirC9rPath = Path.Combine(encryptedSubdirPath, "dir.c9r");
                    File.WriteAllBytes(dirC9rPath, new UTF8Encoding(false).GetBytes(uuid));
                    Console.WriteLine($"✅ Created dir.c9r: {dirC9rPath}");
                    Console.WriteLine($"📄 Content: '{uuid}'");
                }

                // Create properly signed vault.cryptomator file
                Console.WriteLine("\n🔧 Creating properly signed vault.cryptomator...");
                byte[] masterkeyContent = File.ReadAllBytes(testMasterkeyPath);
                var createConfigMethod = typeof(Vault).GetMethod("CreateNewCryptomatorV8VaultConfigContentSigned",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                if (createConfigMethod != null)
                {
                    byte[] vaultConfigContent = (byte[])createConfigMethod.Invoke(null, new object[] { masterkeyContent, password });
                    string testVaultConfigPath = Path.Combine(testVaultPath, "vault.cryptomator");
                    File.WriteAllBytes(testVaultConfigPath, vaultConfigContent);
                    Console.WriteLine($"✅ Created properly signed vault.cryptomator: {vaultConfigContent.Length} bytes");
                }
                else
                {
                    throw new InvalidOperationException("Could not access CreateNewCryptomatorV8VaultConfigContentSigned method");
                }

                // Now populate with actual files from EncryptionTestSource using correct mapping
                string sourcePath = @"D:\temp\uvf\EncryptionTestSource";
                if (Directory.Exists(sourcePath))
                {
                    Console.WriteLine($"\n📂 Populating vault with files from: {sourcePath}");
                    PopulateVaultWithCorrectMapping(vault, sourcePath, testVaultPath, directoryMapping);
                }

                Console.WriteLine($"\n💡 Complete vault created at: {testVaultPath}");
                Console.WriteLine("🎯 Structure and files should now match martintest2 exactly!");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error creating identical vault: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("\n===== Identical Vault Creation Complete =====");
        }

        /// <summary>
        /// Analyzes the structure of a real Cryptomator vault to understand
        /// the mapping between plaintext directory names and their UUIDs.
        /// </summary>
        /// <param name="vault">The loaded vault instance</param>
        /// <param name="realVaultPath">Path to the real Cryptomator vault</param>
        /// <param name="password">Vault password</param>
        /// <returns>Dictionary mapping UUID to plaintext directory name</returns>
        private static Dictionary<string, string> AnalyzeRealVaultStructure(Vault vault, string realVaultPath, string password)
        {
            var directoryMapping = new Dictionary<string, string>();
            
            Console.WriteLine("🔍 Analyzing encrypted directory names in real vault...");
            
            // Get the root directory path
            string realRootPath = vault.GetRootDirectoryPath();
            string realRootFullPath = Path.Combine(realVaultPath, realRootPath);
            
            if (!Directory.Exists(realRootFullPath))
            {
                Console.WriteLine($"❌ Real root directory not found: {realRootFullPath}");
                return directoryMapping;
            }

            // Get root metadata for decryption
            DirectoryMetadata rootMetadata = vault.GetRootDirectoryMetadata();

            // Find all encrypted directories in the root (those ending with .c9r)
            var encryptedDirs = Directory.GetDirectories(realRootFullPath, "*.c9r");
            Console.WriteLine($"📂 Found {encryptedDirs.Length} encrypted directories in root");

            foreach (string encryptedDirPath in encryptedDirs)
            {
                try
                {
                    string encryptedDirName = Path.GetFileName(encryptedDirPath);
                    Console.WriteLine($"\n🔍 Processing encrypted directory: {encryptedDirName}");

                    // Decrypt the directory name to get the original plain name
                    string plainDirName = vault.DecryptFilename(encryptedDirName, rootMetadata);
                    Console.WriteLine($"  📝 Decrypted name: '{plainDirName}'");

                    // Read the dir.c9r file to get the UUID this directory points to
                    string dirC9rPath = Path.Combine(encryptedDirPath, "dir.c9r");
                    if (File.Exists(dirC9rPath))
                    {
                        string uuid = File.ReadAllText(dirC9rPath, System.Text.Encoding.UTF8).Trim();
                        Console.WriteLine($"  🎯 Points to UUID: {uuid}");

                        // Verify this UUID has content (should exist as a directory path)
                        string expectedContentPath = vault.GetCryptomatorV8DirectoryPathByUuid(uuid);
                        string fullContentPath = Path.Combine(realVaultPath, expectedContentPath);
                        bool hasContent = Directory.Exists(fullContentPath);
                        Console.WriteLine($"  📁 Content directory: {expectedContentPath} (exists: {hasContent})");

                        if (hasContent)
                        {
                            // Check if directory has files (not just dirid.c9r)
                            var files = Directory.GetFiles(fullContentPath);
                            var contentFiles = files.Where(f => Path.GetFileName(f) != "dirid.c9r").ToArray();
                            Console.WriteLine($"  📄 Contains {contentFiles.Length} content files");

                            directoryMapping[uuid] = plainDirName;
                            Console.WriteLine($"  ✅ Added mapping: '{plainDirName}' → {uuid}");
                        }
                        else
                        {
                            Console.WriteLine($"  ⚠️ Skipping - no content directory found");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"  ❌ No dir.c9r file found in {encryptedDirPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ❌ Error processing {encryptedDirPath}: {ex.Message}");
                }
            }

            return directoryMapping;
        }

        /// <summary>
        /// Populates the vault with files from the source directory using the correct
        /// directory-to-UUID mapping to ensure files go to the right locations.
        /// </summary>
        /// <param name="vault">The vault instance</param>
        /// <param name="sourcePath">Path to source files</param>
        /// <param name="vaultPath">Path to the vault being created</param>
        /// <param name="directoryMapping">Mapping of UUID to directory name</param>
        private static void PopulateVaultWithCorrectMapping(Vault vault, string sourcePath, string vaultPath, Dictionary<string, string> directoryMapping)
        {
            // Get root metadata and path
            DirectoryMetadata rootMetadata = vault.GetRootDirectoryMetadata();
            string rootDirPath = Path.Combine(vaultPath, vault.GetRootDirectoryPath());

            Console.WriteLine($"📁 Populating root directory: {vault.GetRootDirectoryPath()}");

            // Encrypt files in root directory
            foreach (string sourceFilePath in Directory.GetFiles(sourcePath))
            {
                string plainName = Path.GetFileName(sourceFilePath);
                string encryptedName = vault.EncryptFilename(plainName, rootMetadata);
                string targetEncryptedFilePath = Path.Combine(rootDirPath, encryptedName);

                Console.WriteLine($"  📄 Encrypting: {plainName} -> {encryptedName}");

                using (FileStream sourceStream = File.OpenRead(sourceFilePath))
                using (FileStream targetStream = File.Create(targetEncryptedFilePath))
                using (Stream encryptingStream = vault.GetEncryptingStream(targetStream))
                {
                    sourceStream.CopyTo(encryptingStream);
                }
            }

            // Process subdirectories using correct mapping
            var sourceSubdirs = Directory.GetDirectories(sourcePath);
            foreach (string sourceSubdirPath in sourceSubdirs)
            {
                string plainSubdirName = Path.GetFileName(sourceSubdirPath);
                Console.WriteLine($"\n📁 Processing source subdirectory: {plainSubdirName}");

                // Find the UUID for this directory name in our mapping
                var matchingMapping = directoryMapping.FirstOrDefault(kvp => kvp.Value == plainSubdirName);
                if (matchingMapping.Key != null)
                {
                    string uuid = matchingMapping.Key;
                    Console.WriteLine($"  🎯 Found mapping: '{plainSubdirName}' → UUID: {uuid}");

                    // Calculate content path using correct UUID
                    string contentPath = vault.GetCryptomatorV8DirectoryPathByUuid(uuid);
                    string fullContentPath = Path.Combine(vaultPath, contentPath);

                    Console.WriteLine($"  📂 Content directory: {contentPath}");

                    // Create DirectoryMetadata for this subdirectory
                    DirectoryMetadata subdirMetadata = vault.CreateCryptomatorV8DirectoryMetadataFromUuid(uuid);

                    // Encrypt files in this subdirectory
                    var sourceFiles = Directory.GetFiles(sourceSubdirPath);
                    Console.WriteLine($"  📄 Encrypting {sourceFiles.Length} files in subdirectory");
                    
                    foreach (string sourceFilePath in sourceFiles)
                    {
                        string plainName = Path.GetFileName(sourceFilePath);
                        string encryptedName = vault.EncryptFilename(plainName, subdirMetadata);
                        string targetEncryptedFilePath = Path.Combine(fullContentPath, encryptedName);

                        Console.WriteLine($"    📄 Encrypting: {plainName} -> {encryptedName}");

                        using (FileStream sourceStream = File.OpenRead(sourceFilePath))
                        using (FileStream targetStream = File.Create(targetEncryptedFilePath))
                        using (Stream encryptingStream = vault.GetEncryptingStream(targetStream))
                        {
                            sourceStream.CopyTo(encryptingStream);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"  ⚠️ No mapping found for directory '{plainSubdirName}' - skipping");
                }
            }

            Console.WriteLine("\n✅ Vault population complete!");
        }
    }
} 