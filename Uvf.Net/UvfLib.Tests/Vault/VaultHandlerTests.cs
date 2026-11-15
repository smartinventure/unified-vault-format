using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Vault;

namespace UvfLib.Tests.Vault
{
    [TestClass]
    public class VaultHandlerTests
    {
        private const long HeaderSize = 68;
        private const long ChunkOverhead = 28;
        private const long ChunkSize = 32768;

        [TestMethod]
        [DataRow(0L, 0L)]
        [DataRow(HeaderSize, 0L)]
        [DataRow(HeaderSize + ChunkOverhead + 5, 5L)]
        [DataRow(HeaderSize + ChunkOverhead + ChunkSize, ChunkSize)]
        [DataRow(HeaderSize + ChunkOverhead + ChunkSize + ChunkOverhead + 1, ChunkSize + 1)]
        public void CalculateExpectedDecryptedSize_ValidSize_ReturnsCleartextSize(long encryptedSize, long expectedSize)
        {
            Assert.AreEqual(expectedSize, VaultHandler.CalculateExpectedDecryptedSize(encryptedSize));
        }

        [TestMethod]
        [DataRow(1L)]
        [DataRow(HeaderSize - 1)]
        public void CalculateExpectedDecryptedSize_SizeBetweenZeroAndHeader_ThrowsArgumentException(long encryptedSize)
        {
            Assert.ThrowsException<ArgumentException>(() => VaultHandler.CalculateExpectedDecryptedSize(encryptedSize));
        }
    }
}
