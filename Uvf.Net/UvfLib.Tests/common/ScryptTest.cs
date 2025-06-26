using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// Tests from https://tools.ietf.org/html/rfc7914#section-12
    /// </summary>
    [TestClass]
    public class ScryptTest
    {
        [TestMethod] // Re-enable this test
        [DisplayName("Test Empty String")]
        public void TestEmptyString()
        {
            // Generate key using Scrypt with empty strings
            byte[] key = Scrypt.ScryptDeriveBytes(Encoding.UTF8.GetBytes(""), Encoding.ASCII.GetBytes(""), 16, 1, 64);

            // Expected result from RFC 7914
            byte[] expected = new byte[] {
                0x77, 0xd6, 0x57, 0x62, 0x38, 0x65, 0x7b, 0x20,
                0x3b, 0x19, 0xca, 0x42, 0xc1, 0x8a, 0x04, 0x97,
                0xf1, 0x6b, 0x48, 0x44, 0xe3, 0x07, 0x4a, 0xe8,
                0xdf, 0xdf, 0xfa, 0x3f, 0xed, 0xe2, 0x14, 0x42,
                0xfc, 0xd0, 0x06, 0x9d, 0xed, 0x09, 0x48, 0xf8,
                0x32, 0x6a, 0x75, 0x3a, 0x0f, 0xc8, 0x1f, 0x17,
                0xe8, 0xd3, 0xe0, 0xfb, 0x2e, 0x0d, 0x36, 0x28,
                0xcf, 0x35, 0xe2, 0x0c, 0x38, 0xd1, 0x89, 0x06
            };

            CollectionAssert.AreEqual(expected, key);
        }

        [TestMethod]
        [DisplayName("Test Please Let Me In String")]
        public void TestPleaseLetMeInString()
        {
            // Generate key using Scrypt with test vector
            byte[] key = Scrypt.ScryptDeriveBytes(Encoding.UTF8.GetBytes("pleaseletmein"), Encoding.ASCII.GetBytes("SodiumChloride"), 16384, 8, 64);

            // Expected result from RFC 7914
            byte[] expected = new byte[] {
                0x70, 0x23, 0xbd, 0xcb, 0x3a, 0xfd, 0x73, 0x48,
                0x46, 0x1c, 0x06, 0xcd, 0x81, 0xfd, 0x38, 0xeb,
                0xfd, 0xa8, 0xfb, 0xba, 0x90, 0x4f, 0x8e, 0x3e,
                0xa9, 0xb5, 0x43, 0xf6, 0x54, 0x5d, 0xa1, 0xf2,
                0xd5, 0x43, 0x29, 0x55, 0x61, 0x3f, 0x0f, 0xcf,
                0x62, 0xd4, 0x97, 0x05, 0x24, 0x2a, 0x9a, 0xf9,
                0xe6, 0x1e, 0x85, 0xdc, 0x0d, 0x65, 0x1e, 0x40,
                0xdf, 0xcf, 0x01, 0x7b, 0x45, 0x57, 0x58, 0x87
            };

            CollectionAssert.AreEqual(expected, key);
        }

        [TestMethod] // Re-enable this test
        [DisplayName("Test Invalid Parameters")]
        public void TestInvalidParameters()
        {
            // Test with invalid cost parameter (not a power of 2)
            Assert.ThrowsException<ArgumentException>(() =>
                Scrypt.ScryptDeriveBytes(Encoding.UTF8.GetBytes("test"), Encoding.ASCII.GetBytes("salt"), 15, 1, 64));

            // Test with too small cost parameter
            Assert.ThrowsException<ArgumentException>(() =>
                Scrypt.ScryptDeriveBytes(Encoding.UTF8.GetBytes("test"), Encoding.ASCII.GetBytes("salt"), 0, 1, 64));

            // Test with too small block size
            Assert.ThrowsException<ArgumentException>(() =>
                Scrypt.ScryptDeriveBytes(Encoding.UTF8.GetBytes("test"), Encoding.ASCII.GetBytes("salt"), 16, 0, 64));

            // Test with invalid output length
            Assert.ThrowsException<ArgumentException>(() =>
                Scrypt.ScryptDeriveBytes(Encoding.UTF8.GetBytes("test"), Encoding.ASCII.GetBytes("salt"), 16, 1, 0));
        }
    }
}