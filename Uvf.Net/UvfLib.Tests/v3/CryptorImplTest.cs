using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Tests.Common;
using UvfLib.V3;
using System.Security.Cryptography;
using Moq;
using UvfLib._old.v3;
using UvfLib._old.api;

namespace UvfLib.Tests.V3
{
    [TestClass]
    public class CryptorImplTest
    {
        private static readonly RandomNumberGenerator RANDOM_MOCK = SecureRandomMock.NULL_RANDOM;

        // Define the test data for masterkey creation
        private static readonly Dictionary<int, byte[]> SEEDS = new Dictionary<int, byte[]>
        {
            { -1540072521, Convert.FromBase64String("fP4V4oAjsUw5DqackAvLzA0oP1kAQZ0f5YFZQviXSuU=".Replace('-', '+').Replace('_', '/')) }
        };
        private static readonly byte[] KDF_SALT = Convert.FromBase64String("HE4OP-2vyfLLURicF1XmdIIsWv0Zs6MobLKROUIEhQY=".Replace('-', '+').Replace('_', '/'));

        private UVFMasterkey? _masterkey;

        [TestInitialize]
        public void Setup()
        {
            // Create the masterkey with test data
            _masterkey = new UVFMasterkeyImpl(SEEDS, KDF_SALT, -1540072521, -1540072521);
        }

        [TestMethod]
        [DisplayName("Test Get File Content Cryptor")]
        public void TestGetFileContentCryptor()
        {
            Assert.IsNotNull(_masterkey, "Masterkey should be initialized");
            using var cryptor = new CryptorImpl(_masterkey, RANDOM_MOCK);
            Assert.IsInstanceOfType(cryptor.FileContentCryptor(), typeof(FileContentCryptorImpl));
        }

        [TestMethod]
        [DisplayName("Test Get File Header Cryptor")]
        public void TestGetFileHeaderCryptor()
        {
            Assert.IsNotNull(_masterkey, "Masterkey should be initialized");
            using var cryptor = new CryptorImpl(_masterkey, RANDOM_MOCK);
            Assert.IsInstanceOfType(cryptor.FileHeaderCryptor(), typeof(FileHeaderCryptorImpl));
        }

        [TestMethod]
        [DisplayName("Test Get File Name Cryptor Without Revision")]
        public void TestGetFileNameCryptor()
        {
            Assert.IsNotNull(_masterkey, "Masterkey should be initialized");
            using var cryptor = new CryptorImpl(_masterkey, RANDOM_MOCK);
            Assert.ThrowsException<NotSupportedException>(() => cryptor.FileNameCryptor());
        }

        [TestMethod]
        [DisplayName("Test Get File Name Cryptor With Invalid Revision")]
        public void TestGetFileNameCryptorWithInvalidRevisions()
        {
            Assert.IsNotNull(_masterkey, "Masterkey should be initialized");
            using var cryptor = new CryptorImpl(_masterkey, RANDOM_MOCK);
            Assert.ThrowsException<ArgumentException>(() => cryptor.FileNameCryptor(0xBAD5EED));
        }

        [TestMethod]
        [DisplayName("Test Get File Name Cryptor With Correct Revision")]
        public void TestGetFileNameCryptorWithCorrectRevisions()
        {
            Assert.IsNotNull(_masterkey, "Masterkey should be initialized");
            using var cryptor = new CryptorImpl(_masterkey, RANDOM_MOCK);
            Assert.IsInstanceOfType(cryptor.FileNameCryptor(-1540072521), typeof(FileNameCryptorImpl));
        }

        [TestMethod]
        [DisplayName("Test Directory Content Cryptor")]
        public void TestDirectoryContentCryptor()
        {
            Assert.IsNotNull(_masterkey, "Masterkey should be initialized");
            using var cryptor = new CryptorImpl(_masterkey, RANDOM_MOCK);
            // Since DirectoryContentCryptor is implemented, we should get a valid instance
            Assert.IsInstanceOfType(cryptor.DirectoryContentCryptor(), typeof(DirectoryContentCryptorImpl));
        }

        [TestMethod]
        [DisplayName("Test Explicit Destruction")]
        public void TestExplicitDestruction()
        {
            // Create a mock UVFMasterkey
            var masterkeyMock = new Mock<UVFMasterkey>();

            using var cryptor = new CryptorImpl(masterkeyMock.Object, RANDOM_MOCK);
            // Call destroy
            cryptor.Destroy();

            // Verify destroy was called on the masterkey
            masterkeyMock.Verify(m => m.Destroy(), Times.Once);

            // Setup the mock to report it's destroyed
            masterkeyMock.Setup(m => m.IsDestroyed()).Returns(true);

            // Check that the cryptor reports it's destroyed
            Assert.IsTrue(cryptor.IsDestroyed());
        }

        [TestMethod]
        [DisplayName("Test Implicit Destruction")]
        public void TestImplicitDestruction()
        {
            // Create a mock UVFMasterkey
            var masterkeyMock = new Mock<UVFMasterkey>();

            using (var cryptor = new CryptorImpl(masterkeyMock.Object, RANDOM_MOCK))
            {
                Assert.IsFalse(cryptor.IsDestroyed());
            }

            // Verify destroy was called on the masterkey when the cryptor was disposed
            masterkeyMock.Verify(m => m.Destroy(), Times.Once);
        }
    }
}