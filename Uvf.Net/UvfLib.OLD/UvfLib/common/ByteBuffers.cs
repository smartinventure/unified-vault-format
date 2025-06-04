using System;
using System.Buffers.Binary;

namespace UvfLib.Common
{
    /// <summary>
    /// Utility methods for working with byte buffers.
    /// </summary>
    public static class ByteBuffers
    {
        /// <summary>
        /// Concatenates multiple byte arrays into a single array.
        /// </summary>
        /// <param name="arrays">The arrays to concatenate</param>
        /// <returns>A new array containing all input arrays concatenated</returns>
        public static byte[] Concat(params byte[][] arrays)
        {
            if (arrays == null || arrays.Length == 0)
            {
                return Array.Empty<byte>();
            }

            int totalLength = 0;
            foreach (byte[] array in arrays)
            {
                if (array != null)
                {
                    totalLength += array.Length;
                }
            }

            byte[] result = new byte[totalLength];
            int offset = 0;
            foreach (byte[] array in arrays)
            {
                if (array != null && array.Length > 0)
                {
                    Buffer.BlockCopy(array, 0, result, offset, array.Length);
                    offset += array.Length;
                }
            }

            return result;
        }

        /// <summary>
        /// Converts a long value to a big-endian byte array.
        /// </summary>
        /// <param name="value">The value to convert</param>
        /// <returns>A big-endian byte array representation</returns>
        public static byte[] LongToByteArray(long value)
        {
            byte[] bytes = new byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            return bytes;
        }

        /// <summary>
        /// Converts an int value to a big-endian byte array.
        /// </summary>
        /// <param name="value">The value to convert</param>
        /// <returns>A big-endian byte array representation</returns>
        public static byte[] IntToByteArray(int value)
        {
            byte[] bytes = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            return bytes;
        }

        /// <summary>
        /// Converts a big-endian byte array to a long value.
        /// </summary>
        /// <param name="bytes">The byte array</param>
        /// <returns>The long value</returns>
        public static long ByteArrayToLong(byte[] bytes)
        {
            if (bytes == null || bytes.Length != sizeof(long))
            {
                throw new ArgumentException("Byte array must be 8 bytes long", nameof(bytes));
            }
            return BinaryPrimitives.ReadInt64BigEndian(bytes);
        }

        /// <summary>
        /// Converts a big-endian byte array to an int value.
        /// </summary>
        /// <param name="bytes">The byte array</param>
        /// <returns>The int value</returns>
        public static int ByteArrayToInt(byte[] bytes)
        {
            if (bytes == null || bytes.Length != sizeof(int))
            {
                throw new ArgumentException("Byte array must be 4 bytes long", nameof(bytes));
            }
            return BinaryPrimitives.ReadInt32BigEndian(bytes);
        }

        /// <summary>
        /// Creates a new array with the specified length and copies the content of the source array to it.
        /// </summary>
        /// <param name="src">The source array</param>
        /// <param name="length">The length of the new array</param>
        /// <returns>A new array with the copied content</returns>
        public static byte[] CopyOf(byte[] src, int length)
        {
            if (src == null)
            {
                throw new ArgumentNullException(nameof(src));
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative");
            }

            byte[] dest = new byte[length];
            int copyLength = Math.Min(src.Length, length);
            if (copyLength > 0)
            {
                Buffer.BlockCopy(src, 0, dest, 0, copyLength);
            }
            return dest;
        }
    }
} 