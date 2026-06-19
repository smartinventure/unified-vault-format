// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.



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
        public static byte[] CreateNewVaultKeyFileContentInternal(byte[] passwordBytes, byte[]? pepper)
        {
            if (passwordBytes == null) throw new ArgumentNullException(nameof(passwordBytes));
            
            // For Cryptomator V8, we need to create a traditional JSON masterkey file, not JWE
            // Use MasterkeyFileAccess to create the proper format
            try
            {
                // TODO: Update MasterkeyFileAccess.CreateFromPassphrase to use byte[] passwords
                string passwordString = System.Text.Encoding.UTF8.GetString(passwordBytes);
                MasterkeyFile masterkeyFile;
                try
                {
                    masterkeyFile = MasterkeyFileAccess.CreateFromPassphrase(passwordString);
                }
                finally
                {
                    // Clear temporary string from memory (best effort)
                    passwordString = null;
                }
                
                // Create a Cryptomator-compatible object with ONLY the fields Cryptomator expects
                // This excludes all UVF-specific fields that cause Cryptomator to reject the file
                var cryptomatorMasterkey = new CryptomatorMasterkeyFile
                {
                    Version = masterkeyFile.Version,
                    ScryptSalt = Convert.ToBase64String(masterkeyFile.ScryptSalt!),
                    ScryptCostParam = masterkeyFile.ScryptCostParam,
                    ScryptBlockSize = masterkeyFile.ScryptBlockSize,
                    PrimaryMasterKey = Convert.ToBase64String(masterkeyFile.EncMasterKey!),
                    HmacMasterKey = Convert.ToBase64String(masterkeyFile.MacMasterKey!),
                    VersionMac = Convert.ToBase64String(masterkeyFile.VersionMac!)
                };

                // Serialize with indented formatting to match Cryptomator's format
                return JsonSerializer.SerializeToUtf8Bytes(cryptomatorMasterkey, UvfLib.Core.Common.UvfJsonContext.Default.CryptomatorMasterkeyFile);
            }
            catch (Exception ex)
            {
                throw new CryptoException("Failed to create Cryptomator V8 vault file content", ex);
            }
        }

        /// <summary>
        /// Changes the password for an existing vault's master key file content.
        /// </summary>
        public static byte[] ChangeVaultPasswordInternal(byte[] encryptedKeyFileContent, byte[] oldPasswordBytes, byte[] newPasswordBytes, byte[]? pepper)
        {
            if (encryptedKeyFileContent == null || encryptedKeyFileContent.Length == 0) throw new ArgumentNullException(nameof(encryptedKeyFileContent));
            if (oldPasswordBytes == null) throw new ArgumentNullException(nameof(oldPasswordBytes));
            if (newPasswordBytes == null) throw new ArgumentNullException(nameof(newPasswordBytes));
            // Pepper usage remains for future consideration.

            string jweStringOld = Encoding.UTF8.GetString(encryptedKeyFileContent);

            // 1. Load the existing payload using the old password
            // LoadVaultPayload returns the UvfMasterkeyPayload directly.
            UvfMasterkeyPayload existingPayload = MultiUserJweVaultManager.LoadSingleUserVault(jweStringOld, oldPasswordBytes);

            // 2. Create a new JWE vault with the existing payload and the new password
            // This re-encrypts the same master key material with a new KEK derived from the new password.
            string jweStringNew = MultiUserJweVaultManager.CreateSingleUserVault(existingPayload, newPasswordBytes);

            return Encoding.UTF8.GetBytes(jweStringNew);
        }
    }
}