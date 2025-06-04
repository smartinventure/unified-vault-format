using System;

namespace UvfLib.Api
{
    /// <summary>
    /// A cryptographic file header contains metadata needed to decrypt file contents.
    /// </summary>
    public interface FileHeader : IDisposable
    {
        /// <summary>
        /// Returns a copy of the nonce used in this header.
        /// </summary>
        /// <returns>A copy of the nonce</returns>
        byte[] GetNonce();
        
        /// <summary>
        /// Disposes the header and any associated resources.
        /// </summary>
        void Destroy();
        
        /// <summary>
        /// Checks if the header has been destroyed.
        /// </summary>
        /// <returns>True if the header has been destroyed, false otherwise</returns>
        bool IsDestroyed();
    }
} 