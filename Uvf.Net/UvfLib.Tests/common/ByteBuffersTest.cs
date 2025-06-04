using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class ByteBuffersTest
    {
        [TestMethod]
        [DisplayName("Test Concat with multiple arrays")]
        public void TestConcatMultipleArrays()
        {
            byte[] array1 = { 1, 2, 3 };
            byte[] array2 = { 4, 5 };
            byte[] array3 = { 6, 7, 8, 9 };

            byte[] result = ByteBuffers.Concat(array1, array2, array3);

            Assert.AreEqual(9, result.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, result);
        }

        [TestMethod]
        [DisplayName("Test Concat with empty arrays")]
        public void TestConcatEmptyArrays()
        {
            byte[] array1 = { 1, 2, 3 };
            byte[] array2 = Array.Empty<byte>();
            byte[] array3 = { 6, 7 };

            byte[] result = ByteBuffers.Concat(array1, array2, array3);

            Assert.AreEqual(5, result.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 6, 7 }, result);
        }

        [TestMethod]
        [DisplayName("Test Concat with null arrays")]
        public void TestConcatNullArrays()
        {
            byte[] array1 = { 1, 2, 3 };
            byte[] array2 = null;
            byte[] array3 = { 6, 7 };

            byte[] result = ByteBuffers.Concat(array1, array2, array3);

            Assert.AreEqual(5, result.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 6, 7 }, result);
        }

        [TestMethod]
        [DisplayName("Test Concat with no arrays")]
        public void TestConcatNoArrays()
        {
            byte[] result = ByteBuffers.Concat();

            Assert.AreEqual(0, result.Length);
            CollectionAssert.AreEqual(Array.Empty<byte>(), result);
        }

        [TestMethod]
        [DisplayName("Test LongToByteArray and ByteArrayToLong")]
        public void TestLongConversion()
        {
            long value = 0x0102030405060708L;

            byte[] bytes = ByteBuffers.LongToByteArray(value);

            Assert.AreEqual(8, bytes.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, bytes);

            long result = ByteBuffers.ByteArrayToLong(bytes);

            Assert.AreEqual(value, result);
        }

        [TestMethod]
        [DisplayName("Test IntToByteArray and ByteArrayToInt")]
        public void TestIntConversion()
        {
            int value = 0x01020304;

            byte[] bytes = ByteBuffers.IntToByteArray(value);

            Assert.AreEqual(4, bytes.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, bytes);

            int result = ByteBuffers.ByteArrayToInt(bytes);

            Assert.AreEqual(value, result);
        }

        [TestMethod]
        [DisplayName("Test ByteArrayToInt with invalid array")]
        public void TestByteArrayToIntWithInvalidArray()
        {
            byte[] bytes = { 1, 2, 3 }; // Too short

            Assert.ThrowsException<ArgumentException>(() => ByteBuffers.ByteArrayToInt(bytes));
        }

        [TestMethod]
        [DisplayName("Test ByteArrayToLong with invalid array")]
        public void TestByteArrayToLongWithInvalidArray()
        {
            byte[] bytes = { 1, 2, 3, 4, 5, 6, 7 }; // Too short

            Assert.ThrowsException<ArgumentException>(() => ByteBuffers.ByteArrayToLong(bytes));
        }

        [TestMethod]
        [DisplayName("Test CopyOf with smaller length")]
        public void TestCopyOfWithSmallerLength()
        {
            byte[] src = { 1, 2, 3, 4, 5 };

            byte[] result = ByteBuffers.CopyOf(src, 3);

            Assert.AreEqual(3, result.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result);
        }

        [TestMethod]
        [DisplayName("Test CopyOf with larger length")]
        public void TestCopyOfWithLargerLength()
        {
            byte[] src = { 1, 2, 3 };

            byte[] result = ByteBuffers.CopyOf(src, 5);

            Assert.AreEqual(5, result.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 0, 0 }, result);
        }

        [TestMethod]
        [DisplayName("Test CopyOf with zero length")]
        public void TestCopyOfWithZeroLength()
        {
            byte[] src = { 1, 2, 3 };

            byte[] result = ByteBuffers.CopyOf(src, 0);

            Assert.AreEqual(0, result.Length);
            CollectionAssert.AreEqual(Array.Empty<byte>(), result);
        }

        [TestMethod]
        [DisplayName("Test CopyOf with negative length")]
        public void TestCopyOfWithNegativeLength()
        {
            byte[] src = { 1, 2, 3 };

            Assert.ThrowsException<ArgumentOutOfRangeException>(() => ByteBuffers.CopyOf(src, -1));
        }
    }
}