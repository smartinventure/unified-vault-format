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
using System.IO;
using UvfLib.Core.Api;

namespace UvfLib.VaultHelpers
{
    /// <summary>
    /// Provides helper methods for directory and filename operations within a vault.
    /// </summary>
    internal static class VaultDirectoryHelper
    {
        // --- Directory Metadata Handling --- 

        public static byte[] EncryptDirectoryMetadataInternal(Cryptor cryptor, DirectoryMetadata metadata)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            // Assuming the implementation handles potential type casting if needed
            return cryptor.DirectoryContentCryptor().EncryptDirectoryMetadata(metadata);
        }

        public static DirectoryMetadata CreateNewDirectoryMetadataInternal(Cryptor cryptor)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            return cryptor.DirectoryContentCryptor().NewDirectoryMetadata();
        }

        // --- Filename Handling (Contextual) ---

        public static string EncryptFilenameInternal(Cryptor cryptor, DirectoryMetadata directoryMetadata, string plaintextFilename)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            if (directoryMetadata == null) throw new ArgumentNullException(nameof(directoryMetadata));
            if (plaintextFilename == null) throw new ArgumentNullException(nameof(plaintextFilename));

            var nameEncryptor = cryptor.DirectoryContentCryptor().FileNameEncryptor(directoryMetadata);
            return nameEncryptor.Encrypt(plaintextFilename);
        }

        public static string DecryptFilenameInternal(Cryptor cryptor, DirectoryMetadata directoryMetadata, string encryptedFilename)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            if (directoryMetadata == null) throw new ArgumentNullException(nameof(directoryMetadata));
            if (encryptedFilename == null) throw new ArgumentNullException(nameof(encryptedFilename));

            var nameDecryptor = cryptor.DirectoryContentCryptor().FileNameDecryptor(directoryMetadata);
            return nameDecryptor.Decrypt(encryptedFilename);
        }

        // --- Path Generation ---

        public static string GetDirectoryPathInternal(Cryptor cryptor, DirectoryMetadata directoryMetadata)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            if (directoryMetadata == null) throw new ArgumentNullException(nameof(directoryMetadata));

            return cryptor.DirectoryContentCryptor().DirPath(directoryMetadata);
        }

        // --- Vault Format Specific Helpers ---

        /// <summary>
        /// Gets the directory metadata filename based on the vault format and directory type.
        /// </summary>
        /// <param name="cryptor">The cryptor instance</param>
        /// <param name="directoryMetadata">The directory metadata (optional, used to determine if it's root directory)</param>
        /// <returns>The directory metadata filename</returns>
        public static string GetDirectoryMetadataFilename(Cryptor cryptor, DirectoryMetadata directoryMetadata = null)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            
            // Check if this is a Cryptomator v8 cryptor
            if (cryptor.DirectoryContentCryptor().GetType().FullName?.Contains("CryptomatorV8") == true)
            {
                // For Cryptomator v8, differentiate between root and non-root directories
                if (directoryMetadata != null)
                {
                    var rootMetadata = cryptor.DirectoryContentCryptor().RootDirectoryMetadata();
                    if (directoryMetadata.Equals(rootMetadata))
                    {
                        return "dirid.c9r"; // Root directory uses dirid.c9r
                    }
                }
                return "dir.c9r"; // Subdirectories use dir.c9r
            }
            
            // Default to UVF format
            return "dir.uvf";
        }

        /// <summary>
        /// Gets the directory metadata filename based on the vault format.
        /// For backwards compatibility, this assumes non-root directory for Cryptomator v8.
        /// </summary>
        /// <param name="cryptor">The cryptor instance</param>
        /// <returns>The directory metadata filename</returns>
        public static string GetDirectoryMetadataFilename(Cryptor cryptor)
        {
            return GetDirectoryMetadataFilename(cryptor, null);
        }

        /// <summary>
        /// Checks if the cryptor is for Cryptomator v8 format.
        /// </summary>
        /// <param name="cryptor">The cryptor instance</param>
        /// <returns>True if this is a Cryptomator v8 cryptor</returns>
        public static bool IsCryptomatorV8(Cryptor cryptor)
        {
            if (cryptor?.DirectoryContentCryptor() == null) return false;
            return cryptor.DirectoryContentCryptor().GetType().FullName?.Contains("CryptomatorV8") == true;
        }

        /// <summary>
        /// Checks if directory metadata should be saved to a file for the given directory.
        /// For Cryptomator v8 root directory, metadata is not saved to a file.
        /// </summary>
        /// <param name="cryptor">The cryptor instance</param>
        /// <param name="directoryMetadata">The directory metadata to check</param>
        /// <returns>True if metadata should be saved to a file, false otherwise</returns>
        public static bool ShouldSaveDirectoryMetadata(Cryptor cryptor, DirectoryMetadata directoryMetadata)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            if (directoryMetadata == null) throw new ArgumentNullException(nameof(directoryMetadata));

            // For Cryptomator v8, check if this is the root directory
            if (IsCryptomatorV8(cryptor))
            {
                var rootMetadata = cryptor.DirectoryContentCryptor().RootDirectoryMetadata();
                if (directoryMetadata.Equals(rootMetadata))
                {
                    return false; // Root directory metadata is not saved to file in v8
                }
            }

            // For all other cases (UVF format or v8 non-root directories), save metadata
            return true;
        }

        /// <summary>
        /// Checks if a directory metadata file exists for the given directory.
        /// For Cryptomator v8 root directory, this always returns true since root metadata is generated.
        /// </summary>
        /// <param name="cryptor">The cryptor instance</param>
        /// <param name="directoryPath">The physical directory path</param>
        /// <param name="directoryMetadata">The directory metadata (used to check if it's root)</param>
        /// <returns>True if directory metadata exists or can be generated</returns>
        public static bool DirectoryMetadataExists(Cryptor cryptor, string directoryPath, DirectoryMetadata directoryMetadata)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            if (string.IsNullOrEmpty(directoryPath)) throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));
            if (directoryMetadata == null) throw new ArgumentNullException(nameof(directoryMetadata));

            // For Cryptomator v8, check if this is the root directory
            if (IsCryptomatorV8(cryptor))
            {
                // Root directory in Cryptomator v8 has empty directory ID and no dir.c9r file
                var rootMetadata = cryptor.DirectoryContentCryptor().RootDirectoryMetadata();
                if (directoryMetadata.Equals(rootMetadata))
                {
                    return true; // Root metadata is always available
                }
            }

            // For non-root directories or UVF format, check if the metadata file exists
            string metadataFilename = GetDirectoryMetadataFilename(cryptor, directoryMetadata);
            string metadataFilePath = Path.Combine(directoryPath, metadataFilename);
            return File.Exists(metadataFilePath);
        }

        // --- Vault Traversal ---

        /// <summary>
        /// Checks if a directory exists in the vault by traversing the encrypted directory structure.
        /// </summary>
        /// <param name="cryptor">The cryptor instance</param>
        /// <param name="vaultBasePath">The base path of the vault</param>
        /// <param name="plaintextDirectoryPath">The plaintext directory path to check (e.g., "/myFolder/subFolder")</param>
        /// <returns>True if the directory exists, false otherwise</returns>
        public static bool DirectoryExists(Cryptor cryptor, string vaultBasePath, string plaintextDirectoryPath)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            if (string.IsNullOrEmpty(vaultBasePath)) throw new ArgumentException("Vault base path cannot be null or empty", nameof(vaultBasePath));
            if (string.IsNullOrEmpty(plaintextDirectoryPath)) throw new ArgumentException("Directory path cannot be null or empty", nameof(plaintextDirectoryPath));

            try
            {
                // Normalize the path - remove leading/trailing slashes and split into segments
                string normalizedPath = plaintextDirectoryPath.Trim('/');
                if (string.IsNullOrEmpty(normalizedPath))
                {
                    // Root directory always exists
                    return true;
                }

                string[] pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                
                // Start from root directory
                DirectoryMetadata currentDirMetadata = cryptor.DirectoryContentCryptor().RootDirectoryMetadata();
                string currentPhysicalPath = Path.Combine(vaultBasePath, cryptor.DirectoryContentCryptor().DirPath(currentDirMetadata));

                // Traverse each path segment
                foreach (string segment in pathSegments)
                {
                    // Encrypt the segment name using current directory's metadata
                    string encryptedSegmentName = EncryptFilenameInternal(cryptor, currentDirMetadata, segment);
                    string encryptedSegmentPath = Path.Combine(currentPhysicalPath, encryptedSegmentName);

                    // Check if the encrypted directory exists
                    if (!Directory.Exists(encryptedSegmentPath))
                    {
                        return false;
                    }

                    // Check if directory metadata exists
                    string metadataFilename = GetDirectoryMetadataFilename(cryptor, currentDirMetadata);
                    string dirMetadataPath = Path.Combine(encryptedSegmentPath, metadataFilename);
                    if (!File.Exists(dirMetadataPath))
                    {
                        return false;
                    }

                    // Load and decrypt the metadata for the next level
                    try
                    {
                        byte[] encryptedMetadata = File.ReadAllBytes(dirMetadataPath);
                        currentDirMetadata = ((DirectoryContentCryptor)cryptor.DirectoryContentCryptor()).DecryptDirectoryMetadata(encryptedMetadata);
                        currentPhysicalPath = encryptedSegmentPath;
                    }
                    catch (Exception)
                    {
                        // If we can't decrypt the metadata, the directory is invalid
                        return false;
                    }
                }

                // If we successfully traversed all segments, the directory exists
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Traverses the vault directory structure and returns the directory metadata and physical path
        /// for the given plaintext directory path.
        /// </summary>
        /// <param name="cryptor">The cryptor instance</param>
        /// <param name="vaultBasePath">The base path of the vault</param>
        /// <param name="plaintextDirectoryPath">The plaintext directory path to traverse to</param>
        /// <returns>A tuple containing the directory metadata and physical path, or null if not found</returns>
        public static (DirectoryMetadata metadata, string physicalPath)? TraverseToDirectory(Cryptor cryptor, string vaultBasePath, string plaintextDirectoryPath)
        {
            if (cryptor?.DirectoryContentCryptor() == null) throw new InvalidOperationException("Directory cryptor not available.");
            if (string.IsNullOrEmpty(vaultBasePath)) throw new ArgumentException("Vault base path cannot be null or empty", nameof(vaultBasePath));
            if (string.IsNullOrEmpty(plaintextDirectoryPath)) throw new ArgumentException("Directory path cannot be null or empty", nameof(plaintextDirectoryPath));

            try
            {
                // Normalize the path - remove leading/trailing slashes and split into segments
                string normalizedPath = plaintextDirectoryPath.Trim('/');
                
                // Start from root directory
                DirectoryMetadata currentDirMetadata = cryptor.DirectoryContentCryptor().RootDirectoryMetadata();
                string currentPhysicalPath = Path.Combine(vaultBasePath, cryptor.DirectoryContentCryptor().DirPath(currentDirMetadata));

                // If requesting root directory
                if (string.IsNullOrEmpty(normalizedPath))
                {
                    return (currentDirMetadata, currentPhysicalPath);
                }

                string[] pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // Traverse each path segment
                foreach (string segment in pathSegments)
                {
                    // Encrypt the segment name using current directory's metadata
                    string encryptedSegmentName = EncryptFilenameInternal(cryptor, currentDirMetadata, segment);
                    string encryptedSegmentPath = Path.Combine(currentPhysicalPath, encryptedSegmentName);

                    // Check if the encrypted directory exists
                    if (!Directory.Exists(encryptedSegmentPath))
                    {
                        return null;
                    }

                    // Check if directory metadata exists
                    string metadataFilename = GetDirectoryMetadataFilename(cryptor, currentDirMetadata);
                    string dirMetadataPath = Path.Combine(encryptedSegmentPath, metadataFilename);
                    if (!File.Exists(dirMetadataPath))
                    {
                        return null;
                    }

                    // Load and decrypt the metadata for the next level
                    try
                    {
                        byte[] encryptedMetadata = File.ReadAllBytes(dirMetadataPath);
                        currentDirMetadata = ((DirectoryContentCryptor)cryptor.DirectoryContentCryptor()).DecryptDirectoryMetadata(encryptedMetadata);
                        currentPhysicalPath = encryptedSegmentPath;
                    }
                    catch (Exception)
                    {
                        // If we can't decrypt the metadata, the directory is invalid
                        return null;
                    }
                }

                // Return the final directory metadata and path
                return (currentDirMetadata, currentPhysicalPath);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}