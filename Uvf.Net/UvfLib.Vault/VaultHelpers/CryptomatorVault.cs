/*******************************************************************************
 * Copyright (c) 2016 Sebastian Stenzel and others.
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the accompanying LICENSE.txt.
 *
 * Contributors:
 *     Sebastian Stenzel - initial API and implementation
 *******************************************************************************/

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UvfLib.Vault.VaultHelpers
{
    /// <summary>
    /// High-level API for working with Cryptomator V8 vaults.
    /// Provides a file-system-like interface that handles all the complexity
    /// of directory UUIDs, encryption, and vault structure internally.
    /// </summary>
    public class CryptomatorVault : IDisposable
    {
        private readonly string _vaultFolder;
        private readonly byte[] _passwordBytes;
        private VaultHandler? _vault;
        private bool _disposed;

        // Path constants
        private const string MASTERKEY_FILE = "masterkey.cryptomator";
        private const string VAULT_CONFIG_FILE = "vault.cryptomator";
        private const string ROOT_PATH = "/";

        private CryptomatorVault(string vaultFolder, byte[] passwordBytes)
        {
            _vaultFolder = NormalizePath(vaultFolder);
            _passwordBytes = passwordBytes ?? throw new ArgumentNullException(nameof(passwordBytes));
        }

        #region Factory Methods

        /// <summary>
        /// Creates a new Cryptomator V8 vault in the specified folder.
        /// </summary>
        /// <param name="vaultFolder">Path to the vault folder</param>
        /// <param name="passwordBytes">Vault password as UTF-8 encoded bytes</param>
        /// <returns>New CryptomatorVault instance</returns>
        /// <exception cref="InvalidOperationException">If vault already exists</exception>
        public static CryptomatorVault Create(string vaultFolder, byte[] passwordBytes)
        {
            var vault = new CryptomatorVault(vaultFolder, passwordBytes);
            vault.CreateVaultFiles();
            vault.LoadVault();
            return vault;
        }

        /// <summary>
        /// Loads an existing Cryptomator V8 vault from the specified folder.
        /// </summary>
        /// <param name="vaultFolder">Path to the vault folder</param>
        /// <param name="passwordBytes">Vault password as UTF-8 encoded bytes</param>
        /// <returns>Loaded CryptomatorVault instance</returns>
        /// <exception cref="InvalidOperationException">If vault doesn't exist or is invalid</exception>
        public static CryptomatorVault Load(string vaultFolder, byte[] passwordBytes)
        {
            var vault = new CryptomatorVault(vaultFolder, passwordBytes);
            vault.ValidateVaultExists();
            vault.LoadVault();
            return vault;
        }

        /// <summary>
        /// Loads an existing vault or creates a new one if it doesn't exist.
        /// </summary>
        /// <param name="vaultFolder">Path to the vault folder</param>
        /// <param name="passwordBytes">Vault password as UTF-8 encoded bytes</param>
        /// <returns>CryptomatorVault instance</returns>
        public static CryptomatorVault LoadOrCreate(string vaultFolder, byte[] passwordBytes)
        {
            var vault = new CryptomatorVault(vaultFolder, passwordBytes);
            
            if (vault.VaultExists())
            {
                vault.LoadVault();
            }
            else
            {
                vault.CreateVaultFiles();
                vault.LoadVault();
            }
            
            return vault;
        }

        #endregion

        #region Directory Operations

        /// <summary>
        /// Lists all files and directories in the specified path.
        /// </summary>
        /// <param name="path">Virtual path in the vault (e.g., "/", "/subdir")</param>
        /// <returns>List of vault entries (files and directories)</returns>
        public IEnumerable<VaultEntry> ReadDir(string path = ROOT_PATH)
        {
            EnsureVaultLoaded();
            
            // 1. Normalize path
            path = NormalizePath(path);
            
            // 2. Resolve to directory metadata and physical path
            var (directoryMetadata, physicalPath) = ResolvePathToDirectory(path);
            
            if (!Directory.Exists(physicalPath))
            {
                throw new DirectoryNotFoundException($"Directory not found: {path}");
            }
            
            var entries = new List<VaultEntry>();
            
            // 3. Process all files in the directory
            foreach (string filePath in Directory.GetFiles(physicalPath))
            {
                string fileName = Path.GetFileName(filePath);
                
                // Skip metadata files
                if (fileName == "dirid.c9r" || fileName == "dir.uvf")
                    continue;
                
                // For Cryptomator v8, only process .c9r files
                if (_vault!.IsCryptomatorV8() && !fileName.EndsWith(".c9r"))
                    continue;

                    // Decrypt the filename to get the original name
                    string decryptedName = _vault.DecryptFilename(fileName, directoryMetadata);
                    var fileInfo = new FileInfo(filePath);
                    
                    entries.Add(new VaultEntry
                    {
                        Name = decryptedName,
                        Path = CombinePath(path, decryptedName),
                        IsDirectory = false,
                        Size = CalculateDecryptedFileSize(fileInfo.Length),
                        LastModified = fileInfo.LastWriteTime
                    });
                

            }
            
            // 4. Process all subdirectories
            foreach (string dirPath in Directory.GetDirectories(physicalPath))
            {
                string dirName = Path.GetFileName(dirPath);
                
                // For Cryptomator v8, only process .c9r directories
                if (_vault!.IsCryptomatorV8() && !dirName.EndsWith(".c9r"))
                    continue;

                    // Decrypt the directory name to get the original name
                    string decryptedName = _vault.DecryptFilename(dirName, directoryMetadata);
                    var dirInfo = new DirectoryInfo(dirPath);
                    
                    entries.Add(new VaultEntry
                    {
                        Name = decryptedName,
                        Path = CombinePath(path, decryptedName),
                        IsDirectory = true,
                        Size = 0, // Directories don't have a meaningful size
                        LastModified = dirInfo.LastWriteTime
                    });

            }
            
            return entries.OrderBy(e => e.IsDirectory ? 0 : 1).ThenBy(e => e.Name);
        }

        /// <summary>
        /// Creates a directory at the specified path, creating parent directories as needed.
        /// </summary>
        /// <param name="path">Virtual path to create</param>
        public void CreateDir(string path)
        {
            EnsureVaultLoaded();
            path = NormalizePath(path);

            // Root directory already exists
            if (path == ROOT_PATH)
                return;

            try
            {
                // Split path into segments
                string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                
                // Start from root
                var currentMetadata = _vault!.GetRootDirectoryMetadata();
                string currentPhysicalPath = Path.Combine(_vaultFolder, _vault.GetRootDirectoryPath());
                string currentVirtualPath = "";

                foreach (string segment in segments)
                {
                    currentVirtualPath = currentVirtualPath + "/" + segment;
                    
                    // Check if this directory level already exists
                    if (DirectoryExists(currentVirtualPath))
                    {
                        // Navigate to existing directory
                        var traverseResult = VaultDirectoryHelper.TraverseToDirectory(_vault.Cryptor, _vaultFolder, currentVirtualPath);
                        if (traverseResult.HasValue)
                        {
                            currentMetadata = traverseResult.Value.metadata;
                            currentPhysicalPath = traverseResult.Value.physicalPath;
                        }
                        continue;
                    }

                    // Create new directory
                    // 1. Generate metadata for new directory
                    var newDirMetadata = _vault.CreateNewDirectoryMetadata();
                    
                    // 2. Encrypt directory name using current parent's metadata
                    string encryptedDirName = _vault.EncryptFilename(segment, currentMetadata);
                    
                    // 3. Create the encrypted directory path
                    string encryptedDirPath = Path.Combine(currentPhysicalPath, encryptedDirName);
                    Directory.CreateDirectory(encryptedDirPath);

                    // 4. Create the content directory based on new directory's metadata
                    string contentDirPath = Path.Combine(_vaultFolder, _vault.GetDirectoryPath(newDirMetadata));
                    Directory.CreateDirectory(contentDirPath);

                    // 5. Save directory metadata if needed (format-specific)
                    if (VaultDirectoryHelper.ShouldSaveDirectoryMetadata(_vault.Cryptor, newDirMetadata))
                    {
                        string metadataFilename = VaultDirectoryHelper.GetDirectoryMetadataFilename(_vault.Cryptor, newDirMetadata);
                        string metadataFilePath = Path.Combine(encryptedDirPath, metadataFilename);
                        
                        if (_vault.IsCryptomatorV8())
                        {
                            // For Cryptomator v8, create dir.c9r with UUID
                            var dirIdBytes = Convert.FromBase64String(newDirMetadata.DirId);
                            string uuidString = System.Text.Encoding.ASCII.GetString(dirIdBytes);
                            File.WriteAllText(metadataFilePath, uuidString);
                        }
                        else
                        {
                            // For UVF, encrypt and save metadata
                            byte[] encryptedMetadata = _vault.EncryptDirectoryMetadata(newDirMetadata);
                            File.WriteAllBytes(metadataFilePath, encryptedMetadata);
                        }
                    }

                    // 6. For Cryptomator v8, create dirid.c9r in content directory
                    if (_vault.IsCryptomatorV8())
                    {
                        string diridPath = Path.Combine(contentDirPath, "dirid.c9r");
                        using (var stream = _vault.GetWriteStream(diridPath))
                        {
                            // For non-root directories, write the UUID (36 bytes)
                            var dirIdBytes = Convert.FromBase64String(newDirMetadata.DirId);
                            string uuidString = System.Text.Encoding.ASCII.GetString(dirIdBytes);
                            byte[] uuidBytes = System.Text.Encoding.ASCII.GetBytes(uuidString);
                            stream.Write(uuidBytes, 0, uuidBytes.Length);
                        }
                    }

                    // 7. Update current position for next iteration
                    currentMetadata = newDirMetadata;
                    currentPhysicalPath = contentDirPath; // Content directory becomes the new current path
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to create directory '{path}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Checks if a directory exists at the specified path.
        /// </summary>
        /// <param name="path">Virtual path to check</param>
        /// <returns>True if directory exists, false otherwise</returns>
        public bool DirectoryExists(string path)
        {
            EnsureVaultLoaded();
            path = NormalizePath(path);

            // Root directory always exists
            if (path == ROOT_PATH)
                return true;

            try
            {
                return VaultDirectoryHelper.DirectoryExists(_vault!.Cryptor, _vaultFolder, path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a file exists at the specified path.
        /// </summary>
        /// <param name="path">Virtual path to check</param>
        /// <returns>True if file exists, false otherwise</returns>
        public bool FileExists(string path)
        {
            EnsureVaultLoaded();
            path = NormalizePath(path);

            try
            {
                return VaultFileHelper.FileExists(_vault!.Cryptor, _vaultFolder, path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Gets information about a file or directory.
        /// </summary>
        /// <param name="path">Virtual path</param>
        /// <returns>Information about the entry</returns>
        public VaultEntryInfo GetInfo(string path)
        {
            // TODO: Implementation
            throw new NotImplementedException();
        }

        /// <summary>
        /// Deletes a file or directory at the specified path.
        /// </summary>
        /// <param name="path">Virtual path to delete</param>
        /// <param name="recursive">If true, delete directories recursively</param>
        public void Delete(string path, bool recursive = false)
        {
            // TODO: Implementation
            throw new NotImplementedException();
        }

        /// <summary>
        /// Moves/renames a file or directory.
        /// </summary>
        /// <param name="oldPath">Current virtual path</param>
        /// <param name="newPath">New virtual path</param>
        public void Move(string oldPath, string newPath)
        {
            // TODO: Implementation
            throw new NotImplementedException();
        }

        /// <summary>
        /// Recursively lists all files and directories under the specified path.
        /// </summary>
        /// <param name="path">Virtual path to start from</param>
        /// <returns>All entries under the path</returns>
        public IEnumerable<VaultEntry> ListRecursive(string path = ROOT_PATH)
        {
            // TODO: Implementation
            throw new NotImplementedException();
        }

        #endregion

        #region File Operations

        /// <summary>
        /// Opens a file for reading.
        /// </summary>
        /// <param name="path">Virtual path to the file</param>
        /// <returns>Stream for reading the file content</returns>
        public Stream GetReadStream(string path)
        {
            EnsureVaultLoaded();
            path = NormalizePath(path);

            try
            {
                // 1. Split path into directory and filename
                string directoryPath;
                string fileName;
                
                int lastSlashIndex = path.LastIndexOf('/');
                if (lastSlashIndex == 0)
                {
                    // File is in root directory
                    directoryPath = ROOT_PATH;
                    fileName = path.Substring(1);
                }
                else
                {
                    directoryPath = path.Substring(0, lastSlashIndex);
                    fileName = path.Substring(lastSlashIndex + 1);
                }

                // 2. Resolve to parent directory
                var traverseResult = VaultDirectoryHelper.TraverseToDirectory(_vault!.Cryptor, _vaultFolder, directoryPath);
                if (!traverseResult.HasValue)
                {
                    throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
                }

                var (directoryMetadata, physicalDirPath) = traverseResult.Value;

                // 3. Encrypt the filename to get the physical file path
                string encryptedFileName = _vault.EncryptFilename(fileName, directoryMetadata);
                string physicalFilePath = Path.Combine(physicalDirPath, encryptedFileName);

                // 4. Check if file exists
                if (!File.Exists(physicalFilePath))
                {
                    throw new FileNotFoundException($"File not found: {path}");
                }

                // 5. Return decrypting stream
                return _vault.GetReadStream(physicalFilePath);
            }
            catch (Exception ex) when (!(ex is DirectoryNotFoundException || ex is FileNotFoundException))
            {
                throw new IOException($"Failed to open file for reading '{path}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Opens a file for writing.
        /// </summary>
        /// <param name="path">Virtual path to the file</param>
        /// <returns>Stream for writing the file content</returns>
        public Stream GetWriteStream(string path)
        {
            EnsureVaultLoaded();
            path = NormalizePath(path);

            try
            {
                // 1. Split path into directory and filename
                string directoryPath;
                string fileName;
                
                int lastSlashIndex = path.LastIndexOf('/');
                if (lastSlashIndex == 0)
                {
                    // File is in root directory
                    directoryPath = ROOT_PATH;
                    fileName = path.Substring(1);
                }
                else
                {
                    directoryPath = path.Substring(0, lastSlashIndex);
                    fileName = path.Substring(lastSlashIndex + 1);
                }

                // 2. Ensure parent directory exists
                if (!DirectoryExists(directoryPath))
                {
                    CreateDir(directoryPath);
                }

                // 3. Resolve to parent directory
                var traverseResult = VaultDirectoryHelper.TraverseToDirectory(_vault!.Cryptor, _vaultFolder, directoryPath);
                if (!traverseResult.HasValue)
                {
                    throw new DirectoryNotFoundException($"Directory could not be resolved: {directoryPath}");
                }

                var (directoryMetadata, physicalDirPath) = traverseResult.Value;

                // 4. Encrypt the filename to get the physical file path
                string encryptedFileName = _vault.EncryptFilename(fileName, directoryMetadata);
                string physicalFilePath = Path.Combine(physicalDirPath, encryptedFileName);

                // 5. Return encrypting stream
                return _vault.GetWriteStream(physicalFilePath);
            }
            catch (Exception ex) when (!(ex is DirectoryNotFoundException))
            {
                throw new IOException($"Failed to open file for writing '{path}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Reads the entire content of a file as bytes.
        /// </summary>
        /// <param name="path">Virtual path to the file</param>
        /// <returns>File content as byte array</returns>
        public byte[] ReadAllBytes(string path)
        {
            using var stream = GetReadStream(path);
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }

        /// <summary>
        /// Reads the entire content of a file as text.
        /// </summary>
        /// <param name="path">Virtual path to the file</param>
        /// <returns>File content as string</returns>
        public string ReadAllText(string path)
        {
            using var stream = GetReadStream(path);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Writes bytes to a file, creating it if it doesn't exist.
        /// </summary>
        /// <param name="path">Virtual path to the file</param>
        /// <param name="content">Content to write</param>
        public void WriteAllBytes(string path, byte[] content)
        {
            using var stream = GetWriteStream(path);
            stream.Write(content, 0, content.Length);
        }

        /// <summary>
        /// Writes text to a file, creating it if it doesn't exist.
        /// </summary>
        /// <param name="path">Virtual path to the file</param>
        /// <param name="content">Content to write</param>
        public void WriteAllText(string path, string content)
        {
            using var stream = GetWriteStream(path);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
        }

        #endregion



        #region Utility Methods

        /// <summary>
        /// Gets statistics about the vault (file count, total size, etc.).
        /// </summary>
        /// <returns>Vault statistics</returns>
        public VaultStatistics GetStatistics()
        {
            // TODO: Implementation
            throw new NotImplementedException();
        }

        /// <summary>
        /// Verifies the integrity of the vault structure.
        /// </summary>
        /// <returns>True if vault is valid, false otherwise</returns>
        public bool VerifyIntegrity()
        {
            // TODO: Implementation
            // Check that all dir.c9r and dirid.c9r files are consistent
            // Verify all UUIDs point to existing directories
            // Check that encrypted filenames can be decrypted
            throw new NotImplementedException();
        }

        /// <summary>
        /// Changes the vault password.
        /// </summary>
        /// <param name="newPasswordBytes">New password as UTF-8 encoded bytes</param>
        public void ChangePassword(byte[] newPasswordBytes)
        {
            // TODO: Implementation
            // Re-encrypt masterkey.cryptomator with new password
            // Update vault.cryptomator signature
            throw new NotImplementedException();
        }

        #endregion

        #region Private Helper Methods

        private void CreateVaultFiles()
        {
            if (VaultExists())
            {
                throw new InvalidOperationException($"Vault already exists at {_vaultFolder}");
            }

            Directory.CreateDirectory(_vaultFolder);

            // Use VaultHandler to create proper Cryptomator V8 vault files
            VaultHandler.CreateNewCryptomatorV8VaultComplete(_vaultFolder, _passwordBytes);
        }

        private void LoadVault()
        {
            ValidateVaultExists();

            // Load the vault using our existing Vault class
            var masterkeyPath = Path.Combine(_vaultFolder, MASTERKEY_FILE);
            var masterkeyBytes = File.ReadAllBytes(masterkeyPath);
            _vault = VaultHandler.LoadCryptomatorV8Vault(masterkeyBytes, _passwordBytes);
        }

        private void ValidateVaultExists()
        {
            if (!VaultExists())
            {
                throw new InvalidOperationException($"Vault does not exist at {_vaultFolder}");
            }
        }

        private bool VaultExists()
        {
            var masterkeyPath = Path.Combine(_vaultFolder, MASTERKEY_FILE);
            var vaultConfigPath = Path.Combine(_vaultFolder, VAULT_CONFIG_FILE);
            return File.Exists(masterkeyPath) && File.Exists(vaultConfigPath);
        }

        private void EnsureVaultLoaded()
        {
            if (_vault == null)
            {
                throw new InvalidOperationException("Vault is not loaded. Call Load() or Create() first.");
            }
        }

        private (UvfLib.Core.Api.DirectoryMetadata directoryMetadata, string physicalPath) ResolvePathToDirectory(string path)
        {
            EnsureVaultLoaded();

            // For root path, use root directory metadata
            if (path == ROOT_PATH)
            {
                var rootMetadata = _vault!.GetRootDirectoryMetadata();
                var rootPhysicalPath = Path.Combine(_vaultFolder, _vault.GetRootDirectoryPath());
                return (rootMetadata, rootPhysicalPath);
            }

            // For subdirectories, we need to traverse the path
            // This is complex and would require implementing path traversal
            // For now, throw NotImplementedException for subdirectories
            throw new NotImplementedException("Subdirectory access not yet implemented. Only root directory (\"/\") is supported currently.");
        }

        private static string CombinePath(string basePath, string name)
        {
            if (basePath == ROOT_PATH)
                return ROOT_PATH + name;
            
            return basePath + "/" + name;
        }

        private static long CalculateDecryptedFileSize(long encryptedSize)
        {
            // Use the existing method from Vault class to calculate original size
            return VaultHandler.CalculateExpectedDecryptedSize(encryptedSize);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return ROOT_PATH;

            // Convert backslashes to forward slashes
            path = path.Replace('\\', '/');

            // Ensure it starts with /
            if (!path.StartsWith('/'))
                path = "/" + path;

            // Remove trailing slash (except for root)
            if (path.Length > 1 && path.EndsWith('/'))
                path = path.TrimEnd('/');

            return path;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _vault?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Represents a file or directory entry in the vault.
    /// </summary>
    public class VaultEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }

    /// <summary>
    /// Detailed information about a vault entry.
    /// </summary>
    public class VaultEntryInfo : VaultEntry
    {
        public DateTime Created { get; set; }
        public DateTime LastAccessed { get; set; }
        public string MimeType { get; set; } = "";
    }

    /// <summary>
    /// Statistics about the vault.
    /// </summary>
    public class VaultStatistics
    {
        public int TotalFiles { get; set; }
        public int TotalDirectories { get; set; }
        public long TotalSize { get; set; }
        public long CompressedSize { get; set; }
        public DateTime LastModified { get; set; }
    }

    #endregion
} 