using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Core.Api;
using UvfLib.Core.V3;
using Moq;
using System;

namespace UvfLib.Tests.Api
{
    /// <summary>
    /// Interface defining file size calculation logic, mirroring Java test setup.
    /// Contains default implementations for testing purposes.
    /// </summary>
    public interface IFileSizeCalculator
    {
        long CleartextChunkSize();
        long CiphertextChunkSize();

        long CleartextSize(long ciphertextSize)
        {
            long cleartextChunkSize = CleartextChunkSize();
            long ciphertextChunkSize = CiphertextChunkSize();

            if (ciphertextSize < 0)
            {
                throw new ArgumentException("Ciphertext size must not be negative.", nameof(ciphertextSize));
            }
            if (ciphertextChunkSize <= 0 || cleartextChunkSize <= 0 || ciphertextChunkSize <= (ciphertextChunkSize - cleartextChunkSize))
            {
                throw new InvalidOperationException("Chunk sizes must be positive and ciphertext chunk must be larger than cleartext chunk.");
            }
            if (ciphertextSize == 0)
            {
                return 0;
            }

            long overhead = ciphertextChunkSize - cleartextChunkSize;
            if (overhead <= 0)
            {
                throw new InvalidOperationException("Ciphertext chunk size must be greater than cleartext chunk size.");
            }

            long numFullChunks = ciphertextSize / ciphertextChunkSize;
            long remainderCiphertextSize = ciphertextSize % ciphertextChunkSize;

            // If the remainder is non-zero but smaller than the overhead, it's an invalid size.
            if (remainderCiphertextSize > 0 && remainderCiphertextSize < overhead)
            {
                throw new ArgumentException("Invalid ciphertext size (remainder smaller than overhead).", nameof(ciphertextSize));
            }

            long cleartextFromFullChunks = numFullChunks * cleartextChunkSize;
            long cleartextFromRemainder = Math.Max(0, remainderCiphertextSize - overhead);

            // Check for potential partial chunk smaller than overhead (invalid scenario according to Java test values)
            if (remainderCiphertextSize > 0 && cleartextFromRemainder == 0 && overhead > 0)
            {
                // This case corresponds to ciphertext sizes like 1 to 8 in the Java test (overhead is 8)
                throw new ArgumentException("Invalid ciphertext size (only contains overhead, no payload).", nameof(ciphertextSize));
            }

            // Additional check based on Java test failures for sizes 41, 48, 81, 88
            // These correspond to ciphertextSize = n * 40 + (1..8) which should be invalid
            if (remainderCiphertextSize > 0 && remainderCiphertextSize < overhead)
            {
                throw new ArgumentException("Invalid ciphertext size (remainder smaller than overhead).", nameof(ciphertextSize));
            }


            return cleartextFromFullChunks + cleartextFromRemainder;
        }

        long CiphertextSize(long cleartextSize)
        {
            long cleartextChunkSize = CleartextChunkSize();
            long ciphertextChunkSize = CiphertextChunkSize();

            if (cleartextSize < 0)
            {
                throw new ArgumentException("Cleartext size must not be negative.", nameof(cleartextSize));
            }
            if (cleartextChunkSize <= 0 || ciphertextChunkSize <= 0 || ciphertextChunkSize <= (ciphertextChunkSize - cleartextChunkSize))
            {
                throw new InvalidOperationException("Chunk sizes must be positive and ciphertext chunk must be larger than cleartext chunk.");
            }
            if (cleartextSize == 0)
            {
                return 0;
            }

            long overhead = ciphertextChunkSize - cleartextChunkSize;
            if (overhead <= 0)
            {
                throw new InvalidOperationException("Ciphertext chunk size must be greater than cleartext chunk size.");
            }

            long numFullChunks = cleartextSize / cleartextChunkSize;
            long remainderCleartextSize = cleartextSize % cleartextChunkSize;

            long ciphertext = numFullChunks * ciphertextChunkSize;
            if (remainderCleartextSize > 0)
            {
                ciphertext += remainderCleartextSize + overhead;
            }

            return ciphertext;
        }

    }

    [TestClass]
    public class FileContentCryptorTest
    {
        // Mock the new test interface
        private Mock<IFileSizeCalculator> _calculatorMock;
        private IFileSizeCalculator _calculator; // To access the mocked object

        [TestInitialize]
        public void SetUp()
        {
            _calculatorMock = new Mock<IFileSizeCalculator>();

            // Stub chunk sizes like Java test
            _calculatorMock.Setup(c => c.CleartextChunkSize()).Returns(32);
            _calculatorMock.Setup(c => c.CiphertextChunkSize()).Returns(40);

            // Configure mock to use default interface implementations for calculation methods
            _calculatorMock.Setup(c => c.CleartextSize(It.IsAny<long>())).CallBase();
            _calculatorMock.Setup(c => c.CiphertextSize(It.IsAny<long>())).CallBase();

            // Get the actual object to test against
            _calculator = _calculatorMock.Object;
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(1, 9)]
        [DataRow(31, 39)]
        [DataRow(32, 40)]
        [DataRow(33, 49)]
        [DataRow(34, 50)]
        [DataRow(63, 79)]
        [DataRow(64, 80)]
        [DataRow(65, 89)]
        [DisplayName("CleartextSize Calculation (Mocked Interface)")]
        public void TestCleartextSize(long expectedCleartextSize, long ciphertextSize)
        {
            Assert.AreEqual(expectedCleartextSize, _calculator.CleartextSize(ciphertextSize));
        }

        [TestMethod]
        [DataRow(-1)]
        [DataRow(1)] // 1..8 should fail based on Java
        [DataRow(8)] //
        [DataRow(41)]// 40 + 1..8 should fail
        [DataRow(48)]//
        [DataRow(81)]// 80 + 1..8 should fail
        [DataRow(88)]//
        [DisplayName("CleartextSize Invalid Input (Mocked Interface)")]
        public void TestCleartextSizeWithInvalidCiphertextSize(long invalidCiphertextSize)
        {
            Assert.ThrowsException<ArgumentException>(() => _calculator.CleartextSize(invalidCiphertextSize));
        }

        [TestMethod]
        [DataRow(0, 0)]
        [DataRow(1, 9)]
        [DataRow(31, 39)]
        [DataRow(32, 40)]
        [DataRow(33, 49)]
        [DataRow(34, 50)]
        [DataRow(63, 79)]
        [DataRow(64, 80)]
        [DataRow(65, 89)]
        [DisplayName("CiphertextSize Calculation (Mocked Interface)")]
        public void TestCiphertextSize(long cleartextSize, long expectedCiphertextSize)
        {
            Assert.AreEqual(expectedCiphertextSize, _calculator.CiphertextSize(cleartextSize));
        }

        [TestMethod]
        [DataRow(-1)]
        [DisplayName("CiphertextSize Invalid Input (Mocked Interface)")]
        public void TestCiphertextSizeWithInvalidCleartextSize(long invalidCleartextSize)
        {
            Assert.ThrowsException<ArgumentException>(() => _calculator.CiphertextSize(invalidCleartextSize));
        }
    }
}