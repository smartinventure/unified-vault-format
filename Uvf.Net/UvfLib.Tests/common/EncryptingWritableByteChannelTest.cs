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
    public class EncryptingWritableByteChannelTest
    {
        private Mock<Cryptor> _cryptor;
        private Mock<IFileContentCryptor> _contentCryptor;
        private Mock<FileHeaderCryptor> _headerCryptor;
        private Mock<FileHeader> _header;
        private MemoryStream _out;

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

            _headerCryptor.Setup(h => h.Create()).Returns(_header.Object);
            _headerCryptor.Setup(h => h.EncryptHeader(_header.Object)).Returns(new Memory<byte>(Encoding.UTF8.GetBytes("hhhhh")));

            _contentCryptor.Setup(c => c.EncryptChunk(
                    It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<long>(),
                    It.IsAny<FileHeader>()))
                .Returns<ReadOnlyMemory<byte>, long, FileHeader>((data, chunkNumber, header) =>
                {
                    // Simulate conversion to uppercase and wrapping with < > for testing
                    string content = Encoding.UTF8.GetString(data.ToArray());
                    return new Memory<byte>(Encoding.UTF8.GetBytes("<" + content.ToUpper() + ">"));
                });

            // _out will be initialized within each test method now
            // _out = new MemoryStream();
        }

        [TestMethod]
        [DisplayName("Test Encryption")]
        public void TestEncryption()
        {
            // Initialize _out for this test
            _out = new MemoryStream();

            // Create channel within using block for proper disposal
            using (var channel = new EncryptingWritableByteChannel(_out, _cryptor.Object, leaveOpen: true))
            {
                byte[] data1 = Encoding.UTF8.GetBytes("hello world 1");
                channel.Write(data1, 0, data1.Length); // Use local channel variable

                byte[] data2 = Encoding.UTF8.GetBytes("hello world 2");
                channel.Write(data2, 0, data2.Length); // Use local channel variable

                // No explicit Close() needed, using block handles it
                // channel.Close();
            } // channel is closed and disposed here, but _out remains open

            // Reset stream position AFTER the using block
            _out.Position = 0;

            // Read the encrypted content from _out
            byte[] resultBuffer = new byte[100];
            int bytesRead = _out.Read(resultBuffer, 0, resultBuffer.Length);
            string encrypted = Encoding.UTF8.GetString(resultBuffer, 0, bytesRead);

            // Verify the expected encrypted content (matches Java)
            Assert.AreEqual("hhhhh<HELLO WORL><D 1HELLO W><ORLD 2>", encrypted);
        }

        [TestMethod]
        [DisplayName("Test Encryption Of Empty File")]
        public void TestEncryptionOfEmptyFile()
        {
            // Initialize _out for this test
            _out = new MemoryStream();

            // Create channel within using block
            using (var channel = new EncryptingWritableByteChannel(_out, _cryptor.Object, leaveOpen: true))
            {
                // Write nothing for an empty file
            } // channel is closed and disposed here, but _out remains open

            // Reset stream position AFTER the using block
            _out.Position = 0;
            byte[] resultBytes = new byte[_out.Length]; // Read the actual length written
            _out.Read(resultBytes, 0, resultBytes.Length);
            string resultString = Encoding.UTF8.GetString(resultBytes);

            // Assert against Java's expected output
            Assert.AreEqual("hhhhh<>", resultString, "Encrypting an empty file should result in header + empty chunk marker");
        }
    }
}