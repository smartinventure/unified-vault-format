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
    /// Internal adapter that implements Core DirectoryMetadata interface for our public DirectoryMetadata.
    /// </summary>
    internal class DirectoryMetadataAdapter : UvfLib.Core.Api.DirectoryMetadata
    {
        public string DirId { get; }
        public int SeedId { get; }

        public DirectoryMetadataAdapter(string dirId, int seedId)
        {
            DirId = dirId;
            SeedId = seedId;
        }

        public bool Equals(UvfLib.Core.Api.DirectoryMetadata other)
        {
            return other != null && DirId == other.DirId && SeedId == other.SeedId;
        }

        public override bool Equals(object obj)
        {
            return obj is UvfLib.Core.Api.DirectoryMetadata other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DirId, SeedId);
        }
    }
} 