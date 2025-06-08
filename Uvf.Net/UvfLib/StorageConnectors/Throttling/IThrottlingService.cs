using System;
using System.IO;

namespace FolderMagicLib.StorageConnectors.Throttling
{
    /// <summary>
    /// Interface for the throttling service that applies read/write speed limits to streams.
    /// </summary>
    public interface IThrottlingService
    {
        /// <summary>
        /// Wraps the provided stream with throttling for read operations.
        /// If the throttling rate is 0, returns the original stream unchanged.
        /// </summary>
        /// <param name="stream">The base stream to throttle</param>
        /// <param name="maxBytesPerSecond">Maximum bytes per second (0 = no throttling)</param>
        /// <returns>A throttled stream or the original if no throttling is needed</returns>
        Stream ApplyReadThrottling(Stream stream, long maxBytesPerSecond);

        /// <summary>
        /// Wraps the provided stream with throttling for write operations.
        /// If the throttling rate is 0, returns the original stream unchanged.
        /// </summary>
        /// <param name="stream">The base stream to throttle</param>
        /// <param name="maxBytesPerSecond">Maximum bytes per second (0 = no throttling)</param>
        /// <returns>A throttled stream or the original if no throttling is needed</returns>
        Stream ApplyWriteThrottling(Stream stream, long maxBytesPerSecond);
    }
}