using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UvfLib.Core.Jwe
{
    /// <summary>
    /// Represents a JWE JSON structure for multi-user vault encryption.
    /// </summary>
    public class JweJson
    {
        [JsonPropertyName("protected")]
        public string Protected { get; set; } = string.Empty;

        [JsonPropertyName("recipients")]
        public List<object> Recipients { get; set; } = new();

        [JsonPropertyName("iv")]
        public string Iv { get; set; } = string.Empty;

        [JsonPropertyName("ciphertext")]
        public string Ciphertext { get; set; } = string.Empty;

        [JsonPropertyName("tag")]
        public string Tag { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a JWE recipient for multi-user vault encryption.
    /// </summary>
    public class JweRecipient
    {
        [JsonPropertyName("header")]
        public JweRecipientHeader Header { get; set; } = new();

        [JsonPropertyName("encrypted_key")]
        public string EncryptedKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a JWE recipient header for PBKDF2-based encryption.
    /// </summary>
    public class JweRecipientHeader
    {
        [JsonPropertyName("alg")]
        public string Algorithm { get; set; } = string.Empty;

        [JsonPropertyName("kid")]
        public string KeyId { get; set; } = string.Empty;

        [JsonPropertyName("p2s")]
        public string Salt { get; set; } = string.Empty;

        [JsonPropertyName("p2c")]
        public int Iterations { get; set; }
    }

    /// <summary>
    /// Represents a JWE recipient header for Scrypt-based encryption.
    /// </summary>
    public class JweScryptRecipientHeader
    {
        [JsonPropertyName("alg")]
        public string Algorithm { get; set; } = string.Empty;

        [JsonPropertyName("kid")]
        public string KeyId { get; set; } = string.Empty;

        [JsonPropertyName("uvf_kdf_scrypt_n")]
        public int ScryptN { get; set; }

        [JsonPropertyName("uvf_kdf_scrypt_r")]
        public int ScryptR { get; set; }

        [JsonPropertyName("uvf_kdf_scrypt_p")]
        public int ScryptP { get; set; }

        [JsonPropertyName("uvf_kdf_scrypt_salt")]
        public string ScryptSalt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a JWE recipient for Scrypt-based encryption.
    /// </summary>
    public class JweScryptRecipient
    {
        [JsonPropertyName("header")]
        public JweScryptRecipientHeader Header { get; set; } = new();

        [JsonPropertyName("encrypted_key")]
        public string EncryptedKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a JWE protected header.
    /// </summary>
    public class JweProtectedHeader
    {
        [JsonPropertyName("enc")]
        public string Encryption { get; set; } = string.Empty;

        [JsonPropertyName("cty")]
        public string ContentType { get; set; } = string.Empty;

        [JsonPropertyName("crit")]
        public string[] Critical { get; set; } = System.Array.Empty<string>();

        [JsonPropertyName("uvf.spec.version")]
        public int UvfSpecVersion { get; set; }

        [JsonPropertyName("uvf.kdf.method")]
        public string? UvfKdfMethod { get; set; }
    }
} 