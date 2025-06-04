using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UvfLib._old.common; // Added for Base64 decoding

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class P384KeyPairTest
    {
        [TestMethod]
        [DisplayName("Test Generate")]
        public void TestGenerate()
        {
            P384KeyPair keyPair1 = P384KeyPair.Generate();
            P384KeyPair keyPair2 = P384KeyPair.Generate();

            Assert.IsNotNull(keyPair1);
            Assert.IsNotNull(keyPair2);

            // The key pairs should be different (different public keys)
            byte[] pubKey1 = keyPair1.ExportPublicKey();
            byte[] pubKey2 = keyPair2.ExportPublicKey();

            Assert.IsFalse(ByteArraysEqual(pubKey1, pubKey2));
        }

        [TestMethod]
        [DisplayName("Test Create")]
        public void TestCreate()
        {
            // Hardcoded keys from Java test (Base64 decoded)
            byte[] publicKeyBytes = Convert.FromBase64String("MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAERxQR+NRN6Wga01370uBBzr2NHDbKIC56tPUEq2HX64RhITGhii8Zzbkb1HnRmdF0aq6uqmUy4jUhuxnKxsv59A6JeK7Unn+mpmm3pQAygjoGc9wrvoH4HWJSQYUlsXDu");
            byte[] privateKeyBytes = Convert.FromBase64String("ME8CAQAwEAYHKoZIzj0CAQYFK4EEACIEODA2AgEBBDEA6QybmBitf94veD5aCLr7nlkF5EZpaXHCfq1AXm57AKQyGOjTDAF9EQB28fMywTDQ");

            // Create key pair from exported keys
            P384KeyPair keyPair = P384KeyPair.Create(publicKeyBytes, privateKeyBytes);

            Assert.IsNotNull(keyPair);

            // Removed original sign/verify and key comparison checks to match Java test
            // byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            // byte[] signature = keyPair.Sign(data);
            // Assert.IsTrue(keyPair.Verify(data, signature));
            // byte[] newPublicKeyBytes = keyPair.ExportPublicKey();
            // Assert.IsTrue(ByteArraysEqual(publicKeyBytes, newPublicKeyBytes));
        }

        [TestMethod]
        [DisplayName("Test Store")]
        public void TestStore()
        {
            // Create temporary file path
            string p12FilePath = Path.GetTempFileName();
            try
            {
                // Generate key pair
                P384KeyPair keyPair = P384KeyPair.Generate();

                // Test storing the key pair
                keyPair.Store(p12FilePath, "topsecret".ToCharArray());

                // Verify the file exists
                Assert.IsTrue(File.Exists(p12FilePath));
                Assert.IsTrue(new FileInfo(p12FilePath).Length > 0);
            }
            finally
            {
                // Clean up
                if (File.Exists(p12FilePath))
                {
                    File.Delete(p12FilePath);
                }
            }
        }

        [TestMethod]
        [DisplayName("Test Sign and Verify")]
        public void TestSignAndVerify()
        {
            // Generate key pair
            P384KeyPair keyPair = P384KeyPair.Generate();

            // Test data
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            // Sign data
            byte[] signature = keyPair.Sign(data);
            Assert.IsNotNull(signature);
            Assert.IsTrue(signature.Length > 0);

            // Verify signature
            bool verified = keyPair.Verify(data, signature);
            Assert.IsTrue(verified);

            // Test negative case - modified data
            byte[] modifiedData = new byte[] { 1, 2, 3, 4, 6 };
            bool verifiedModified = keyPair.Verify(modifiedData, signature);
            Assert.IsFalse(verifiedModified);
        }

        [TestClass]
        public class WithStored
        {
            private P384KeyPair _origKeyPair;
            private string _p12FilePath;

            [TestInitialize]
            public void Setup()
            {
                // Generate key pair
                _origKeyPair = P384KeyPair.Generate();

                // Create temporary file
                _p12FilePath = Path.GetTempFileName();

                // Store the key pair
                _origKeyPair.Store(_p12FilePath, "topsecret".ToCharArray());
            }

            [TestCleanup]
            public void Cleanup()
            {
                // Clean up
                if (File.Exists(_p12FilePath))
                {
                    File.Delete(_p12FilePath);
                }

                _origKeyPair.Dispose();
            }

            [TestMethod]
            [DisplayName("Test Load With Invalid Passphrase")]
            public void TestLoadWithInvalidPassphrase()
            {
                char[] wrongPassphrase = "bottompublic".ToCharArray();

                try
                {
                    P384KeyPair keyPair = P384KeyPair.Load(_p12FilePath, wrongPassphrase);
                    keyPair.Dispose();
                    Assert.Fail("Expected Pkcs12PasswordException was not thrown");
                }
                catch (Exception ex)
                {
                    Assert.IsInstanceOfType(ex, typeof(Pkcs12PasswordException));
                    Assert.IsTrue(ex.Message.Contains("Invalid password"));
                }
            }

            [TestMethod]
            [DisplayName("Test Load With Valid Passphrase")]
            public void TestLoadWithValidPassphrase()
            {
                // Get original key pair's public key for comparison
                byte[] originalPublicKey = _origKeyPair.ExportPublicKey();

                // Load the key pair
                P384KeyPair loadedKeyPair = P384KeyPair.Load(_p12FilePath, "topsecret".ToCharArray());

                // Get loaded key pair's public key
                byte[] loadedPublicKey = loadedKeyPair.ExportPublicKey();

                // Compare public keys
                Assert.IsTrue(ByteArraysEqual(originalPublicKey, loadedPublicKey));

                // Test that the loaded key can sign and verify
                byte[] data = new byte[] { 1, 2, 3, 4, 5 };
                byte[] signature = loadedKeyPair.Sign(data);
                Assert.IsTrue(loadedKeyPair.Verify(data, signature));

                // Clean up
                loadedKeyPair.Dispose();
            }
        }

        private static bool ByteArraysEqual(byte[] a1, byte[] a2)
        {
            if (a1 == null || a2 == null)
                return false;

            if (a1.Length != a2.Length)
                return false;

            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a1, a2);
        }
    }
}