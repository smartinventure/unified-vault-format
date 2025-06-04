using System;
using UvfLib.Common;

namespace UvfLib.Api
{
    /// <summary>
    /// A master key with versioning capability (for key rotation).
    /// </summary>
    public interface RevolvingMasterkey : Masterkey
    {
        /// <summary>
        /// Gets the current revision of this key.
        /// </summary>
        /// <returns>The current revision</returns>
        int GetCurrentRevision();

        /// <summary>
        /// Gets the initial revision of this key.
        /// </summary>
        /// <returns>The initial revision</returns>
        int GetInitialRevision();

        /// <summary>
        /// Gets the first revision of this key.
        /// </summary>
        /// <returns>The first revision</returns>
        int GetFirstRevision();

        /// <summary>
        /// Gets the root directory ID.
        /// </summary>
        /// <returns>The root directory ID</returns>
        byte[] GetRootDirId();

        /// <summary>
        /// Checks if this key has the given revision.
        /// </summary>
        /// <param name="revision">The revision to check</param>
        /// <returns>True if the key has the given revision, false otherwise</returns>
        bool HasRevision(int revision);

        /// <summary>
        /// Gets the current master key.
        /// </summary>
        /// <returns>The current master key</returns>
        DestroyableMasterkey Current();

        /// <summary>
        /// Gets a master key by its seed ID.
        /// </summary>
        /// <param name="seedId">The seed ID</param>
        /// <returns>The master key</returns>
        /// <exception cref="ArgumentException">If no key with the given seed ID exists</exception>
        DestroyableMasterkey GetBySeedId(string seedId);

        /// <summary>
        /// Derive a key from this master key.
        /// </summary>
        /// <param name="seedId">Seed identifier</param>
        /// <param name="size">Key size in bytes</param>
        /// <param name="context">Context for key derivation</param>
        /// <param name="algorithm">Algorithm for which this key will be used</param>
        /// <returns>A derived key that must be destroyed after usage</returns>
        DestroyableSecretKey SubKey(int seedId, int size, byte[] context, string algorithm);
    }
}