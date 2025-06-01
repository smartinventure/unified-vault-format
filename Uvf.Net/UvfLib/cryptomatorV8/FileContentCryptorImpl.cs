/*******************************************************************************
 * Copyright (c) 2016 Sebastian Stenzel and others.
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the accompanying LICENSE.txt.
 *
 * Contributors:
 *     Sebastian Stenzel - initial API and implementation
 *******************************************************************************/

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using UvfLib.Api;
using UvfLib.Common;

namespace UvfLib.CryptomatorV8
{
    /// <summary>
    /// File content cryptor implementation for Cryptomator v2.
    /// Uses AES-GCM for chunk-based file content encryption.
    /// </summary>
    internal class FileContentCryptorImpl : IFileContentCryptor
    {
        private readonly RandomNumberGenerator _random;

        /// <summary>
        /// Initializes a new instance of the FileContentCryptorImpl class.
        /// </summary>
        /// <param name="random">The secure random number generator</param>
        internal FileContentCryptorImpl(RandomNumberGenerator random)
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
        /// <param name="chunkNumber">The chunk number (for nonce derivation)</param>
        /// <param name="header">The file header containing the content key</param>
        /// <returns>The encrypted chunk (nonce + ciphertext + tag)</returns>
        public Memory<byte> EncryptChunk(ReadOnlyMemory<byte> cleartextChunk, long chunkNumber, FileHeader header)
        {
            if (cleartextChunk.Length > Constants.PAYLOAD_SIZE)
            {
                throw new ArgumentException($"Chunk too large: {cleartextChunk.Length} bytes, max: {Constants.PAYLOAD_SIZE}");
            }

            FileHeaderImpl headerImpl = FileHeaderImpl.Cast(header);
            using var contentKey = headerImpl.GetPayload().GetContentKey();

            // Create nonce: 8 bytes chunk number (big endian) + 4 random bytes
            byte[] nonce = new byte[Constants.GCM_NONCE_SIZE];
            BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(0, 8), chunkNumber);
            _random.GetBytes(nonce.AsSpan(8, 4));

            // Prepare result buffer
            byte[] result = new byte[Constants.GCM_NONCE_SIZE + cleartextChunk.Length + Constants.GCM_TAG_SIZE];
            
            // Copy nonce to result
            Buffer.BlockCopy(nonce, 0, result, 0, Constants.GCM_NONCE_SIZE);

            try
            {
                // Encrypt using AES-GCM
                byte[] ciphertext = new byte[cleartextChunk.Length];
                byte[] tag = new byte[Constants.GCM_TAG_SIZE];

                using var gcmAlg = new AesGcm(contentKey.GetEncoded());
                gcmAlg.Encrypt(nonce, cleartextChunk.Span, ciphertext, tag);

                // Copy ciphertext and tag to result
                Buffer.BlockCopy(ciphertext, 0, result, Constants.GCM_NONCE_SIZE, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, result, Constants.GCM_NONCE_SIZE + ciphertext.Length, Constants.GCM_TAG_SIZE);

                // Clear temporary arrays
                UvfLib.Common.CryptographicOperations.ZeroMemory(ciphertext);
                UvfLib.Common.CryptographicOperations.ZeroMemory(tag);
            }
            finally
            {
                UvfLib.Common.CryptographicOperations.ZeroMemory(nonce);
            }

            return new Memory<byte>(result);
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
            using var contentKey = headerImpl.GetPayload().GetContentKey();

            var span = ciphertextChunk.Span;
            
            // Extract components
            byte[] nonce = span.Slice(0, Constants.GCM_NONCE_SIZE).ToArray();
            int payloadLength = ciphertextChunk.Length - Constants.GCM_NONCE_SIZE - Constants.GCM_TAG_SIZE;
            byte[] encryptedPayload = span.Slice(Constants.GCM_NONCE_SIZE, payloadLength).ToArray();
            byte[] tag = span.Slice(Constants.GCM_NONCE_SIZE + payloadLength, Constants.GCM_TAG_SIZE).ToArray();

            try
            {
                // Verify chunk number matches nonce
                long nonceChunkNumber = BinaryPrimitives.ReadInt64BigEndian(nonce.AsSpan(0, 8));
                if (nonceChunkNumber != chunkNumber)
                {
                    throw new AuthenticationFailedException($"Chunk number mismatch: expected {chunkNumber}, found {nonceChunkNumber}");
                }

                // Decrypt using AES-GCM
                byte[] decryptedPayload = new byte[payloadLength];

                try
                {
                    using var gcmAlg = new AesGcm(contentKey.GetEncoded());
                    gcmAlg.Decrypt(nonce, encryptedPayload, tag, decryptedPayload);
                }
                catch (CryptographicException ex)
                {
                    throw new AuthenticationFailedException($"Chunk {chunkNumber} authentication failed", ex);
                }

                return new Memory<byte>(decryptedPayload);
            }
            finally
            {
                // Clear temporary arrays
                UvfLib.Common.CryptographicOperations.ZeroMemory(nonce);
                UvfLib.Common.CryptographicOperations.ZeroMemory(encryptedPayload);
                UvfLib.Common.CryptographicOperations.ZeroMemory(tag);
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