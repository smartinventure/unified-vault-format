using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class MacSupplierTest
    {
        [TestMethod]
        [DisplayName("Test Constructor With Invalid MAC Algorithm")]
        public void TestConstructorWithInvalidMac()
        {
            // Test creating a MacSupplier with an invalid algorithm name
            Assert.ThrowsException<ArgumentException>(() =>
                new MacSupplier("FOO3000"));
        }

        [TestMethod]
        [DisplayName("Test Get MAC")]
        public void TestGetMac()
        {
            // Create a key for HMAC
            byte[] keyData = new byte[16]; // 128-bit key
            var key = new DestroyableSecretKey(keyData, "HMAC");

            // Get a MAC from the supplier
            using (var mac1 = MacSupplier.HMAC_SHA256.CreateMac(keyData))
            {
                Assert.IsNotNull(mac1);
            }

            // Get another MAC from the supplier (should be pooled and reused)
            using (var mac2 = MacSupplier.HMAC_SHA256.CreateMac(keyData))
            {
                Assert.IsNotNull(mac2);
            }
        }

        [TestMethod]
        [DisplayName("Test Get MAC With ObjectPool Lease")]
        public void TestGetMacWithLease()
        {
            // Create a key for HMAC
            byte[] keyData = new byte[16]; // 128-bit key

            // Get a MAC from the supplier
            using (var mac1 = MacSupplier.HMAC_SHA256.CreateMac(keyData))
            {
                Assert.IsNotNull(mac1);
            }

            // Get another MAC from the supplier (should be pooled and reused)
            using (var mac2 = MacSupplier.HMAC_SHA256.CreateMac(keyData))
            {
                Assert.IsNotNull(mac2);
            }
        }

        [TestMethod]
        [DisplayName("Test Get MAC With Invalid Key")]
        public void TestGetMacWithInvalidKey()
        {
            // In C#, we would test with an inappropriate key type or invalid key data
            // For example, a null key or too short key
            Assert.ThrowsException<ArgumentNullException>(() =>
                MacSupplier.HMAC_SHA256.CreateMac(null));

            // Or an empty key
            Assert.ThrowsException<ArgumentException>(() =>
                MacSupplier.HMAC_SHA256.CreateMac(Array.Empty<byte>()));
        }
    }
}