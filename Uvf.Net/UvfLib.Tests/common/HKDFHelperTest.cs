using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class HKDFHelperTest
    {
        private static readonly Func<string, byte[]> HexDecode = hex =>
            Convert.FromHexString(hex);

        [TestMethod]
        [DisplayName("RFC 5869 Test Case 1")]
        public void TestCase1()
        {
            // https://datatracker.ietf.org/doc/html/rfc5869#appendix-A.1
            byte[] ikm = HexDecode("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
            byte[] salt = HexDecode("000102030405060708090a0b0c");
            byte[] info = HexDecode("f0f1f2f3f4f5f6f7f8f9");

            byte[] result = HKDFHelper.HkdfSha256(salt, ikm, info, 42);

            byte[] expectedOkm = HexDecode("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865");
            CollectionAssert.AreEqual(expectedOkm, result);
        }

        [TestMethod]
        [DisplayName("RFC 5869 Test Case 2")]
        public void TestCase2()
        {
            // https://datatracker.ietf.org/doc/html/rfc5869#appendix-A.2
            byte[] ikm = HexDecode("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f404142434445464748494a4b4c4d4e4f");
            byte[] salt = HexDecode("606162636465666768696a6b6c6d6e6f707172737475767778797a7b7c7d7e7f808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9fa0a1a2a3a4a5a6a7a8a9aaabacadaeaf");
            byte[] info = HexDecode("b0b1b2b3b4b5b6b7b8b9babbbcbdbebfc0c1c2c3c4c5c6c7c8c9cacbcccdcecfd0d1d2d3d4d5d6d7d8d9dadbdcdddedfe0e1e2e3e4e5e6e7e8e9eaebecedeeeff0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");

            byte[] result = HKDFHelper.HkdfSha256(salt, ikm, info, 82);

            byte[] expectedOkm = HexDecode("b11e398dc80327a1c8e7f78c596a49344f012eda2d4efad8a050cc4c19afa97c59045a99cac7827271cb41c65e590e09da3275600c2f09b8367793a9aca3db71cc30c58179ec3e87c14c01d5c1f3434f1d87");
            CollectionAssert.AreEqual(expectedOkm, result);
        }

        [TestMethod]
        [DisplayName("RFC 5869 Test Case 3")]
        public void TestCase3()
        {
            // https://datatracker.ietf.org/doc/html/rfc5869#appendix-A.3
            byte[] ikm = HexDecode("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
            byte[] salt = Array.Empty<byte>();
            byte[] info = Array.Empty<byte>();

            byte[] result = HKDFHelper.HkdfSha256(salt, ikm, info, 42);

            byte[] expectedOkm = HexDecode("8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8");
            CollectionAssert.AreEqual(expectedOkm, result);
        }

        [TestMethod]
        [DisplayName("Inofficial SHA-512 Test")]
        public void Sha512Test()
        {
            // https://github.com/patrickfav/hkdf/blob/60152fff852506a1b46f730b14d1b8f8ff69d071/src/test/java/at/favre/lib/hkdf/RFC5869TestCases.java#L116-L124
            byte[] ikm = HexDecode("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b");
            byte[] salt = HexDecode("000102030405060708090a0b0c");
            byte[] info = HexDecode("f0f1f2f3f4f5f6f7f8f9");

            byte[] result = HKDFHelper.HkdfSha512(salt, ikm, info, 42);

            byte[] expectedOkm = HexDecode("832390086CDA71FB47625BB5CEB168E4C8E26A1A16ED34D9FC7FE92C1481579338DA362CB8D9F925D7CB");
            CollectionAssert.AreEqual(expectedOkm, result);
        }
    }
}