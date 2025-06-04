using System;
using System.IO;
using UvfLib.Core.Common;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using UvfLib.Core.Api;

namespace UvfLib.Tests.Common
{
    /// <summary>
    /// Simplified test version of EncryptingWritableByteChannel.
    /// Does not perform actual encryption, only logs.
    /// </summary>
    internal class TestEncryptingWritableByteChannel : IWritableByteChannel, IDisposable // Renamed class
    {
        private readonly IWritableByteChannel _sink;
        private readonly ICryptor _cryptor;
        private bool _closed;

        /// <summary>
        /// Simplified constructor for testing.
        /// </summary>
        /// <param name="sink">The underlying channel to write (non-encrypted) data to.</param>
        /// <param name="cryptor">The cryptor (ignored in this test version).</param>
        public TestEncryptingWritableByteChannel(IWritableByteChannel sink, Cryptor cryptor)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _cryptor = (ICryptor)(cryptor ?? throw new ArgumentNullException(nameof(cryptor)));
            _closed = false;
            Console.WriteLine("Warning: Using simplified TestEncryptingWritableByteChannel (Test Version). No encryption will occur.");
        }

        public bool IsOpen => !_closed;

        public async Task<int> Write(byte[] src)
        {
            if (_closed)
            {
                throw new IOException("Channel closed");
            }
            Console.WriteLine($"Warning: Simplified Write called with {src.Length} bytes. Data not encrypted.");
            // In a real scenario, encryption would happen here.
            // For the test version, we just return the count as if write succeeded.
            // Optionally, write plain data to sink for verification?
            await _sink.Write(src);
            return src.Length;
        }

        // Add synchronous Write overload needed by benchmark
        public int Write(byte[] buffer, int offset, int count)
        {
            if (_closed)
            {
                throw new IOException("Channel closed");
            }
            Console.WriteLine($"Warning: Simplified sync Write called with {count} bytes. Data not encrypted.");
            // In a real scenario, encryption and writing to _sink would happen here.
            // For the test version, we just return the count as if write succeeded.
            // Optionally, delegate to sink if it supports sync write:
            if (_sink is Stream sinkStream) { sinkStream.Write(buffer, offset, count); return count; }
            // else { /* handle non-stream sink or throw */ }
            // Fallback if sink is not a stream but supports sync write (assuming IWritableByteChannel has a sync Write)
            // This requires IWritableByteChannel to define a sync Write(byte[], int, int)
            // If not, we might need a different approach or ensure _sink is always a Stream for this test class.
            // For now, let's assume the stream case covers the usage in tests.
            else
            {
                // Throw exception as IWritableByteChannel only guarantees async Write(byte[])
                throw new NotSupportedException("Synchronous write with offset/count is not supported by the underlying IWritableByteChannel sink in this test stub.");
                // If the sink doesn't support synchronous write directly via Stream,
                // try calling its own Write method if the interface defines it.
                // This assumes IWritableByteChannel might have its own sync Write.
                // If _sink.Write isn't available or doesn't match, this needs refinement.
                // return _sink.Write(buffer, offset, count); // This line caused CS1501
                // If no sync write is possible, throw or return an error indicator:
                // throw new NotSupportedException("Synchronous write not supported by the underlying sink.");
            }
            // Original return count removed as write is now attempted.
        }

        public void Close()
        {
            if (!_closed)
            {
                _closed = true;
                _sink.Close();
                Console.WriteLine("Simplified TestEncryptingWritableByteChannel closed.");
            }
        }

        // Added Dispose method
        public void Dispose()
        {
            // Nothing specific to dispose in this simplified version, but implement the interface.
            Close();
        }

        // Other methods from ISeekableByteChannel could be added here if needed
        // throwing NotImplementedException, e.g.:
        // public long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        // public long Size() => throw new NotImplementedException();
        // public ISeekableByteChannel Truncate(long size) => throw new NotImplementedException();
    }
}