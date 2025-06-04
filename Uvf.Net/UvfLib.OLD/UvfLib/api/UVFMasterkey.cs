using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace UvfLib.Api
{
    /// <summary>
    /// A master key for the Universal Vault Format.
    /// </summary>
    public interface UVFMasterkey : RevolvingMasterkey
    {
        /// <summary>
        /// The dictionary of seed IDs to seed values.
        /// </summary>
        Dictionary<int, byte[]> Seeds { get; }

        /// <summary>
        /// The KDF salt used for key derivation.
        /// </summary>
        byte[] KdfSalt { get; }

        /// <summary>
        /// The initial seed ID.
        /// </summary>
        int InitialSeed { get; }

        /// <summary>
        /// The latest seed ID.
        /// </summary>
        int LatestSeed { get; }

        /// <summary>
        /// The root directory ID.
        /// </summary>
        byte[] RootDirId { get; }

        /// <summary>
        /// The first revision.
        /// </summary>
        int FirstRevision { get; }

        /// <summary>
        /// Gets the root directory ID.
        /// </summary>
        /// <returns>The root directory ID</returns>
        byte[] GetRootDirId() => RootDirId;

        /// <summary>
        /// Gets the first revision.
        /// </summary>
        /// <returns>The first revision</returns>
        int GetFirstRevision() => FirstRevision;

        /// <summary>
        /// Gets the current revision (method interface for Java compatibility).
        /// </summary>
        /// <returns>The current revision</returns>
        int CurrentRevision() => GetCurrentRevision();

        /// <summary>
        /// Creates a new UVF master key from raw data.
        /// </summary>
        /// <param name="rawKey">The raw key material</param>
        /// <returns>A new UVF master key</returns>
        /// <exception cref="ArgumentNullException">If rawKey is null</exception>
        /// <exception cref="ArgumentException">If rawKey is invalid</exception>
        public static UVFMasterkey CreateFromRaw(byte[] rawKey)
        {
            // Allow implementation to be provided by the concrete implementation class
            return V3.UVFMasterkeyImpl.CreateFromRaw(rawKey);
        }

        /// <summary>
        /// Creates a UVF masterkey from a JSON payload.
        /// </summary>
        /// <param name="json">The JSON payload</param>
        /// <returns>A UVF masterkey</returns>
        public static UVFMasterkey FromDecryptedPayload(string json)
        {
            // Allow implementation to be provided by the concrete implementation class
            return V3.UVFMasterkeyImpl.FromDecryptedPayload(json);
        }

        /// <summary>
        /// Gets the version of this master key.
        /// </summary>
        /// <returns>The version</returns>
        public int Version();

        /// <summary>
        /// Creates a new independent copy of this master key.
        /// </summary>
        /// <returns>A new copy of this master key</returns>
        [return: MaybeNull]
        public UVFMasterkey Copy();

        /// <summary>
        /// Gets key data based on the master key, that can be used to derive secrets.
        /// </summary>
        /// <param name="context">The context for the derivation (will be UTF-8 encoded)</param>
        /// <returns>The derived key data</returns>
        public byte[] KeyData(string context);

        /// <summary>
        /// Gets key data based on the master key, that can be used to derive secrets.
        /// </summary>
        /// <param name="context">The context for the derivation</param>
        /// <returns>The derived key data</returns>
        public byte[] KeyData(byte[] context);

        /// <summary>
        /// Gets a deterministically generated unique key identifier for this key.
        /// </summary>
        /// <returns>A key identifier that is unique to this key</returns>
        public byte[] KeyID();

        /// <summary>
        /// The key ID as a hexadecimal string.
        /// </summary>
        /// <returns>The key ID as a hexadecimal string</returns>
        public string KeyIDHex();
    }
}