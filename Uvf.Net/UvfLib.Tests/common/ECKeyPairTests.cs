using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class ECKeyPairTests
    {
        [TestMethod]
        public void Constructor_WithNullPrivateKey_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ECKeyPair(null));
        }

        [TestMethod]
        public void Generate_WithP256Curve_CreatesValidKeyPair()
        {
            // Act
            ECKeyPair keyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);

            // Assert
            Assert.IsNotNull(keyPair);
            Assert.IsNotNull(keyPair.PrivateKey);
        }

        [TestMethod]
        public void FromPrivateKey_WithValidKey_CreatesKeyPair()
        {
            // Arrange
            ECKeyPair originalKeyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);
            byte[] privateKeyBytes = originalKeyPair.ExportPrivateKeyPkcs8();

            // Act
            ECKeyPair keyPair = ECKeyPair.FromPrivateKey(privateKeyBytes, ECCurve.NamedCurves.nistP256);

            // Assert
            Assert.IsNotNull(keyPair);
            Assert.IsNotNull(keyPair.PrivateKey);
        }

        [TestMethod]
        public void FromPrivateKey_WithNullBytes_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                ECKeyPair.FromPrivateKey(null, ECCurve.NamedCurves.nistP256));
        }

        [TestMethod]
        public void FromPrivateKey_WithEmptyBytes_ThrowsArgumentException()
        {
            Assert.ThrowsException<ArgumentException>(() =>
                ECKeyPair.FromPrivateKey(Array.Empty<byte>(), ECCurve.NamedCurves.nistP256));
        }

        [TestMethod]
        public void ExportPrivateKeyPkcs8_ReturnsValidBytes()
        {
            // Arrange
            ECKeyPair keyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);

            // Act
            byte[] privateKeyBytes = keyPair.ExportPrivateKeyPkcs8();

            // Assert
            Assert.IsNotNull(privateKeyBytes);
            Assert.IsTrue(privateKeyBytes.Length > 0);
        }

        [TestMethod]
        public void ExportPublicKey_ReturnsValidBytes()
        {
            // Arrange
            ECKeyPair keyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);

            // Act
            byte[] publicKeyBytes = keyPair.ExportPublicKey();

            // Assert
            Assert.IsNotNull(publicKeyBytes);
            Assert.IsTrue(publicKeyBytes.Length > 0);
        }

        [TestMethod]
        public void SignAndVerifyData_WithValidData_Succeeds()
        {
            // Arrange
            ECKeyPair keyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            byte[] signature = keyPair.SignData(data);
            bool verified = keyPair.VerifyData(data, signature);

            // Assert
            Assert.IsNotNull(signature);
            Assert.IsTrue(signature.Length > 0);
            Assert.IsTrue(verified);
        }

        [TestMethod]
        public void VerifyData_WithInvalidSignature_ReturnsFalse()
        {
            // Arrange
            ECKeyPair keyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            byte[] invalidSignature = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 };

            // Act
            bool verified = keyPair.VerifyData(data, invalidSignature);

            // Assert
            Assert.IsFalse(verified);
        }

        [TestMethod]
        public void VerifyData_WithModifiedData_ReturnsFalse()
        {
            // Arrange
            ECKeyPair keyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);
            byte[] originalData = new byte[] { 1, 2, 3, 4, 5 };
            byte[] modifiedData = new byte[] { 1, 2, 3, 4, 6 }; // Last byte changed
            byte[] signature = keyPair.SignData(originalData);

            // Act
            bool verified = keyPair.VerifyData(modifiedData, signature);

            // Assert
            Assert.IsFalse(verified);
        }

        [TestMethod]
        public void SignAndVerify_DifferentKeyPairs_ReturnsFalse()
        {
            // Arrange
            ECKeyPair keyPair1 = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);
            ECKeyPair keyPair2 = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };

            // Act
            byte[] signature = keyPair1.SignData(data);
            bool verified = keyPair2.VerifyData(data, signature);

            // Assert
            Assert.IsFalse(verified);
        }

        [TestMethod]
        public void ExportAndReimport_MaintainsKeyPairFunctionality()
        {
            // Arrange
            ECKeyPair originalKeyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);
            byte[] privateKeyBytes = originalKeyPair.ExportPrivateKeyPkcs8();
            byte[] data = new byte[] { 1, 2, 3, 4, 5 };
            byte[] signature = originalKeyPair.SignData(data);

            // Act
            ECKeyPair reimportedKeyPair = ECKeyPair.FromPrivateKey(privateKeyBytes, ECCurve.NamedCurves.nistP256);
            bool verified = reimportedKeyPair.VerifyData(data, signature);

            // Assert
            Assert.IsTrue(verified);
        }

        [TestMethod]
        public void Generate_WithDifferentCurves_CreatesDifferentKeyPairs()
        {
            // Act
            ECKeyPair p256KeyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP256);
            ECKeyPair p384KeyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP384);
            ECKeyPair p521KeyPair = ECKeyPair.Generate(ECCurve.NamedCurves.nistP521);

            // Assert - they should have different key lengths
            byte[] p256Public = p256KeyPair.ExportPublicKey();
            byte[] p384Public = p384KeyPair.ExportPublicKey();
            byte[] p521Public = p521KeyPair.ExportPublicKey();

            Assert.AreNotEqual(p256Public.Length, p384Public.Length);
            Assert.AreNotEqual(p256Public.Length, p521Public.Length);
            Assert.AreNotEqual(p384Public.Length, p521Public.Length);
        }
    }
}