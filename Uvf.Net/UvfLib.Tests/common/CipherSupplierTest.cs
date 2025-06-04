using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Core.Common;
using System;
using System.Security.Cryptography;
using System.Linq;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class CipherSupplierTest
    {
        [TestMethod]
        [DisplayName("Test Get Unknown Cipher")]
        public void TestGetUnknownCipher()
        {
            // Test creating a CipherSupplier with an invalid algorithm name
            Assert.ThrowsException<ArgumentException>(() =>
                new CipherSupplier("doesNotExist"));
        }

        [TestMethod]
        [DisplayName("Test Get Cipher With Invalid Key")]
        public void TestGetCipherWithInvalidKey()
        {
            // Create a CipherSupplier
            CipherSupplier supplier = new CipherSupplier("AES-CBC");

            // Create an invalid key (wrong size for AES)
            byte[] keyData = new byte[13]; // AES keys should be 16, 24, or 32 bytes
            var key = new DestroyableSecretKey(keyData, "AES");

            // Create valid IV
            byte[] iv = new byte[16];

            // Attempt to get a cipher with the invalid key
            var exception = Assert.ThrowsException<ArgumentException>(() =>
                supplier.EncryptionCipher(key, iv));

            // Verify the exception message contains expected text about key size
            StringAssert.Contains(exception.Message, "Specified key is not a valid size"); // Adjusted expectation
        }

        [TestMethod]
        [DisplayName("Test Get Cipher With Invalid Parameters")]
        public void TestGetCipherWithInvalidParameters()
        {
            // Create a CipherSupplier for AES-CBC
            CipherSupplier supplier = new CipherSupplier("AES-CBC");

            // Create a valid key
            byte[] keyData = new byte[16]; // Valid 128-bit AES key
            var key = new DestroyableSecretKey(keyData, "AES");

            // Create invalid IV (wrong size for AES-CBC - needs 16 bytes)
            byte[] invalidIv = new byte[8];

            // Attempt to get a cipher with the invalid IV
            var exception = Assert.ThrowsException<ArgumentException>(() =>
                supplier.EncryptionCipher(key, invalidIv));

            // Verify the exception message indicates an IV/block size issue
            StringAssert.Contains(exception.Message, "initialization vector (IV) does not match the block size"); // Adjusted expectation
        }

        [TestMethod]
        [DisplayName("Test Get Valid Cipher")]
        public void TestGetValidCipher()
        {
            // Create a CipherSupplier for AES-GCM
            CipherSupplier supplier = CipherSupplier.AES_GCM;

            // Create a valid key
            byte[] keyData = new byte[32]; // 256-bit AES key
            var key = new DestroyableSecretKey(keyData, "AES");

            // Create valid nonce for GCM
            byte[] nonce = new byte[12]; // GCM typically uses 12-byte nonce

            // Get encryption cipher
            using (var lease = supplier.EncryptionCipher(key, nonce))
            {
                Assert.IsNotNull(lease);
            }

            // Get decryption cipher
            using (var lease = supplier.DecryptionCipher(key, nonce))
            {
                Assert.IsNotNull(lease);
            }
        }

        [TestMethod]
        [DisplayName("Test AES CTR Encryption/Decryption Inverse")]
        public void TestAESCTREncryptionDecryptionInverse()
        {
            CipherSupplier supplier = CipherSupplier.AES_CTR;
            byte[] keyData = new byte[16];
            var key = new DestroyableSecretKey(keyData, "AES");
            byte[] iv = new byte[16];
            byte[] cleartext = new byte[100];
            for (int i = 0; i < cleartext.Length; i++) { cleartext[i] = (byte)(i + 1); }

            byte[] ciphertext;
            // Encrypt using our custom supplier
            using (var encryptTransform = supplier.EncryptionCipher(key, iv))
            {
                ciphertext = encryptTransform.TransformFinalBlock(cleartext, 0, cleartext.Length);
            }

            // Decrypt using our custom supplier
            byte[] decryptedtext;
            using (var decryptTransform = supplier.DecryptionCipher(key, iv))
            {
                decryptedtext = decryptTransform.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }

            // Assert that decryption reverses encryption
            Assert.IsTrue(cleartext.SequenceEqual(decryptedtext), "Custom CTR decryption did not reverse custom CTR encryption.");
        }
    }
}