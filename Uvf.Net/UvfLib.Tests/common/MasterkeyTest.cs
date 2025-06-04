using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Api; // Use the Api namespace for interfaces if needed
using System;
using System.Security.Cryptography;
using Moq; // Add Moq for mocking CSPRNG if needed for Generate test
using System.Linq;
using UvfLib._old.common; // Add Linq for checking zeroed array

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class MasterkeyTest
    {
        // Qualify Masterkey to UvfLib.Common.Masterkey to resolve ambiguity
        private const int SubkeyLength = _old.common.Masterkey.SubkeyLength;
        // Qualify Masterkey to UvfLib.Common.Masterkey to resolve ambiguity
        private const int KeyLength = _old.common.Masterkey.KeyLength;

        // Helper to create a deterministic mock RNG for testing Generate
        private static RandomNumberGenerator CreateMockRng(byte[] outputBytes)
        {
            var mockRng = new Mock<RandomNumberGenerator>();
            mockRng.Setup(rng => rng.GetBytes(It.Is<byte[]>(b => b.Length == outputBytes.Length)))
                   .Callback<byte[]>(buffer => Buffer.BlockCopy(outputBytes, 0, buffer, 0, outputBytes.Length));
            // Add setup for other lengths if needed, maybe throw?
            mockRng.Setup(rng => rng.GetBytes(It.Is<byte[]>(b => b.Length != outputBytes.Length)))
                   .Throws(new ArgumentException("Mock RNG only configured for specific length"));
            return mockRng.Object;
        }

        [TestMethod]
        [DisplayName("Test Generate Creates Valid Masterkey")]
        public void TestGenerate()
        {
            byte[] expectedKeyMaterial = new byte[KeyLength];
            for (int i = 0; i < KeyLength; i++) expectedKeyMaterial[i] = (byte)i; // Example deterministic bytes

            var mockRng = CreateMockRng(expectedKeyMaterial);

            // Qualify Masterkey type
            using (var masterkey = _old.common.Masterkey.Generate(mockRng))
            {
                Assert.IsNotNull(masterkey);
                Assert.IsFalse(masterkey.IsDestroyed());
                // Verify the internal key material using GetRaw (which returns a copy)
                CollectionAssert.AreEqual(expectedKeyMaterial, masterkey.GetRaw());
            }
            // Verify mock was called (optional)
            // Mock.Get(mockRng).Verify(rng => rng.GetBytes(It.Is<byte[]>(b => b.Length == KeyLength)), Times.Once);
        }

        [TestMethod]
        [DisplayName("Test From Creates Valid Masterkey From Subkeys")]
        public void TestFrom()
        {
            byte[] encKeyBytes = Enumerable.Range(0, SubkeyLength).Select(i => (byte)0x55).ToArray();
            byte[] macKeyBytes = Enumerable.Range(0, SubkeyLength).Select(i => (byte)0x77).ToArray();
            byte[] expectedCombined = encKeyBytes.Concat(macKeyBytes).ToArray();

            using (var encKey = new DestroyableSecretKey(encKeyBytes, "AES")) // Use correct C# class
            using (var macKey = new DestroyableSecretKey(macKeyBytes, "HmacSHA256")) // Use correct C# class
            // Qualify Masterkey type
            using (var masterkey = _old.common.Masterkey.From(encKey, macKey)) // Use static From method
            {
                Assert.IsNotNull(masterkey);
                Assert.IsFalse(masterkey.IsDestroyed());
                CollectionAssert.AreEqual(expectedCombined, masterkey.GetRaw());

                // Optional: Verify subkeys match
                using (var derivedEncKey = masterkey.GetEncKey())
                using (var derivedMacKey = masterkey.GetMacKey())
                {
                    CollectionAssert.AreEqual(encKeyBytes, derivedEncKey.GetEncoded());
                    CollectionAssert.AreEqual(macKeyBytes, derivedMacKey.GetEncoded());
                }
            }
            // Clear temp arrays for safety (though From should handle its inputs securely)
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(encKeyBytes); // Qualify
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(macKeyBytes); // Qualify
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(expectedCombined); // Qualify
        }

        [TestMethod]
        [DisplayName("Test GetEncKey Returns Correct Subkey")]
        public void TestGetEncKey()
        {
            byte[] rawKey = new byte[KeyLength];
            for (int i = 0; i < KeyLength; i++) rawKey[i] = (byte)i;
            byte[] expectedEncKey = rawKey.Take(SubkeyLength).ToArray();

            // Need to create Masterkey instance first, e.g., using From or Generate
            // Simplest might be to use internal knowledge for test setup or use From
            using (var encK = new DestroyableSecretKey(expectedEncKey, "AES"))
            using (var macK = new DestroyableSecretKey(rawKey.Skip(SubkeyLength).ToArray(), "HMAC"))
            // Qualify Masterkey type
            using (var masterkey = _old.common.Masterkey.From(encK, macK))
            using (var derivedEncKey = masterkey.GetEncKey())
            {
                Assert.IsNotNull(derivedEncKey);
                CollectionAssert.AreEqual(expectedEncKey, derivedEncKey.GetEncoded());
                Assert.AreEqual("AES", derivedEncKey.Algorithm); // Check algorithm if set by GetEncKey
            }
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(rawKey); // Qualify
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(expectedEncKey); // Qualify
        }

        [TestMethod]
        [DisplayName("Test GetMacKey Returns Correct Subkey")]
        public void TestGetMacKey()
        {
            byte[] rawKey = new byte[KeyLength];
            for (int i = 0; i < KeyLength; i++) rawKey[i] = (byte)i;
            byte[] expectedMacKey = rawKey.Skip(SubkeyLength).ToArray();

            using (var encK = new DestroyableSecretKey(rawKey.Take(SubkeyLength).ToArray(), "AES"))
            using (var macK = new DestroyableSecretKey(expectedMacKey, "HmacSHA256"))
            // Qualify Masterkey type
            using (var masterkey = _old.common.Masterkey.From(encK, macK))
            using (var derivedMacKey = masterkey.GetMacKey())
            {
                Assert.IsNotNull(derivedMacKey);
                CollectionAssert.AreEqual(expectedMacKey, derivedMacKey.GetEncoded());
                Assert.AreEqual("HmacSHA256", derivedMacKey.Algorithm); // Check algorithm if set by GetMacKey
            }
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(rawKey); // Qualify
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(expectedMacKey); // Qualify
        }

        [TestMethod]
        [DisplayName("Test Destroy Zeros Key and Sets Flag")]
        public void TestDestroy()
        {
            // Qualify Masterkey type
            _old.common.Masterkey masterkey = _old.common.Masterkey.Generate(); // Don't dispose immediately
            byte[] rawKeyCopy = masterkey.GetRaw(); // Get copy before destroy

            masterkey.Destroy();

            Assert.IsTrue(masterkey.IsDestroyed(), "IsDestroyed() should return true after Destroy()");

            // Verify accessing via GetRaw throws after destroy
            Assert.ThrowsException<InvalidOperationException>(() => masterkey.GetRaw(), "GetRaw() should throw after Destroy()");
            Assert.ThrowsException<InvalidOperationException>(() => masterkey.GetEncKey(), "GetEncKey() should throw after Destroy()");
            Assert.ThrowsException<InvalidOperationException>(() => masterkey.GetMacKey(), "GetMacKey() should throw after Destroy()");


            // Check that the original copy is not zeroed
            Assert.IsTrue(rawKeyCopy.Any(b => b != 0), "External copy of raw key should not be zeroed by Destroy()");

            // Optional: verify internal state (if possible without reflection, maybe by effect)
            // For example, trying to use it in 'From' might fail differently if zeroed vs destroyed flag? Difficult to test internal state reliably.

            System.Security.Cryptography.CryptographicOperations.ZeroMemory(rawKeyCopy); // Qualify
            masterkey.Dispose(); // Now dispose
        }

        // REMOVED TestRootDirId - No Java equivalent

        // REMOVED: Original TestCreateMasterkey, TestCreateFromRawKey, TestCreateFromRawKeyWithInvalidLength, TestEncryptAndDecryptMasterkey, TestDecryptWithWrongPassphrase
        // NOTE: Equality/HashCode tests might need adaptation depending on Masterkey's implementation (if it overrides Equals/GetHashCode) - Java test has them, C# doesn't currently.
    }
}