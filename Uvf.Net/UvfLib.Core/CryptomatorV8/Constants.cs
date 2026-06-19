// Unified Vault Format (UVF) for C# and other languages.
// Copyright (c) Smart In Venture 2025- https://www.speedbits.io
// Licensed under AGPL-3.0 (commercial licenses available); see LICENSE.


namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// Constants for Cryptomator v2 vault format.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Cryptomator file extension.
        /// </summary>
        public const string C9R_FILE_EXT = ".c9r";

        /// <summary>
        /// Cryptomator shortened name directory extension.
        /// </summary>
        public const string C9S_DIR_EXT = ".c9s";

        /// <summary>
        /// Content encryption algorithm.
        /// </summary>
        public const string CONTENT_ENC_ALG = "AES";

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
        /// Filename shortening threshold (220 characters).
        /// If an encrypted filename exceeds this length, it will be shortened.
        /// </summary>
        public const int SHORTENING_THRESHOLD = 220;

        /// <summary>
        /// Name of the file that stores the original long encrypted filename in shortened directories.
        /// </summary>
        public const string INFLATED_NAME_FILE = "name.c9s";

        /// <summary>
        /// Name of the file that stores the actual file content in shortened directories.
        /// </summary>
        public const string SHORTENED_CONTENTS_FILE = "contents.c9r";

        /// <summary>
        /// Name of the file that stores the directory ID in shortened directories.
        /// </summary>
        public const string SHORTENED_DIR_FILE = "dir.c9r";

        /// <summary>
        /// Name of the file that stores the symlink target in shortened directories.
        /// </summary>
        public const string SHORTENED_SYMLINK_FILE = "symlink.c9r";
    }
} 
