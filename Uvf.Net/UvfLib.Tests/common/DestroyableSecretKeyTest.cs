using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography;
using System.Linq;
using Moq;
using UvfLib._old.common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class DestroyableSecretKeyTest
    {
        private byte[] GetKeyBytesDirect(DestroyableSecretKey key)
        {
            var field = typeof(DestroyableSecretKey).GetField("_keyMaterial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (byte[]?)field?.GetValue(key) ?? Array.Empty<byte>();
        }

        [TestMethod]
        [DisplayName("Test Create Secret Key")]
        public void TestCreateSecretKey()
        {
            byte[] keyBytes = new byte[] { 1, 2, 3, 4, 5 };
            string algorithm = "TEST";

            using var key = new DestroyableSecretKey(keyBytes, algorithm);

            Assert.AreEqual(algorithm, key.Algorithm);
            CollectionAssert.AreEqual(keyBytes, key.GetEncoded());
        }

        [TestMethod]
        [DisplayName("Test Destroy")]
        public void TestDestroy()
        {
            byte[] keyBytes = new byte[] { 1, 2, 3, 4, 5 };
            var key = new DestroyableSecretKey(keyBytes, "TEST");

            byte[] keyBytesCopy = new byte[keyBytes.Length];
            Array.Copy(keyBytes, keyBytesCopy, keyBytes.Length);

            key.Destroy();

            byte[] destroyedKeyBytes = GetKeyBytesDirect(key);
            Assert.IsTrue(destroyedKeyBytes.All(b => b == 0), "Internal key material should be zeroed.");

            CollectionAssert.AreEqual(keyBytesCopy, keyBytes);

            Assert.ThrowsException<InvalidOperationException>(() => key.GetEncoded());

            key.Dispose();
        }

        [TestMethod]
        [DisplayName("Test IsDestroyed")]
        public void TestIsDestroyed()
        {
            byte[] keyBytes = new byte[] { 1, 2, 3, 4, 5 };
            using var key = new DestroyableSecretKey(keyBytes, "TEST");

            Assert.IsFalse(key.IsDestroyed);

            key.Destroy();

            Assert.IsTrue(key.IsDestroyed);
        }

        [TestMethod]
        [DisplayName("Test Dispose")]
        public void TestDispose()
        {
            byte[] keyBytes = new byte[] { 1, 2, 3, 4, 5 };
            var key = new DestroyableSecretKey(keyBytes, "TEST");

            key.Dispose();

            Assert.IsTrue(key.IsDestroyed);
            Assert.ThrowsException<InvalidOperationException>(() => key.GetEncoded());
            byte[] destroyedKeyBytes = GetKeyBytesDirect(key);
            Assert.IsTrue(destroyedKeyBytes.All(b => b == 0), "Internal key material should be zeroed after Dispose.");
        }

        [TestMethod]
        [DisplayName("Constructor Fails For Null Key")]
        public void TestConstructorFailsForNullKey()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new DestroyableSecretKey(null, "TEST"));
        }

        [TestMethod]
        [DisplayName("Constructor Fails For Null Algorithm")]
        public void TestConstructorFailsForNullAlgorithm()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                new DestroyableSecretKey(new byte[16], null));
        }

        [TestMethod]
        [DisplayName("Constructor Fails For Invalid Length")]
        public void TestConstructorFailsForInvalidLength()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                new DestroyableSecretKey(new byte[16], 0, -1, "TEST"));
        }

        [TestMethod]
        [DisplayName("Constructor Fails For Invalid Offset")]
        public void TestConstructorFailsForInvalidOffset()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                new DestroyableSecretKey(new byte[16], -1, 16, "TEST"));
        }

        [TestMethod]
        [DisplayName("Constructor Fails For Invalid Length And Offset")]
        public void TestConstructorFailsForInvalidLengthAndOffset()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                new DestroyableSecretKey(new byte[16], 8, 16, "TEST"));
        }

        [TestMethod]
        [DisplayName("Constructor Creates Local Copy")]
        public void TestConstructorCreatesLocalCopy()
        {
            byte[] orig = new byte[] { 1, 2, 3, 4, 5 };

            using DestroyableSecretKey key = new DestroyableSecretKey(orig, "TEST");

            byte[] origBeforeClear = (byte[])orig.Clone();
            Array.Clear(orig, 0, orig.Length);

            byte[] keyEncoded = key.GetEncoded();
            CollectionAssert.AreNotEqual(orig, keyEncoded, "Internal key should differ from cleared original array");
            CollectionAssert.AreEqual(origBeforeClear, keyEncoded, "Internal key should match original before clear");
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(keyEncoded);
        }

        [TestClass]
        public class UndestroyedKeyTests
        {
            private byte[] _rawKey;
            private DestroyableSecretKey _key;

            [TestInitialize]
            public void Setup()
            {
                _rawKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
                _key = new DestroyableSecretKey(_rawKey, "EXAMPLE");
            }

            [TestCleanup]
            public void Cleanup()
            {
                _key?.Dispose();
            }

            [TestMethod]
            [DisplayName("IsDestroyed Returns False For Undestroyed Key")]
            public void TestIsDestroyed()
            {
                Assert.IsFalse(_key.IsDestroyed);
            }

            [TestMethod]
            [DisplayName("Algorithm Property Returns Algorithm Name")]
            public void TestAlgorithm()
            {
                Assert.AreEqual("EXAMPLE", _key.Algorithm);
            }

            [TestMethod]
            [DisplayName("GetEncoded Returns Raw Key Copy")]
            public void TestGetEncoded()
            {
                byte[] encoded = _key.GetEncoded();
                CollectionAssert.AreEqual(_rawKey, encoded);
                Assert.AreNotSame(_rawKey, encoded, "GetEncoded should return a copy");
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded);
            }

            [TestMethod]
            [DisplayName("Dispose Destroys Key")]
            public void TestDispose()
            {
                _key.Dispose();

                Assert.IsTrue(_key.IsDestroyed);
            }
        }

        [TestClass]
        public class DestroyedKeyTests
        {
            private DestroyableSecretKey _key;

            [TestInitialize]
            public void Setup()
            {
                byte[] keyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
                _key = new DestroyableSecretKey(keyBytes, "EXAMPLE");
                _key.Destroy();
            }

            [TestCleanup]
            public void Cleanup()
            {
                _key?.Dispose();
            }

            [TestMethod]
            [DisplayName("IsDestroyed Returns True For Destroyed Key")]
            public void TestIsDestroyed()
            {
                Assert.IsTrue(_key.IsDestroyed);
            }

            [TestMethod]
            [DisplayName("Algorithm Property Throws For Destroyed Key")]
            public void TestAlgorithm()
            {
                Assert.ThrowsException<InvalidOperationException>(() => _ = _key.Algorithm);
            }

            [TestMethod]
            [DisplayName("GetEncoded Throws For Destroyed Key")]
            public void TestGetEncoded()
            {
                Assert.ThrowsException<InvalidOperationException>(() => _key.GetEncoded());
            }
        }
    }
}