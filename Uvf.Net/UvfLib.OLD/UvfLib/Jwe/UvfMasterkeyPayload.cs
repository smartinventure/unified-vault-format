using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UvfLib.Jwe
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