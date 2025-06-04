using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography;
using UvfLib._old.common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class AesKeyWrapTest
    {
        [TestMethod]
        [DisplayName("Wrap And Unwrap")]
        public void WrapAndUnwrap()
        {
            // Create a key encryption key and a key to wrap
            byte[] kek = new byte[32];
            byte[] keyToWrap = new byte[32];

            // Wrap the key
            byte[] wrapped = AesKeyWrap.Wrap(kek, keyToWrap);

            // Unwrap the key
            byte[] unwrapped = AesKeyWrap.Unwrap(kek, wrapped);

            // Verify the unwrapped key matches the original
            CollectionAssert.AreEqual(keyToWrap, unwrapped);
        }

        [TestMethod]
        [DisplayName("Wrap With Invalid Key")]
        public void WrapWithInvalidKey()
        {
            // Create a key encryption key and an invalid key to wrap (not multiple of 8 bytes)
            byte[] kek = new byte[32];
            byte[] invalidKey = new byte[17]; // Not a multiple of 8

            // Attempt to wrap should throw ArgumentException
            Assert.ThrowsException<ArgumentException>(() =>
            {
                AesKeyWrap.Wrap(kek, invalidKey);
            });
        }

        [TestMethod]
        [DisplayName("Unwrap With Invalid Data")]
        public void UnwrapWithInvalidData()
        {
            // Create a key encryption key and a key to wrap
            byte[] kek = new byte[32];
            byte[] keyToWrap = new byte[32];

            // Wrap the key
            byte[] wrapped = AesKeyWrap.Wrap(kek, keyToWrap);

            // Modify the wrapped key to make it invalid
            wrapped[0] ^= 0xFF; // Flip bits in the first byte

            // Attempt to unwrap should throw CryptographicException due to integrity check failure
            Assert.ThrowsException<CryptographicException>(() =>
            {
                AesKeyWrap.Unwrap(kek, wrapped);
            });
        }
    }
}