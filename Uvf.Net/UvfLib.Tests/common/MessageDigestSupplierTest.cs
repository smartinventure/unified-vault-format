using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography;
using UvfLib._old.common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class MessageDigestSupplierTest
    {
        [TestMethod]
        [DisplayName("Test Constructor With Invalid Digest Algorithm")]
        public void TestConstructorWithInvalidDigest()
        {
            // Test creating a MessageDigestSupplier with an invalid algorithm name
            Assert.ThrowsException<ArgumentException>(() =>
                new MessageDigestSupplier("FOO3000"));
        }

        [TestMethod]
        [DisplayName("Test Get SHA256 Instance")]
        public void TestGetSha256()
        {
            // Get a MessageDigest from the supplier
            using (var digestLease = MessageDigestSupplier.SHA256.Get())
            {
                Assert.IsNotNull(digestLease.Get());
            }

            // Get another MessageDigest from the supplier (should be pooled and reused)
            using (var digestLease = MessageDigestSupplier.SHA256.Get())
            {
                Assert.IsNotNull(digestLease.Get());
            }
        }

        [TestMethod]
        [DisplayName("Test Direct Use Of MessageDigest")]
        public void TestDirectUseOfMessageDigest()
        {
            // Create test data
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            // Use the message digest - use Hash method directly
            byte[] hash1 = MessageDigestSupplier.SHA256.Hash(data);
            Assert.IsNotNull(hash1);

            // Use another message digest (should be reset and reused)
            byte[] hash2 = MessageDigestSupplier.SHA256.Hash(data);
            Assert.IsNotNull(hash2);

            // Both digests should produce the same hash for the same data
            CollectionAssert.AreEqual(hash1, hash2);
        }
    }
}