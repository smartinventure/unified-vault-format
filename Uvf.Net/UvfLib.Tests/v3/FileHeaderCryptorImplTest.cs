using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Tests.Common;
using UvfLib.V3;
using V3Constants = UvfLib._old.v3.Constants;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Moq;
using UvfLib._old.v3;
using UvfLib._old.common;
using UvfLib._old.api;

namespace UvfLib.Tests.V3
{
    [TestClass]
    public class FileHeaderCryptorImplTest
    {
        // Constants aligned with FileNameCryptorImplTest for consistency
        private static readonly RandomNumberGenerator RandomMock = RandomNumberGenerator.Create(); // Using real RNG for C#
        private const int TestRevision = -1540072521;
        private static readonly Dictionary<int, byte[]> SEEDS = new Dictionary<int, byte[]>
        {
            { TestRevision, Convert.FromBase64String("fP4V4oAjsUw5DqackAvLzA0oP1kAQZ0f5YFZQviXSuU=".Replace('-', '+').Replace('_', '/')) }
        };
        private static readonly byte[] KDF_SALT = Convert.FromBase64String("HE4OP-2vyfLLURicF1XmdIIsWv0Zs6MobLKROUIEhQY=".Replace('-', '+').Replace('_', '/'));
        // Use RevolvingMasterkey interface type
        private static readonly RevolvingMasterkey MASTERKEY = new UVFMasterkeyImpl(SEEDS, KDF_SALT, TestRevision, TestRevision);

        private FileHeaderCryptorImpl _headerCryptor;

        [TestInitialize]
        public void Setup()
        {
            _headerCryptor = new FileHeaderCryptorImpl(MASTERKEY, RandomMock, TestRevision);

            // Note: Java GcmTestHelper.reset is not directly needed/translatable
            // C# AesGcm is instance-based, avoiding static state issues.
        }

        [TestMethod]
        [DisplayName("Test Header Size")]
        public void TestHeaderSize()
        {
            // Assert HeaderSize() returns the constant size
            Assert.AreEqual(FileHeaderImpl.SIZE, _headerCryptor.HeaderSize(), "HeaderSize() mismatch");

            // Assert encrypted header length matches the constant size
            using var headerToEncrypt = _headerCryptor.Create(); // Create uses RNG
            Memory<byte> encrypted = _headerCryptor.EncryptHeader(headerToEncrypt);
            Assert.AreEqual(FileHeaderImpl.SIZE, encrypted.Length, "Encrypted header length mismatch");
        }

        // Skipping Java's testSubkeyGeneration as it tests MASTERKEY directly, not the cryptor.

        [TestMethod]
        [DisplayName("Test Encryption Matches Java Output")]
        public void TestEncryption()
        {
            // Arrange: Create header with known (zeroed) nonce and content key, matching Java test setup implicitly using NULL_RANDOM
            // We need to simulate NULL_RANDOM's effect here.
            byte[] zeroNonce = new byte[FileHeaderImpl.NONCE_LEN]; // 12 bytes
            byte[] zeroKeyBytes = new byte[FileHeaderImpl.CONTENT_KEY_LEN]; // 32 bytes
            using DestroyableSecretKey zeroContentKey = new DestroyableSecretKey(zeroKeyBytes, V3Constants.CONTENT_ENC_ALG); // "AES"
            using FileHeader header = new FileHeaderImpl(TestRevision, zeroNonce, zeroContentKey);

            // Expected ciphertext from Java test (Base64 decoded)
            // Java: "dXZmAKQ0W7cAAAAAAAAAAAAAAAA/UGgFA8QGho7E1QTsHWyZIVFqabbGJ/TCwvp3StG0JTkKGj3hwERhnFmZek61Xtc="
            byte[] expectedCiphertext = Convert.FromBase64String("dXZmAKQ0W7cAAAAAAAAAAAAAAAA/UGgFA8QGho7E1QTsHWyZIVFqabbGJ/TCwvp3StG0JTkKGj3hwERhnFmZek61Xtc=");

            // Act
            Memory<byte> actualCiphertextMemory = _headerCryptor.EncryptHeader(header);

            // Assert
            CollectionAssert.AreEqual(expectedCiphertext, actualCiphertextMemory.ToArray(), "Encrypted header does not match expected Java output.");
        }

        [TestMethod]
        [DisplayName("Test Decryption Matches Java Output")]
        public void TestDecryption()
        {
            // Arrange: Ciphertext from Java test
            byte[] ciphertext = Convert.FromBase64String("dXZmAKQ0W7cAAAAAAAAAAAAAAAA/UGgFA8QGho7E1QTsHWyZIVFqabbGJ/TCwvp3StG0JTkKGj3hwERhnFmZek61Xtc=");
            ReadOnlyMemory<byte> ciphertextMemory = new ReadOnlyMemory<byte>(ciphertext);

            // Expected results (zeroed nonce and key from the specific encryption test)
            byte[] expectedNonce = new byte[FileHeaderImpl.NONCE_LEN];
            byte[] expectedKeyBytes = new byte[FileHeaderImpl.CONTENT_KEY_LEN];

            // Act
            using FileHeader decryptedHeader = _headerCryptor.DecryptHeader(ciphertextMemory);

            // Assert
            Assert.IsNotNull(decryptedHeader, "Decrypted header should not be null.");
            Assert.AreEqual(TestRevision, ((FileHeaderImpl)decryptedHeader).GetSeedId(), "Decrypted Seed ID mismatch.");
            CollectionAssert.AreEqual(expectedNonce, decryptedHeader.GetNonce(), "Decrypted nonce mismatch.");

            // Safely get and compare content key
            using (DestroyableSecretKey contentKey = ((FileHeaderImpl)decryptedHeader).GetContentKey())
            {
                CollectionAssert.AreEqual(expectedKeyBytes, contentKey.GetEncoded(), "Decrypted content key mismatch.");
            }
        }

        [TestMethod]
        [DisplayName("Test Decryption With Too Short Header")]
        public void TestDecryptionWithTooShortHeader()
        {
            // Arrange: Create ciphertext shorter than required size
            ReadOnlyMemory<byte> ciphertext = new byte[FileHeaderImpl.SIZE - 1];

            // Act & Assert: Expect ArgumentException (or potentially InvalidCiphertextException)
            var ex = Assert.ThrowsException<ArgumentException>(() =>
            {
                _headerCryptor.DecryptHeader(ciphertext);
            });
            Assert.IsTrue(ex.Message.Contains("Malformed ciphertext header", StringComparison.OrdinalIgnoreCase), "Exception message mismatch.");
        }

        [TestMethod]
        [DisplayName("Test Decryption With Invalid Tag")]
        public void TestDecryptionWithInvalidTag()
        {
            // Arrange: Ciphertext from Java test with last byte modified (affects tag)
            // Java: "dXZmAKQ0W7cAAAAAAAAAAAAAAAA/UGgFA8QGho7E1QTsHWyZIVFqabbGJ/TCwvp3StG0JTkKGj3hwERhnFmZek61XtX=" (note 'X' instead of 'c')
            byte[] ciphertextBytes = Convert.FromBase64String("dXZmAKQ0W7cAAAAAAAAAAAAAAAA/UGgFA8QGho7E1QTsHWyZIVFqabbGJ/TCwvp3StG0JTkKGj3hwERhnFmZek61XtX=");
            ReadOnlyMemory<byte> ciphertext = new ReadOnlyMemory<byte>(ciphertextBytes);

            // Act & Assert: Expect AuthenticationFailedException
            Assert.ThrowsException<AuthenticationFailedException>(() =>
            {
                _headerCryptor.DecryptHeader(ciphertext);
            }, "Should throw AuthenticationFailedException for invalid tag.");
        }


        [TestMethod]
        [DisplayName("Test Decryption With Invalid Ciphertext")]
        public void TestDecryptionWithInvalidCiphertext()
        {
            // Arrange: Ciphertext from Java test with a byte modified in the encrypted key part
            // Java: "dXZmAKQ0W7cAAAAAAAAAAAAAAAA/UGgFA8QGho7E1QTsHWyZIVFqabbGJ/XCwvp3StG0JTkKGj3hwERhnFmZek61Xtc=" (note 'X' instead of 'T' near middle)
            byte[] ciphertextBytes = Convert.FromBase64String("dXZmAKQ0W7cAAAAAAAAAAAAAAAA/UGgFA8QGho7E1QTsHWyZIVFqabbGJ/XCwvp3StG0JTkKGj3hwERhnFmZek61Xtc=");
            ReadOnlyMemory<byte> ciphertext = new ReadOnlyMemory<byte>(ciphertextBytes);

            // Act & Assert: Expect AuthenticationFailedException
            // Note: This manipulates the ciphertext itself, which GCM should detect during decryption/tag verification.
            Assert.ThrowsException<AuthenticationFailedException>(() =>
           {
               _headerCryptor.DecryptHeader(ciphertext);
           }, "Should throw AuthenticationFailedException for invalid ciphertext.");
        }
    }
}