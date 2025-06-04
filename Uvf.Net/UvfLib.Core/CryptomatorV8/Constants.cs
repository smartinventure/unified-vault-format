/*******************************************************************************
 * Copyright (c) 2016 Sebastian Stenzel and others.
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the accompanying LICENSE.txt.
 *
 * Contributors:
 *     Sebastian Stenzel - initial API and implementation
 *******************************************************************************/

// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// Constants for Cryptomator v2/v8 cryptography operations.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Cryptomator file extension.
        /// </summary>
        public const string C9R_FILE_EXT = ".c9r";

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
    }
} 
