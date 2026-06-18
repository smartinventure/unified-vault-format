using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace UvfLib.Core.Common
{
    /// <summary>
    /// EC (P-384) key material and ECDH-ES key agreement for public-key vault recipients.
    /// <para>
    /// Wrapping a vault CEK to a user's public key uses ECDH-ES+A256KW (RFC 7518 §4.6): an ephemeral
    /// EC key agrees a shared secret Z with the recipient's static key, a 256-bit key-encryption key
    /// (KEK) is derived from Z with the Concat KDF (NIST SP 800-56A; a single SHA-256 block suffices
    /// for a 256-bit key), and the CEK is AES-KeyWrapped (RFC 3394) under that KEK.
    /// </para>
    /// <para>
    /// Private keys are exported as password-encrypted PKCS#8 (AES-256-CBC); public keys as
    /// SubjectPublicKeyInfo. Both are standard, AOT-safe .NET crypto APIs.
    /// </para>
    /// </summary>
    public static class EcdhKeyMaterial
    {
        /// <summary>The curve used for user key pairs.</summary>
        public const string CurveName = "P-384";

        /// <summary>Generates a fresh P-384 key pair for a user.</summary>
        public static ECDiffieHellman GenerateKeyPair() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

        /// <summary>Exports the public key as SubjectPublicKeyInfo (DER).</summary>
        public static byte[] ExportPublicKey(ECDiffieHellman key) => key.ExportSubjectPublicKeyInfo();

        /// <summary>Imports a SubjectPublicKeyInfo (DER) public key. The returned instance must be disposed.</summary>
        public static ECDiffieHellman ImportPublicKey(byte[] subjectPublicKeyInfo)
        {
            var ecdh = ECDiffieHellman.Create();
            ecdh.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return ecdh;
        }

        /// <summary>Exports the private key as password-encrypted PKCS#8 (AES-256-CBC, PBKDF2-SHA256).</summary>
        public static byte[] ExportEncryptedPrivateKey(ECDiffieHellman key, char[] password)
        {
            var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 210_000);
            return key.ExportEncryptedPkcs8PrivateKey(password, pbe);
        }

        /// <summary>Imports a password-encrypted PKCS#8 private key. The returned instance must be disposed.</summary>
        public static ECDiffieHellman ImportEncryptedPrivateKey(byte[] encryptedPkcs8, char[] password)
        {
            var ecdh = ECDiffieHellman.Create();
            ecdh.ImportEncryptedPkcs8PrivateKey(password, encryptedPkcs8, out _);
            return ecdh;
        }

        /// <summary>
        /// Derives the 256-bit KEK for ECDH-ES+A256KW:
        /// SHA-256( 0x00000001 || Z || OtherInfo ), where
        /// OtherInfo = AlgorithmID("A256KW") || PartyUInfo() || PartyVInfo() || SuppPubInfo(256).
        /// Empty apu/apv. .NET's DeriveKeyFromHash computes hash(prepend || Z || append) over the
        /// agreed secret Z, which equals the single-block Concat KDF output.
        /// </summary>
        public static byte[] DeriveKek(ECDiffieHellman ourKey, ECDiffieHellmanPublicKey otherPublicKey)
        {
            byte[] prepend = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(prepend, 1u); // Concat KDF counter = 1
            byte[] otherInfo = BuildOtherInfo("A256KW", 256);
            return ourKey.DeriveKeyFromHash(otherPublicKey, HashAlgorithmName.SHA256, prepend, otherInfo);
        }

        private static byte[] BuildOtherInfo(string algorithmId, int keyDataLenBits)
        {
            using var ms = new MemoryStream();
            WriteLengthPrefixed(ms, Encoding.ASCII.GetBytes(algorithmId)); // AlgorithmID
            WriteLengthPrefixed(ms, Array.Empty<byte>());                  // PartyUInfo (apu) — empty
            WriteLengthPrefixed(ms, Array.Empty<byte>());                  // PartyVInfo (apv) — empty
            byte[] supp = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(supp, (uint)keyDataLenBits); // SuppPubInfo = keydatalen
            ms.Write(supp, 0, 4);
            return ms.ToArray();
        }

        private static void WriteLengthPrefixed(MemoryStream ms, byte[] data)
        {
            byte[] len = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            ms.Write(len, 0, 4);
            if (data.Length > 0) ms.Write(data, 0, data.Length);
        }
    }
}
