// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

using System;
using System.Security.Cryptography;
using System.Collections.Generic; // Added for HashSet

namespace UvfLib.Core.Common
{
    /// <summary>
    /// Supplier for cryptographic ciphers.
    /// </summary>
    public sealed class CipherSupplier : IDisposable
    {
        // Initialize the set *before* static fields that use the constructor
        private static readonly HashSet<string> SupportedAlgorithms = new HashSet<string> { "AES-CBC", "AES-CTR", "AES-GCM" };

        /// <summary>
        /// AES in CBC mode
        /// </summary>
        public static readonly CipherSupplier AES_CBC = new CipherSupplier("AES-CBC");

        /// <summary>
        /// AES in CTR mode
        /// </summary>
        public static readonly CipherSupplier AES_CTR = new CipherSupplier("AES-CTR");

        /// <summary>
        /// AES in GCM mode
        /// </summary>
        public static readonly CipherSupplier AES_GCM = new CipherSupplier("AES-GCM");

        private readonly string _algorithm;

        /// <summary>
        /// Initializes a new instance of the <see cref="CipherSupplier"/> class.
        /// </summary>
        /// <param name="algorithm">The algorithm identifier.</param>
        public CipherSupplier(string algorithm)
        {
            if (!SupportedAlgorithms.Contains(algorithm))
            {
                throw new ArgumentException($"Unsupported or unknown algorithm: {algorithm}", nameof(algorithm));
            }
            _algorithm = algorithm;
        }

        /// <summary>
        /// Leases a reusable cipher object initialized for encryption.
        /// </summary>
        /// <param name="key">Encryption key</param>
        /// <param name="iv">IV/Nonce</param>
        /// <returns>A crypto transform for encryption</returns>
        public ICryptoTransform EncryptionCipher(DestroyableSecretKey key, byte[] iv)
        {
            if (key == null || key.IsDestroyed)
            {
                throw new ArgumentException("Key must be valid and not destroyed", nameof(key));
            }

            byte[] keyBytes = key.GetEncoded();
            return EncryptionCipher(keyBytes, iv);
        }

        /// <summary>
        /// Leases a reusable cipher object initialized for encryption.
        /// </summary>
        /// <param name="key">Encryption key</param>
        /// <param name="iv">IV/Nonce</param>
        /// <returns>A crypto transform for encryption</returns>
        public ICryptoTransform EncryptionCipher(byte[] key, byte[] iv)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            // IV can be null for AES-WRAP
            // if (iv == null)
            //     throw new ArgumentNullException(nameof(iv));

            // Create and return the transform directly
            ICryptoTransform transform = CreateTransform(key, iv, true);
            return transform;
        }

        /// <summary>
        /// Leases a reusable cipher object initialized for decryption.
        /// </summary>
        /// <param name="key">Decryption key</param>
        /// <param name="iv">IV/Nonce</param>
        /// <returns>A crypto transform for decryption</returns>
        public ICryptoTransform DecryptionCipher(DestroyableSecretKey key, byte[] iv)
        {
            if (key == null || key.IsDestroyed)
            {
                throw new ArgumentException("Key must be valid and not destroyed", nameof(key));
            }

            byte[] keyBytes = key.GetEncoded();
            return DecryptionCipher(keyBytes, iv);
        }

        /// <summary>
        /// Leases a reusable cipher object initialized for decryption.
        /// </summary>
        /// <param name="key">Decryption key</param>
        /// <param name="iv">IV/Nonce</param>
        /// <returns>A crypto transform for decryption</returns>
        public ICryptoTransform DecryptionCipher(byte[] key, byte[] iv)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            // IV can be null for AES-WRAP
            // if (iv == null)
            //     throw new ArgumentNullException(nameof(iv));

            // Create and return the transform directly
            ICryptoTransform transform = CreateTransform(key, iv, false);
            return transform;
        }

        private ICryptoTransform CreateTransform(byte[] key, byte[] iv, bool forEncryption)
        {
            // Basic validation (specific transforms might do more)
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (iv == null)
                throw new ArgumentNullException(nameof(iv));

            try
            {
                switch (_algorithm)
                {
                    case "AES-CBC":
                        if (iv == null) throw new ArgumentNullException(nameof(iv), "IV is required for AES-CBC.");
                        return new AesCbcTransform(key, iv, forEncryption);
                    case "AES-CTR":
                        if (iv == null) throw new ArgumentNullException(nameof(iv), "IV is required for AES-CTR.");
                        return new AesCtrTransform(key, iv, forEncryption);
                    case "AES-GCM":
                        if (iv == null) throw new ArgumentNullException(nameof(iv), "Nonce (IV) is required for AES-GCM.");
                        return new AesGcmTransform(key, iv, forEncryption);
                    default:
                        // This case should technically be unreachable due to constructor validation
                        throw new NotSupportedException($"Unsupported algorithm: {_algorithm}");
                }
            }
            catch (CryptographicException ex) // Catch crypto errors (e.g., invalid key size)
            {
                // Re-throw as ArgumentException to match test expectations (though CryptographicException might be more correct)
                throw new ArgumentException($"Cryptographic error for algorithm {_algorithm}: {ex.Message}", ex);
            }
        }

        #region Custom Crypto Transforms

        /// <summary>
        /// Custom implementation of AES-CBC mode for .NET
        /// </summary>
        private class AesCbcTransform : ICryptoTransform
        {
            private readonly Aes _aes;
            private readonly ICryptoTransform _transform;

            public AesCbcTransform(byte[] key, byte[] iv, bool forEncryption)
            {
                _aes = Aes.Create();
                _aes.Mode = CipherMode.CBC;
                _aes.Padding = PaddingMode.PKCS7; // Standard padding for CBC
                _aes.Key = key; // Let Aes class handle key size validation
                _aes.IV = iv;   // Let Aes class handle IV size validation

                _transform = forEncryption ? _aes.CreateEncryptor() : _aes.CreateDecryptor();
            }

            public bool CanReuseTransform => _transform.CanReuseTransform;
            public bool CanTransformMultipleBlocks => _transform.CanTransformMultipleBlocks;
            public int InputBlockSize => _transform.InputBlockSize;
            public int OutputBlockSize => _transform.OutputBlockSize;

            public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
            {
                return _transform.TransformBlock(inputBuffer, inputOffset, inputCount, outputBuffer, outputOffset);
            }

            public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
            {
                return _transform.TransformFinalBlock(inputBuffer, inputOffset, inputCount);
            }

            public void Dispose()
            {
                _transform?.Dispose();
                _aes?.Dispose();
            }
        }

        /// <summary>
        /// Custom implementation of AES-CTR mode for .NET
        /// </summary>
        private class AesCtrTransform : ICryptoTransform
        {
            private readonly Aes _aesEcbInstance; // Renamed to avoid confusion
            private readonly byte[] _counter;
            private byte[] _keystreamBlock; // Renamed from _counterBlock
            private int _keystreamBlockPos; // Renamed from _counterPosition
            private readonly ICryptoTransform _ecbEncryptor; // Used to generate keystream

            public AesCtrTransform(byte[] key, byte[] iv, bool forEncryption) // forEncryption is ignored for CTR stream cipher
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                if (iv == null || iv.Length != 16) throw new ArgumentException("IV (nonce) must be 16 bytes for CTR", nameof(iv));

                _aesEcbInstance = Aes.Create();
                _aesEcbInstance.Key = key;
                _aesEcbInstance.Mode = CipherMode.ECB; // Use ECB to encrypt the counter
                _aesEcbInstance.Padding = PaddingMode.None; // No padding for ECB counter encryption
                _ecbEncryptor = _aesEcbInstance.CreateEncryptor(); // Create encryptor once

                _counter = (byte[])iv.Clone(); // Initial counter is the IV
                _keystreamBlock = new byte[16];
                _keystreamBlockPos = _keystreamBlock.Length; // Start as if block is fully used
            }

            public int InputBlockSize => 16;
            public int OutputBlockSize => 16;
            public bool CanTransformMultipleBlocks => true;
            public bool CanReuseTransform => false; // CTR state (counter) changes, so can't reuse naively

            public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
            {
                if (inputBuffer == null) throw new ArgumentNullException(nameof(inputBuffer));
                if (outputBuffer == null) throw new ArgumentNullException(nameof(outputBuffer));
                if (inputOffset < 0 || inputCount < 0 || inputOffset + inputCount > inputBuffer.Length) throw new ArgumentOutOfRangeException(nameof(inputBuffer), "input parameters out of range");
                if (outputOffset < 0) throw new ArgumentOutOfRangeException(nameof(outputOffset));
                if (outputOffset + inputCount > outputBuffer.Length) throw new ArgumentException("output buffer too small");

                // Process the input block by block or byte by byte
                for (int i = 0; i < inputCount; i++)
                {
                    // If the current keystream block is exhausted, generate a new one
                    if (_keystreamBlockPos >= 16)
                    {
                        GenerateKeystreamBlock(); // Generates new _keystreamBlock, increments _counter, resets _keystreamBlockPos to 0
                    }

                    // XOR the input byte with the corresponding keystream byte
                    outputBuffer[outputOffset + i] = (byte)(inputBuffer[inputOffset + i] ^ _keystreamBlock[_keystreamBlockPos]);
                    _keystreamBlockPos++; // Increment position in the keystream block
                }

                return inputCount;
            }

            public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
            {
                if (inputBuffer == null) throw new ArgumentNullException(nameof(inputBuffer));

                byte[] output = new byte[inputCount];

                if (inputCount > 0)
                    TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);

                return output;
            }

            private void GenerateKeystreamBlock() // Renamed from UpdateCounterBlock
            {
                _ecbEncryptor.TransformBlock(_counter, 0, _counter.Length, _keystreamBlock, 0);
                _keystreamBlockPos = 0;
                IncrementCounter(); // Increment counter for next block
            }

            private void IncrementCounter()
            {
                // Increment counter - start from the last byte and carry over
                for (int i = 15; i >= 0; i--)
                {
                    if (++_counter[i] != 0)
                        break;
                }
            }

            public void Dispose()
            {
                _ecbEncryptor?.Dispose(); // Dispose the encryptor
                _aesEcbInstance?.Dispose(); // Dispose the Aes instance used for ECB
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// Custom implementation of AES-GCM mode for .NET Core 3.0+ / .NET 5+
        /// Requires the System.Security.Cryptography.Algorithms package for AesGcm
        /// </summary>
        private class AesGcmTransform : ICryptoTransform
        {
            private readonly byte[] _key;
            private readonly byte[] _nonce;
            private readonly bool _forEncryption;
            private readonly AesGcm _aesGcm;
            private readonly byte[] _tag;

            public AesGcmTransform(byte[] key, byte[] nonce, bool forEncryption)
            {
                _key = key;
                _nonce = nonce;
                _forEncryption = forEncryption;
                _aesGcm = new AesGcm(key);
                _tag = new byte[16]; // AES-GCM tag size
            }

            public bool CanReuseTransform => false;
            public bool CanTransformMultipleBlocks => true;
            public int InputBlockSize => 1;
            public int OutputBlockSize => 1;

            public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
            {
                try
                {
                    if (_forEncryption)
                    {
                        _aesGcm.Encrypt(_nonce, inputBuffer.AsSpan(inputOffset, inputCount),
                            outputBuffer.AsSpan(outputOffset, inputCount), _tag);
                    }
                    else
                    {
                        _aesGcm.Decrypt(_nonce, inputBuffer.AsSpan(inputOffset, inputCount),
                            _tag, outputBuffer.AsSpan(outputOffset, inputCount));
                    }
                    return inputCount;
                }
                catch (CryptographicException ex)
                {
                    throw new CryptographicException("AES-GCM operation failed", ex);
                }
            }

            public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset, int inputCount)
            {
                byte[] output = new byte[inputCount];

                if (inputCount > 0)
                    TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);

                return output;
            }

            public void Dispose()
            {
                _aesGcm?.Dispose(); // Dispose the AesGcm instance
                                    // No other disposable fields here in this version
                GC.SuppressFinalize(this);
            }
        }

        #endregion

        public void Dispose()
        {
            // No longer managing a top-level _aes instance here, so nothing to dispose.
            // The inner transform classes handle disposal of their own Aes instances.
            GC.SuppressFinalize(this);
        }
    }
}
