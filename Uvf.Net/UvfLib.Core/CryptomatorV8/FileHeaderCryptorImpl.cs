// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

using System;
using System.Security.Cryptography;
using UvfLib.Core.Api;
using UvfLib.Core.Common;
using CryptoOps = UvfLib.Core.Common.CryptographicOperations;

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// File header cryptor implementation for Cryptomator v2.
    /// Uses AES-GCM for header encryption.
    /// </summary>
    internal class FileHeaderCryptorImpl : FileHeaderCryptor
    {
        private readonly PerpetualMasterkey _masterkey;
        private readonly RandomNumberGenerator _random;

        /// <summary>
        /// Initializes a new instance of the FileHeaderCryptorImpl class.
        /// </summary>
        /// <param name="masterkey">The perpetual masterkey</param>
        /// <param name="random">The secure random number generator</param>
        internal FileHeaderCryptorImpl(PerpetualMasterkey masterkey, RandomNumberGenerator random)
        {
            _masterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// Gets the size of encrypted file headers in bytes.
        /// </summary>
        /// <returns>The header size</returns>
        public int HeaderSize()
        {
            return FileHeaderImpl.SIZE;
        }

        /// <summary>
        /// Gets the size of encrypted file headers in bytes (interface requirement).
        /// </summary>
        /// <returns>The header size</returns>
        public int GetHeaderSize()
        {
            return HeaderSize();
        }

        /// <summary>
        /// Creates a new file header containing a random content encryption key.
        /// </summary>
        /// <returns>A new file header</returns>
        public FileHeader Create()
        {
            // Generate random nonce
            byte[] nonce = new byte[Constants.GCM_NONCE_SIZE];
            _random.GetBytes(nonce);

            // Generate random content key
            byte[] contentKey = new byte[FileHeaderImpl.Payload.CONTENT_KEY_LEN];
            _random.GetBytes(contentKey);

            try
            {
                // Create payload with reserved=0xFFFFFFFFFFFFFFFF (as per Cryptomator specification)
                var payload = new FileHeaderImpl.Payload(unchecked((long)0xFFFFFFFFFFFFFFFF), contentKey);
                return new FileHeaderImpl(nonce, payload);
            }
            finally
            {
                // Clear the temporary content key
                CryptoOps.ZeroMemory(contentKey);
            }
        }

        /// <summary>
        /// Encrypts a file header using AES-GCM.
        /// </summary>
        /// <param name="header">The header to encrypt</param>
        /// <returns>The encrypted header bytes</returns>
        public Memory<byte> EncryptHeader(FileHeader header)
        {
            FileHeaderImpl headerImpl = FileHeaderImpl.Cast(header);
            
            byte[] nonce = headerImpl.GetNonce();
            byte[] payload = headerImpl.GetPayload().Encode();
            
            using var encKey = _masterkey.GetEncKey();
            
            // Create the complete encrypted header
            byte[] result = new byte[FileHeaderImpl.SIZE];
            
            // Copy nonce to the beginning
            Buffer.BlockCopy(nonce, 0, result, FileHeaderImpl.NONCE_POS, FileHeaderImpl.NONCE_LEN);
            
            try
            {
                // Encrypt payload using AES-GCM
                byte[] ciphertext = new byte[payload.Length];
                byte[] tag = new byte[Constants.GCM_TAG_SIZE];

                using var gcmAlg = new AesGcm(encKey.GetEncoded());
                gcmAlg.Encrypt(nonce, payload, ciphertext, tag);
                
                // Copy encrypted payload and tag to result
                Buffer.BlockCopy(ciphertext, 0, result, FileHeaderImpl.PAYLOAD_POS, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, result, FileHeaderImpl.TAG_POS, tag.Length);
                
                // Clear temporary arrays
                CryptoOps.ZeroMemory(ciphertext);
                CryptoOps.ZeroMemory(tag);
            }
            finally
            {
                CryptoOps.ZeroMemory(payload);
            }
            
            return new Memory<byte>(result);
        }

        /// <summary>
        /// Decrypts a file header using AES-GCM.
        /// </summary>
        /// <param name="ciphertext">The encrypted header bytes</param>
        /// <returns>The decrypted file header</returns>
        /// <exception cref="AuthenticationFailedException">If header authentication fails</exception>
        public FileHeader DecryptHeader(ReadOnlyMemory<byte> ciphertext)
        {
            if (ciphertext.Length != FileHeaderImpl.SIZE)
            {
                throw new ArgumentException($"Invalid header length: {ciphertext.Length}, expected: {FileHeaderImpl.SIZE}");
            }
            
            var span = ciphertext.Span;
            
            // Extract components
            byte[] nonce = span.Slice(FileHeaderImpl.NONCE_POS, FileHeaderImpl.NONCE_LEN).ToArray();
            byte[] encryptedPayload = span.Slice(FileHeaderImpl.PAYLOAD_POS, FileHeaderImpl.PAYLOAD_LEN).ToArray();
            byte[] tag = span.Slice(FileHeaderImpl.TAG_POS, FileHeaderImpl.TAG_LEN).ToArray();
            
            using var encKey = _masterkey.GetEncKey();
            
            try
            {
                // Decrypt payload using AES-GCM
                byte[] decryptedPayload = new byte[FileHeaderImpl.PAYLOAD_LEN];
                
                try
                {
                    using var gcmAlg = new AesGcm(encKey.GetEncoded());
                    gcmAlg.Decrypt(nonce, encryptedPayload, tag, decryptedPayload);
                }
                catch (CryptographicException ex)
                {
                    throw new AuthenticationFailedException("Header authentication failed", ex);
                }
                
                // Decode payload
                var payload = FileHeaderImpl.Payload.Decode(decryptedPayload);
                
                return new FileHeaderImpl(nonce, payload);
            }
            finally
            {
                // Clear temporary arrays
                CryptoOps.ZeroMemory(encryptedPayload);
                CryptoOps.ZeroMemory(tag);
            }
        }
    }
} 
