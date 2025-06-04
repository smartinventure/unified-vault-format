using System;

namespace UvfLib.V3
{
    /// <summary>
    /// Constants for v3 cryptography operations.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// UVF file extension.
        /// </summary>
        public const string UVF_FILE_EXT = ".uvf";
        
        /// <summary>
        /// Content encryption algorithm.
        /// </summary>
        public const string CONTENT_ENC_ALG = "AES";
        
        /// <summary>
        /// UVF magic bytes
        /// </summary>
        public static readonly byte[] UVF_MAGIC_BYTES = new byte[] { (byte)'u', (byte)'v', (byte)'f', 0x00 };
        
        /// <summary>
        /// GCM nonce size (96 bit IVs strongly recommended for GCM)
        /// </summary>
        public const int GCM_NONCE_SIZE = 12;
        
        /// <summary>
        /// Size of the payload (32KB)
        /// </summary>
        public const int PAYLOAD_SIZE = 32 * 1024;
        
        /// <summary>
        /// Size of the GCM tag
        /// </summary>
        public const int GCM_TAG_SIZE = 16;
        
        /// <summary>
        /// Size of a chunk (nonce + payload + tag)
        /// </summary>
        public const int CHUNK_SIZE = GCM_NONCE_SIZE + PAYLOAD_SIZE + GCM_TAG_SIZE;

        /// <summary>
        /// Standard size for Directory IDs in bytes.
        /// </summary>
        public const int DIR_ID_SIZE = 32;

        /// <summary>
        /// Prefix for vault-internal directory paths.
        /// </summary>
        public const string VAULT_DIR_PREFIX = "d/";
    }
} 