using UvfLib.Api;
using System;
using UvfLib.Common; // For Base64Url

namespace UvfLib.V3
{
    /// <summary>
    /// Implementation of the DirectoryMetadata interface for v3 format.
    /// This represents the metadata stored in a dir.uvf file, which contains only the directory ID.
    /// </summary>
    internal sealed class DirectoryMetadataImpl : DirectoryMetadata
    {
        private readonly int _seedId;
        private readonly byte[] _dirIdBytes; // Store as raw bytes internally

        // Public getters for the interface
        public string DirId => Base64Url.Encode(_dirIdBytes); // Encode on demand for the interface
        public int SeedId => _seedId;

        /// <summary>
        /// Creates a new directory metadata.
        /// </summary>
        /// <param name="seedId">The masterkey seed ID.</param>
        /// <param name="dirIdBytes">The raw bytes of the directory ID.</param>
        /// <exception cref="ArgumentNullException">If dirIdBytes is null.</exception>
        /// <exception cref="ArgumentException">If dirIdBytes length is invalid.</exception>
        public DirectoryMetadataImpl(int seedId, byte[] dirIdBytes)
        {
            if (dirIdBytes == null) throw new ArgumentNullException(nameof(dirIdBytes));
            if (dirIdBytes.Length != Constants.DIR_ID_SIZE) throw new ArgumentException($"DirId must be {Constants.DIR_ID_SIZE} bytes long.", nameof(dirIdBytes));

            _seedId = seedId;
            _dirIdBytes = (byte[])dirIdBytes.Clone(); // Defensive copy
        }

        /// <summary>
        /// Gets the raw bytes of the directory ID.
        /// </summary>
        /// <returns>A clone of the internal DirId byte array.</returns>
        internal byte[] GetDirIdBytes()
        {
            return (byte[])_dirIdBytes.Clone(); // Return a clone for safety
        }

        /// <summary>
        /// Helper to cast DirectoryMetadata to DirectoryMetadataImpl.
        /// </summary>
        public static DirectoryMetadataImpl Cast(DirectoryMetadata metadata)
        {
            if (metadata is DirectoryMetadataImpl impl)
            {
                return impl;
            }
            throw new ArgumentException("Metadata object is not an instance of DirectoryMetadataImpl.", nameof(metadata));
        }
    }
}