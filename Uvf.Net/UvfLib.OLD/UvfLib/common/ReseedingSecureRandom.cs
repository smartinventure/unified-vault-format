using System;
using System.Security.Cryptography;
using System.Threading;

namespace UvfLib.Common
{
    /// <summary>
    /// A secure random number generator that automatically reseeds itself after a certain number of bytes.
    /// </summary>
    public sealed class ReseedingSecureRandom : IDisposable
    {
        private readonly RandomNumberGenerator _seedGenerator;
        private readonly int _reseedThresholdBytes;
        private readonly object _lock = new object();
        
        private RandomNumberGenerator _rng;
        private long _bytesGeneratedSinceReseed;
        private bool _disposed;

        /// <summary>
        /// Creates a new reseeding secure random number generator.
        /// </summary>
        /// <param name="seedGenerator">The random number generator to use for seeding</param>
        /// <param name="reseedThresholdBytes">The number of bytes after which to reseed</param>
        public ReseedingSecureRandom(RandomNumberGenerator seedGenerator, int reseedThresholdBytes)
        {
            _seedGenerator = seedGenerator ?? throw new ArgumentNullException(nameof(seedGenerator));
            
            if (reseedThresholdBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reseedThresholdBytes), "Reseed threshold must be positive");
            }
            
            _reseedThresholdBytes = reseedThresholdBytes;
            _bytesGeneratedSinceReseed = reseedThresholdBytes; // Force a reseed on first use
            _rng = RandomNumberGenerator.Create();
        }

        /// <summary>
        /// Creates a new reseeding secure random number generator using the default reseed threshold of 1 MB.
        /// </summary>
        /// <param name="seedGenerator">The random number generator to use for seeding</param>
        public ReseedingSecureRandom(RandomNumberGenerator seedGenerator)
            : this(seedGenerator, 1024 * 1024) // 1 MB
        {
        }

        /// <summary>
        /// Creates a new reseeding secure random number generator using the system RNG for seeding.
        /// </summary>
        /// <param name="reseedThresholdBytes">The number of bytes after which to reseed</param>
        public ReseedingSecureRandom(int reseedThresholdBytes)
            : this(RandomNumberGenerator.Create(), reseedThresholdBytes)
        {
        }

        /// <summary>
        /// Creates a new reseeding secure random number generator using the system RNG for seeding 
        /// and the default reseed threshold of 1 MB.
        /// </summary>
        public ReseedingSecureRandom()
            : this(RandomNumberGenerator.Create(), 1024 * 1024) // 1 MB
        {
        }

        /// <summary>
        /// Fills a buffer with random bytes.
        /// </summary>
        /// <param name="buffer">The buffer to fill</param>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public void GetBytes(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            
            GetBytes(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Fills a section of a buffer with random bytes.
        /// </summary>
        /// <param name="buffer">The buffer to fill</param>
        /// <param name="offset">The offset at which to start filling</param>
        /// <param name="count">The number of bytes to fill</param>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public void GetBytes(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative");
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative");
            }
            if (offset + count > buffer.Length)
            {
                throw new ArgumentException("The sum of offset and count is larger than the buffer length");
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ReseedingSecureRandom));
                }

                // Check if we need to reseed
                if (_bytesGeneratedSinceReseed >= _reseedThresholdBytes)
                {
                    Reseed();
                }

                // Generate random bytes
                _rng.GetBytes(buffer, offset, count);
                
                // Update statistics
                _bytesGeneratedSinceReseed += count;
            }
        }

        /// <summary>
        /// Forces a reseed of the random number generator.
        /// </summary>
        /// <exception cref="ObjectDisposedException">If this instance has been disposed</exception>
        public void Reseed()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(ReseedingSecureRandom));
                }
                
                // Create a new RNG (dispose the old one)
                var oldRng = _rng;
                _rng = RandomNumberGenerator.Create();
                oldRng.Dispose();
                
                // Reset the counter
                _bytesGeneratedSinceReseed = 0;
            }
        }

        /// <summary>
        /// Disposes this instance, releasing all resources.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (!_disposed)
                {
                    _rng.Dispose();
                    _disposed = true;
                }
            }
        }
    }
} 