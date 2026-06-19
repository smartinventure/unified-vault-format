using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Reflection;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// AES-SIV (RFC 5297) known-answer vector for the production S2V/SIV primitive (audit finding F-04).
    ///
    /// IMPLEMENTATION CONVENTION NOTE:
    /// UvfLib.Core.V3.AesSivHelper requires a 64-byte key and splits it 32/32, i.e. it is
    /// AES-256-SIV (a.k.a. AES-SIV-512). The two vectors in RFC 5297 Appendix A.1 and A.2 both
    /// use a 32-byte key (AES-128-SIV) and therefore CANNOT be fed to this implementation directly.
    ///
    /// We instead use the published "NIST SIV test vectors (256-bit subkeys #1)" deterministic
    /// AES-256-SIV vector from the cross-implementation Miscreant test suite
    /// (https://github.com/miscreant/miscreant.js/blob/master/vectors/aes_siv.tjson). That case has
    /// exactly ONE associated-data component, matching how AesSivHelper treats a non-null `ad`
    /// argument (a single S2V component). The implementation returns SIV || ciphertext, which is the
    /// same layout as the vector's expected output (RFC 5297 Section 2: the SIV is prepended).
    ///
    /// AesSivHelper is `internal` and the test assembly (UvfLib.Tests) is not in the
    /// InternalsVisibleTo list, so the production method is invoked via reflection. This exercises
    /// the real S2V/CTR code path rather than a re-implementation.
    /// </summary>
    [TestClass]
    public class AesSivKatTest
    {
        private static byte[] Hex(string hex) => Convert.FromHexString(hex);

        // NIST SIV test vectors (256-bit subkeys #1) - AES-256-SIV, single AD component.
        // Source: miscreant aes_siv.tjson (vectors/aes_siv.tjson).
        private const string KeyHex =
            "fffefdfcfbfaf9f8f7f6f5f4f3f2f1f06f6e6d6c6b6a69686766656463626160" +
            "f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff000102030405060708090a0b0c0d0e0f";
        private const string AdHex = "101112131415161718191a1b1c1d1e1f2021222324252627";
        private const string PlaintextHex = "112233445566778899aabbccddee";
        // Expected output is SIV (16 bytes) || ciphertext (14 bytes).
        private const string ExpectedOutputHex = "f125274c598065cfc26b0e71575029088b035217e380cac8919ee800c126";

        private static MethodInfo GetAesSivMethod(string name)
        {
            Type? type = typeof(UvfLib.Core.Common.AesKeyWrap).Assembly
                .GetType("UvfLib.Core.V3.AesSivHelper");
            Assert.IsNotNull(type, "Could not locate internal type UvfLib.Core.V3.AesSivHelper.");

            MethodInfo? method = type!.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(byte[]), typeof(byte[]), typeof(byte[]) },
                modifiers: null);
            Assert.IsNotNull(method, $"Could not locate AesSivHelper.{name}(byte[], byte[], byte[]).");
            return method!;
        }

        [TestMethod]
        [DisplayName("AES-256-SIV NIST vector #1 - Encrypt matches published output")]
        public void Aes256Siv_NistVector1_Encrypt_MatchesVector()
        {
            byte[] key = Hex(KeyHex);
            byte[] ad = Hex(AdHex);
            byte[] plaintext = Hex(PlaintextHex);
            byte[] expected = Hex(ExpectedOutputHex);

            MethodInfo encrypt = GetAesSivMethod("Encrypt");
            byte[] actual = (byte[])encrypt.Invoke(null, new object[] { key, plaintext, ad })!;

            CollectionAssert.AreEqual(expected, actual,
                "AES-256-SIV encryption (SIV || ciphertext) must match the published NIST/Miscreant vector.");
        }

        [TestMethod]
        [DisplayName("AES-256-SIV NIST vector #1 - Decrypt recovers plaintext")]
        public void Aes256Siv_NistVector1_Decrypt_RecoversPlaintext()
        {
            byte[] key = Hex(KeyHex);
            byte[] ad = Hex(AdHex);
            byte[] expectedPlaintext = Hex(PlaintextHex);
            byte[] sivAndCiphertext = Hex(ExpectedOutputHex);

            MethodInfo decrypt = GetAesSivMethod("Decrypt");
            byte[] actual = (byte[])decrypt.Invoke(null, new object[] { key, sivAndCiphertext, ad })!;

            CollectionAssert.AreEqual(expectedPlaintext, actual,
                "Decrypting the published vector ciphertext (with its SIV) must recover the plaintext.");
        }
    }
}
