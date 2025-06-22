using System;

namespace UvfLib.Core.Api
{
    /// <summary>
    /// Defines the key derivation method used for password-based encryption in UVF vaults.
    /// </summary>
    public enum KeyDerivationMethod
    {
        /// <summary>
        /// PBKDF2 with HMAC-SHA512 (DEFAULT - JWE/JWT standard compliant)
        /// Fast, widely supported, suitable for most use cases.
        /// Default iterations: 64,000
        /// </summary>
        PBKDF2_HMAC_SHA512 = 0,

        /// <summary>
        /// Scrypt key derivation function (memory-hard, more secure against hardware attacks)
        /// Slower but more resistant to ASIC/GPU attacks.
        /// Default parameters: N=32768 (2^15), r=8, p=1
        /// </summary>
        Scrypt = 1
    }
} 