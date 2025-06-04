using System;
using System.Security.Cryptography;

namespace UvfLib.Common
{
    /// <summary>
    /// Factory for message authentication code (MAC) algorithm instances.
    /// </summary>
    public sealed class MacSupplier
    {
        /// <summary>
        /// Supplier for HMAC-SHA-256 message authentication code
        /// </summary>
        public static readonly MacSupplier HMAC_SHA256 = new MacSupplier("HMAC-SHA256");

        /// <summary>
        /// Supplier for HMAC-SHA-384 message authentication code
        /// </summary>
        public static readonly MacSupplier HMAC_SHA384 = new MacSupplier("HMAC-SHA384");

        /// <summary>
        /// Supplier for HMAC-SHA-512 message authentication code
        /// </summary>
        public static readonly MacSupplier HMAC_SHA512 = new MacSupplier("HMAC-SHA512");

        private readonly string _algorithm;

        /// <summary>
        /// Creates a new MAC supplier for the specified algorithm.
        /// </summary>
        /// <param name="algorithm">The MAC algorithm name</param>
        public MacSupplier(string algorithm)
        {
            _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));

            // Validate that we can create a MAC
            using (CreateMac(new byte[1]))
            {
                // Just testing if the algorithm is supported
            }
        }

        /// <summary>
        /// Creates a new MAC instance with the specified key.
        /// </summary>
        /// <param name="key">The key for the MAC</param>
        /// <returns>A new MAC instance</returns>
        public HMAC CreateMac(byte[] key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            if (key.Length == 0)
            {
                throw new ArgumentException("Key cannot be empty.", nameof(key));
            }

            return _algorithm switch
            {
                "HMAC-SHA256" => new HMACSHA256(key),
                "HMAC-SHA384" => new HMACSHA384(key),
                "HMAC-SHA512" => new HMACSHA512(key),
                _ => throw new ArgumentException($"Unsupported MAC algorithm: {_algorithm}")
            };
        }

        /// <summary>
        /// Creates a new MAC with the provided key and computes the MAC for the given data.
        /// </summary>
        /// <param name="key">The key for the MAC</param>
        /// <param name="data">The data to authenticate</param>
        /// <returns>The computed MAC</returns>
        public byte[] ComputeMac(byte[] key, byte[] data)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using (HMAC mac = CreateMac(key))
            {
                return mac.ComputeHash(data);
            }
        }

        /// <summary>
        /// Creates a new MAC with the provided key and computes the MAC for a portion of the given data.
        /// </summary>
        /// <param name="key">The key for the MAC</param>
        /// <param name="data">The data to authenticate</param>
        /// <param name="offset">The offset into the data where authentication begins</param>
        /// <param name="count">The number of bytes to authenticate</param>
        /// <returns>The computed MAC</returns>
        public byte[] ComputeMac(byte[] key, byte[] data, int offset, int count)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative");
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative");
            }
            if (offset + count > data.Length)
            {
                throw new ArgumentException("The sum of offset and count is larger than the buffer length");
            }

            using (HMAC mac = CreateMac(key))
            {
                return mac.ComputeHash(data, offset, count);
            }
        }

        /// <summary>
        /// Verifies a MAC for the given data.
        /// </summary>
        /// <param name="key">The key for the MAC</param>
        /// <param name="data">The data to authenticate</param>
        /// <param name="expectedMac">The expected MAC</param>
        /// <returns>True if the MAC is valid, false otherwise</returns>
        public bool VerifyMac(byte[] key, byte[] data, byte[] expectedMac)
        {
            if (key == null || data == null || expectedMac == null)
            {
                return false;
            }

            byte[] actualMac = ComputeMac(key, data);
            return CryptographicOperations.FixedTimeEquals(actualMac, expectedMac);
        }

        /// <summary>
        /// Verifies a MAC for a portion of the given data.
        /// </summary>
        /// <param name="key">The key for the MAC</param>
        /// <param name="data">The data to authenticate</param>
        /// <param name="offset">The offset into the data where authentication begins</param>
        /// <param name="count">The number of bytes to authenticate</param>
        /// <param name="expectedMac">The expected MAC</param>
        /// <returns>True if the MAC is valid, false otherwise</returns>
        public bool VerifyMac(byte[] key, byte[] data, int offset, int count, byte[] expectedMac)
        {
            if (key == null || data == null || expectedMac == null)
            {
                return false;
            }
            if (offset < 0 || count < 0 || offset + count > data.Length)
            {
                return false;
            }

            byte[] actualMac = ComputeMac(key, data, offset, count);
            return CryptographicOperations.FixedTimeEquals(actualMac, expectedMac);
        }
    }
}