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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UvfLib.Core.Common;
using UvfLib.Core.Jwe;


// using System.Text.Json; // No longer directly needed here for payload construction
using UvfLib.Core.Api; // For UVFMasterkey for type hinting if needed
using UvfLib.Core.V3; // For UVFMasterkeyImpl

namespace UvfLib.Vault.VaultHelpers
{
    /// <summary>
    /// Provides helper methods for vault key management (creation, password changes).
    /// </summary>
    internal static class VaultKeyHelper
    {
        // private static readonly RandomNumberGenerator CsPrng = RandomNumberGenerator.Create(); // Not used if key gen is in UVFMasterkeyImpl

        /// <summary>
        /// Creates the encrypted master key file content for a new vault.
        /// </summary>
        public static byte[] CreateNewVaultKeyFileContentInternal(string password, byte[]? pepper)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));
            
            // For Cryptomator V8, we need to create a traditional JSON masterkey file, not JWE
            // Use MasterkeyFileAccess to create the proper format
            try
            {
                var masterkeyFile = MasterkeyFileAccess.CreateFromPassphrase(password);
                
                // Create a Cryptomator-compatible object with ONLY the fields Cryptomator expects
                // This excludes all UVF-specific fields that cause Cryptomator to reject the file
                var cryptomatorMasterkey = new 
                {
                    version = masterkeyFile.Version,
                    scryptSalt = Convert.ToBase64String(masterkeyFile.ScryptSalt!),
                    scryptCostParam = masterkeyFile.ScryptCostParam,
                    scryptBlockSize = masterkeyFile.ScryptBlockSize,
                    primaryMasterKey = Convert.ToBase64String(masterkeyFile.EncMasterKey!),
                    hmacMasterKey = Convert.ToBase64String(masterkeyFile.MacMasterKey!),
                    versionMac = Convert.ToBase64String(masterkeyFile.VersionMac!)
                };

                // Serialize with indented formatting to match Cryptomator's format
                var jsonOptions = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                
                return JsonSerializer.SerializeToUtf8Bytes(cryptomatorMasterkey, jsonOptions);
            }
            catch (Exception ex)
            {
                throw new CryptoException("Failed to create Cryptomator V8 vault file content", ex);
            }
        }

        /// <summary>
        /// Changes the password for an existing vault's master key file content.
        /// </summary>
        public static byte[] ChangeVaultPasswordInternal(byte[] encryptedKeyFileContent, string oldPassword, string newPassword, byte[]? pepper)
        {
            if (encryptedKeyFileContent == null || encryptedKeyFileContent.Length == 0) throw new ArgumentNullException(nameof(encryptedKeyFileContent));
            if (string.IsNullOrEmpty(oldPassword)) throw new ArgumentNullException(nameof(oldPassword));
            if (string.IsNullOrEmpty(newPassword)) throw new ArgumentNullException(nameof(newPassword));
            // Pepper usage remains for future consideration.

            string jweStringOld = Encoding.UTF8.GetString(encryptedKeyFileContent);

            // 1. Load the existing payload using the old password
            // LoadVaultPayload returns the UvfMasterkeyPayload directly.
            UvfMasterkeyPayload existingPayload = JweVaultManager.LoadVaultPayload(jweStringOld, oldPassword);

            // 2. Create a new JWE vault with the existing payload and the new password
            // This re-encrypts the same master key material with a new KEK derived from the new password.
            string jweStringNew = JweVaultManager.CreateVault(existingPayload, newPassword);

            return Encoding.UTF8.GetBytes(jweStringNew);
        }
    }
}