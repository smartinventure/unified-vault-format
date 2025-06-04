using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Security.Cryptography;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class Pkcs12HelperTest
    {
        private string _p12FilePath;

        [TestInitialize]
        public void Setup()
        {
            _p12FilePath = Path.GetTempFileName();
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (File.Exists(_p12FilePath))
            {
                File.Delete(_p12FilePath);
            }
        }

        [TestMethod]
        [DisplayName("Attempt export EC key pair with EC signature alg")]
        public void TestExport()
        {
            using (var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                using (var fileStream = new FileStream(_p12FilePath, FileMode.Create, FileAccess.Write))
                {
                    char[] passphrase = "topsecret".ToCharArray();

                    Pkcs12Helper.ExportTo(ec, fileStream, passphrase, "SHA256withECDSA");

                    Assert.IsTrue(File.Exists(_p12FilePath));
                    Assert.IsTrue(new FileInfo(_p12FilePath).Length > 0);
                }
            }
        }

        [TestClass]
        public class WithExported
        {
            private ECDsa _keyPair;
            private string _p12FilePath;
            private char[] _passphrase = "topsecret".ToCharArray();

            [TestInitialize]
            public void Setup()
            {
                _p12FilePath = Path.GetTempFileName();
                _keyPair = ECDsa.Create(ECCurve.NamedCurves.nistP384);

                using (var fileStream = new FileStream(_p12FilePath, FileMode.Create, FileAccess.Write))
                {
                    Pkcs12Helper.ExportTo(_keyPair, fileStream, _passphrase, "SHA384withECDSA");
                }
            }

            [TestCleanup]
            public void Cleanup()
            {
                _keyPair?.Dispose();

                if (File.Exists(_p12FilePath))
                {
                    File.Delete(_p12FilePath);
                }
            }

            [TestMethod]
            [DisplayName("Attempt import with invalid passphrase")]
            public void TestImportWithInvalidPassphrase()
            {
                using (var fileStream = new FileStream(_p12FilePath, FileMode.Open, FileAccess.Read))
                {
                    char[] wrongPassphrase = "bottompublic".ToCharArray();

                    Assert.ThrowsException<Pkcs12PasswordException>(() =>
                        Pkcs12Helper.ImportFrom(fileStream, wrongPassphrase));
                }
            }

            [TestMethod]
            [DisplayName("Attempt import with valid passphrase")]
            public void TestImportWithValidPassphrase()
            {
                using (var fileStream = new FileStream(_p12FilePath, FileMode.Open, FileAccess.Read))
                {
                    using var imported = Pkcs12Helper.ImportFrom(fileStream, _passphrase);

                    var originalParams = _keyPair.ExportParameters(false);
                    var importedParams = imported.ExportParameters(false);

                    Assert.AreEqual(originalParams.Curve.Oid.FriendlyName, importedParams.Curve.Oid.FriendlyName);
                }
            }
        }
    }
}