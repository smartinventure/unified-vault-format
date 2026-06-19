using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// RFC 3394 known-answer vectors for the AES Key Wrap primitive (audit finding F-04).
    ///
    /// The library wraps/unwraps masterkeys with a 256-bit KEK (A256KW), so the relevant
    /// published vector is RFC 3394 Section 4.6 ("Wrap 256 bits of Key Data with a 256-bit KEK").
    /// See https://www.rfc-editor.org/rfc/rfc3394#section-4.6
    ///
    /// These complement the existing round-trip tests in AesKeyWrapTest.cs by pinning the exact
    /// wrapped bytes against the standard.
    /// </summary>
    [TestClass]
    public class AesKeyWrapKatTest
    {
        private static byte[] Hex(string hex) => Convert.FromHexString(hex);

        [TestMethod]
        [DisplayName("RFC 3394 Section 4.6 - Wrap 256-bit key with 256-bit KEK")]
        public void Rfc3394_Section46_Wrap256BitKeyWith256BitKek_MatchesVector()
        {
            // RFC 3394 Section 4.6 test vector.
            byte[] kek = Hex("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");
            byte[] keyData = Hex("00112233445566778899AABBCCDDEEFF000102030405060708090A0B0C0D0E0F");
            byte[] expectedWrapped = Hex("28C9F404C4B810F4CBCCB35CFB87F8263F5786E2D80ED326CBC7F0E71A99F43BFB988B9B7A02DD21");

            // AesKeyWrap.Wrap(kek, keyToWrap) -> wrapped key (RFC 3394, BouncyCastle AesWrapEngine).
            byte[] wrapped = AesKeyWrap.Wrap(kek, keyData);

            CollectionAssert.AreEqual(expectedWrapped, wrapped,
                "RFC 3394 Section 4.6 wrapped output must match the published vector exactly.");
        }

        [TestMethod]
        [DisplayName("RFC 3394 Section 4.6 - Unwrap round-trips to original key")]
        public void Rfc3394_Section46_UnwrapVector_ReturnsOriginalKey()
        {
            // RFC 3394 Section 4.6 test vector (unwrap direction).
            byte[] kek = Hex("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");
            byte[] expectedKeyData = Hex("00112233445566778899AABBCCDDEEFF000102030405060708090A0B0C0D0E0F");
            byte[] wrapped = Hex("28C9F404C4B810F4CBCCB35CFB87F8263F5786E2D80ED326CBC7F0E71A99F43BFB988B9B7A02DD21");

            byte[] unwrapped = AesKeyWrap.Unwrap(kek, wrapped);

            CollectionAssert.AreEqual(expectedKeyData, unwrapped,
                "Unwrapping the RFC 3394 Section 4.6 ciphertext must recover the original key data.");
        }
    }
}
