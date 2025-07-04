using System.Collections.Generic;
using System.Text.Json.Serialization;
using UvfLib.Core.Api;
using UvfLib.Core.Jwe;

namespace UvfLib.Core.Common
{
    /// <summary>
    /// JSON serialization context for AOT-compatible JSON operations in UVF.
    /// This source generator eliminates reflection-based serialization warnings (IL3050, IL2026).
    /// </summary>
    [JsonSerializable(typeof(UvfMasterkeyPayload))]
    [JsonSerializable(typeof(PayloadKey))]
    [JsonSerializable(typeof(PayloadKdf))]
    [JsonSerializable(typeof(PayloadSeed))]
    [JsonSerializable(typeof(UvfLibNetConfig))]
    [JsonSerializable(typeof(KeyDerivationParameters))]
    [JsonSerializable(typeof(MasterkeyFile))]
    [JsonSerializable(typeof(CryptomatorVaultConfig))] // For Cryptomator vault.cryptomator JWT payload
    [JsonSerializable(typeof(CryptomatorMasterkeyFile))] // For Cryptomator masterkey.cryptomator file
    [JsonSerializable(typeof(List<PayloadKey>))]
    [JsonSerializable(typeof(List<PayloadSeed>))]
    [JsonSerializable(typeof(Dictionary<string, object>))] // For JWE headers
    [JsonSerializable(typeof(Dictionary<string, string>))] // For properties
    [JsonSerializable(typeof(object))] // For anonymous objects in JWE
    [JsonSerializable(typeof(string[]))] // For arrays in JWE headers
    [JsonSerializable(typeof(JweJson))] // JWE JSON structure
    [JsonSerializable(typeof(JweRecipient))] // JWE recipient
    [JsonSerializable(typeof(JweRecipientHeader))] // JWE recipient header (PBKDF2)
    [JsonSerializable(typeof(JweScryptRecipient))] // JWE recipient (Scrypt)
    [JsonSerializable(typeof(JweScryptRecipientHeader))] // JWE recipient header (Scrypt)
    [JsonSerializable(typeof(JweProtectedHeader))] // JWE protected header
    [JsonSerializable(typeof(List<JweRecipient>))] // List of recipients
    [JsonSerializable(typeof(List<JweScryptRecipient>))] // List of Scrypt recipients
[JsonSerializable(typeof(System.Text.Json.JsonElement))] // For parsing existing JSON in AOT
[JsonSerializable(typeof(List<object>))] // For JweJson.Recipients which can contain mixed types
    public partial class UvfJsonContext : JsonSerializerContext
    {
    }
} 