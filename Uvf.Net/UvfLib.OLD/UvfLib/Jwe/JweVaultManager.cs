using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jose;
using UvfLib.Api;

namespace UvfLib.Jwe
{
    /// <summary>
    /// Manages JWE-formatted vault files (vault.uvf).
    /// </summary>
    public static class JweVaultManager
    {
        private const JweAlgorithm KeyManagementAlgorithm = JweAlgorithm.PBES2_HS512_A256KW;
        // private const JweAlgorithm KeyManagementAlgorithm = JweAlgorithm.PBES2_HS256_A128KW; // Diagnostic change
        private const JweEncryption ContentEncryptionAlgorithm = JweEncryption.A256GCM;
        // private const JweEncryption ContentEncryptionAlgorithm = JweEncryption.A128GCM; // Diagnostic change
        private const int DefaultPbkdf2Iterations = 64000; // Default iteration count
        // private const int Pbkdf2SaltSizeBytes = 16; // Salt size will be handled by JwtSettings or jose-jwt default

        /// <summary>
        /// Creates a JWE-formatted string representing an encrypted vault.uvf file.
        /// </summary>
        /// <param name="payload">The UVF masterkey payload to encrypt.</param>
        /// <param name="password">The password to protect the vault.</param>
        /// <param name="pbkdf2Iterations">Configurable PBKDF2 iteration count. If null, DefaultPbkdf2Iterations is used.</param>
        /// <returns>A JWE compact serialization string.</returns>
        public static string CreateVault(UvfMasterkeyPayload payload, string password, int? pbkdf2Iterations = null)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));

            int iterationsToUse = pbkdf2Iterations ?? DefaultPbkdf2Iterations;
            if (iterationsToUse < 8192) 
            {
                 Console.WriteLine($"[WARN] CreateVault - Requested PBKDF2 iteration count {iterationsToUse} is low. Consider using at least 8192.");
            }

            string payloadJson = JsonSerializer.Serialize(payload);

#if DEBUG
            Console.WriteLine(); 
            Console.WriteLine("---------------- JWE CreateVault DEBUG START ----------------");
            Console.WriteLine($"[DEBUG] CreateVault - Password Length: {password.Length}");
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                Console.WriteLine($"[DEBUG] CreateVault - Password SHA256: {Convert.ToBase64String(hashedBytes)}");
            }
            Console.WriteLine($"[DEBUG] CreateVault - Using PBKDF2 Iterations (via custom JWA): {iterationsToUse}");
            Console.WriteLine($"[DEBUG] CreateVault - KeyManagementAlgorithm: {KeyManagementAlgorithm}");
            Console.WriteLine($"[DEBUG] CreateVault - ContentEncryptionAlgorithm: {ContentEncryptionAlgorithm}");
            Console.WriteLine("---------------- JWE CreateVault DEBUG END   ----------------");
            Console.WriteLine(); 
#endif

            var extraHeaders = new Dictionary<string, object>
            {
                { "uvf.spec.version", payload.UvfSpecVersion },
                { "p2c", iterationsToUse } // Explicitly set p2c in the header
            };
            
            var settings = new JwtSettings();
            
            // Determine PBES2 parameters from KeyManagementAlgorithm
            // The first parameter to Pbse2HmacShaKeyManagementWithAesKeyWrap constructor selects the PRF:
            // 128 -> HMACSHA256
            // 192 -> HMACSHA384
            // 256 -> HMACSHA512
            int prfSelectionArgument = 0; // This will be 128, 192, or 256 to select the PRF.
            int actualAesKwSizeBits = 0;  // This is the key size for the AesKeyWrapManagement (e.g., 256 for A256KW).

            if (KeyManagementAlgorithm == JweAlgorithm.PBES2_HS512_A256KW)
            {
                prfSelectionArgument = 256; // To select HMACSHA512
                actualAesKwSizeBits = 256;  // For A256KW
            }
            else if (KeyManagementAlgorithm == JweAlgorithm.PBES2_HS384_A192KW)
            {
                prfSelectionArgument = 192; // To select HMACSHA384
                actualAesKwSizeBits = 192;  // For A192KW
            }
            else if (KeyManagementAlgorithm == JweAlgorithm.PBES2_HS256_A128KW)
            {
                prfSelectionArgument = 128; // To select HMACSHA256
                actualAesKwSizeBits = 128;  // For A128KW
            }
            else
            {
                throw new NotSupportedException($"The KeyManagementAlgorithm {KeyManagementAlgorithm} is not a supported PBES2 type for custom iteration count setting.");
            }

            var aesKeyWrapManagement = new Jose.AesKeyWrapManagement(actualAesKwSizeBits);
            var customPbse2Impl = new Jose.Pbse2HmacShaKeyManagementWithAesKeyWrap(prfSelectionArgument, aesKeyWrapManagement, iterationsToUse, iterationsToUse);
            settings.RegisterJwa(KeyManagementAlgorithm, customPbse2Impl);

            return JWT.Encode(payloadJson, password, KeyManagementAlgorithm, ContentEncryptionAlgorithm, extraHeaders: extraHeaders, settings: settings);
        }

        /// <summary>
        /// Loads and decrypts a UVF masterkey from a JWE-formatted string.
        /// </summary>
        /// <param name="jweString">The JWE compact serialization string (content of vault.uvf).</param>
        /// <param name="password">The password to decrypt the vault.</param>
        /// <param name="pbkdf2Iterations">The expected PBKDF2 iteration count used during encryption. If null, DefaultPbkdf2Iterations is used for creating JwtSettings.</param>
        /// <returns>A UVFMasterkeyPayload instance.</returns>
        /// <exception cref="ArgumentNullException">If jweString or password is null/empty.</exception>
        /// <exception cref="InvalidOperationException">If decryption fails or payload is invalid.</exception>
        /// <exception cref="JoseException">If JWE processing fails (e.g., wrong password, malformed token).</exception>
        public static UvfMasterkeyPayload LoadVaultPayload(string jweString, string password, int? pbkdf2Iterations = null)
        {
            if (string.IsNullOrEmpty(jweString)) throw new ArgumentNullException(nameof(jweString));
            if (string.IsNullOrEmpty(password)) throw new ArgumentNullException(nameof(password));

            int iterationsExpected = pbkdf2Iterations ?? DefaultPbkdf2Iterations; // This is for the settings instance. The actual p2c is in the header.

#if DEBUG
            Console.WriteLine(); 
            Console.WriteLine("---------------- JWE LoadVaultPayload DEBUG START ----------------");
            Console.WriteLine($"[DEBUG] LoadVaultPayload - Password Length: {password.Length}");
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                Console.WriteLine($"[DEBUG] LoadVaultPayload - Password SHA256: {Convert.ToBase64String(hashedBytes)}");
            }
            
            long actualP2cFromHeader = 0;
            string actualAlgFromHeader = "";
            string actualEncFromHeader = "";
            try
            {
                var parts = jweString.Split('.');
                if (parts.Length >= 1)
                {
                    var decodedHeader = Encoding.UTF8.GetString(Base64Url.Decode(parts[0]));
                    Console.WriteLine($"[DEBUG] LoadVaultPayload - JWE Protected Header (Raw Decoded): {decodedHeader}");
                    using (JsonDocument doc = JsonDocument.Parse(decodedHeader))
                    {
                        if (doc.RootElement.TryGetProperty("p2c", out JsonElement p2cElement) && p2cElement.TryGetInt64(out long p2cVal))
                        {
                            actualP2cFromHeader = p2cVal;
                        }
                        if (doc.RootElement.TryGetProperty("alg", out JsonElement algElement))
                        {
                            actualAlgFromHeader = algElement.GetString() ?? "";
                        }
                        if (doc.RootElement.TryGetProperty("enc", out JsonElement encElement))
                        {
                            actualEncFromHeader = encElement.GetString() ?? "";
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[DEBUG] LoadVaultPayload - Error decoding JWE header for debug: {ex.Message}");
            }
            Console.WriteLine($"[DEBUG] LoadVaultPayload - Actual 'alg' from JWE Header: {actualAlgFromHeader}");
            Console.WriteLine($"[DEBUG] LoadVaultPayload - Actual 'enc' from JWE Header: {actualEncFromHeader}");
            Console.WriteLine($"[DEBUG] LoadVaultPayload - Actual 'p2c' from JWE Header: {actualP2cFromHeader}");
            Console.WriteLine($"[DEBUG] LoadVaultPayload - Expected Iterations (for custom JWA setup): {iterationsExpected}");
            Console.WriteLine($"[DEBUG] LoadVaultPayload - Expected KeyManagementAlgorithm (const): {KeyManagementAlgorithm}");
            Console.WriteLine($"[DEBUG] LoadVaultPayload - Expected ContentEncryptionAlgorithm (const): {ContentEncryptionAlgorithm}");
            Console.WriteLine("---------------- JWE LoadVaultPayload DEBUG END   ----------------");
            Console.WriteLine(); 
#endif

            var settings = new JwtSettings();

            // Determine PBES2 parameters for registering the JWA provider
            // This needs to match the ALGORITHM declared in the JWE header (which should match our KeyManagementAlgorithm const)
            // The iteration count used here for setup doesn't override the p2c from the header for decryption,
            // but it's good practice to initialize the provider consistently.
            // The jose-jwt library internally uses the 'p2c' from the JWE header during PBES2 decryption.
            
            JweAlgorithm effectiveAlgToUse = KeyManagementAlgorithm; // Default to our constant
            // Optionally, if we wanted to be super robust and use the alg from the header if available:
            // if (!string.IsNullOrEmpty(actualAlgFromHeader)) {
            //     try { effectiveAlgToUse = JweAlgorithm.FromString(actualAlgFromHeader); }
            //     catch { /* stick to KeyManagementAlgorithm const */ }
            // }

            // Renaming for clarity based on new understanding of Pbse2HmacShaKeyManagementWithAesKeyWrap constructor
            int prfSelectionArgumentForLoad = 0; 
            int actualAesKwSizeBitsForLoad = 0;

            if (effectiveAlgToUse == JweAlgorithm.PBES2_HS512_A256KW)
            {
                prfSelectionArgumentForLoad = 256; // To select HMACSHA512 for the PRF
                actualAesKwSizeBitsForLoad = 256;  // For A256KW
            }
            else if (effectiveAlgToUse == JweAlgorithm.PBES2_HS384_A192KW)
            {
                prfSelectionArgumentForLoad = 192; // To select HMACSHA384
                actualAesKwSizeBitsForLoad = 192;  // For A192KW
            }
            else if (effectiveAlgToUse == JweAlgorithm.PBES2_HS256_A128KW)
            {
                prfSelectionArgumentForLoad = 128; // To select HMACSHA256
                actualAesKwSizeBitsForLoad = 128;  // For A128KW
            }
            else
            {
                 // If the alg from header is not one we support for PBES2 or is our const, this will be caught.
                 // If it's a non-PBES2 alg, this registration isn't strictly necessary but doesn't hurt.
                // For simplicity, we'll only register if it's one of our known PBES2 types.
                // If not, JWT.Decode will use its default handlers.
                if (actualAlgFromHeader.StartsWith("PBES2"))
                {
                     throw new NotSupportedException($"The KeyManagementAlgorithm {effectiveAlgToUse} (from header or const) is not a supported PBES2 type for custom JWA registration.");
                }
            }

            if (prfSelectionArgumentForLoad > 0) // Only register if we determined it's a PBES2 we handle
            {
                var aesKeyWrapManagement = new Jose.AesKeyWrapManagement(actualAesKwSizeBitsForLoad);
                // For decryption, min/max iterations in the registered provider are less critical 
                // as 'p2c' from header is used. Setting them to iterationsExpected for consistency.
                var customPbse2Impl = new Jose.Pbse2HmacShaKeyManagementWithAesKeyWrap(prfSelectionArgumentForLoad, aesKeyWrapManagement, iterationsExpected, iterationsExpected);
                settings.RegisterJwa(effectiveAlgToUse, customPbse2Impl);
            }

            string decryptedJsonPayload = JWT.Decode(jweString, password, settings: settings);

            if (string.IsNullOrEmpty(decryptedJsonPayload))
            {
                throw new InvalidOperationException("Decrypted JWE payload was null or empty.");
            }

            // Use JsonSerializerOptions consistent with potential payload serialization if needed,
            // though for UvfMasterkeyPayload, default or PropertyNameCaseInsensitive is usually fine.
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var deserializedPayload = JsonSerializer.Deserialize<UvfMasterkeyPayload>(decryptedJsonPayload, options);
            if (deserializedPayload == null)
            {
                throw new InvalidOperationException("Failed to deserialize the JWE payload into UvfMasterkeyPayload.");
            }
            return deserializedPayload;
        }
    }
} 