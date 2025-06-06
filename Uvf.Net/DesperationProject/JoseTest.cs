using Jose;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace DesperationProject
{
    internal class JoseTest
    {
        internal void Test()
        {
            Console.WriteLine("Starting Minimal Reproducible Example for jose-jwt...");

            string password = "your-super-secret-password"; // Same as in your UvfConsole
            int pbkdf2Iterations = 10000;
            int saltSizeBytes = 16;
            JweAlgorithm keyManagementAlgorithm = JweAlgorithm.PBES2_HS512_A256KW;
            JweEncryption contentEncryptionAlgorithm = JweEncryption.A256GCM;

            string dummyPayloadJson = "{\"message\":\"hello world\", \"timestamp\":\"" + DateTime.UtcNow.ToLongTimeString() + "\"}";
            byte[] salt = RandomNumberGenerator.GetBytes(saltSizeBytes);

            Console.WriteLine($"MRE - Password Length: {password.Length}");
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                Console.WriteLine($"MRE - Password SHA256: {Convert.ToBase64String(hashedBytes)}");
            }
            Console.WriteLine($"MRE - PBKDF2 Iterations (p2c): {pbkdf2Iterations}");
            Console.WriteLine($"MRE - Generated Salt (p2s): {Base64Url.Encode(salt)}");
            Console.WriteLine($"MRE - KeyManagementAlgorithm: {keyManagementAlgorithm}");
            Console.WriteLine($"MRE - ContentEncryptionAlgorithm: {contentEncryptionAlgorithm}");
            Console.WriteLine($"MRE - Payload: {dummyPayloadJson}");

            var extraHeaders = new Dictionary<string, object>
        {
            { "p2s", Base64Url.Encode(salt) },
            { "p2c", pbkdf2Iterations }
        };

            var settings = new JwtSettings();
            string jweString = "";

            try
            {
                Console.WriteLine("MRE - Attempting JWT.Encode...");
                jweString = JWT.Encode(dummyPayloadJson, password, keyManagementAlgorithm, contentEncryptionAlgorithm, extraHeaders: extraHeaders, settings: settings);
                Console.WriteLine($"MRE - JWT.Encode successful. JWE String (first 30 chars): {(jweString.Length > 30 ? jweString.Substring(0, 30) : jweString)}...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MRE - ERROR during JWT.Encode: {ex.ToString()}");
                Console.WriteLine("MRE - Test cannot continue.");
                return;
            }

            Console.WriteLine("MRE - Attempting JWT.Decode...");
            try
            {
                string decryptedPayload = JWT.Decode(jweString, password, settings: settings);
                Console.WriteLine("MRE - JWT.Decode successful!");
                Console.WriteLine($"MRE - Decrypted Payload: {decryptedPayload}");
            }
            catch (Jose.IntegrityException intEx)
            {
                Console.WriteLine($"MRE - INTEGRITY EXCEPTION during JWT.Decode: {intEx.Message}");
                Console.WriteLine($"MRE - Stack trace: {intEx.StackTrace}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MRE - OTHER EXCEPTION during JWT.Decode: {ex.GetType().Name}: {ex.Message}");
            }

            Console.WriteLine("\n\n========== TEST 2: Without explicit p2s/p2c ==========\n");

            // Test 2: Let jose-jwt handle p2s and p2c internally
            Console.WriteLine("MRE Test 2 - Attempting JWT.Encode WITHOUT explicit p2s/p2c...");
            string jweString2 = "";

            try
            {
                // No extraHeaders this time - let jose-jwt generate salt and use default iterations
                jweString2 = JWT.Encode(dummyPayloadJson, password, keyManagementAlgorithm, contentEncryptionAlgorithm, settings: settings);
                Console.WriteLine($"MRE Test 2 - JWT.Encode successful. JWE String (first 30 chars): {(jweString2.Length > 30 ? jweString2.Substring(0, 30) : jweString2)}...");

                // Decode the header to see what jose-jwt generated
                var parts = jweString2.Split('.');
                if (parts.Length >= 1)
                {
                    var decodedHeader = Encoding.UTF8.GetString(Base64Url.Decode(parts[0]));
                    Console.WriteLine($"MRE Test 2 - JWE Protected Header: {decodedHeader}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MRE Test 2 - ERROR during JWT.Encode: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine("MRE Test 2 - Cannot continue with decode.");
                Console.WriteLine("\nMRE - All tests finished.");
                return;
            }

            Console.WriteLine("MRE Test 2 - Attempting JWT.Decode...");
            try
            {
                string decryptedPayload2 = JWT.Decode(jweString2, password, settings: settings);
                Console.WriteLine("MRE Test 2 - JWT.Decode successful!");
                Console.WriteLine($"MRE Test 2 - Decrypted Payload: {decryptedPayload2}");
            }
            catch (Jose.IntegrityException intEx)
            {
                Console.WriteLine($"MRE Test 2 - INTEGRITY EXCEPTION during JWT.Decode: {intEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MRE Test 2 - OTHER EXCEPTION during JWT.Decode: {ex.GetType().Name}: {ex.Message}");
            }

            Console.WriteLine("\nMRE - All tests finished.");
        }
    }
}
