using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Core.Common;
using Moq;
using System;
using System.Security;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class DestroyablesTest
    {
        [TestMethod]
        [DisplayName("Test DestroySilently Calls Destroy")]
        public void TestDestroySilently()
        {
            // Create a mock IDestroyable object
            var destroyable = new Mock<IDestroyable>();

            // Call DestroySilently - should not throw
            try
            {
                // Use explicit array to avoid ambiguity
                Destroyables.DestroySilently(new IDestroyable[] { destroyable.Object });
            }
            catch (Exception)
            {
                Assert.Fail("DestroySilently should not throw exceptions");
            }

            // Verify destroy was called on the mock
            destroyable.Verify(d => d.Destroy(), Times.Once);
        }

        [TestMethod]
        [DisplayName("Test DestroySilently Ignores Null")]
        public void TestDestroySilentlyIgnoresNull()
        {
            // Call DestroySilently with null - should not throw
            try
            {
                // Use null in array context to avoid ambiguity
                Destroyables.DestroySilently(new IDestroyable[] { null });
            }
            catch (Exception)
            {
                Assert.Fail("DestroySilently should not throw exceptions when passed null");
            }

            // If we reach here without exceptions, the test passes
            Assert.IsTrue(true);
        }

        [TestMethod]
        [DisplayName("Test DestroySilently Suppresses Exceptions")]
        public void TestDestroySilentlySuppressesException()
        {
            // Create a mock IDestroyable object that throws when Destroy is called
            var destroyable = new Mock<IDestroyable>();
            destroyable.Setup(d => d.Destroy()).Throws<SecurityException>();

            // Call DestroySilently - should not propagate the exception
            try
            {
                // Use explicit array to avoid ambiguity
                Destroyables.DestroySilently(new IDestroyable[] { destroyable.Object });
            }
            catch (Exception)
            {
                Assert.Fail("DestroySilently should suppress exceptions from Destroy");
            }

            // Verify destroy was called on the mock
            destroyable.Verify(d => d.Destroy(), Times.Once);
        }

        [TestMethod]
        [DisplayName("Test DestroySilently On Multiple Objects")]
        public void TestDestroySilentlyOnMultipleObjects()
        {
            // Create multiple mock IDestroyable objects
            var destroyable1 = new Mock<IDestroyable>();
            var destroyable2 = new Mock<IDestroyable>();
            var destroyable3 = new Mock<IDestroyable>();

            // Make one of them throw an exception
            destroyable2.Setup(d => d.Destroy()).Throws<InvalidOperationException>();

            // Call DestroySilently with array - should not throw
            try
            {
                // Use explicit array parameter to avoid ambiguity
                IDestroyable[] destroyables = { destroyable1.Object, destroyable2.Object, destroyable3.Object };
                Destroyables.DestroySilently(destroyables);
            }
            catch (Exception)
            {
                Assert.Fail("DestroySilently should not throw exceptions");
            }

            // Verify destroy was called on all mocks
            destroyable1.Verify(d => d.Destroy(), Times.Once);
            destroyable2.Verify(d => d.Destroy(), Times.Once);
            destroyable3.Verify(d => d.Destroy(), Times.Once);
        }
    }
}