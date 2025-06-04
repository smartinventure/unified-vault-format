using System;
using System.Security.Cryptography;

namespace UvfLib.Common
{
    /// <summary>
    /// Factory for hash algorithm instances.
    /// </summary>
    public sealed class MessageDigestSupplier
    {
        /// <summary>
        /// Supplier for SHA-256 hash algorithm
        /// </summary>
        public static readonly MessageDigestSupplier SHA256 = new MessageDigestSupplier("SHA256");

        /// <summary>
        /// Supplier for SHA-384 hash algorithm
        /// </summary>
        public static readonly MessageDigestSupplier SHA384 = new MessageDigestSupplier("SHA384");

        /// <summary>
        /// Supplier for SHA-512 hash algorithm
        /// </summary>
        public static readonly MessageDigestSupplier SHA512 = new MessageDigestSupplier("SHA512");

        private readonly string _algorithm;
        private readonly ObjectPool<HashAlgorithm> _digestPool;

        /// <summary>
        /// Creates a new message digest supplier for the specified algorithm.
        /// </summary>
        /// <param name="algorithm">The hash algorithm name</param>
        public MessageDigestSupplier(string algorithm)
        {
            _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
            _digestPool = new ObjectPool<HashAlgorithm>(CreateDigest);

            // Validate that we can create a digest
            using (ObjectPool<HashAlgorithm>.Lease<HashAlgorithm> lease = _digestPool.Get())
            {
                _ = lease.Get();
            }
        }

        private HashAlgorithm CreateDigest()
        {
            return _algorithm switch
            {
                "SHA256" => SHA256Managed.Create(),
                "SHA384" => SHA384Managed.Create(),
                "SHA512" => SHA512Managed.Create(),
                _ => throw new ArgumentException($"Unsupported hash algorithm: {_algorithm}")
            };
        }

        /// <summary>
        /// Gets a hash algorithm instance.
        /// </summary>
        /// <returns>A lease providing access to a hash algorithm</returns>
        public ObjectPool<HashAlgorithm>.Lease<HashAlgorithm> Get()
        {
            return _digestPool.Get();
        }

        /// <summary>
        /// Computes the hash of the specified data.
        /// </summary>
        /// <param name="data">The data to hash</param>
        /// <returns>The computed hash</returns>
        public byte[] Hash(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using (ObjectPool<HashAlgorithm>.Lease<HashAlgorithm> lease = _digestPool.Get())
            {
                return lease.Get().ComputeHash(data);
            }
        }

        /// <summary>
        /// Computes the hash of a portion of the specified data.
        /// </summary>
        /// <param name="data">The data to hash</param>
        /// <param name="offset">The offset into the data where hashing begins</param>
        /// <param name="count">The number of bytes to hash</param>
        /// <returns>The computed hash</returns>
        public byte[] Hash(byte[] data, int offset, int count)
        {
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

            using (ObjectPool<HashAlgorithm>.Lease<HashAlgorithm> lease = _digestPool.Get())
            {
                return lease.Get().ComputeHash(data, offset, count);
            }
        }
    }
}