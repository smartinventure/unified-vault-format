using System;

namespace UvfLib.Vault.VaultHelpers
{
    /// <summary>
    /// Represents an in-memory chunk buffer for random write operations in encrypted files.
    /// Each chunk is 32KB of cleartext data that can be modified before encryption.
    /// </summary>
    internal class ChunkBuffer : IDisposable
    {
        private const int CLEARTEXT_CHUNK_SIZE = 32 * 1024; // 32KB
        
        /// <summary>
        /// The chunk number (0-based index)
        /// </summary>
        public long ChunkNumber { get; }
        
        /// <summary>
        /// The cleartext data buffer (always 32KB)
        /// </summary>
        public byte[] Data { get; }
        
        /// <summary>
        /// Whether this chunk has been modified and needs to be written to disk
        /// </summary>
        public bool IsDirty { get; set; }
        
        /// <summary>
        /// The actual size of valid data in this chunk (may be less than 32KB for the last chunk)
        /// </summary>
        public int ValidDataSize { get; set; }
        
        private bool _disposed;

        public ChunkBuffer(long chunkNumber)
        {
            ChunkNumber = chunkNumber;
            Data = new byte[CLEARTEXT_CHUNK_SIZE];
            IsDirty = false;
            ValidDataSize = 0;
        }

        /// <summary>
        /// Writes data to this chunk at the specified offset
        /// </summary>
        /// <param name="offset">Offset within the chunk (0-32767)</param>
        /// <param name="data">Data to write</param>
        /// <param name="dataOffset">Offset in the source data</param>
        /// <param name="count">Number of bytes to write</param>
        public void WriteAt(int offset, byte[] data, int dataOffset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkBuffer));
            if (offset < 0 || offset >= CLEARTEXT_CHUNK_SIZE) 
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset + count > CLEARTEXT_CHUNK_SIZE)
                throw new ArgumentException("Write would exceed chunk boundary");

            Buffer.BlockCopy(data, dataOffset, Data, offset, count);
            
            // Update valid data size if we wrote beyond the current end
            if (offset + count > ValidDataSize)
            {
                ValidDataSize = offset + count;
            }
            
            IsDirty = true;
        }

        /// <summary>
        /// Reads data from this chunk at the specified offset
        /// </summary>
        /// <param name="offset">Offset within the chunk (0-32767)</param>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="bufferOffset">Offset in the destination buffer</param>
        /// <param name="count">Number of bytes to read</param>
        /// <returns>Number of bytes actually read</returns>
        public int ReadAt(int offset, byte[] buffer, int bufferOffset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkBuffer));
            if (offset < 0 || offset >= CLEARTEXT_CHUNK_SIZE) 
                throw new ArgumentOutOfRangeException(nameof(offset));

            // Don't read beyond the valid data
            int availableBytes = Math.Max(0, ValidDataSize - offset);
            int bytesToRead = Math.Min(count, availableBytes);
            
            if (bytesToRead > 0)
            {
                Buffer.BlockCopy(Data, offset, buffer, bufferOffset, bytesToRead);
            }
            
            return bytesToRead;
        }

        /// <summary>
        /// Gets a memory span for the valid data portion of this chunk
        /// </summary>
        public ReadOnlyMemory<byte> GetValidData()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkBuffer));
            return new ReadOnlyMemory<byte>(Data, 0, ValidDataSize);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Clear sensitive data
                Array.Clear(Data, 0, Data.Length);
                _disposed = true;
            }
        }
    }
} 