using System;
using System.IO;
using FolderMagicLib.Logging;

namespace UvfLib.Storage.Abstractions.Throttling
{
    /// <summary>
    /// Implementation of the throttling service that applies read/write speed limits to streams.
    /// </summary>
    public class ThrottlingService : IThrottlingService
    {
        private static readonly Logging.Logging _logger = Logging.Logging.Instance;

        /// <summary>
        /// Wraps the provided stream with throttling for read operations.
        /// If the throttling rate is 0, returns the original stream unchanged for better performance.
        /// </summary>
        /// <param name="stream">The base stream to throttle</param>
        /// <param name="maxBytesPerSecond">Maximum bytes per second (0 = no throttling)</param>
        /// <returns>A throttled stream or the original if no throttling is needed</returns>
        public Stream ApplyReadThrottling(Stream stream, long maxBytesPerSecond)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // If throttling is disabled (0) or the stream is not readable, return the original stream
            if (maxBytesPerSecond == 0 || !stream.CanRead)
                return stream;

            _logger.LogInfo($"Applying read throttling: {maxBytesPerSecond} bytes/sec");
            return new ThrottledStreamStopWatch(stream, maxBytesPerSecond);
        }

        /// <summary>
        /// Wraps the provided stream with throttling for write operations.
        /// If the throttling rate is 0, returns the original stream unchanged for better performance.
        /// </summary>
        /// <param name="stream">The base stream to throttle</param>
        /// <param name="maxBytesPerSecond">Maximum bytes per second (0 = no throttling)</param>
        /// <returns>A throttled stream or the original if no throttling is needed</returns>
        public Stream ApplyWriteThrottling(Stream stream, long maxBytesPerSecond)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // If throttling is disabled (0) or the stream is not writable, return the original stream
            if (maxBytesPerSecond == 0 || !stream.CanWrite)
                return stream;

            _logger.LogInfo($"Applying write throttling: {maxBytesPerSecond} bytes/sec");
            return new ThrottledStreamStopWatch(stream, maxBytesPerSecond);
        }
    }
}