using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Core.Common;
using Moq;
using System;
using System.IO;
using System.Text;
using UvfLib.Core.Api;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class DecryptingReadableByteChannelTest
    {
        private Mock<Cryptor> _cryptor;
        private Mock<IFileContentCryptor> _contentCryptor;
        private Mock<FileHeaderCryptor> _headerCryptor;
        private Mock<FileHeader> _header;

        [TestInitialize]
        public void Setup()
        {
            _cryptor = new Mock<Cryptor>();
            _contentCryptor = new Mock<IFileContentCryptor>();
            _headerCryptor = new Mock<FileHeaderCryptor>();
            _header = new Mock<FileHeader>();

            _cryptor.Setup(c => c.FileContentCryptor()).Returns(_contentCryptor.Object);
            _cryptor.Setup(c => c.FileHeaderCryptor()).Returns(_headerCryptor.Object);

            _contentCryptor.Setup(c => c.CleartextChunkSize()).Returns(10);
            _contentCryptor.Setup(c => c.CiphertextChunkSize()).Returns(10);

            _headerCryptor.Setup(h => h.HeaderSize()).Returns(5);
            _headerCryptor.Setup(h => h.DecryptHeader(It.IsAny<ReadOnlyMemory<byte>>())).Returns(_header.Object);

            _contentCryptor.Setup(c => c.DecryptChunk(
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<Memory<byte>>(),
                    It.IsAny<long>(),
                    It.IsAny<FileHeader>(),
                    It.IsAny<bool>()))
                .Returns<ReadOnlyMemory<byte>, Memory<byte>, long, FileHeader, bool>(
                    (data, output, chunkNumber, header, auth) =>
                    {
                        // Simulate conversion to lowercase for testing purposes
                        // Note: This mock assumes the input 'data' size matches the expected cleartext size for the chunk
                        string content = Encoding.UTF8.GetString(data.Span);
                        byte[] result = Encoding.UTF8.GetBytes(content.ToLower());

                        // Ensure output buffer is large enough (Moq might pass a smaller one)
                        if (output.Length < result.Length)
                        {
                            throw new ArgumentException("Mock output buffer too small");
                        }

                        result.CopyTo(output.Span.Slice(0, result.Length));
                        // Return the number of bytes written
                        return result.Length;
                    });
        }

        [TestMethod]
        [DisplayName("Test Decryption")]
        public void TestDecryption()
        {
            // Create a source stream with test data
            byte[] sourceData = Encoding.UTF8.GetBytes("hhhhhTOPSECRET!TOPSECRET!");
            using (MemoryStream source = new MemoryStream(sourceData))
            {
                byte[] resultBuffer = new byte[30];

                // Create decrypting channel - use existing 4-arg constructor with MemoryStream
                int blockSize = 1024; // Provide block size
                using (var channel = new DecryptingReadableByteChannel(source, (Cryptor)_cryptor.Object, blockSize, true))
                {
                    // Read data from the channel
                    int bytesRead1 = channel.Read(resultBuffer, 0, resultBuffer.Length);
                    Assert.AreEqual(20, bytesRead1);

                    // Try to read more (should return 0 indicating EOF)
                    int bytesRead2 = channel.Read(resultBuffer, bytesRead1, resultBuffer.Length - bytesRead1);
                    Assert.AreEqual(0, bytesRead2);

                    // Verify the decrypted content
                    byte[] decryptedData = new byte[bytesRead1];
                    Array.Copy(resultBuffer, 0, decryptedData, 0, bytesRead1);
                    CollectionAssert.AreEqual(
                        Encoding.UTF8.GetBytes("topsecret!topsecret!"),
                        decryptedData);
                }
            }

            // Verify the expected method calls
            _headerCryptor.Verify(h => h.DecryptHeader(It.IsAny<ReadOnlyMemory<byte>>()), Times.Once);
            _contentCryptor.Verify(c => c.DecryptChunk(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<Memory<byte>>(),
                It.Is<long>(chunkNum => chunkNum == 0),
                It.Is<FileHeader>(h => h == _header.Object),
                It.Is<bool>(auth => auth == true)),
                Times.Once); // Verify first chunk
            _contentCryptor.Verify(c => c.DecryptChunk(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<Memory<byte>>(),
                It.Is<long>(chunkNum => chunkNum == 1),
                It.Is<FileHeader>(h => h == _header.Object),
                It.Is<bool>(auth => auth == true)),
                Times.Once); // Verify second chunk
        }

        [TestMethod]
        [DisplayName("Test Random Access Decryption")]
        public void TestRandomAccessDecryption()
        {
            // Create a source stream with test data
            byte[] sourceData = Encoding.UTF8.GetBytes("TOPSECRET!");
            using (MemoryStream source = new MemoryStream(sourceData))
            {
                byte[] resultBuffer = new byte[30];

                // Create decrypting channel - use existing 6-arg random access constructor with MemoryStream
                int blockSize = 1024; // Provide block size
                using (var channel = new DecryptingReadableByteChannel(source, (Cryptor)_cryptor.Object, blockSize, true, _header.Object, 1))
                {
                    // Read data from the channel
                    int bytesRead1 = channel.Read(resultBuffer, 0, resultBuffer.Length);
                    Assert.AreEqual(10, bytesRead1);

                    // Try to read more (should return 0 indicating EOF)
                    int bytesRead2 = channel.Read(resultBuffer, bytesRead1, resultBuffer.Length - bytesRead1);
                    Assert.AreEqual(0, bytesRead2);

                    // Verify the decrypted content
                    byte[] decryptedData = new byte[bytesRead1];
                    Array.Copy(resultBuffer, 0, decryptedData, 0, bytesRead1);
                    CollectionAssert.AreEqual(
                        Encoding.UTF8.GetBytes("topsecret!"),
                        decryptedData);
                }
            }

            // Verify the expected method calls
            _headerCryptor.Verify(h => h.DecryptHeader(It.IsAny<ReadOnlyMemory<byte>>()), Times.Never); // Header provided, shouldn't be decrypted again
            _contentCryptor.Verify(c => c.DecryptChunk(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<Memory<byte>>(),
                It.Is<long>(chunkNum => chunkNum == 1),
                It.Is<FileHeader>(h => h == _header.Object),
                It.Is<bool>(auth => auth == true)),
                Times.Once); // Verify only chunk 1 is decrypted
        }
    }
}