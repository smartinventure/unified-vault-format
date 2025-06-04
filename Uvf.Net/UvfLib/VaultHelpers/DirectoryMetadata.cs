/*******************************************************************************
 * Copyright (c) 2025 Smart In Venture GmbH for C# Implementation
 * All rights reserved. This program and the accompanying materials
 * are made available under the terms of the accompanying LICENSE.txt.
 *
 * Contributors:
 *     Smart In Venture GmbH - C# Implementation
 *******************************************************************************/

using System;

namespace UvfLib
{
    /// <summary>
    /// Public directory metadata class that wraps Core DirectoryMetadata objects.
    /// This class provides a public API while preserving the original Core object.
    /// </summary>
    public class DirectoryMetadata
    {
        private readonly UvfLib.Core.Api.DirectoryMetadata _coreMetadata;

        public string DirId => _coreMetadata.DirId;
        public int SeedId => _coreMetadata.SeedId;

        /// <summary>
        /// Creates a new DirectoryMetadata wrapper around a Core DirectoryMetadata object.
        /// </summary>
        /// <param name="coreMetadata">The Core DirectoryMetadata object to wrap</param>
        internal DirectoryMetadata(UvfLib.Core.Api.DirectoryMetadata coreMetadata)
        {
            _coreMetadata = coreMetadata ?? throw new ArgumentNullException(nameof(coreMetadata));
        }

        /// <summary>
        /// Legacy constructor for backward compatibility.
        /// Creates a new DirectoryMetadata with the specified properties.
        /// Note: This creates an adapter and may not be compatible with all Core operations.
        /// </summary>
        /// <param name="dirId">The directory ID</param>
        /// <param name="seedId">The seed ID</param>
        [Obsolete("Use factory methods from Vault instead to ensure Core compatibility")]
        public DirectoryMetadata(string dirId, int seedId)
        {
            _coreMetadata = new DirectoryMetadataAdapter(dirId, seedId);
        }

        /// <summary>
        /// Gets the original Core DirectoryMetadata object.
        /// </summary>
        internal UvfLib.Core.Api.DirectoryMetadata GetCoreMetadata()
        {
            return _coreMetadata;
        }

        /// <summary>
        /// Checks if two DirectoryMetadata instances are equal.
        /// </summary>
        public bool Equals(DirectoryMetadata other)
        {
            return other != null && DirId == other.DirId && SeedId == other.SeedId;
        }

        /// <summary>
        /// Checks if this DirectoryMetadata equals another object.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is DirectoryMetadata other && Equals(other);
        }

        /// <summary>
        /// Gets the hash code for this DirectoryMetadata.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(DirId, SeedId);
        }

        /// <summary>
        /// Gets a string representation of this DirectoryMetadata.
        /// </summary>
        public override string ToString()
        {
            return $"DirectoryMetadata(DirId: {DirId}, SeedId: {SeedId})";
        }
    }
} 