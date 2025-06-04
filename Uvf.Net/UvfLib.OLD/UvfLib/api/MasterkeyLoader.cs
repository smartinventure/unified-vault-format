using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Masterkey loaders load keys to unlock Uvf vaults.
    /// </summary>
    public interface MasterkeyLoader
    {
        /// <summary>
        /// Loads a master key. This might be a long-running operation, as it may require user input or expensive computations.
        /// 
        /// It is the caller's responsibility to destroy the returned <see cref="Masterkey"/> after usage by calling <see cref="Masterkey.Destroy"/>.
        /// This can easily be done using a using statement:
        /// <code>
        /// MasterkeyLoader keyLoader;
        /// Uri keyId;
        /// using (Masterkey key = keyLoader.LoadKey(keyId))
        /// {
        ///     // Do stuff with the key
        /// }
        /// </code>
        /// </summary>
        /// <param name="keyId">A URI uniquely identifying the source and identity of the key</param>
        /// <returns>A <see cref="Masterkey"/> object wrapping the raw key bytes. Must not be null.</returns>
        /// <exception cref="MasterkeyLoadingFailedException">Thrown when it is impossible to fulfill the request</exception>
        Masterkey LoadKey(Uri keyId);
    }
} 