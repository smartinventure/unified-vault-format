using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace UvfLib.Common
{
    /// <summary>
    /// Utility methods for destroying sensitive resources.
    /// </summary>
    public static class Destroyables
    {
        /// <summary>
        /// Securely destroys a byte array by overwriting its content with zeros.
        /// </summary>
        /// <param name="data">The data to destroy</param>
        public static void Destroy(byte[]? data)
        {
            if (data != null)
            {
                CryptographicOperations.ZeroMemory(data);
            }
        }

        /// <summary>
        /// Securely destroys a collection of <see cref="IDisposable"/> objects.
        /// </summary>
        /// <param name="destroyables">The objects to dispose</param>
        public static void DestroyAll(IEnumerable<IDisposable>? destroyables)
        {
            if (destroyables == null)
            {
                return;
            }

            foreach (var destroyable in destroyables)
            {
                destroyable?.Dispose();
            }
        }

        /// <summary>
        /// Cleans up a multidimensional byte array by overwriting all its sub-arrays.
        /// </summary>
        /// <param name="arrays">The arrays to clean</param>
        public static void CleanMultiDimensionalByteArray(params byte[][]? arrays)
        {
            if (arrays == null)
            {
                return;
            }

            foreach (var array in arrays)
            {
                Destroy(array);
            }
        }
    }
} 