using System.Text.Json.Serialization;

namespace UvfLib.Core.Common
{
    /// <summary>
    /// Represents a Cryptomator masterkey.cryptomator file structure.
    /// Used for AOT-compatible JSON serialization with exact field names expected by Cryptomator.
    /// </summary>
    public class CryptomatorMasterkeyFile
    {
        /// <summary>
        /// Masterkey file format version
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; }

        /// <summary>
        /// Base64-encoded scrypt salt
        /// </summary>
        [JsonPropertyName("scryptSalt")]
        public string ScryptSalt { get; set; } = string.Empty;

        /// <summary>
        /// Scrypt cost parameter (N)
        /// </summary>
        [JsonPropertyName("scryptCostParam")]
        public int ScryptCostParam { get; set; }

        /// <summary>
        /// Scrypt block size parameter (r)
        /// </summary>
        [JsonPropertyName("scryptBlockSize")]
        public int ScryptBlockSize { get; set; }

        /// <summary>
        /// Base64-encoded wrapped primary master key
        /// </summary>
        [JsonPropertyName("primaryMasterKey")]
        public string PrimaryMasterKey { get; set; } = string.Empty;

        /// <summary>
        /// Base64-encoded wrapped HMAC master key
        /// </summary>
        [JsonPropertyName("hmacMasterKey")]
        public string HmacMasterKey { get; set; } = string.Empty;

        /// <summary>
        /// Base64-encoded version MAC
        /// </summary>
        [JsonPropertyName("versionMac")]
        public string VersionMac { get; set; } = string.Empty;
    }
} 