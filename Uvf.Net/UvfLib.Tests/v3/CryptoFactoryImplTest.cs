using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Core.Common;
using UvfLib.Core.V3;
using Moq;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UvfLib.Core.V3;
using UvfLib.Core.Api;

namespace UvfLib.Tests.V3
{
    [TestClass]
    public class CryptoFactoryImplTest
    {
        // Define test data for masterkey creation - same as in other tests for consistency
        private static readonly Dictionary<int, byte[]> SEEDS = new Dictionary<int, byte[]>
        {
            { -1540072521, Convert.FromBase64String("fP4V4oAjsUw5DqackAvLzA0oP1kAQZ0f5YFZQviXSuU=".Replace('-', '+').Replace('_', '/')) }
        };
        private static readonly byte[] KDF_SALT = Convert.FromBase64String("HE4OP-2vyfLLURicF1XmdIIsWv0Zs6MobLKROUIEhQY=".Replace('-', '+').Replace('_', '/'));

        private UVFMasterkey _masterkey;
        private CryptoFactoryImpl _factory;

        [TestInitialize]
        public void Setup()
        {
            _masterkey = new UVFMasterkeyImpl(SEEDS, KDF_SALT, -1540072521, -1540072521);
            _factory = new CryptoFactoryImpl(_masterkey);
        }

        [TestMethod]
        [DisplayName("Test Create Cryptor")]
        public void TestCreateCryptor()
        {
            // Act
            Cryptor cryptor = _factory.Create();

            // Assert
            Assert.IsNotNull(cryptor);
            Assert.IsInstanceOfType(cryptor, typeof(CryptorImpl));
        }

        [TestMethod]
        [DisplayName("Test Create File Name Cryptor")]
        public void TestCreateFileNameCryptor()
        {
            // Act
            FileNameCryptor cryptor = _factory.CreateFileNameCryptor();

            // Assert
            Assert.IsNotNull(cryptor);
            Assert.IsInstanceOfType(cryptor, typeof(FileNameCryptorImpl));
        }

        [TestMethod]
        [DisplayName("Test Create File Content Cryptor")]
        public void TestCreateFileContentCryptor()
        {
            // Act
            IFileContentCryptor cryptor = _factory.CreateFileContentCryptor();

            // Assert
            Assert.IsNotNull(cryptor);
            Assert.IsInstanceOfType(cryptor, typeof(FileContentCryptorImpl));
        }

        [TestMethod]
        [DisplayName("Test Constructor With Null Masterkey")]
        public void TestConstructorWithNullMasterkey()
        {
            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => new CryptoFactoryImpl(null));
        }

        [TestMethod]
        [DisplayName("Test Dispose")]
        public void TestDispose()
        {
            // Arrange
            var mockMasterkey = new Mock<UVFMasterkey>();
            mockMasterkey.As<IDisposable>();

            // Act
            using (var factory = new CryptoFactoryImpl(mockMasterkey.Object))
            {
                // Factory created and will be disposed
            }

            // Assert
            mockMasterkey.As<IDisposable>().Verify(d => d.Dispose(), Times.Once);
        }
    }
}