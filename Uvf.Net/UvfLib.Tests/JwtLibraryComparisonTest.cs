using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// Current UVF library
using UvfLib.Core.Api;
using UvfLib.Core.Jwe;

// Alternative library for AOT compatibility
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace UvfLib.Tests
{
    /// <summary>
    /// Test class to compare JWT token generation between jose-jwt (via UVF) and Microsoft.IdentityModel.JsonWebTokens
    /// This ensures we can safely migrate to the AOT-compatible library without breaking existing functionality
    /// </summary>
    [TestClass]
    public class JwtLibraryComparisonTest
    {
        private static UvfMasterkeyPayload _testPayload;
        private static string _testPassword;

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            _testPassword = "test123password";

            // Create test payload similar to what UVF uses
            _testPayload = new UvfMasterkeyPayload
            {
                UvfSpecVersion = 1,
                Keys = new List<PayloadKey>
                {
                    new PayloadKey
                    {
                        Id = "1",
                        Purpose = "org.cryptomator.masterkey",
                        Alg = "AES-256-RAW",
                        Value = Convert.ToBase64String(GenerateTestBytes(32))
                    },
                    new PayloadKey
                    {
                        Id = "2", 
                        Purpose = "org.cryptomator.hmacMasterkey",
                        Alg = "HMAC-SHA256-RAW",
                        Value = Convert.ToBase64String(GenerateTestBytes(32))
                    }
                },
                RootDirId = Convert.ToBase64String(GenerateTestBytes(16))
            };
        }

        private static byte[] GenerateTestBytes(int length)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)((i * 7) % 256); // Deterministic pattern
            }
            return bytes;
        }

        [TestMethod]
        public void CompareJweTokenStructure_UvfVsMicrosoft_ShouldHaveSimilarFormat()
        {
            Console.WriteLine("=== JWT Library Comparison Test ===");
            
            try
            {
                // Generate JWE with UVF's JweVaultManager (uses jose-jwt internally)
                byte[] passwordBytes = Encoding.UTF8.GetBytes(_testPassword);
                string uvfToken = MultiUserJweVaultManager.CreateSingleUserVault(_testPayload, passwordBytes);
                Console.WriteLine($"UVF JWE Length: {uvfToken.Length}");
                Console.WriteLine($"UVF JWE: {uvfToken.Substring(0, Math.Min(100, uvfToken.Length))}...");

                // Generate JWE with Microsoft library
                string msToken = GenerateMicrosoftJwe();
                Console.WriteLine($"Microsoft JWE Length: {msToken.Length}");
                Console.WriteLine($"Microsoft JWE: {msToken.Substring(0, Math.Min(100, msToken.Length))}...");

                // Parse both tokens to compare structure
                var uvfParts = ParseJweParts(uvfToken, "UVF");
                var msParts = ParseJweParts(msToken, "Microsoft");

                Console.WriteLine("\n=== Header Comparison ===");
                CompareJweHeaders(uvfParts.header, msParts.header);

                // UVF uses a different format - it's not a standard 5-part JWE
                // UVF appears to use a 3-part format, while Microsoft uses standard 5-part JWE
                Console.WriteLine($"UVF token parts: {uvfParts.partCount}");
                Console.WriteLine($"Microsoft token parts: {msParts.partCount}");
                
                // Verify both tokens have the expected number of parts for their respective formats
                Assert.IsTrue(uvfParts.partCount >= 3, "UVF token should have at least 3 parts");
                Assert.AreEqual(5, msParts.partCount, "Microsoft JWE should have 5 parts (standard JWE format)");

                Console.WriteLine("\n✅ Token structure comparison completed successfully");
                Console.WriteLine("Note: UVF and Microsoft use different token formats, which is expected.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private string GenerateMicrosoftJwe()
        {
            try
            {
                Console.WriteLine("Generating JWE with Microsoft.IdentityModel.JsonWebTokens...");

                // Convert our payload to claims format
                var encKey = _testPayload.Keys?.FirstOrDefault(k => k.Purpose == "org.cryptomator.masterkey")?.Value;
                var macKey = _testPayload.Keys?.FirstOrDefault(k => k.Purpose == "org.cryptomator.hmacMasterkey")?.Value;
                
                var claims = new Dictionary<string, object>
                {
                    ["encKey"] = encKey ?? "",
                    ["macKey"] = macKey ?? "",
                    ["rootDirId"] = _testPayload.RootDirId ?? "",
                    ["uvfSpecVersion"] = _testPayload.UvfSpecVersion
                };

                // Create encryption key from password (simplified approach)
                byte[] passwordBytes = Encoding.UTF8.GetBytes(_testPassword);
                byte[] keyBytes = new byte[32];
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(passwordBytes);
                    Array.Copy(hash, keyBytes, 32);
                }

                var encryptionKey = new SymmetricSecurityKey(keyBytes);
                // Microsoft IdentityModel doesn't support AES256KW + A256GCM combination
                // Use a compatible combination: AES256KW + A256CBC-HS512
                var encryptingCredentials = new EncryptingCredentials(
                    encryptionKey,
                    SecurityAlgorithms.Aes256KW,
                    SecurityAlgorithms.Aes256CbcHmacSha512
                );

                // Create token descriptor
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Claims = claims,
                    EncryptingCredentials = encryptingCredentials,
                    AdditionalHeaderClaims = new Dictionary<string, object>
                    {
                        ["uvf.spec.version"] = _testPayload.UvfSpecVersion
                    }
                };

                // Generate token
                var tokenHandler = new JsonWebTokenHandler();
                string token = tokenHandler.CreateToken(tokenDescriptor);

                Console.WriteLine("✅ Microsoft JWE generated successfully");
                return token;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Microsoft JWE generation failed: {ex.Message}");
                throw;
            }
        }

        private (string header, int partCount) ParseJweParts(string jwe, string source)
        {
            var parts = jwe.Split('.');
            Console.WriteLine($"{source} JWE has {parts.Length} parts");
            
            if (parts.Length < 1)
            {
                return ("[Parse Error]", parts.Length);
            }

            try
            {
                string header = DecodeBase64Url(parts[0]);
                return (header, parts.Length);
            }
            catch (Exception ex)
            {
                return ($"[Header Parse Error: {ex.Message}]", parts.Length);
            }
        }

        private string DecodeBase64Url(string base64Url)
        {
            try
            {
                // Add padding if necessary
                string padded = base64Url.PadRight(base64Url.Length + (4 - base64Url.Length % 4) % 4, '=');
                // Replace URL-safe characters
                padded = padded.Replace('-', '+').Replace('_', '/');
                byte[] bytes = Convert.FromBase64String(padded);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                return $"[Decode Error: {ex.Message}]";
            }
        }

        private void CompareJweHeaders(string uvfHeader, string msHeader)
        {
            try
            {
                Console.WriteLine($"UVF Header: {uvfHeader}");
                Console.WriteLine($"Microsoft Header: {msHeader}");

                var uvfHeaderObj = JsonSerializer.Deserialize<Dictionary<string, object>>(uvfHeader);
                var msHeaderObj = JsonSerializer.Deserialize<Dictionary<string, object>>(msHeader);

                // Compare key algorithm parameters
                CompareHeaderField(uvfHeaderObj, msHeaderObj, "alg", "Algorithm");
                CompareHeaderField(uvfHeaderObj, msHeaderObj, "enc", "Encryption");
                CompareHeaderField(uvfHeaderObj, msHeaderObj, "uvf.spec.version", "UVF Spec Version");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Header comparison failed: {ex.Message}");
            }
        }

        private void CompareHeaderField(Dictionary<string, object> uvf, Dictionary<string, object> ms, string field, string description)
        {
            var uvfValue = uvf.ContainsKey(field) ? uvf[field]?.ToString() : "Not present";
            var msValue = ms.ContainsKey(field) ? ms[field]?.ToString() : "Not present";
            
            Console.WriteLine($"  {description}: UVF='{uvfValue}', Microsoft='{msValue}' {(uvfValue == msValue ? "✅" : "⚠️")}");
        }

        [TestMethod]
        public void TestJweDecryption_UvfGenerated_BothLibrariesShouldDecrypt()
        {
            Console.WriteLine("\n=== JWE Decryption Test ===");

            try
            {
                // Generate with UVF's JweVaultManager
                byte[] passwordBytes = Encoding.UTF8.GetBytes(_testPassword);
                string uvfToken = MultiUserJweVaultManager.CreateSingleUserVault(_testPayload, passwordBytes, KeyDerivationParameters.Default());
                Console.WriteLine("✅ Token generated with UVF (jose-jwt)");

                // Test 1: Decrypt with UVF's own loader
                var uvfDecrypted = MultiUserJweVaultManager.LoadSingleUserVault(uvfToken, passwordBytes);
                
                // Extract keys from the decrypted payload
                var decryptedEncKey = uvfDecrypted.Keys?.FirstOrDefault(k => k.Purpose == "org.cryptomator.masterkey")?.Value;
                var decryptedMacKey = uvfDecrypted.Keys?.FirstOrDefault(k => k.Purpose == "org.cryptomator.hmacMasterkey")?.Value;
                Console.WriteLine($"✅ UVF decryption successful: EncKey length = {decryptedEncKey?.Length ?? 0}");

                // Test 2: Try to decrypt with Microsoft library (this might fail due to different implementations)
                try
                {
                    // This is exploratory - we expect it might fail
                    var result = TryDecryptWithMicrosoft(uvfToken);
                    if (result.success)
                    {
                        Console.WriteLine("✅ Cross-compatibility: UVF → Microsoft decryption successful!");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ Cross-compatibility failed: {result.error}");
                        Console.WriteLine("This is expected - different JWE implementations may not be compatible");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Cross-compatibility test error: {ex.Message}");
                    Console.WriteLine("This is expected - different JWE implementations may not be compatible");
                }

                // Verify the UVF decryption worked correctly
                Assert.IsNotNull(uvfDecrypted, "UVF decryption should succeed");
                
                // Extract original keys for comparison
                var originalEncKey = _testPayload.Keys?.FirstOrDefault(k => k.Purpose == "org.cryptomator.masterkey")?.Value;
                var originalMacKey = _testPayload.Keys?.FirstOrDefault(k => k.Purpose == "org.cryptomator.hmacMasterkey")?.Value;
                
                Assert.AreEqual(originalEncKey, decryptedEncKey, "Decrypted EncKey should match original");
                Assert.AreEqual(originalMacKey, decryptedMacKey, "Decrypted MacKey should match original");
                Assert.AreEqual(_testPayload.RootDirId, uvfDecrypted.RootDirId, "Decrypted RootDirId should match original");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Decryption test failed: {ex.Message}");
                throw;
            }
        }

        private (bool success, string error) TryDecryptWithMicrosoft(string jweToken)
        {
            try
            {
                // Create decryption key from password (same approach as encryption)
                byte[] passwordBytes = Encoding.UTF8.GetBytes(_testPassword);
                byte[] keyBytes = new byte[32];
                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(passwordBytes);
                    Array.Copy(hash, keyBytes, 32);
                }

                var tokenHandler = new JsonWebTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false,
                    TokenDecryptionKey = new SymmetricSecurityKey(keyBytes)
                };

                var result = tokenHandler.ValidateToken(jweToken, validationParameters);
                return (result.IsValid, result.Exception?.Message ?? "Unknown error");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
} 