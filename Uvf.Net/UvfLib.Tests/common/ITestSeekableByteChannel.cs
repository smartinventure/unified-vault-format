using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// Extended interface for testing seekable byte channels, supports explicit implementation
    /// to avoid signature conflicts with the base interface.
    /// </summary>
    public interface ITestSeekableByteChannel : ISeekableByteChannel, IDisposable
    {
        /// <summary>
        /// Gets whether the channel is open.
        /// </summary>
        bool IsOpen { get; }
        
        /// <summary>
        /// Sets the channel's position.
        /// </summary>
        /// <param name="newPosition">The new position</param>
        /// <returns>The channel</returns>
        new ITestSeekableByteChannel Position(long newPosition);

        /// <summary>
        /// Truncates the channel to the given size.
        /// </summary>
        /// <param name="size">The new size</param>
        /// <returns>The channel</returns>
        ITestSeekableByteChannel Truncate(long size);
    }
} 