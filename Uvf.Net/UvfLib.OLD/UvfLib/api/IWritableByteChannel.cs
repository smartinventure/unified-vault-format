using System;
using System.Threading.Tasks;

namespace UvfLib.Api
{
    /// <summary>
    /// Interface for a channel that can write sequences of bytes.
    /// </summary>
    public interface IWritableByteChannel : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether this channel is open.
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Writes a sequence of bytes to this channel from the given buffer.
        /// </summary>
        /// <param name="src">The buffer from which bytes are to be retrieved.</param>
        /// <returns>A task representing the asynchronous write operation. 
        /// The task result contains the number of bytes written, possibly zero.</returns>
        Task<int> Write(byte[] src);

        // Consider adding: Task<int> Write(byte[] src, int offset, int count); ?
        // Or: Task<int> Write(ReadOnlyMemory<byte> src); ?

        /// <summary>
        /// Closes this channel.
        /// </summary>
        void Close();
    }
}