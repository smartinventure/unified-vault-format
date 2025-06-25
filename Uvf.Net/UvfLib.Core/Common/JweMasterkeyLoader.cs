using System;
using System.IO;
using System.Text;
using System.Text.Json;
using UvfLib.Core.Api;
using UvfLib.Core.Common;
using UvfLib.Core.Jwe;
using UvfLib.Core.V3;

namespace UvfLib.Core.Common // Or UvfLib.Jwe, depending on preference
{
    /// <summary>
    /// Masterkey loader for JWE-based UVF vaults (vault.uvf).
    /// Note: The standard MasterkeyLoader.LoadKey(Uri) interface does not provide a password.
    /// This implementation will throw if a password-protected key is identified 
    /// and no password has been made available through other means (e.g., a static context or property).
    /// For typical use, a method like Vault.LoadMasterkey(string vaultPath, string password) is recommended.
    /// </summary>
    public class JweMasterkeyLoader : MasterkeyLoader
    {
        // A mechanism to provide the password if needed, e.g., via a static property or constructor injection.
        // For this example, we'll assume it needs to be available when LoadKey is called.
        // This is a placeholder for application-specific password provision.
        public Func<Uri, string?>? PasswordProvider { get; set; }

        public JweMasterkeyLoader(Func<Uri, string?>? passwordProvider = null)
        {
            PasswordProvider = passwordProvider;
        }

        public Api.Masterkey LoadKey(Uri keyId)
        {
            if (keyId == null)
                throw new ArgumentNullException(nameof(keyId));
            if (!keyId.IsFile)
                throw new ArgumentException("Key ID URI must be a local file path.", nameof(keyId));

            string vaultPath = keyId.LocalPath;
            if (!File.Exists(vaultPath))
                throw new FileNotFoundException("Vault file not found.", vaultPath);

            string? password = PasswordProvider?.Invoke(keyId);
            if (string.IsNullOrEmpty(password))
            {
                // Check if the JWE is for public key crypto (alg != PBES2*), 
                // then a password might not be needed. For now, PBES2 is assumed.
                throw new InvalidOperationException(
                    "Password is required to load the JWE vault key, but no password was provided through the PasswordProvider."
                    + " Consider using a dedicated loading method that accepts a password.");
            }

            try
            {
                string jweString = File.ReadAllText(vaultPath, Encoding.UTF8);
                
                // Try to load as single-user vault (admin user)
                char[] passwordChars = password.ToCharArray();
                UvfMasterkeyPayload payload;
                try
                {
                    payload = MultiUserJweVaultManager.LoadMultiUserVault(jweString, passwordChars, "admin");
                }
                finally
                {
                    Array.Clear(passwordChars, 0, passwordChars.Length);
                }
                
                // Serialize payload back to JSON string to pass to UVFMasterkey.FromDecryptedPayload
                // (as UVFMasterkeyImpl expects a JSON string)
                string jsonPayloadString = JsonSerializer.Serialize(payload, UvfJsonContext.Default.UvfMasterkeyPayload);
                
                // The static method on the interface delegates to the concrete implementation (UVFMasterkeyImpl)
                return Api.UVFMasterkey.FromDecryptedPayload(jsonPayloadString);
            }
            catch (FileNotFoundException)
            {
                throw; // Already specific
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is ArgumentException)
            {
                // JsonException for payload format issues, InvalidOperationException for missing password
                throw new MasterkeyLoadingFailedException($"Failed to load or decrypt JWE master key from {vaultPath}. Possible incorrect password or corrupted file.", ex);
            }
            catch (Exception ex)
            {
                throw new MasterkeyLoadingFailedException($"An unexpected error occurred while loading master key from {vaultPath}.", ex);
            }
        }
    }
} 
