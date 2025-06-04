using System;
using System.IO;

namespace UvfLib.Common
{
    /// <summary>
    /// Utility class for creating channel objects from streams.
    /// </summary>
    public static class Channels
    {
        /// <summary>
        /// Creates a new readable byte channel from a stream.
        /// </summary>
        /// <param name="stream">The input stream</param>
        /// <returns>A readable byte channel</returns>
        public static Stream NewReadableChannel(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            return stream;
        }

        /// <summary>
        /// Creates a new writable byte channel from a stream.
        /// </summary>
        /// <param name="stream">The output stream</param>
        /// <returns>A writable byte channel</returns>
        public static Stream NewWritableChannel(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            return stream;
        }
    }
}