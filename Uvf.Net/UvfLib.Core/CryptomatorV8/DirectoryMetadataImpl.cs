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
    /// Directory metadata implementation for Cryptomator v2.
    /// In v2, directory IDs are stored in plaintext and there's no SeedId concept.
    /// </summary>
    public class DirectoryMetadataImpl : DirectoryMetadata
    {
        private readonly byte[] _dirId;

        /// <summary>
        /// Initializes a new instance of the DirectoryMetadataImpl class.
        /// </summary>
        /// <param name="dirId">The directory ID bytes</param>
        public DirectoryMetadataImpl(byte[] dirId)
        {
            _dirId = dirId ?? throw new ArgumentNullException(nameof(dirId));
        }

        /// <summary>
        /// Gets the Base64Url encoded Directory ID.
        /// </summary>
        public string DirId => Base64Url.Encode(_dirId);

        /// <summary>
        /// Gets the masterkey seed ID (always 0 for v2 as it doesn't use revolving keys).
        /// </summary>
        public int SeedId => 0; // v2 doesn't use revolving masterkeys, so always 0

        /// <summary>
        /// Safely casts a DirectoryMetadata to DirectoryMetadataImpl.
        /// </summary>
        /// <param name="metadata">The metadata to cast</param>
        /// <returns>The cast metadata</returns>
        /// <exception cref="ArgumentException">If metadata is not of the correct type</exception>
        public static DirectoryMetadataImpl Cast(DirectoryMetadata metadata)
        {
            if (metadata is DirectoryMetadataImpl impl)
            {
                return impl;
            }
            else
            {
                throw new ArgumentException($"Unsupported metadata type {metadata.GetType()}", nameof(metadata));
            }
        }

        /// <summary>
        /// Gets the directory ID bytes.
        /// </summary>
        /// <returns>The directory ID</returns>
        public byte[] DirIdBytes() => _dirId;
    }
} 
