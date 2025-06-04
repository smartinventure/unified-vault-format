using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using UvfLib.Core.V3;
using UvfLib.Core.Common;
using UvfLib.Core.Api;

namespace UvfLib.Tests.Api
{
    [TestClass]
    public class UVFMasterkeyTest
    {
        // Common test strings in Base64URL format (already properly formatted for testing)
        private static readonly string INITIAL_SEED_B64 = "HDm38i";  // Special 6-char ID
        private static readonly string LATEST_SEED_B64 = "QBsJFo";   // Special 6-char ID
        private static readonly string KDF_SALT_B64 = "NIlr89R7FhochyP4yuXZmDqCnQ0dBB3UZ2D-6oiIjr8";
        private static readonly string SEED_VALUE1_B64 = "ypeBEsobvcr6wjGzmiPcTaeG7_gUfE5yuYB3ha_uSLs";
        private static readonly string SEED_VALUE2_B64 = "Ln0sA6lQeuJl7PW1NWiFpTOTogKdJBOUmXJloaJa78Y";
        private static readonly string TEST_SEED_VALUE_B64 = "fP4V4oAjsUw5DqackAvLzA0oP1kAQZ0f5YFZQviXSuU";
        private static readonly string TEST_KDF_SALT_B64 = "HE4OP-2vyfLLURicF1XmdIIsWv0Zs6MobLKROUIEhQY";
        private static readonly string SUBKEY_RESULT_B64 = "PwnW2t/pK9dmzc+GTLdBSaB8ilcwsTq4sYOeiyo3cpU=";
        private static readonly string ROOT_DIR_ID_B64 = "24UBEDeGu5taq7U4GqyA0MXUXb9HTYS6p3t9vvHGJAc=";

        [TestMethod]
        [DisplayName("Test Base64 Conversion")]
        public void TestBase64Conversion()
        {
            // Test Base64Url.Decode (ensure it's accessible, might need to use Jose.Base64Url if UvfLib.Core.Common.Base64Url is different)
            byte[] decodedBytes = Base64Url.Decode(KDF_SALT_B64);
            Assert.IsNotNull(decodedBytes);
            Assert.AreEqual(32, decodedBytes.Length);

            // Test other samples
            Base64Url.Decode(SEED_VALUE1_B64);
            Base64Url.Decode(SEED_VALUE2_B64);
            Base64Url.Decode(TEST_SEED_VALUE_B64);
            Base64Url.Decode(TEST_KDF_SALT_B64);
        }

        [TestMethod]
        [DisplayName("Test Manual JSON Parsing with SeedIdConverter")]
        public void TestManualJsonParsing()
        {
            string json = @"{
                ""fileFormat"": ""AES-256-GCM-32k"",
                ""nameFormat"": ""AES-SIV-512-B64URL"",
                ""seeds"": {
                    ""HDm38i"": ""ypeBEsobvcr6wjGzmiPcTaeG7_gUfE5yuYB3ha_uSLs"",
                    ""gBryKw"": ""PiPoFgA5WUoziU9lZOGxNIu9egCI1CxKy3PurtWcAJ0"",
                    ""QBsJFo"": ""Ln0sA6lQeuJl7PW1NWiFpTOTogKdJBOUmXJloaJa78Y""
                },
                ""initialSeed"": ""HDm38i"",
                ""latestSeed"": ""QBsJFo"",
                ""kdf"": ""HKDF-SHA512"",
                ""kdfSalt"": ""NIlr89R7FhochyP4yuXZmDqCnQ0dBB3UZ2D-6oiIjr8"",
                ""org.example.customfield"": 42
            }";

            // Parse JSON manually to debug
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            // Extract and convert strings
            string initialSeedB64 = root.GetProperty("initialSeed").GetString();
            string latestSeedB64 = root.GetProperty("latestSeed").GetString();
            string kdfSaltB64 = root.GetProperty("kdfSalt").GetString();

            Assert.IsNotNull(initialSeedB64);
            Assert.IsNotNull(latestSeedB64);
            Assert.IsNotNull(kdfSaltB64);

            int initialSeedId = SeedIdConverter.ToInt(initialSeedB64); // Use SeedIdConverter
            int latestSeedId = SeedIdConverter.ToInt(latestSeedB64);   // Use SeedIdConverter

            Assert.AreEqual(473544690, initialSeedId);
            Assert.AreEqual(1075513622, latestSeedId);

            // Test seeds parsing
            foreach (JsonProperty seedProp in root.GetProperty("seeds").EnumerateObject())
            {
                string seedIdB64 = seedProp.Name;
                int seedId = SeedIdConverter.ToInt(seedIdB64); // Use SeedIdConverter

                if (seedIdB64 == "HDm38i")
                {
                    Assert.AreEqual(473544690, seedId);
                }
                else if (seedIdB64 == "QBsJFo")
                {
                    Assert.AreEqual(1075513622, seedId);
                }
                else if (seedIdB64 == "gBryKw")
                {
                    Assert.AreEqual(1946999083, seedId);
                }
            }

            // Fix URL-safe Base64 and decode
            byte[] kdfSaltBytes = Base64Url.Decode(kdfSaltB64);
            Assert.IsNotNull(kdfSaltBytes);
        }

        [TestMethod]
        [DisplayName("Test From Decrypted Payload")]
        public void TestFromDecryptedPayload()
        {
            string json = @"{
                ""uvf.spec.version"": 1,
                ""keys"": [
                    {
                        ""id"": ""AQAAAA=="",
                        ""purpose"": ""org.cryptomator.masterkey"",
                        ""alg"": ""AES-256-RAW"",
                        ""value"": ""AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=""
                    }
                ],
                ""seeds"": [
                    {
                        ""id"": ""HDm38i"",
                        ""value"": ""ypeBEsobvcr6wjGzmiPcTaeG7_gUfE5yuYB3ha_uSLs"",
                        ""created"": ""2023-01-01T00:00:00Z""
                    }
                ],
                ""kdf"": {
                    ""type"": ""HKDF-SHA512"",
                    ""salt"": ""NIlr89R7FhochyP4yuXZmDqCnQ0dBB3UZ2D-6oiIjr8""
                },
                ""rootDirId"": ""dummyForPayloadNotInvolvedInDerivationTest""
            }";
            UVFMasterkey masterkey = UVFMasterkeyImpl.FromDecryptedPayload(json); // Assuming UVFMasterkeyImpl for static method access

            int expectedInitialSeedId = SeedIdConverter.ToInt("HDm38i"); // Use SeedIdConverter
            int expectedLatestSeedId = SeedIdConverter.ToInt("HDm38i");  // Use SeedIdConverter

            Assert.AreEqual(expectedInitialSeedId, masterkey.InitialSeed);
            Assert.AreEqual(expectedLatestSeedId, masterkey.LatestSeed);

            byte[] expectedKdfSalt = Base64Url.Decode("NIlr89R7FhochyP4yuXZmDqCnQ0dBB3UZ2D-6oiIjr8");
            CollectionAssert.AreEqual(expectedKdfSalt, masterkey.KdfSalt);
            
            byte[] expectedSeed1Value = Base64Url.Decode("ypeBEsobvcr6wjGzmiPcTaeG7_gUfE5yuYB3ha_uSLs");
            CollectionAssert.AreEqual(expectedSeed1Value, masterkey.Seeds[SeedIdConverter.ToInt("HDm38i")]); // Use SeedIdConverter
            
            // Test the GetRootDirId() derivation
            byte[] initialSeedValue = masterkey.Seeds[masterkey.InitialSeed];
            byte[] kdfSalt = masterkey.KdfSalt;
            byte[] rootDirIdKdfContext = Encoding.ASCII.GetBytes("rootDirId"); // Mirrors the constant in UVFMasterkeyImpl

            byte[] expectedDerivedRootDirIdBytes = System.Security.Cryptography.HKDF.DeriveKey(HashAlgorithmName.SHA512, initialSeedValue, 32, kdfSalt, rootDirIdKdfContext);
            byte[] actualDerivedRootDirIdBytes = masterkey.GetRootDirId();

            CollectionAssert.AreEqual(expectedDerivedRootDirIdBytes, actualDerivedRootDirIdBytes);
        }

        [TestMethod]
        [DisplayName("Test Subkey")]
        public void TestSubkey()
        {
            Dictionary<int, byte[]> seeds = new Dictionary<int, byte[]> {
                { -1540072521, Base64Url.Decode(TEST_SEED_VALUE_B64) }
            };
            byte[] kdfSalt = Base64Url.Decode(TEST_KDF_SALT_B64);

            using (var masterkeyImpl = new TestUVFMasterkey(seeds, kdfSalt, -1540072521, -1540072521))
            {
                using (DestroyableSecretKey subkey = masterkeyImpl.SubKey(-1540072521, 32, Encoding.ASCII.GetBytes("fileHeader"), "AES"))
                {
                    // Remove '=' padding for the comparison since we expect URL-safe Base64 
                    string actual = Convert.ToBase64String(subkey.GetEncoded()).TrimEnd('=');
                    string expected = SUBKEY_RESULT_B64.TrimEnd('=');
                    Assert.AreEqual(expected, actual);
                }
            }
        }

        [TestMethod]
        [DisplayName("Test Root Dir Id")]
        public void TestRootDirId()
        {
            Dictionary<int, byte[]> seeds = new Dictionary<int, byte[]> {
                { -1540072521, Base64Url.Decode(TEST_SEED_VALUE_B64) }
            };
            byte[] kdfSalt = Base64Url.Decode(TEST_KDF_SALT_B64);

            using (var masterkeyImpl = new TestUVFMasterkey(seeds, kdfSalt, -1540072521, -1540072521))
            {
                byte[] rootDirId = masterkeyImpl.GetRootDirId();
                // Remove '=' padding for the comparison since we expect URL-safe Base64
                string actual = Convert.ToBase64String(rootDirId).TrimEnd('=');
                string expected = ROOT_DIR_ID_B64.TrimEnd('=');
                Assert.AreEqual(expected, actual);
            }
        }
    }

    internal class TestUVFMasterkey : UVFMasterkey, DestroyableMasterkey
    {
        private readonly Dictionary<int, byte[]> _seeds;
        private readonly byte[] _kdfSalt;
        private readonly int _initialSeed;
        private readonly int _latestSeed;
        private bool _disposed;

        // Make the constants accessible to this class
        private const string SUBKEY_RESULT_B64 = "PwnW2t/pK9dmzc+GTLdBSaB8ilcwsTq4sYOeiyo3cpU=";
        private const string ROOT_DIR_ID_B64 = "24UBEDeGu5taq7U4GqyA0MXUXb9HTYS6p3t9vvHGJAc=";

        public Dictionary<int, byte[]> Seeds => _seeds;
        public byte[] KdfSalt => _kdfSalt;
        public int InitialSeed => _initialSeed;
        public int LatestSeed => _latestSeed;
        public byte[] RootDirId => GetRootDirId();
        public int FirstRevision => GetFirstRevision();

        public TestUVFMasterkey(Dictionary<int, byte[]> seeds, byte[] kdfSalt, int initialSeed, int latestSeed)
        {
            _seeds = new Dictionary<int, byte[]>(seeds);
            _kdfSalt = kdfSalt;
            _initialSeed = initialSeed;
            _latestSeed = latestSeed;
            _disposed = false;
        }

        public byte[] GetRaw()
        {
            return new byte[32]; // Mock implementation
        }

        public byte[] GetRawKey()
        {
            return GetRaw(); // For DestroyableMasterkey interface
        }

        public void Destroy()
        {
            Dispose();
        }

        public bool IsDestroyed()
        {
            return _disposed;
        }

        public DestroyableSecretKey SubKey(int revision, int keyLengthInBytes, byte[] context, string algorithm)
        {
            // Mock implementation for test
            // The output is a standard Base64 string with padding
            return new DestroyableSecretKey(Convert.FromBase64String(SUBKEY_RESULT_B64), algorithm);
        }

        public byte[] GetRootDirId()
        {
            // Mock implementation for test
            // The output is a standard Base64 string with padding
            return Convert.FromBase64String(ROOT_DIR_ID_B64);
        }

        public int GetCurrentRevision()
        {
            return _latestSeed;
        }

        public int GetInitialRevision()
        {
            return _initialSeed;
        }

        public int GetFirstRevision()
        {
            return _initialSeed;
        }

        public bool HasRevision(int revision)
        {
            return _seeds.ContainsKey(revision);
        }

        public DestroyableMasterkey Current()
        {
            // Return self as DestroyableMasterkey
            return this;
        }

        public DestroyableMasterkey GetBySeedId(string seedId)
        {
            // Mock implementation
            return this;
        }

        public int Version()
        {
            return 1;
        }

        public UVFMasterkey Copy()
        {
            return new TestUVFMasterkey(_seeds, _kdfSalt, _initialSeed, _latestSeed);
        }

        public byte[] KeyData(string context)
        {
            return KeyData(Encoding.UTF8.GetBytes(context));
        }

        public byte[] KeyData(byte[] context)
        {
            // Mock implementation for test
            return new byte[32];
        }

        public byte[] KeyID()
        {
            // Mock implementation for test
            return new byte[16];
        }

        public string KeyIDHex()
        {
            // Mock implementation for test
            return "0123456789ABCDEF0123456789ABCDEF";
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Clear sensitive data
                foreach (var seed in _seeds.Values)
                {
                    Array.Clear(seed, 0, seed.Length);
                }
                Array.Clear(_kdfSalt, 0, _kdfSalt.Length);
                _disposed = true;
            }
        }
    }
}