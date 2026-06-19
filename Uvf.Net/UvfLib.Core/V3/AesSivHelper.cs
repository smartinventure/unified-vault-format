// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

using System;
using System.Security.Cryptography;
using UvfLib.Core.Api; // Add this for InvalidCiphertextException
using System.Diagnostics; // Added for Debug.WriteLine
using System.Linq; // Added for LINQ
using System.Text; // Added for Encoding
// BouncyCastle Imports
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;

namespace UvfLib.Core.V3
{
    /// <summary>
    /// Helper methods for AES-SIV encryption and decryption with compatibility with the Java implementation
    /// </summary>
    internal static class AesSivHelper
    {
        // AES-SIV block size (16 bytes)
        private const int BLOCK_SIZE = 16;

        /// <summary>
        /// Encrypts data using AES-SIV, implementing RFC5297
        /// </summary>
        /// <param name="key">The encryption key (must be 64 bytes for AES-SIV-512)</param>
        /// <param name="plaintext">The plaintext to encrypt</param>
        /// <param name="ad">The associated data</param>
        /// <returns>The ciphertext (with SIV/Tag prepended)</returns>
        public static byte[] Encrypt(byte[] key, byte[] plaintext, byte[] ad)
        {

            if (key == null || key.Length != 64)
            {
                throw new ArgumentException("Key must be 64 bytes for AES-SIV-512", nameof(key));
            }

            if (plaintext == null)
            {
                throw new ArgumentNullException(nameof(plaintext));
            }

            // Split the key into two halves (K1 for CMAC, K2 for CTR encryption)
            byte[] k1 = new byte[32];
            byte[] k2 = new byte[32];
            Buffer.BlockCopy(key, 0, k1, 0, 32);
            Buffer.BlockCopy(key, 32, k2, 0, 32);


            // Step 1: Generate the SIV (Synthetic Initialization Vector) using S2V operation.
            // 'ad' is THE single associated-data field: null => no field (zero S2V components, as used by
            // directory-id hashing), non-null => exactly ONE component — even when empty. An empty but
            // present field (e.g. the Cryptomator root directory id "") is a single empty component,
            // matching the reference siv-mode; collapsing it to "no AD" changes the SIV and makes our
            // vaults incompatible with real Cryptomator vaults.
            byte[] siv = S2V(k1, plaintext, ad != null ? new[] { ad } : Array.Empty<byte[]>());

            // Step 2: Encrypt the plaintext using AES-CTR with modified SIV as counter
            byte[] modifiedSiv = new byte[BLOCK_SIZE];
            Buffer.BlockCopy(siv, 0, modifiedSiv, 0, BLOCK_SIZE);
            // Clear the most significant bit in the last 32 bits (per RFC5297)
            modifiedSiv[8] &= 0x7F;
            modifiedSiv[12] &= 0x7F;

            // Encrypt the plaintext with AES-CTR
            byte[] ciphertext = EncryptWithCtr(k2, modifiedSiv, plaintext);

            // Combine SIV and ciphertext
            byte[] result = new byte[siv.Length + ciphertext.Length];
            Buffer.BlockCopy(siv, 0, result, 0, siv.Length);
            Buffer.BlockCopy(ciphertext, 0, result, siv.Length, ciphertext.Length);

            return result;
        }

        /// <summary>
        /// Generate Synthetic Initialization Vector using S2V operation as defined in RFC 5297
        /// This implementation matches the Java version in org.cryptomator.siv.SivMode
        /// </summary>
        private static byte[] S2V(byte[] key, byte[] plaintext, byte[][] associatedData)
        {
#if DEBUG_VERBOSE_AES_SIV
            for (int i = 0; i < associatedData.Length; i++)
            {
                Debug.WriteLine($"    [AesSivHelper.S2V] AD[{i}]: {(associatedData[i] == null ? "null" : Convert.ToBase64String(associatedData[i]))}");
            }
            if (plaintext != null) Debug.WriteLine($"    [AesSivHelper.S2V] Plaintext: {Convert.ToBase64String(plaintext)}");
#endif
            if (associatedData.Length > 126)
            {
                throw new ArgumentException("too many Associated Data fields");
            }

            // Use BouncyCastle AES Engine and CMac
            AesEngine aesEngine = new AesEngine();
            CMac cmac = new CMac(aesEngine);
            KeyParameter keyParam = new KeyParameter(key);
            cmac.Init(keyParam);

            // D = AES-CMAC(K1, <zero>)
            byte[] zero = new byte[BLOCK_SIZE];
            byte[] d = MacWithBouncyCastle(cmac, zero);

            // Process associated data if present
            foreach (byte[] s in associatedData)
            {
                if (s != null)  // Process all non-null arrays, including empty ones
                {
                    byte[] adMac = MacWithBouncyCastle(cmac, s);
                    byte[] doubledD = Dbl(d);
                    d = Xor(doubledD, adMac); // XOR requires same length, Mac always returns BLOCK_SIZE
                }
            }

            // Process plaintext
            byte[] t;
            if (plaintext.Length >= BLOCK_SIZE)
            {
                t = XorEnd(plaintext, d);
            }
            else
            {
                byte[] paddedPlaintext = PadWithBouncyCastle(plaintext);
                byte[] doubledD = Dbl(d);
                t = Xor(doubledD, paddedPlaintext); // XOR requires same length
            }

            //byte[] finalMac = ComputeAesCmac(key, t); // Old manual CMAC
            byte[] finalMac = MacWithBouncyCastle(cmac, t);
            return finalMac;
        }

        /// <summary>
        /// Performs AES-CMAC using BouncyCastle
        /// </summary>
        private static byte[] MacWithBouncyCastle(IMac mac, byte[] input)
        {
            mac.Reset(); // Reset MAC for new input
            mac.BlockUpdate(input, 0, input.Length);
            byte[] result = new byte[mac.GetMacSize()];
            mac.DoFinal(result, 0);
            return result;
        }

        /// <summary>
        /// Pads the input according to ISO7816-4 using BouncyCastle
        /// </summary>
        private static byte[] PadWithBouncyCastle(byte[] input)
        {
            if (input.Length >= BLOCK_SIZE) // Should not happen if called correctly from S2V
            {
                throw new ArgumentException("Input length must be less than block size for padding.");
            }
            byte[] padded = new byte[BLOCK_SIZE];
            Buffer.BlockCopy(input, 0, padded, 0, input.Length);
            IBlockCipherPadding padding = new ISO7816d4Padding();
            padding.AddPadding(padded, input.Length);
            return padded;
        }

        /// <summary>
        /// Doubles a value (left shift by 1) with conditional XOR if high bit is set
        /// Equivalent to dbl() in Java implementation
        /// </summary>
        private static byte[] Dbl(byte[] input)
        {
            byte[] ret = new byte[input.Length];
            int carry = ShiftLeft(input, ret);
            int xor = 0xff & 0x87;  // DOUBLING_CONST in Java

            // This construction is an attempt at a constant-time implementation
            int mask = (-carry) & 0xff;
            ret[input.Length - 1] ^= (byte)(xor & mask);

            return ret;
        }

        /// <summary>
        /// Shifts left by one bit - equivalent to shiftLeft() in Java implementation
        /// </summary>
        private static int ShiftLeft(byte[] block, byte[] output)
        {
            int i = block.Length;
            int bit = 0;
            while (--i >= 0)
            {
                int b = block[i] & 0xff;
                output[i] = (byte)((b << 1) | bit);
                bit = (b >> 7) & 1;
            }
            return bit;
        }

        /// <summary>
        /// XOR two byte arrays - equivalent to xor() in Java implementation
        /// </summary>
        private static byte[] Xor(byte[] in1, byte[] in2)
        {
            // Ensure arrays are the same length
            if (in1.Length != in2.Length)
            {
                throw new ArgumentException("Arrays must be same length for XOR operation");
            }

            byte[] result = new byte[in1.Length];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = (byte)(in1[i] ^ in2[i]);
            }
            return result;
        }

        /// <summary>
        /// XOR at the end of array - equivalent to xorend() in Java implementation
        /// </summary>
        private static byte[] XorEnd(byte[] in1, byte[] in2)
        {
            if (in1.Length < in2.Length)
            {
                throw new ArgumentException("Length of first input must be >= length of second input");
            }

            byte[] result = new byte[in1.Length];
            Buffer.BlockCopy(in1, 0, result, 0, in1.Length);

            int diff = in1.Length - in2.Length;
            for (int i = 0; i < in2.Length; i++)
            {
                result[i + diff] = (byte)(result[i + diff] ^ in2[i]);
            }
            return result;
        }

        /// <summary>
        /// Encrypt using AES-CTR mode
        /// </summary>
        private static byte[] EncryptWithCtr(byte[] key, byte[] counter, byte[] plaintext)
        {
            byte[] ciphertext = new byte[plaintext.Length];

            // Could potentially use BC CTR mode here too, but standard AES should be fine
            using Aes aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;

            byte[] counterBlock = new byte[BLOCK_SIZE];
            Buffer.BlockCopy(counter, 0, counterBlock, 0, BLOCK_SIZE);

            using ICryptoTransform encryptor = aes.CreateEncryptor();

            for (int i = 0; i < plaintext.Length; i += BLOCK_SIZE)
            {
                // Encrypt the counter
                byte[] encryptedCounter = new byte[BLOCK_SIZE];
                encryptor.TransformBlock(counterBlock, 0, BLOCK_SIZE, encryptedCounter, 0);

                // XOR with plaintext to get ciphertext
                int bytesToProcess = Math.Min(BLOCK_SIZE, plaintext.Length - i);
                for (int j = 0; j < bytesToProcess; j++)
                {
                    ciphertext[i + j] = (byte)(plaintext[i + j] ^ encryptedCounter[j]);
                }

                // Increment counter
                IncrementCounter(counterBlock);
            }

            return ciphertext;
        }

        /// <summary>
        /// Increment the counter (big-endian)
        /// </summary>
        private static void IncrementCounter(byte[] counter)
        {
            for (int i = counter.Length - 1; i >= 0; i--)
            {
                if (++counter[i] != 0)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Decrypts data using AES-SIV
        /// </summary>
        /// <param name="key">The decryption key (must be 64 bytes for AES-SIV-512)</param>
        /// <param name="ciphertext">The ciphertext to decrypt (with SIV/Tag prepended)</param>
        /// <param name="ad">The associated data</param>
        /// <returns>The decrypted plaintext</returns>
        /// <exception cref="AuthenticationFailedException">If authentication fails</exception>
        public static byte[] Decrypt(byte[] key, byte[] ciphertext, byte[] ad)
        {
            if (key == null || key.Length != 64)
            {
                throw new ArgumentException("Key must be 64 bytes for AES-SIV-512", nameof(key));
            }

            if (ciphertext == null || ciphertext.Length < BLOCK_SIZE)
            {
                throw new ArgumentException("Ciphertext must be at least block size (16 bytes)", nameof(ciphertext));
            }

            // Split the key into two halves
            byte[] k1 = new byte[32];
            byte[] k2 = new byte[32];
            Buffer.BlockCopy(key, 0, k1, 0, 32);
            Buffer.BlockCopy(key, 32, k2, 0, 32);

            // Extract SIV (first 16 bytes) and the actual ciphertext
            byte[] siv = new byte[BLOCK_SIZE];
            byte[] actualCiphertext = new byte[ciphertext.Length - BLOCK_SIZE];
            Buffer.BlockCopy(ciphertext, 0, siv, 0, BLOCK_SIZE);
            Buffer.BlockCopy(ciphertext, BLOCK_SIZE, actualCiphertext, 0, actualCiphertext.Length);


            // Step 1: Decrypt the ciphertext using AES-CTR with modified SIV as counter
            byte[] modifiedSiv = new byte[BLOCK_SIZE];
            Buffer.BlockCopy(siv, 0, modifiedSiv, 0, BLOCK_SIZE);
            // Clear the most significant bit in the last 32 bits
            modifiedSiv[8] &= 0x7F;
            modifiedSiv[12] &= 0x7F;
            byte[] plaintext = EncryptWithCtr(k2, modifiedSiv, actualCiphertext); // CTR encryption is its own inverse

            // Step 2: Recalculate SIV using S2V on the decrypted plaintext and associated data.
            // Mirror the encrypt side: null => zero components, non-null (even empty) => one component.
            byte[] calculatedSiv = S2V(k1, plaintext, ad != null ? new[] { ad } : Array.Empty<byte[]>());

            // Step 3: Compare the original SIV with the recalculated SIV
            if (!CryptographicOperations.FixedTimeEquals(siv, calculatedSiv))
            {
                throw new AuthenticationFailedException("Authentication failed");
            }

            return plaintext;
        }
    }
}
