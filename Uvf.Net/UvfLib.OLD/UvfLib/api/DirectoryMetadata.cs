using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Represents directory metadata stored in dir.uvf files.
    /// According to the UVF specification, this contains only the directory ID and seed information.
    /// </summary>
    public interface DirectoryMetadata
    {
        /// <summary>
        /// Gets the Base64Url encoded Directory ID.
        /// This ID is unique for each directory in the vault.
        /// </summary>
        string DirId { get; }

        /// <summary>
        /// Gets the masterkey seed ID (often referred to as revision) 
        /// used for cryptographic operations related to this directory and its direct children's names.
        /// </summary>
        int SeedId { get; }
    }
} 