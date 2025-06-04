using System;
using System.IO;

namespace UvfLib.Common
{
    /// <summary>
    /// Interface for a byte channel that supports seeking.
    /// </summary>
    public interface ISeekableByteChannel
    {
        /// <summary>
        /// Gets the current position within the channel.
        /// </summary>
        long CurrentPosition { get; }

        /// <summary>
        /// Gets the size of the channel's content.
        /// </summary>
        long CurrentSize { get; }

        /// <summary>
        /// Sets the position within the channel.
        /// </summary>
        /// <param name="position">The new position</param>
        /// <returns>The updated channel position</returns>
        long Seek(long position);

        /// <summary>
        /// Gets the current position within the channel.
        /// </summary>
        /// <returns>The current position</returns>
        long Position();

        /// <summary>
        /// Sets the position within the channel.
        /// </summary>
        /// <param name="newPosition">The new position</param>
        /// <returns>The channel</returns>
        ISeekableByteChannel Position(long newPosition);

        /// <summary>
        /// Gets the size of the channel's content.
        /// </summary>
        /// <returns>The size</returns>
        long Size();

        /// <summary>
        /// Reads bytes from the channel into a buffer.
        /// </summary>
        /// <param name="buffer">The buffer to read into</param>
        /// <param name="offset">The offset in the buffer to start writing at</param>
        /// <param name="count">The maximum number of bytes to read</param>
        /// <returns>The number of bytes read, or -1 if the end of the channel is reached</returns>
        int Read(byte[] buffer, int offset, int count);

        /// <summary>
        /// Writes bytes from a buffer to the channel.
        /// </summary>
        /// <param name="buffer">The buffer to write from</param>
        /// <param name="offset">The offset in the buffer to start reading from</param>
        /// <param name="count">The number of bytes to write</param>
        /// <returns>The number of bytes written</returns>
        int Write(byte[] buffer, int offset, int count);

        /// <summary>
        /// Closes the channel and releases any associated resources.
        /// </summary>
        void Close();
    }

    /// <summary>
    /// Extension methods for working with seekable byte channels.
    /// </summary>
    public static class SeekableByteChannelExtensions
    {
        /// <summary>
        /// Converts a Stream to an ISeekableByteChannel.
        /// </summary>
        /// <param name="stream">The stream to convert</param>
        /// <returns>A seekable byte channel wrapping the stream</returns>
        public static ISeekableByteChannel AsSeekableByteChannel(this Stream stream)
        {
            return new StreamByteChannel(stream);
        }

        /// <summary>
        /// Adapter class that implements ISeekableByteChannel on top of a Stream.
        /// </summary>
        private class StreamByteChannel : ISeekableByteChannel
        {
            private readonly Stream _stream;

            public StreamByteChannel(Stream stream)
            {
                _stream = stream ?? throw new ArgumentNullException(nameof(stream));
                if (!stream.CanSeek)
                    throw new ArgumentException("Stream must support seeking", nameof(stream));
            }

            public long CurrentPosition => _stream.Position;

            public long CurrentSize => _stream.Length;

            public long Seek(long position)
            {
                return _stream.Seek(position, SeekOrigin.Begin);
            }

            public long Position()
            {
                return _stream.Position;
            }

            public ISeekableByteChannel Position(long newPosition)
            {
                _stream.Position = newPosition;
                return this;
            }

            public long Size()
            {
                return _stream.Length;
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                return _stream.Read(buffer, offset, count);
            }

            public int Write(byte[] buffer, int offset, int count)
            {
                _stream.Write(buffer, offset, count);
                return count;
            }

            public void Close()
            {
                _stream.Close();
            }
        }
    }
}