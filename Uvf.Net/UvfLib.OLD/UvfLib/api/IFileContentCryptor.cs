using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Provides operations to encrypt and decrypt file contents.
    /// </summary>
    public interface IFileContentCryptor
    {
        /// <summary>
        /// Determines whether authentication can be skipped for performance reasons.
        /// </summary>
        /// <returns>True if authentication can be skipped, false otherwise</returns>
        bool CanSkipAuthentication();

        /// <summary>
        /// Gets the size in bytes of a cleartext chunk.
        /// </summary>
        /// <returns>The maximum number of bytes of unencrypted content that fit into a single encrypted chunk</returns>
        int CleartextChunkSize();

        /// <summary>
        /// Gets the size in bytes of a ciphertext chunk.
        /// </summary>
        /// <returns>The number of bytes of an encrypted chunk</returns>
        int CiphertextChunkSize();

        /// <summary>
        /// Calculates the cleartext size of a ciphertext.
        /// </summary>
        /// <param name="ciphertextSize">The ciphertext size in bytes</param>
        /// <returns>The cleartext size in bytes</returns>
        long CleartextSize(long ciphertextSize) => (long)Math.Floor((ciphertextSize - HeaderSize()) / (double)CiphertextChunkSize()) * CleartextChunkSize();

        /// <summary>
        /// Calculates the ciphertext size of a cleartext.
        /// </summary>
        /// <param name="cleartextSize">The cleartext size in bytes</param>
        /// <returns>The ciphertext size in bytes</returns>
        long CiphertextSize(long cleartextSize) => HeaderSize() + (long)Math.Ceiling(cleartextSize / (double)CleartextChunkSize()) * CiphertextChunkSize();

        /// <summary>
        /// Gets the header size in bytes.
        /// </summary>
        /// <returns>The header size in bytes</returns>
        int HeaderSize();

        /// <summary>
        /// Encrypts a chunk of data.
        /// </summary>
        /// <param name="cleartextChunk">The chunk to encrypt</param>
        /// <param name="chunkNumber">The number of the chunk in the stream</param>
        /// <param name="header">The file header</param>
        /// <returns>The encrypted chunk</returns>
        Memory<byte> EncryptChunk(ReadOnlyMemory<byte> cleartextChunk, long chunkNumber, FileHeader header);

        /// <summary>
        /// Encrypts a chunk of data.
        /// </summary>
        /// <param name="cleartextChunk">The chunk to encrypt</param>
        /// <param name="ciphertextChunk">The buffer to store the encrypted chunk</param>
        /// <param name="chunkNumber">The number of the chunk in the stream</param>
        /// <param name="header">The file header</param>
        void EncryptChunk(ReadOnlyMemory<byte> cleartextChunk, Memory<byte> ciphertextChunk, long chunkNumber, FileHeader header);

        /// <summary>
        /// Decrypts a chunk of data.
        /// </summary>
        /// <param name="ciphertextChunk">The encrypted chunk</param>
        /// <param name="chunkNumber">The number of the chunk in the stream</param>
        /// <param name="header">The file header</param>
        /// <param name="authenticate">Whether to authenticate the data</param>
        /// <returns>The decrypted chunk</returns>
        /// <exception cref="AuthenticationFailedException">If authentication fails</exception>
        Memory<byte> DecryptChunk(ReadOnlyMemory<byte> ciphertextChunk, long chunkNumber, FileHeader header, bool authenticate);

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
        int DecryptChunk(ReadOnlyMemory<byte> ciphertextChunk, Memory<byte> cleartextChunk, long chunkNumber, FileHeader header, bool authenticate);
    }
}