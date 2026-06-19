// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using UvfLib.Core.Api;
using UvfLib.Core.Common;

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// File content cryptor implementation for Cryptomator v2.
    /// Uses AES-GCM for chunk-based file content encryption.
    /// </summary>
    public class FileContentCryptorImpl : IFileContentCryptor
    {
        private readonly RandomNumberGenerator _random;

        /// <summary>
        /// Initializes a new instance of the FileContentCryptorImpl class.
        /// </summary>
        /// <param name="random">The secure random number generator</param>
        public FileContentCryptorImpl(RandomNumberGenerator random)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// Determines whether authentication can be skipped for performance reasons.
        /// </summary>
        /// <returns>Always false for GCM mode</returns>
        public bool CanSkipAuthentication()
        {
            return false; // Authentication is integral part of GCM
        }

        /// <summary>
        /// Gets the cleartext chunk size (payload only).
        /// </summary>
        /// <returns>The cleartext chunk size</returns>
        public int CleartextChunkSize()
        {
            return Constants.PAYLOAD_SIZE;
        }

        /// <summary>
        /// Gets the ciphertext chunk size (including nonce and tag).
        /// </summary>
        /// <returns>The ciphertext chunk size</returns>
        public int CiphertextChunkSize()
        {
            return Constants.CHUNK_SIZE;
        }

        /// <summary>
        /// Gets the header size in bytes.
        /// </summary>
        /// <returns>The header size</returns>
        public int HeaderSize()
        {
            return FileHeaderImpl.SIZE;
        }

        /// <summary>
        /// Encrypts a chunk of data using AES-GCM.
        /// </summary>
        /// <param name="cleartextChunk">The plaintext chunk</param>
        /// <param name="chunkNumber">The chunk number (used for nonce generation)</param>
        /// <param name="header">The file header containing the content key</param>
        /// <returns>The encrypted chunk (nonce + ciphertext + tag)</returns>
        public Memory<byte> EncryptChunk(ReadOnlyMemory<byte> cleartextChunk, long chunkNumber, FileHeader header)
        {
            if (cleartextChunk.Length > Constants.PAYLOAD_SIZE)
            {
                throw new ArgumentException($"Cleartext chunk too large: {cleartextChunk.Length} bytes, max: {Constants.PAYLOAD_SIZE}");
            }

            FileHeaderImpl headerImpl = FileHeaderImpl.Cast(header);
            var contentKey = headerImpl.GetPayload().GetContentKey();

            // Generate random nonce (like actual working implementation)
            byte[] nonce = new byte[Constants.GCM_NONCE_SIZE];
            _random.GetBytes(nonce);

            // Construct AAD according to specification: bigEndian(chunkNumber) . headerNonce
            byte[] chunkNumberBytes = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(chunkNumberBytes, chunkNumber);
            byte[] headerNonce = headerImpl.GetNonce();
            byte[] aad = new byte[chunkNumberBytes.Length + headerNonce.Length];
            Array.Copy(chunkNumberBytes, 0, aad, 0, chunkNumberBytes.Length);
            Array.Copy(headerNonce, 0, aad, chunkNumberBytes.Length, headerNonce.Length);

            try
            {
                // Encrypt using AES-GCM with AAD
                byte[] ciphertext = new byte[cleartextChunk.Length];
                byte[] tag = new byte[Constants.GCM_TAG_SIZE];

                using var gcmAlg = new AesGcm(contentKey.GetEncoded());
                gcmAlg.Encrypt(nonce, cleartextChunk.Span, ciphertext, tag, aad);

                // Combine all components: nonce + ciphertext + tag
                byte[] result = new byte[Constants.GCM_NONCE_SIZE + cleartextChunk.Length + Constants.GCM_TAG_SIZE];
                Array.Copy(nonce, 0, result, 0, Constants.GCM_NONCE_SIZE);
                Array.Copy(ciphertext, 0, result, Constants.GCM_NONCE_SIZE, ciphertext.Length);
                Array.Copy(tag, 0, result, Constants.GCM_NONCE_SIZE + ciphertext.Length, Constants.GCM_TAG_SIZE);

                return new Memory<byte>(result);
            }
            finally
            {
                // Clear sensitive data
                UvfLib.Core.Common.CryptographicOperations.ZeroMemory(nonce);
                UvfLib.Core.Common.CryptographicOperations.ZeroMemory(chunkNumberBytes);
                UvfLib.Core.Common.CryptographicOperations.ZeroMemory(aad);
            }
        }

        /// <summary>
        /// Encrypts a chunk of data into the provided buffer.
        /// </summary>
        /// <param name="cleartextChunk">The plaintext chunk</param>
        /// <param name="ciphertextChunk">The buffer to store the encrypted chunk</param>
        /// <param name="chunkNumber">The chunk number (for nonce derivation)</param>
        /// <param name="header">The file header containing the content key</param>
        public void EncryptChunk(ReadOnlyMemory<byte> cleartextChunk, Memory<byte> ciphertextChunk, long chunkNumber, FileHeader header)
        {
            var encrypted = EncryptChunk(cleartextChunk, chunkNumber, header);
            encrypted.CopyTo(ciphertextChunk);
        }

        /// <summary>
        /// Decrypts a chunk of data using AES-GCM.
        /// </summary>
        /// <param name="ciphertextChunk">The encrypted chunk (nonce + ciphertext + tag)</param>
        /// <param name="chunkNumber">The chunk number (for nonce verification)</param>
        /// <param name="header">The file header containing the content key</param>
        /// <param name="authenticate">Whether to authenticate the data (ignored for GCM)</param>
        /// <returns>The decrypted chunk</returns>
        /// <exception cref="AuthenticationFailedException">If chunk authentication fails</exception>
        public Memory<byte> DecryptChunk(ReadOnlyMemory<byte> ciphertextChunk, long chunkNumber, FileHeader header, bool authenticate)
        {
            if (ciphertextChunk.Length < Constants.GCM_NONCE_SIZE + Constants.GCM_TAG_SIZE)
            {
                throw new ArgumentException($"Ciphertext too short: {ciphertextChunk.Length} bytes");
            }

            // For non-final chunks, verify exact size
            bool isLastChunk = ciphertextChunk.Length != Constants.CHUNK_SIZE;
            if (!isLastChunk && ciphertextChunk.Length != Constants.CHUNK_SIZE)
            {
                throw new ArgumentException($"Non-final chunk has invalid size: {ciphertextChunk.Length}, expected: {Constants.CHUNK_SIZE}");
            }

            FileHeaderImpl headerImpl = FileHeaderImpl.Cast(header);
            var contentKey = headerImpl.GetPayload().GetContentKey();

            var span = ciphertextChunk.Span;
            
            // Extract components
            byte[] nonce = span.Slice(0, Constants.GCM_NONCE_SIZE).ToArray();
            int payloadLength = ciphertextChunk.Length - Constants.GCM_NONCE_SIZE - Constants.GCM_TAG_SIZE;
            byte[] encryptedPayload = span.Slice(Constants.GCM_NONCE_SIZE, payloadLength).ToArray();
            byte[] tag = span.Slice(Constants.GCM_NONCE_SIZE + payloadLength, Constants.GCM_TAG_SIZE).ToArray();

            try
            {
                // Construct AAD according to specification: bigEndian(chunkNumber) . headerNonce
                byte[] chunkNumberBytes = new byte[8];
                BinaryPrimitives.WriteInt64BigEndian(chunkNumberBytes, chunkNumber);
                byte[] headerNonce = headerImpl.GetNonce();
                byte[] aad = new byte[chunkNumberBytes.Length + headerNonce.Length];
                Array.Copy(chunkNumberBytes, 0, aad, 0, chunkNumberBytes.Length);
                Array.Copy(headerNonce, 0, aad, chunkNumberBytes.Length, headerNonce.Length);

                // Decrypt using AES-GCM
                byte[] decryptedPayload = new byte[payloadLength];

                try
                {
                    using var gcmAlg = new AesGcm(contentKey.GetEncoded());
                    gcmAlg.Decrypt(nonce, encryptedPayload, tag, decryptedPayload, aad);
                }
                catch (CryptographicException ex)
                {
                    throw new AuthenticationFailedException($"Chunk {chunkNumber} authentication failed", ex);
                }

                return new Memory<byte>(decryptedPayload);
            }
            finally
            {
                // Clear sensitive data
                UvfLib.Core.Common.CryptographicOperations.ZeroMemory(nonce);
                UvfLib.Core.Common.CryptographicOperations.ZeroMemory(encryptedPayload);
                UvfLib.Core.Common.CryptographicOperations.ZeroMemory(tag);
            }
        }

        /// <summary>
        /// Decrypts a chunk of data into the provided buffer.
        /// </summary>
        /// <param name="ciphertextChunk">The encrypted chunk data</param>
        /// <param name="cleartextChunk">The buffer to store the decrypted chunk</param>
        /// <param name="chunkNumber">The chunk number</param>
        /// <param name="header">The file header</param>
        /// <param name="authenticate">Whether to authenticate the data</param>
        /// <returns>The number of bytes written to cleartextChunk</returns>
        /// <exception cref="ArgumentException">If the ciphertext chunk is too small</exception>
        /// <exception cref="AuthenticationFailedException">If the data fails authentication</exception>
        public int DecryptChunk(ReadOnlyMemory<byte> ciphertextChunk, Memory<byte> cleartextChunk, long chunkNumber, FileHeader header, bool authenticate)
        {
            var decrypted = DecryptChunk(ciphertextChunk, chunkNumber, header, authenticate);
            decrypted.CopyTo(cleartextChunk);
            return decrypted.Length;
        }
    }
} 
