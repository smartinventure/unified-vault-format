using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UvfLib.Common
{
    /// <summary>
    /// Represents a masterkey file with its metadata.
    /// </summary>
    public class MasterkeyFile
    {
        private const int CURRENT_VERSION = 3;
        
        /// <summary>
        /// The optional UTF-8 encoded JSON representation of the keyfile.
        /// </summary>
        private byte[]? _rawJsonRepresentation;

        /// <summary>
        /// Gets or sets the version of this masterkey file.
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = CURRENT_VERSION;
        
        /// <summary>
        /// Gets or sets the scrypt cost parameter.
        /// </summary>
        [JsonPropertyName("scryptCostParam")]
        public int ScryptCostParam { get; set; }
        
        /// <summary>
        /// Gets or sets the scrypt block size.
        /// </summary>
        [JsonPropertyName("scryptBlockSize")]
        public int ScryptBlockSize { get; set; }
        
        /// <summary>
        /// Gets or sets the scrypt parallelism parameter.
        /// </summary>
        [JsonPropertyName("scryptParallelism")]
        public int ScryptParallelism { get; set; }
        
        /// <summary>
        /// Gets or sets the scrypt salt.
        /// </summary>
        [JsonPropertyName("scryptSalt")]
        public byte[]? ScryptSalt { get; set; }
        
        /// <summary>
        /// Gets or sets the encrypted master key.
        /// </summary>
        [JsonPropertyName("primaryMasterKey")]
        public byte[]? EncMasterKey { get; set; }
        
        /// <summary>
        /// Gets or sets the MAC master key.
        /// </summary>
        [JsonPropertyName("hmacMasterKey")]
        public byte[]? MacMasterKey { get; set; }
        
        /// <summary>
        /// Gets or sets the version MAC.
        /// </summary>
        [JsonPropertyName("versionMac")]
        public byte[]? VersionMac { get; set; }
        
        /// <summary>
        /// Gets or sets the primary masterkey.
        /// </summary>
        [JsonPropertyName("primaryMasterKeyPlain")]
        public string? PrimaryMasterkey { get; set; }
        
        /// <summary>
        /// Gets or sets the primary masterkey's nonce (IV).
        /// </summary>
        [JsonPropertyName("primaryMasterKeyNonce")]
        public string? PrimaryMasterkeyNonce { get; set; }
        
        /// <summary>
        /// Gets or sets the MAC of the primary masterkey.
        /// </summary>
        [JsonPropertyName("primaryMasterKeyMac")]
        public string? PrimaryMasterkeyMac { get; set; }
        
        /// <summary>
        /// Gets or sets the version of the vault.
        /// </summary>
        [JsonPropertyName("vaultVersion")]
        public int VaultVersion { get; set; }
        
        /// <summary>
        /// Gets or sets the encryption scheme used for content encryption.
        /// </summary>
        [JsonPropertyName("contentEncryptionScheme")]
        public string? ContentEncryptionScheme { get; set; }
        
        /// <summary>
        /// Gets or sets the encryption scheme used for filename encryption.
        /// </summary>
        [JsonPropertyName("filenameEncryptionScheme")]
        public string? FilenameEncryptionScheme { get; set; }
        
        /// <summary>
        /// Gets or sets the key ID for UVF masterkeys.
        /// </summary>
        [JsonPropertyName("keyID")]
        public string? KeyId { get; set; }
        
        /// <summary>
        /// Gets or sets the salt for UVF masterkeys.
        /// </summary>
        [JsonPropertyName("salt")]
        public string? Salt { get; set; }
        
        /// <summary>
        /// Gets or sets the iterations for UVF masterkeys.
        /// </summary>
        [JsonPropertyName("iterations")]
        public int Iterations { get; set; }
        
        /// <summary>
        /// Gets or sets the wrapping algorithm for UVF masterkeys.
        /// </summary>
        [JsonPropertyName("wrappingAlgorithm")]
        public string? WrappingAlgorithm { get; set; }
        
        /// <summary>
        /// Gets or sets the KDF algorithm for UVF masterkeys.
        /// </summary>
        [JsonPropertyName("kdfAlgorithm")]
        public string? KdfAlgorithm { get; set; }
        
        /// <summary>
        /// Gets or sets the wrapped key for UVF masterkeys.
        /// </summary>
        [JsonPropertyName("wrappedKey")]
        public string? WrappedKey { get; set; }
        
        /// <summary>
        /// Gets or sets the encryption scheme for UVF. 
        /// </summary>
        [JsonPropertyName("encryptionAlgorithm")]
        public string? EncryptionAlgorithm { get; set; }
        
        /// <summary>
        /// Creates a masterkey file from its JSON representation.
        /// </summary>
        /// <param name="json">The JSON representation</param>
        /// <returns>The parsed masterkey file</returns>
        public static MasterkeyFile FromJson(byte[] json)
        {
            if (json == null || json.Length == 0)
            {
                throw new ArgumentException("JSON cannot be null or empty", nameof(json));
            }
            
            try
            {
                var masterkeyFile = JsonSerializer.Deserialize<MasterkeyFile>(json);
                if (masterkeyFile == null)
                {
                    throw new JsonException("Failed to deserialize masterkey file");
                }
                
                masterkeyFile._rawJsonRepresentation = new byte[json.Length];
                Buffer.BlockCopy(json, 0, masterkeyFile._rawJsonRepresentation, 0, json.Length);
                
                return masterkeyFile;
            }
            catch (JsonException ex)
            {
                throw new JsonException("Failed to parse masterkey file", ex);
            }
        }

        /// <summary>
        /// Creates a masterkey file from its JSON representation.
        /// </summary>
        /// <param name="json">The JSON representation</param>
        /// <returns>The parsed masterkey file</returns>
        public static MasterkeyFile FromJson(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }
            
            return FromJson(Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        /// Serializes this masterkey file to JSON.
        /// </summary>
        /// <returns>The JSON representation of this masterkey file</returns>
        public byte[] ToJson()
        {
            try
            {
                return JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch (JsonException ex)
            {
                throw new JsonException("Failed to serialize masterkey file", ex);
            }
        }

        /// <summary>
        /// Converts this masterkey file to its JSON representation as a string.
        /// </summary>
        /// <returns>The JSON representation</returns>
        public string ToJsonString()
        {
            return Encoding.UTF8.GetString(ToJson());
        }
    }
} 