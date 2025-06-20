using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UvfLib.Core.Jwe
{
    public class UvfMasterkeyPayload
    {
        [JsonPropertyName("uvf.spec.version")]
        public int UvfSpecVersion { get; set; } = 1;

        [JsonPropertyName("keys")]
        public List<PayloadKey> Keys { get; set; } = new List<PayloadKey>();

        [JsonPropertyName("kdf")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PayloadKdf? Kdf { get; set; }

        [JsonPropertyName("seeds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<PayloadSeed>? Seeds { get; set; }

        [JsonPropertyName("rootDirId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RootDirId { get; set; } // Base64urlOctets

        /// <summary>
        /// Custom extension field for UvfLib.Net-specific vault configuration.
        /// This field is optional and follows UVF extension naming conventions.
        /// </summary>
        [JsonPropertyName("uvf.ext.uvflib-net.config")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public UvfLibNetConfig? Config { get; set; }
    }

    /// <summary>
    /// Configuration object for UvfLib.Net-specific vault settings.
    /// </summary>
    public class UvfLibNetConfig
    {
        /// <summary>
        /// Whether filename encryption is enabled in this vault.
        /// If null/missing, defaults to true (encrypted filenames) for compatibility.
        /// </summary>
        [JsonPropertyName("encryptFilenames")]
        public bool? EncryptFilenames { get; set; }

        /// <summary>
        /// Version of UvfLib.Net that created this vault.
        /// </summary>
        [JsonPropertyName("createdByVersion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CreatedByVersion { get; set; }
    }

    public class PayloadKey
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty; // Base64urlUInt

        [JsonPropertyName("purpose")]
        public string Purpose { get; set; } = string.Empty;

        [JsonPropertyName("alg")]
        public string Alg { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty; // Base64urlOctets
    }

    public class PayloadKdf
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("salt")]
        public string Salt { get; set; } = string.Empty; // Base64urlOctets
    }

    public class PayloadSeed
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty; // Base64urlUInt

        [JsonPropertyName("created")]
        public string Created { get; set; } = string.Empty; // RFC3339 timestamp

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty; // Base64urlOctets
    }
} 