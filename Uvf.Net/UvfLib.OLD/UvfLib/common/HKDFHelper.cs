using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace UvfLib.Common
{
    /// <summary>
    /// Implementation of HMAC-based Extract-and-Expand Key Derivation Function (HKDF) as specified in RFC 5869.
    /// </summary>
    public static class HKDFHelper
    {
        /// <summary>
        /// Extracts pseudorandom key from input keying material.
        /// </summary>
        /// <param name="salt">Salt value (a non-secret random value)</param>
        /// <param name="ikm">Input keying material</param>
        /// <returns>A pseudorandom key (of HashLen octets)</returns>
        public static byte[] Extract(byte[]? salt, byte[] ikm)
        {
            if (ikm == null)
            {
                throw new ArgumentNullException(nameof(ikm));
            }

            // If salt is not provided, use a string of HashLen zeros
            byte[] usedSalt = salt ?? new byte[32]; // SHA-256 output length

            using (var hmac = new HMACSHA256(usedSalt))
            {
                return hmac.ComputeHash(ikm);
            }
        }

        /// <summary>
        /// Expands a pseudorandom key to desired length.
        /// </summary>
        /// <param name="prk">Pseudorandom key</param>
        /// <param name="info">Optional context and application specific information</param>
        /// <param name="length">Length of output keying material in octets</param>
        /// <returns>Output keying material</returns>
        public static byte[] Expand(byte[] prk, byte[]? info, int length)
        {
            if (prk == null)
            {
                throw new ArgumentNullException(nameof(prk));
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative");
            }

            // Check if the output length is too large
            // For SHA-256, HashLen = 32, so max length is 32 * 255 = 8160
            if (length > 255 * 32)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Length too large");
            }

            // If no context info is provided, use an empty array
            byte[] usedInfo = info ?? Array.Empty<byte>();

            using (var hmac = new HMACSHA256(prk))
            {
                byte[] result = new byte[length];
                byte[] t = Array.Empty<byte>();
                byte[] tInput;
                int offset = 0;

                // Number of iterations needed to generate the required length
                int iterations = (length + 31) / 32; // Ceiling division by 32

                for (int i = 1; i <= iterations; i++)
                {
                    // T(i) = HMAC-Hash(PRK, T(i-1) | info | i)
                    tInput = new byte[t.Length + usedInfo.Length + 1];
                    if (t.Length > 0)
                    {
                        Buffer.BlockCopy(t, 0, tInput, 0, t.Length);
                    }
                    Buffer.BlockCopy(usedInfo, 0, tInput, t.Length, usedInfo.Length);
                    tInput[tInput.Length - 1] = (byte)i;

                    t = hmac.ComputeHash(tInput);

                    // Copy to result
                    int copyLen = Math.Min(length - offset, t.Length);
                    Buffer.BlockCopy(t, 0, result, offset, copyLen);
                    offset += copyLen;
                }

                return result;
            }
        }

        /// <summary>
        /// Derives a key using HKDF with SHA-256.
        /// </summary>
        /// <param name="key">The source key material</param>
        /// <param name="salt">The salt (optional)</param>
        /// <param name="info">Context and application specific information (optional)</param>
        /// <param name="length">Length of the derived key in bytes</param>
        /// <returns>The derived key</returns>
        public static byte[] DeriveKey(byte[] key, byte[]? salt, byte[]? info, int length)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            byte[] prk = Extract(salt, key);
            try
            {
                return Expand(prk, info, length);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(prk);
            }
        }

        /// <summary>
        /// Derives a key using HKDF with SHA-256.
        /// </summary>
        /// <param name="salt">Optional salt</param>
        /// <param name="ikm">Input key material</param>
        /// <param name="info">Optional context info</param>
        /// <param name="length">Length of derived key in bytes</param>
        /// <returns>Derived key</returns>
        public static byte[] HkdfSha256(byte[]? salt, byte[] ikm, byte[]? info, int length)
        {
            if (ikm == null)
                throw new ArgumentNullException(nameof(ikm));

            // Use SHA-256 for extraction
            byte[] usedSalt = salt ?? Array.Empty<byte>();
            byte[] prk;

            using (var hmac = new HMACSHA256(usedSalt))
            {
                prk = hmac.ComputeHash(ikm);
            }

            try
            {
                // Use SHA-256 for expansion
                using (var hmac = new HMACSHA256(prk))
                {
                    byte[] result = new byte[length];
                    byte[] t = Array.Empty<byte>();
                    byte[] tInput;
                    int offset = 0;

                    // Use context info if provided
                    byte[] usedInfo = info ?? Array.Empty<byte>();

                    for (int i = 1; offset < length; i++)
                    {
                        // T(i) = HMAC-Hash(PRK, T(i-1) | info | i)
                        tInput = new byte[t.Length + usedInfo.Length + 1];
                        if (t.Length > 0)
                        {
                            Buffer.BlockCopy(t, 0, tInput, 0, t.Length);
                        }
                        Buffer.BlockCopy(usedInfo, 0, tInput, t.Length, usedInfo.Length);
                        tInput[tInput.Length - 1] = (byte)i;

                        t = hmac.ComputeHash(tInput);

                        // Copy to result
                        int copyLen = Math.Min(length - offset, t.Length);
                        Buffer.BlockCopy(t, 0, result, offset, copyLen);
                        offset += copyLen;
                    }

                    return result;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(prk);
            }
        }

        /// <summary>
        /// Derives a key using HKDF with SHA-512.
        /// </summary>
        /// <param name="salt">Optional salt</param>
        /// <param name="ikm">Input key material</param>
        /// <param name="info">Optional context info</param>
        /// <param name="length">Length of derived key in bytes</param>
        /// <returns>Derived key</returns>
        public static byte[] HkdfSha512(byte[]? salt, byte[] ikm, byte[]? info, int length)
        {
            if (ikm == null)
                throw new ArgumentNullException(nameof(ikm));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative");

            byte[] result = new byte[length];
            IDigest digest = new Sha512Digest();
            var hkdf = new HkdfBytesGenerator(digest);

            // Use empty arrays if salt or info are null, matching Bouncy Castle Java behavior
            byte[] usedSalt = salt ?? Array.Empty<byte>();
            byte[] usedInfo = info ?? Array.Empty<byte>();

            var parameters = new HkdfParameters(ikm, usedSalt, usedInfo);
            hkdf.Init(parameters);
            hkdf.GenerateBytes(result, 0, length);

            return result;
        }

        /// <summary>
        /// Simple version for directly supplying ikm, output buffer, and info.
        /// </summary>
        public static void HkdfSha256(byte[]? salt, byte[] ikm, byte[] output, byte[] info)
        {
            if (ikm == null)
                throw new ArgumentNullException(nameof(ikm));
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            byte[] derived = HkdfSha256(salt, ikm, info, output.Length);
            Buffer.BlockCopy(derived, 0, output, 0, output.Length);
            CryptographicOperations.ZeroMemory(derived);
        }
    }
}