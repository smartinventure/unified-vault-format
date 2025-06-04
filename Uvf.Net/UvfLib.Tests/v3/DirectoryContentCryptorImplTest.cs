using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Common;
using System;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Diagnostics;
using UvfLib.Tests.Common;
using UvfLib._old.v3;
using UvfLib._old.api;

namespace UvfLib.Tests.V3
{
    [TestClass]
    public class DirectoryContentCryptorImplTest
    {
        private static RandomNumberGenerator CSPRNG;
        private static UVFMasterkey masterkey;
        private static DirectoryContentCryptorImpl dirCryptor;

        [ClassInitialize]
        public static void SetUp(TestContext context)
        {
            // Use deterministic RNG for tests
            CSPRNG = SecureRandomMock.NULL_RANDOM;

            // Setup masterkey with the new JWE payload format
            // Simplified seeds to ensure HDm38i is the initial seed for root operations, matching original test intent.
            string json = @"{
                ""uvf.spec.version"": 1,
                ""keys"": [
                    { ""id"": ""AQAAAA=="", ""purpose"": ""org.cryptomator.masterkey"", ""alg"": ""AES-256-RAW"", ""value"": ""AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="" },
                    { ""id"": ""AgAAAA=="", ""purpose"": ""org.cryptomator.hmacMasterkey"", ""alg"": ""HMAC-SHA256-RAW"", ""value"": ""BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB="" }
                ],
                ""seeds"": [
                    { ""id"": ""HDm38i"", ""value"": ""ypeBEsobvcr6wjGzmiPcTaeG7_gUfE5yuYB3ha_uSLs"", ""created"": ""2023-01-01T00:00:00Z"" }
                ],
                ""kdf"": {
                    ""type"": ""HKDF-SHA512"",
                    ""salt"": ""NIlr89R7FhochyP4yuXZmDqCnQ0dBB3UZ2D-6oiIjr8""
                },
                ""rootDirId"": ""dummyPreCalculatedRootDirIdOptional""
            }";

            masterkey = UVFMasterkey.FromDecryptedPayload(json);
            // Now masterkey.GetFirstRevision() will be SeedIdToInt("HDm38i")
            // and masterkey.GetRootDirId() will be derived using HDm38i's seed value.
            dirCryptor = (DirectoryContentCryptorImpl)CryptorProvider.ForScheme(CryptorProvider.Scheme.UVF_DRAFT).Provide(masterkey, CSPRNG).DirectoryContentCryptor();
        }

        [ClassCleanup]
        public static void TearDown()
        {
            CSPRNG.Dispose();
        }

        [TestMethod]
        [DisplayName("Encrypt and decrypt dir.uvf files")]
        public void EncryptAndDecryptDirectoryMetadata()
        {
            DirectoryMetadataImpl origMetadata = (DirectoryMetadataImpl)dirCryptor.NewDirectoryMetadata();

            byte[] encryptedMetadata = dirCryptor.EncryptDirectoryMetadata(origMetadata);
            DirectoryMetadataImpl decryptedMetadata = (DirectoryMetadataImpl)dirCryptor.DecryptDirectoryMetadata(encryptedMetadata);

            Assert.AreEqual(origMetadata.SeedId, decryptedMetadata.SeedId);
            Assert.AreEqual(origMetadata.DirId, decryptedMetadata.DirId);
        }

        [TestMethod]
        [DisplayName("Encrypt WELCOME.rtf in root dir")]
        public void TestEncryptReadme()
        {
            DirectoryMetadata rootDirMetadata = dirCryptor.RootDirectoryMetadata();
            IDirectoryContentCryptor.Encrypting enc = dirCryptor.FileNameEncryptor(rootDirMetadata);

            string ciphertext = enc.Encrypt("WELCOME.rtf");

            Assert.AreEqual("Dx1binBPsg_KNby6KFD_2k3vZHPgo39rg4ks.uvf", ciphertext);
        }

        [TestMethod]
        [DisplayName("Decrypt WELCOME.rtf in root dir")]
        public void TestDecryptReadme()
        {
            DirectoryMetadata rootDirMetadata = dirCryptor.RootDirectoryMetadata();
            IDirectoryContentCryptor.Decrypting dec = dirCryptor.FileNameDecryptor(rootDirMetadata);

            string plaintext = dec.Decrypt("Dx1binBPsg_KNby6KFD_2k3vZHPgo39rg4ks.uvf");

            Assert.AreEqual("WELCOME.rtf", plaintext);
        }

        [TestMethod]
        [DisplayName("Get root dir path")]
        public void TestRootDirPath()
        {
            DirectoryMetadata rootDirMetadata = dirCryptor.RootDirectoryMetadata();

            string path = dirCryptor.DirPath(rootDirMetadata);

            Assert.AreEqual("d/RZ/K7ZH7KBXULNEKBMGX3CU42PGUIAIX4", path);
        }

        [TestClass]
        [TestCategory("WithDirectoryMetadata")]
        public class WithDirectoryMetadata
        {
            private DirectoryMetadataImpl dirUvf;
            private IDirectoryContentCryptor.Encrypting enc;
            private IDirectoryContentCryptor.Decrypting dec;

            [TestInitialize]
            public void Setup()
            {
                // Ensure outer class static fields are initialized
                if (DirectoryContentCryptorImplTest.masterkey == null || DirectoryContentCryptorImplTest.dirCryptor == null)
                {
                    // Re-run the outer class's setup if needed. 
                    // Pass null for TestContext if it's not strictly used by SetUp logic after initial call.
                    DirectoryContentCryptorImplTest.SetUp(null); 
                }

                // Add null checks for debugging (can be removed after fixing)
                if (DirectoryContentCryptorImplTest.masterkey == null)
                {
                    throw new InvalidOperationException("Outer class masterkey is STILL null in nested Setup after re-init attempt");
                }
                if (DirectoryContentCryptorImplTest.dirCryptor == null)
                {
                    throw new InvalidOperationException("Outer class dirCryptor is STILL null in nested Setup after re-init attempt");
                }

                // Create an empty directory ID as in Java test
                dirUvf = new DirectoryMetadataImpl(DirectoryContentCryptorImplTest.masterkey.GetCurrentRevision(), new byte[32]);
                enc = DirectoryContentCryptorImplTest.dirCryptor.FileNameEncryptor(dirUvf);
                dec = DirectoryContentCryptorImplTest.dirCryptor.FileNameDecryptor(dirUvf);
            }

            [DataTestMethod]
            [DataRow("file1.txt", "p9FyZmPc9-PUI7AOihp84cwIIWdJKCKKsg==.uvf")]
            [DataRow("file2.txt", "BLgwXhv87jMAVC0oJci7P-pMOQDRrPdlrw==.uvf")]
            [DataRow("file3.txt", "dM0BEmaKPMofsgLNXfDEiHSyzi_Z2EoRIA==.uvf")]
            [DataRow("file4.txt", "ZHJZegnbA2YRz5IB19O6Qwg0Qls_VLeuYg==.uvf")]
            [DisplayName("Encrypt multiple file names")]
            public void TestBulkEncryption(string plaintext, string expectedCiphertext)
            {
                string actualCiphertext = enc.Encrypt(plaintext);
                Debug.WriteLine($"Plaintext: {plaintext}, Generated Ciphertext: {actualCiphertext}");
                Assert.AreEqual(expectedCiphertext, actualCiphertext);
            }

            [DataTestMethod]
            [DataRow("file1.txt", "p9FyZmPc9-PUI7AOihp84cwIIWdJKCKKsg==.uvf")]
            [DataRow("file2.txt", "BLgwXhv87jMAVC0oJci7P-pMOQDRrPdlrw==.uvf")]
            [DataRow("file3.txt", "dM0BEmaKPMofsgLNXfDEiHSyzi_Z2EoRIA==.uvf")]
            [DataRow("file4.txt", "ZHJZegnbA2YRz5IB19O6Qwg0Qls_VLeuYg==.uvf")]
            [DisplayName("Decrypt multiple file names")]
            public void TestBulkDecryption(string expectedPlaintext, string ciphertext)
            {
                string actualPlaintext = dec.Decrypt(ciphertext);
                Assert.AreEqual(expectedPlaintext, actualPlaintext);
            }

            [TestMethod]
            [DisplayName("Decrypt file with invalid extension")]
            public void TestDecryptMalformed1()
            {
                Assert.ThrowsException<ArgumentException>(() =>
                {
                    dec.Decrypt("NIWamUJBS3u619f3yKOWlT2q_raURsHXhg==.INVALID");
                });
            }

            [TestMethod]
            [DisplayName("Decrypt file with unauthentic ciphertext")]
            public void TestDecryptMalformed2()
            {
                Assert.ThrowsException<AuthenticationFailedException>(() =>
                {
                    dec.Decrypt("INVALIDamUJBS3u619f3yKOWlT2q_raURsHXhg==.uvf");
                });
            }

            [TestMethod]
            [DisplayName("Decrypt file with incorrect seed")]
            public void TestDecryptMalformed3()
            {
                DirectoryMetadataImpl differentRevision = new DirectoryMetadataImpl(
                    DirectoryContentCryptorImplTest.masterkey.GetFirstRevision(),
                    new byte[32]); // Removed null for children

                IDirectoryContentCryptor.Decrypting differentRevisionDec =
                    DirectoryContentCryptorImplTest.dirCryptor.FileNameDecryptor(differentRevision);

                Assert.ThrowsException<AuthenticationFailedException>(() =>
                {
                    differentRevisionDec.Decrypt("NIWamUJBS3u619f3yKOWlT2q_raURsHXhg==.uvf");
                });
            }

            [TestMethod]
            [DisplayName("Decrypt file with incorrect dirId")]
            public void TestDecryptMalformed4()
            {
                // Create a different, but valid, 32-byte directory ID
                byte[] differentDirId = new byte[32];
                Array.Fill(differentDirId, (byte)0xFF); // Fill with a different value

                DirectoryMetadataImpl differentDirIdMetadata = new DirectoryMetadataImpl(
                    DirectoryContentCryptorImplTest.masterkey.GetCurrentRevision(), // Use current revision like in setup
                    differentDirId); // Removed null for children

                IDirectoryContentCryptor.Decrypting differentDirIdDec =
                    DirectoryContentCryptorImplTest.dirCryptor.FileNameDecryptor(differentDirIdMetadata);

                Assert.ThrowsException<AuthenticationFailedException>(() =>
                {
                    differentDirIdDec.Decrypt("NIWamUJBS3u619f3yKOWlT2q_raURsHXhg==.uvf");
                });
            }
        }
    }
}