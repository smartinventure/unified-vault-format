using System;

namespace UvfLib.Core.Api
{
    /// <summary>
    /// Parameters for key derivation functions used in UVF vaults.
    /// Provides backward-compatible defaults and opt-in Scrypt support.
    /// </summary>
    public class KeyDerivationParameters
    {
        /// <summary>
        /// The key derivation method to use (DEFAULT: PBKDF2_HMAC_SHA512).
        /// </summary>
        public KeyDerivationMethod Method { get; set; } = KeyDerivationMethod.PBKDF2_HMAC_SHA512;

        // PBKDF2 Parameters (CURRENT DEFAULTS)
        /// <summary>
        /// PBKDF2 iteration count (DEFAULT: 210,000 — OWASP 2023 guidance for PBKDF2-HMAC-SHA512).
        /// Higher values increase security but slow down key derivation. Existing vaults keep opening at
        /// their stored iteration count (read from the JWE 'p2c' header); this default only affects new vaults.
        /// </summary>
        public int Pbkdf2Iterations { get; set; } = 210000;

        // Scrypt Parameters (Only used when Method = Scrypt)
        /// <summary>
        /// Scrypt CPU/memory cost parameter N (default: 32768 = 2^15).
        /// Must be a power of 2. Higher values increase security and memory usage.
        /// Only used when Method = Scrypt.
        /// </summary>
        public int ScryptN { get; set; } = 32768; // 2^15, matches Cryptomator

        /// <summary>
        /// Scrypt block size parameter r (default: 8).
        /// Only used when Method = Scrypt.
        /// </summary>
        public int ScryptR { get; set; } = 8;

        /// <summary>
        /// Scrypt parallelization parameter p (default: 1).
        /// Only used when Method = Scrypt.
        /// </summary>
        public int ScryptP { get; set; } = 1;

        /// <summary>
        /// Creates default PBKDF2 parameters (STANDARD DEFAULT).
        /// This maintains backward compatibility with existing vaults.
        /// </summary>
        public static KeyDerivationParameters Default() => new KeyDerivationParameters();

        /// <summary>
        /// Creates default Scrypt parameters (opt-in enhancement).
        /// Uses parameters compatible with Cryptomator for consistency.
        /// </summary>
        public static KeyDerivationParameters Scrypt() => new KeyDerivationParameters
        {
            Method = KeyDerivationMethod.Scrypt,
            ScryptN = 32768, // 2^15
            ScryptR = 8,
            ScryptP = 1
        };

        /// <summary>
        /// Creates high-security Scrypt parameters for enhanced protection.
        /// Uses higher N value for increased memory hardness.
        /// </summary>
        public static KeyDerivationParameters HighSecurityScrypt() => new KeyDerivationParameters
        {
            Method = KeyDerivationMethod.Scrypt,
            ScryptN = 131072, // 2^17
            ScryptR = 8,
            ScryptP = 1
        };

        /// <summary>
        /// Creates custom PBKDF2 parameters with specified iteration count.
        /// </summary>
        /// <param name="iterations">PBKDF2 iteration count</param>
        public static KeyDerivationParameters Pbkdf2(int iterations) => new KeyDerivationParameters
        {
            Method = KeyDerivationMethod.PBKDF2_HMAC_SHA512,
            Pbkdf2Iterations = iterations
        };

        /// <summary>
        /// Creates custom Scrypt parameters.
        /// </summary>
        /// <param name="n">CPU/memory cost parameter (must be power of 2)</param>
        /// <param name="r">Block size parameter</param>
        /// <param name="p">Parallelization parameter</param>
        public static KeyDerivationParameters Scrypt(int n, int r, int p) => new KeyDerivationParameters
        {
            Method = KeyDerivationMethod.Scrypt,
            ScryptN = n,
            ScryptR = r,
            ScryptP = p
        };

        /// <summary>
        /// Validates the parameters for the selected key derivation method.
        /// </summary>
        public void Validate()
        {
            switch (Method)
            {
                case KeyDerivationMethod.PBKDF2_HMAC_SHA512:
                    if (Pbkdf2Iterations < 100000)
                        throw new ArgumentException("PBKDF2 iterations must be at least 100,000 for security");
                    break;

                case KeyDerivationMethod.Scrypt:
                    if (ScryptN < 2 || (ScryptN & (ScryptN - 1)) != 0)
                        throw new ArgumentException("Scrypt N must be > 1 and a power of 2");
                    if (ScryptR <= 0)
                        throw new ArgumentException("Scrypt r must be positive");
                    if (ScryptP <= 0)
                        throw new ArgumentException("Scrypt p must be positive");
                    break;

                default:
                    throw new ArgumentException($"Unsupported key derivation method: {Method}");
            }
        }

        /// <summary>
        /// Returns a string representation of the key derivation parameters.
        /// </summary>
        public override string ToString()
        {
            return Method switch
            {
                KeyDerivationMethod.PBKDF2_HMAC_SHA512 => $"PBKDF2-HMAC-SHA512(iterations={Pbkdf2Iterations})",
                KeyDerivationMethod.Scrypt => $"Scrypt(N={ScryptN}, r={ScryptR}, p={ScryptP})",
                _ => $"Unknown({Method})"
            };
        }
    }
} 