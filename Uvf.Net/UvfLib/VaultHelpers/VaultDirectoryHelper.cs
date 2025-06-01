/*******************************************************************************
 * Copyright (c) 2016 Sebastian Stenzel and others.
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the accompanying LICENSE.txt.
 *
 * Contributors:
 *     Sebastian Stenzel - initial API and implementation
 *******************************************************************************/

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting


using UvfLib.Api;
using System;
using System.IO;

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

                    // Check if dir.uvf exists in the encrypted directory
                    string dirUvfPath = Path.Combine(encryptedSegmentPath, "dir.uvf");
                    if (!File.Exists(dirUvfPath))
                    {
                        return false;
                    }

                    // Load and decrypt the metadata for the next level
                    try
                    {
                        byte[] encryptedMetadata = File.ReadAllBytes(dirUvfPath);
                        currentDirMetadata = ((Api.DirectoryContentCryptor)cryptor.DirectoryContentCryptor()).DecryptDirectoryMetadata(encryptedMetadata);
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

                    // Check if dir.uvf exists in the encrypted directory
                    string dirUvfPath = Path.Combine(encryptedSegmentPath, "dir.uvf");
                    if (!File.Exists(dirUvfPath))
                    {
                        return null;
                    }

                    // Load and decrypt the metadata for the next level
                    try
                    {
                        byte[] encryptedMetadata = File.ReadAllBytes(dirUvfPath);
                        currentDirMetadata = ((Api.DirectoryContentCryptor)cryptor.DirectoryContentCryptor()).DecryptDirectoryMetadata(encryptedMetadata);
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