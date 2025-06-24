using System.Text.Json.Serialization;

namespace UvfLib.Core.Common
{
    /// <summary>
    /// Represents the payload of a Cryptomator vault configuration JWT (vault.cryptomator).
    /// Used for AOT-compatible JSON serialization.
    /// </summary>
    public class CryptomatorVaultConfig
    {
        /// <summary>
        /// JWT ID - unique identifier for this vault
        /// </summary>
        [JsonPropertyName("jti")]
        public string Jti { get; set; } = string.Empty;

        /// <summary>
        /// Vault format version (typically 8 for Cryptomator V8)
        /// </summary>
        [JsonPropertyName("format")]
        public int Format { get; set; }

        /// <summary>
        /// Cipher combination used (e.g., "SIV_GCM")
        /// </summary>
        [JsonPropertyName("cipherCombo")]
        public string CipherCombo { get; set; } = string.Empty;

        /// <summary>
        /// Filename shortening threshold (typically 220)
        /// </summary>
        [JsonPropertyName("shorteningThreshold")]
        public int ShorteningThreshold { get; set; }

        /// <summary>
        /// Creates a new Cryptomator vault configuration with default values.
        /// </summary>
        /// <returns>A new CryptomatorVaultConfig instance</returns>
        public static CryptomatorVaultConfig CreateDefault()
        {
            return new CryptomatorVaultConfig
            {
                Jti = System.Guid.NewGuid().ToString(),
                Format = 8,
                CipherCombo = "SIV_GCM",
                ShorteningThreshold = 220
            };
        }
    }
} 