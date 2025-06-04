using System;

namespace UvfLib.Api
{
    /// <summary>
    /// Represents a master key, providing access to raw key material and destroy capability.
    /// (Original structure before refactoring attempt)
    /// </summary>
    public interface Masterkey : IDisposable
    {
        /// <summary>
        /// Gets a copy of the raw key material. Caller is responsible for zeroing out the memory when done.
        /// </summary>
        /// <returns>The raw key material</returns>
        byte[] GetRaw();

        /// <summary>
        /// Securely destroys the key material.
        /// </summary>
        void Destroy();

        /// <summary>
        /// Checks if the key has been destroyed.
        /// </summary>
        /// <returns>True if the key has been destroyed, false otherwise</returns>
        bool IsDestroyed(); // Method, not property
    }
}