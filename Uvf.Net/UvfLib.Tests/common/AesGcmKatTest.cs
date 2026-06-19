using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// AES-256-GCM known-answer vectors for the GCM primitive the content/header cryptors rely on
    /// (audit finding F-04). The library always uses AES-256-GCM with a 12-byte nonce, so both
    /// vectors below are AES-256 with a 96-bit IV. These are asserted directly against
    /// System.Security.Cryptography.AesGcm (the same primitive the production code uses).
    /// </summary>
    [TestClass]
    public class AesGcmKatTest
    {
        private static byte[] Hex(string hex) => Convert.FromHexString(hex);

        [TestMethod]
        [DisplayName("NIST CAVP AES-256-GCM (96-bit IV, with AAD, 120-bit tag)")]
        public void Aes256Gcm_NistCavpVectorWithAad_EncryptsAndDecrypts()
        {
            // NIST CAVP gcmEncryptExtIV256.rsp:
            //   [Keylen = 256] [IVlen = 96] [PTlen = 128] [AADlen = 128] [Taglen = 120], Count = 0
            // (gcmtestvectors.zip from the NIST Cryptographic Algorithm Validation Program).
            byte[] key = Hex("7f7168a406e7c1ef0fd47ac922c5ec5f659765fb6aaa048f7056f6c6b5d8513d");
            byte[] nonce = Hex("b8b5e407adc0e293e3e7e991");
            byte[] plaintext = Hex("b706194bb0b10c474e1b2d7b2278224c");
            byte[] aad = Hex("ff7628f6427fbcef1f3b82b37404e116");
            byte[] expectedCiphertext = Hex("8fada0b8e777a829ca9680d3bf4f3574");
            byte[] expectedTag = Hex("daca354277f6335fc8bec90886da70"); // 15 bytes = 120-bit tag

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[expectedTag.Length];

            using (var aesGcm = new AesGcm(key, expectedTag.Length))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            }

            CollectionAssert.AreEqual(expectedCiphertext, ciphertext, "Ciphertext must match the NIST vector.");
            CollectionAssert.AreEqual(expectedTag, tag, "Authentication tag must match the NIST vector.");

            // Round-trip: decrypt the published ciphertext/tag back to the plaintext.
            byte[] decrypted = new byte[expectedCiphertext.Length];
            using (var aesGcm = new AesGcm(key, expectedTag.Length))
            {
                aesGcm.Decrypt(nonce, expectedCiphertext, expectedTag, decrypted, aad);
            }

            CollectionAssert.AreEqual(plaintext, decrypted, "Decryption must recover the original plaintext.");
        }

        [TestMethod]
        [DisplayName("AES-256-GCM (96-bit IV, no AAD, 128-bit tag) - Go crypto test vector")]
        public void Aes256Gcm_NoAadVector_EncryptsAndDecrypts()
        {
            // AES-256-GCM vector from the Go standard library crypto/cipher GCM test suite
            // (src/crypto/cipher/gcm_test.go). Layout: ciphertext || 16-byte tag.
            byte[] key = Hex("feffe9928665731c6d6a8f9467308308feffe9928665731c6d6a8f9467308308");
            byte[] nonce = Hex("54cc7dc2c37ec006bcc6d1da");
            byte[] plaintext = Hex("007c5e5b3e59df24a7c355584fc1518d");
            byte[] expectedCiphertext = Hex("d50b9e252b70945d4240d351677eb10f");
            byte[] expectedTag = Hex("937cdaef6f2822b6a3191654ba41b197"); // 16 bytes = 128-bit tag

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[expectedTag.Length];

            using (var aesGcm = new AesGcm(key, expectedTag.Length))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            CollectionAssert.AreEqual(expectedCiphertext, ciphertext, "Ciphertext must match the vector.");
            CollectionAssert.AreEqual(expectedTag, tag, "Authentication tag must match the vector.");

            byte[] decrypted = new byte[expectedCiphertext.Length];
            using (var aesGcm = new AesGcm(key, expectedTag.Length))
            {
                aesGcm.Decrypt(nonce, expectedCiphertext, expectedTag, decrypted);
            }

            CollectionAssert.AreEqual(plaintext, decrypted, "Decryption must recover the original plaintext.");
        }
    }
}
