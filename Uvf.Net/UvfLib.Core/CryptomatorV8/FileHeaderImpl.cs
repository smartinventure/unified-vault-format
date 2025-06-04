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
using UvfLib.Core.Api;
using UvfLib.Core.Common;

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// File header implementation for Cryptomator v2.
    /// Contains a nonce and encrypted payload with content key.
    /// </summary>
    public class FileHeaderImpl : FileHeader, IDisposable
    {
        public const int NONCE_POS = 0;
        public const int NONCE_LEN = Constants.GCM_NONCE_SIZE;
        public const int PAYLOAD_POS = NONCE_POS + NONCE_LEN; // 12
        public const int PAYLOAD_LEN = Payload.SIZE;
        public const int TAG_POS = PAYLOAD_POS + PAYLOAD_LEN; // 52
        public const int TAG_LEN = Constants.GCM_TAG_SIZE;
        public const int SIZE = NONCE_LEN + PAYLOAD_LEN + TAG_LEN;

        private readonly byte[] _nonce;
        private readonly Payload _payload;

        /// <summary>
        /// Initializes a new instance of the FileHeaderImpl class.
        /// </summary>
        /// <param name="nonce">The nonce bytes</param>
        /// <param name="payload">The payload</param>
        public FileHeaderImpl(byte[] nonce, Payload payload)
        {
            if (nonce == null) throw new ArgumentNullException(nameof(nonce));
            if (nonce.Length != NONCE_LEN)
            {
                throw new ArgumentException($"Invalid nonce length. (was: {nonce.Length}, required: {NONCE_LEN})", nameof(nonce));
            }
            _nonce = nonce;
            _payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        /// <summary>
        /// Safely casts a FileHeader to FileHeaderImpl.
        /// </summary>
        /// <param name="header">The header to cast</param>
        /// <returns>The cast header</returns>
        /// <exception cref="ArgumentException">If header is not of the correct type</exception>
        public static FileHeaderImpl Cast(FileHeader header)
        {
            if (header is FileHeaderImpl impl)
            {
                return impl;
            }
            else
            {
                throw new ArgumentException($"Unsupported header type {header.GetType()}", nameof(header));
            }
        }

        /// <summary>
        /// Gets the nonce bytes.
        /// </summary>
        /// <returns>The nonce</returns>
        public byte[] GetNonce() => _nonce;

        /// <summary>
        /// Gets the payload.
        /// </summary>
        /// <returns>The payload</returns>
        public Payload GetPayload() => _payload;

        /// <summary>
        /// Gets the reserved field value.
        /// </summary>
        public long Reserved
        {
            get => _payload.GetReserved();
            set => _payload.SetReserved(value);
        }

        /// <summary>
        /// Checks if this header has been destroyed.
        /// </summary>
        /// <returns>True if destroyed, false otherwise</returns>
        public bool IsDestroyed()
        {
            return _payload.IsDestroyed();
        }

        /// <summary>
        /// Destroys this header and all associated keys.
        /// </summary>
        public void Destroy()
        {
            _payload.Destroy();
        }

        /// <summary>
        /// Disposes this header by calling Destroy().
        /// </summary>
        public void Dispose()
        {
            Destroy();
        }

        /// <summary>
        /// Payload class containing the reserved field and content key.
        /// </summary>
        public class Payload : IDisposable
        {
            public const int RESERVED_LEN = sizeof(long);
            public const int CONTENT_KEY_LEN = 32;
            public const int SIZE = RESERVED_LEN + CONTENT_KEY_LEN;

            private long _reserved;
            private readonly DestroyableSecretKey _contentKey;

            /// <summary>
            /// Initializes a new instance of the Payload class.
            /// </summary>
            /// <param name="reserved">The reserved field value</param>
            /// <param name="contentKeyBytes">The content key bytes</param>
            public Payload(long reserved, byte[] contentKeyBytes)
            {
                if (contentKeyBytes == null) throw new ArgumentNullException(nameof(contentKeyBytes));
                if (contentKeyBytes.Length != CONTENT_KEY_LEN)
                {
                    throw new ArgumentException($"Invalid key length. (was: {contentKeyBytes.Length}, required: {CONTENT_KEY_LEN})", nameof(contentKeyBytes));
                }
                _reserved = reserved;
                _contentKey = new DestroyableSecretKey(contentKeyBytes, Constants.CONTENT_ENC_ALG);
            }

            /// <summary>
            /// Decodes a payload from a buffer.
            /// </summary>
            /// <param name="cleartextPayloadBytes">The cleartext payload bytes</param>
            /// <returns>The decoded payload</returns>
            public static Payload Decode(byte[] cleartextPayloadBytes)
            {
                if (cleartextPayloadBytes == null) throw new ArgumentNullException(nameof(cleartextPayloadBytes));
                if (cleartextPayloadBytes.Length != SIZE)
                {
                    throw new ArgumentException($"Invalid payload buffer length: {cleartextPayloadBytes.Length}, expected: {SIZE}");
                }

                // Read reserved field (8 bytes, big endian)
                long reserved = 0;
                for (int i = 0; i < 8; i++)
                {
                    reserved = (reserved << 8) | cleartextPayloadBytes[i];
                }

                // Read content key bytes (32 bytes)
                byte[] contentKeyBytes = new byte[CONTENT_KEY_LEN];
                Buffer.BlockCopy(cleartextPayloadBytes, RESERVED_LEN, contentKeyBytes, 0, CONTENT_KEY_LEN);

                return new Payload(reserved, contentKeyBytes);
            }

            /// <summary>
            /// Encodes this payload to bytes.
            /// </summary>
            /// <returns>The encoded payload bytes</returns>
            public byte[] Encode()
            {
                byte[] buf = new byte[SIZE];
                
                // Write reserved field (8 bytes, big endian)
                for (int i = 7; i >= 0; i--)
                {
                    buf[7 - i] = (byte)(_reserved >> (i * 8));
                }
                
                // Write content key
                Buffer.BlockCopy(_contentKey.GetEncoded(), 0, buf, RESERVED_LEN, CONTENT_KEY_LEN);
                
                return buf;
            }

            /// <summary>
            /// Gets the reserved field value.
            /// </summary>
            /// <returns>The reserved value</returns>
            public long GetReserved() => _reserved;

            /// <summary>
            /// Sets the reserved field value.
            /// </summary>
            /// <param name="reserved">The reserved value</param>
            public void SetReserved(long reserved) => _reserved = reserved;

            /// <summary>
            /// Gets the content key.
            /// </summary>
            /// <returns>The content key</returns>
            public DestroyableSecretKey GetContentKey() => _contentKey;

            /// <summary>
            /// Checks if this payload has been destroyed.
            /// </summary>
            /// <returns>True if destroyed, false otherwise</returns>
            public bool IsDestroyed() => _contentKey.IsDestroyed;

            /// <summary>
            /// Destroys this payload and all associated keys.
            /// </summary>
            public void Destroy() => _contentKey.Destroy();

            /// <summary>
            /// Disposes this payload by calling Destroy().
            /// </summary>
            public void Dispose() => Destroy();
        }
    }
} 
