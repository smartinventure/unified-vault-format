using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using UvfLib.Api;
using UvfLib.Common;

namespace UvfLib.V3
{
    /// <summary>
    /// Implementation of the FileHeaderCryptor interface for v3 format.
    /// </summary>
    public sealed class FileHeaderCryptorImpl : FileHeaderCryptor
    {
        private static readonly byte[] KDF_CONTEXT = System.Text.Encoding.ASCII.GetBytes("fileHeader");

        private readonly RevolvingMasterkey _masterkey;
        private readonly RandomNumberGenerator _random;
        private readonly int _revision;

        /// <summary>
        /// Creates a new file header cryptor.
        /// </summary>
        /// <param name="masterkey">The masterkey</param>
        /// <param name="random">The random number generator</param>
        /// <param name="revision">The revision of the masterkey to use</param>
        public FileHeaderCryptorImpl(RevolvingMasterkey masterkey, RandomNumberGenerator random, int revision)
        {
            _masterkey = masterkey ?? throw new ArgumentNullException(nameof(masterkey));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _revision = revision;
        }

        /// <summary>
        /// Creates a new file header with random content.
        /// </summary>
        /// <returns>A new file header</returns>
        public FileHeader Create()
        {
            // Generate random nonce
            byte[] nonce = new byte[FileHeaderImpl.NONCE_LEN];
            _random.GetBytes(nonce);

            // Generate random content key
            byte[] contentKeyBytes = new byte[FileHeaderImpl.CONTENT_KEY_LEN];
            _random.GetBytes(contentKeyBytes);

            try
            {
                // Create content key
                DestroyableSecretKey contentKey = new DestroyableSecretKey(contentKeyBytes, Constants.CONTENT_ENC_ALG);

                // Create header with current revision/seed ID
                return new FileHeaderImpl(_revision, nonce, contentKey);
            }
            finally
            {
                UvfLib.Common.CryptographicOperations.ZeroMemory(contentKeyBytes);
            }
        }

        /// <summary>
        /// Gets the size of an encrypted header in bytes.
        /// </summary>
        /// <returns>The number of bytes of an encrypted header</returns>
        public int HeaderSize()
        {
            return FileHeaderImpl.SIZE;
        }

        /// <summary>
        /// Gets the size of a v3 file header.
        /// </summary>
        /// <returns>The header size in bytes</returns>
        public int GetHeaderSize()
        {
            return FileHeaderImpl.SIZE;
        }

        /// <summary>
        /// Encrypts a file header.
        /// </summary>
        /// <param name="header">The header to encrypt</param>
        /// <returns>The encrypted header</returns>
        public Memory<byte> EncryptHeader(FileHeader header)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            FileHeaderImpl headerImpl = FileHeaderImpl.Cast(header);
            DestroyableSecretKey headerKey = null;

            try
            {
                // Derive header key from masterkey
                headerKey = _masterkey.SubKey(headerImpl.GetSeedId(), 32, KDF_CONTEXT, "AES");

                // Prepare result buffer
                byte[] result = new byte[FileHeaderImpl.SIZE];

                // Write general header
                Buffer.BlockCopy(Constants.UVF_MAGIC_BYTES, 0, result, 0, Constants.UVF_MAGIC_BYTES.Length);
                BinaryPrimitives.WriteInt32BigEndian(
                    result.AsSpan(Constants.UVF_MAGIC_BYTES.Length, sizeof(int)),
                    headerImpl.GetSeedId());

                // Write nonce
                byte[] nonce = headerImpl.GetNonce();
                Buffer.BlockCopy(nonce, 0, result, FileHeaderImpl.NONCE_POS, FileHeaderImpl.NONCE_LEN);

                // Create GCM parameters
                byte[] aad = new byte[FileHeaderImpl.UVF_GENERAL_HEADERS_LEN];
                Buffer.BlockCopy(result, 0, aad, 0, FileHeaderImpl.UVF_GENERAL_HEADERS_LEN);

                try
                {
                    // Create a GCM instance
                    using var aesGcm = new AesGcm(headerKey.GetEncoded());
                    byte[] contentKeyData = headerImpl.GetContentKey().GetEncoded();
                    byte[] tag = new byte[FileHeaderImpl.TAG_LEN];

                    // Encrypt content key
                    aesGcm.Encrypt(
                        nonce,
                        contentKeyData,
                        result.AsSpan(FileHeaderImpl.CONTENT_KEY_POS, FileHeaderImpl.CONTENT_KEY_LEN),
                        tag,
                        aad);

                    // Copy tag to result
                    Buffer.BlockCopy(tag, 0, result, FileHeaderImpl.CONTENT_KEY_POS + FileHeaderImpl.CONTENT_KEY_LEN, FileHeaderImpl.TAG_LEN);

                    return result;
                }
                catch (CryptographicException ex)
                {
                    throw new CryptoException("Error encrypting file header", ex);
                }
            }
            finally
            {
                headerKey?.Dispose();
            }
        }

        /// <summary>
        /// Decrypts a file header.
        /// </summary>
        /// <param name="ciphertextHeader">The encrypted header</param>
        /// <returns>The decrypted header</returns>
        /// <exception cref="AuthenticationFailedException">If the header cannot be authenticated</exception>
        public FileHeader DecryptHeader(ReadOnlyMemory<byte> ciphertextHeader)
        {
            if (ciphertextHeader.Length < FileHeaderImpl.SIZE)
            {
                throw new ArgumentException("Malformed ciphertext header", nameof(ciphertextHeader));
            }

            var ciphertextSpan = ciphertextHeader.Span;

            // Check magic bytes
            for (int i = 0; i < Constants.UVF_MAGIC_BYTES.Length; i++)
            {
                if (ciphertextSpan[i] != Constants.UVF_MAGIC_BYTES[i])
                {
                    throw new ArgumentException("Not a UVF file", nameof(ciphertextHeader));
                }
            }

            // Extract seed ID
            int seedId = BinaryPrimitives.ReadInt32BigEndian(
                ciphertextSpan.Slice(Constants.UVF_MAGIC_BYTES.Length, sizeof(int)));

            // Extract nonce
            byte[] nonce = ciphertextHeader.Slice(FileHeaderImpl.NONCE_POS, FileHeaderImpl.NONCE_LEN).ToArray();

            // Extract AAD (general header)
            byte[] aad = ciphertextHeader.Slice(0, FileHeaderImpl.UVF_GENERAL_HEADERS_LEN).ToArray();

            // Extract ciphertext and tag
            byte[] ciphertext = ciphertextHeader.Slice(FileHeaderImpl.CONTENT_KEY_POS, FileHeaderImpl.CONTENT_KEY_LEN).ToArray();
            byte[] tag = ciphertextHeader.Slice(FileHeaderImpl.CONTENT_KEY_POS + FileHeaderImpl.CONTENT_KEY_LEN, FileHeaderImpl.TAG_LEN).ToArray();

            DestroyableSecretKey headerKey = null;
            byte[] contentKeyBytes = new byte[FileHeaderImpl.CONTENT_KEY_LEN];

            try
            {
                // Derive header key from masterkey
                headerKey = _masterkey.SubKey(seedId, 32, KDF_CONTEXT, "AES");

                try
                {
                    // Decrypt with AES-GCM
                    using var aesGcm = new AesGcm(headerKey.GetEncoded());
                    aesGcm.Decrypt(nonce, ciphertext, tag, contentKeyBytes, aad);
                }
                catch (CryptographicException ex)
                {
                    throw new AuthenticationFailedException("Header authentication failed", ex);
                }

                // Create content key
                DestroyableSecretKey contentKey = new DestroyableSecretKey(contentKeyBytes, Constants.CONTENT_ENC_ALG);

                // Create and return header
                return new FileHeaderImpl(seedId, nonce, contentKey);
            }
            finally
            {
                headerKey?.Dispose();
                UvfLib.Common.CryptographicOperations.ZeroMemory(contentKeyBytes);
            }
        }
    }
}