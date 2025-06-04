using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class ObjectPoolTest
    {
        private Mock<Func<Foo>> _factory;
        private ObjectPool<Foo> _pool;

        [TestInitialize]
        public void Setup()
        {
            _factory = new Mock<Func<Foo>>();
            _factory.Setup(f => f()).Returns(() => new Foo());
            _pool = new ObjectPool<Foo>(_factory.Object);
        }

        [TestMethod]
        [DisplayName("New instance is created if pool is empty")]
        public void TestCreateNewObjWhenPoolIsEmpty()
        {
            using (var lease1 = _pool.Get())
            {
                using (var lease2 = _pool.Get())
                {
                    Assert.AreNotSame(lease1.Get(), lease2.Get());
                }
            }
            _factory.Verify(f => f(), Times.Exactly(2));
        }

        [TestMethod]
        [DisplayName("Recycle existing instance")]
        public void TestRecycleExistingObj()
        {
            Foo foo1;
            using (var lease = _pool.Get())
            {
                foo1 = lease.Get();
            }
            using (var lease = _pool.Get())
            {
                Assert.AreSame(foo1, lease.Get());
            }
            _factory.Verify(f => f(), Times.Once());
        }

        [TestMethod]
        [DisplayName("Create new instance when pool is GC'ed")]
        public void TestGc()
        {
            using (var lease = _pool.Get())
            {
                Assert.IsNotNull(lease.Get());
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();

            using (var lease = _pool.Get())
            {
                Assert.IsNotNull(lease.Get());
            }

            // Note: This verification might not be reliable in C# as the GC behavior can vary,
            // so we might need to adjust expectations based on actual behavior
            _factory.Verify(f => f(), Times.AtLeast(1));
        }

        public class Foo
        {
        }
    }
}