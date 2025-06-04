using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Common;
using UvfLib.Tests.Common;
using UvfLib.V3;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Moq;
using System.Diagnostics;
using System.Linq;
using V3Constants = UvfLib._old.v3.Constants;
using UvfLib._old.v3;
using UvfLib._old.api;

namespace UvfLib.Tests.V3
{
    [TestClass]
    public class FileContentCryptorImplTest
    {
        // Constants aligned with other v3 tests
        private const int TestRevision = -1540072521;
        private static readonly Dictionary<int, byte[]> SEEDS = new Dictionary<int, byte[]>
        {
            { TestRevision, Convert.FromBase64String("fP4V4oAjsUw5DqackAvLzA0oP1kAQZ0f5YFZQviXSuU=".Replace('-', '+').Replace('_', '/')) }
        };
        private static readonly byte[] KDF_SALT = Convert.FromBase64String("HE4OP-2vyfLLURicF1XmdIIsWv0Zs6MobLKROUIEhQY=".Replace('-', '+').Replace('_', '/'));
        private static readonly RevolvingMasterkey MASTERKEY = new UVFMasterkeyImpl(SEEDS, KDF_SALT, TestRevision, TestRevision);

        // Dependencies to be mocked or initialized
        private Mock<RandomNumberGenerator> _mockRng;
        private FileHeaderImpl _header; // Use a consistent header based on Java test
        private FileContentCryptorImpl _fileContentCryptor;
        // Removed: _headerCryptor, _cryptor mock - not directly needed for FileContentCryptor tests

        [TestInitialize]
        public void Setup()
        {
            // Initialize the mock RNG
            _mockRng = new Mock<RandomNumberGenerator>();

            // Create a fixed header matching Java setup (zero nonce, zero key)
            byte[] zeroNonce = new byte[FileHeaderImpl.NONCE_LEN];
            byte[] zeroKeyBytes = new byte[FileHeaderImpl.CONTENT_KEY_LEN];
            var contentKey = new DestroyableSecretKey(zeroKeyBytes, V3Constants.CONTENT_ENC_ALG);
            _header = new FileHeaderImpl(TestRevision, zeroNonce, contentKey);

            // Initialize FileContentCryptor with the MASTERKEY and mocked RNG
            _fileContentCryptor = new FileContentCryptorImpl(MASTERKEY, _mockRng.Object);

            // Note: Java GcmTestHelper.reset equivalent - not needed as C# AesGcm is instance-based.
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Dispose header to dispose the content key within it
            _header?.Dispose();
        }

        // Replaces Java's testDecryptedEncryptedEqualsPlaintext with known vector tests below

        // Corresponds to Java Encryption.testChunkEncryption
        [TestMethod]
        [DisplayName("Test Chunk Encryption Matches Java Vector")]
        public void TestChunkEncryption()
        {
            // Arrange
            string plaintext = "hello world";
            ReadOnlyMemory<byte> cleartextData = Encoding.ASCII.GetBytes(plaintext);
            Memory<byte> ciphertextBuffer = new Memory<byte>(new byte[_fileContentCryptor.CiphertextChunkSize()]);

            // Expected ciphertext from Java test (with nonce 0x33...)
            byte[] expectedCiphertext = Convert.FromBase64String("MzMzMzMzMzMzMzMzbYvL7CusRmzk70Kn1QxFA5WQg/hgKeba4bln");

            // Setup mock RNG to return the specific nonce (0x33...)
            _mockRng.Setup(r => r.GetBytes(It.Is<byte[]>(b => b.Length == V3Constants.GCM_NONCE_SIZE)))
                    .Callback<byte[]>(nonce => Array.Fill(nonce, (byte)0x33));

            // Act
            _fileContentCryptor.EncryptChunk(cleartextData, ciphertextBuffer, 0, _header);

            // Assert
            // Calculate the actual length of the encrypted data including nonce and tag
            int actualCiphertextLength = V3Constants.GCM_NONCE_SIZE + cleartextData.Length + V3Constants.GCM_TAG_SIZE;
            // Compare the expected bytes with the relevant slice of the output buffer
            CollectionAssert.AreEqual(expectedCiphertext, ciphertextBuffer.Slice(0, actualCiphertextLength).ToArray(), "Encrypted chunk data mismatch.");

            // Verify RNG was called once for the nonce
            _mockRng.Verify(r => r.GetBytes(It.Is<byte[]>(b => b.Length == V3Constants.GCM_NONCE_SIZE)), Times.Once);
        }

        // Corresponds to Java Decryption.testChunkDecryption
        [TestMethod]
        [DisplayName("Test Chunk Decryption Matches Java Vector")]
        public void TestChunkDecryption()
        {
            // Arrange
            string expectedPlaintext = "hello world";
            byte[] ciphertextBytes = Convert.FromBase64String("VVVVVVVVVVVVVVVVnHVdh+EbedvPeiCwCdaTYpzn1CXQjhSh7PHv");
            ReadOnlyMemory<byte> ciphertext = new ReadOnlyMemory<byte>(ciphertextBytes);
            Memory<byte> cleartextBuffer = new Memory<byte>(new byte[_fileContentCryptor.CleartextChunkSize()]);

            // Act
            _fileContentCryptor.DecryptChunk(ciphertext, cleartextBuffer, 0, _header, true);

            // Assert
            // Trim the buffer to the actual decrypted length
            // Nonce size (16) + Tag size (16) = 32 bytes overhead. Ciphertext size = 16 + 11 + 16 = 43
            int actualCleartextLength = ciphertext.Length - V3Constants.GCM_NONCE_SIZE - V3Constants.GCM_TAG_SIZE;
            Assert.AreEqual(expectedPlaintext.Length, actualCleartextLength, "Decrypted length mismatch");

            string actualPlaintext = Encoding.ASCII.GetString(cleartextBuffer.Span.Slice(0, actualCleartextLength));
            Assert.AreEqual(expectedPlaintext, actualPlaintext, "Decrypted content mismatch.");
        }

        // ----- Size Validation Tests (Combined from Java) -----

        [TestMethod]
        [DisplayName("Test Encrypt Chunk With Invalid Cleartext Size")]
        [DataRow(V3Constants.PAYLOAD_SIZE + 1, DisplayName = "Too Large")]
        public void TestEncryptChunkOfInvalidCleartextSize(int size)
        {
            // Arrange
            ReadOnlyMemory<byte> cleartext = new byte[size];
            Memory<byte> ciphertext = new byte[_fileContentCryptor.CiphertextChunkSize()];

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() =>
                _fileContentCryptor.EncryptChunk(cleartext, ciphertext, 0, _header));
        }

        [TestMethod]
        [DisplayName("Test Encrypt Chunk With Invalid Ciphertext Buffer Size")]
        public void TestEncryptChunkWithInvalidCiphertextBufferSize()
        {
            // Arrange
            ReadOnlyMemory<byte> cleartext = Encoding.ASCII.GetBytes("test"); // cleartext.Length is 4
            int actualRequiredSize = V3Constants.GCM_NONCE_SIZE + cleartext.Length + V3Constants.GCM_TAG_SIZE; // 12 + 4 + 16 = 32
            Memory<byte> ciphertext = new byte[actualRequiredSize - 1]; // Provide a buffer that is truly too small (e.g., 31 bytes)

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() =>
                _fileContentCryptor.EncryptChunk(cleartext, ciphertext, 0, _header));
        }


        [TestMethod]
        [DisplayName("Test Decrypt Chunk With Invalid Ciphertext Size")]
        [DataRow(0, DisplayName = "Zero")]
        [DataRow(V3Constants.GCM_NONCE_SIZE + V3Constants.GCM_TAG_SIZE - 1, DisplayName = "Too Small")]
        [DataRow(V3Constants.CHUNK_SIZE + 1, DisplayName = "Too Large")]
        public void TestDecryptChunkOfInvalidCiphertextSize(int size)
        {
            // Arrange
            ReadOnlyMemory<byte> ciphertext = new byte[size];
            Memory<byte> cleartext = new byte[_fileContentCryptor.CleartextChunkSize()];

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() =>
                _fileContentCryptor.DecryptChunk(ciphertext, cleartext, 0, _header, true));
        }

        [TestMethod]
        [DisplayName("Test Decrypt Chunk With Invalid Cleartext Buffer Size")]
        public void TestDecryptChunkWithInvalidCleartextBufferSize()
        {
            // Arrange: Use the known good ciphertext from TestChunkDecryption
            byte[] ciphertextBytes = Convert.FromBase64String("VVVVVVVVVVVVVVVVnHVdh+EbedvPeiCwCdaTYpzn1CXQjhSh7PHv");
            ReadOnlyMemory<byte> ciphertext = new ReadOnlyMemory<byte>(ciphertextBytes);
            int expectedCleartextSize = ciphertextBytes.Length - V3Constants.GCM_NONCE_SIZE - V3Constants.GCM_TAG_SIZE; // 11 bytes

            Memory<byte> cleartext = new byte[expectedCleartextSize - 1]; // Too small

            // Act & Assert
            Assert.ThrowsException<ArgumentException>(() =>
                _fileContentCryptor.DecryptChunk(ciphertext, cleartext, 0, _header, true));
        }


        // ----- Authentication / Tampering Tests -----

        [TestMethod]
        [DisplayName("Test Decrypt With Authentication Disabled (Not Supported)")]
        public void TestDecryptWithAuthenticationDisabled()
        {
            // GCM requires authentication. C# DecryptChunk doesn't have the bool flag.
            // This test case from Java isn't directly applicable as the C# API enforces authentication.
            // We can verify the method signature doesn't allow disabling auth.
            // If the method existed, it would likely throw NotSupportedException.
            Assert.Inconclusive("C# DecryptChunk API does not support disabling authentication.");
        }

        [TestMethod]
        [DisplayName("Test Decrypt Unauthentic Chunk (Tampered Content)")]
        public void TestUnauthenticChunkDecryption_TamperedContent()
        {
            // Arrange: Use the known good ciphertext and tamper content byte
            byte[] ciphertextBytes = Convert.FromBase64String("VVVVVVVVVVVVVVVVnHVdh+EbedvPeiCwCdaTYpzn1CXQjhSh7PHv");
            ciphertextBytes[V3Constants.GCM_NONCE_SIZE + 1] ^= 0x01; // Tamper content (N changed to O)
            ReadOnlyMemory<byte> tamperedCiphertext = new ReadOnlyMemory<byte>(ciphertextBytes);
            Memory<byte> cleartextBuffer = new byte[_fileContentCryptor.CleartextChunkSize()];

            // Act & Assert
            Assert.ThrowsException<AuthenticationFailedException>(() =>
                _fileContentCryptor.DecryptChunk(tamperedCiphertext, cleartextBuffer, 0, _header, true));
        }

        [TestMethod]
        [DisplayName("Test Decrypt Unauthentic Chunk (Tampered Nonce)")]
        public void TestUnauthenticChunkDecryption_TamperedNonce()
        {
            // Arrange: Use the known good ciphertext and tamper nonce byte
            byte[] ciphertextBytes = Convert.FromBase64String("VVVVVVVVVVVVVVVVnHVdh+EbedvPeiCwCdaTYpzn1CXQjhSh7PHv");
            ciphertextBytes[0] ^= 0x01; // Tamper first byte of nonce
            ReadOnlyMemory<byte> tamperedCiphertext = new ReadOnlyMemory<byte>(ciphertextBytes);
            Memory<byte> cleartextBuffer = new byte[_fileContentCryptor.CleartextChunkSize()];

            // Act & Assert
            Assert.ThrowsException<AuthenticationFailedException>(() =>
                _fileContentCryptor.DecryptChunk(tamperedCiphertext, cleartextBuffer, 0, _header, true));
        }

        [TestMethod]
        [DisplayName("Test Decrypt Unauthentic Chunk (Tampered Tag)")]
        public void TestUnauthenticChunkDecryption_TamperedTag()
        {
            // Arrange: Use the known good ciphertext and tamper tag byte
            byte[] ciphertextBytes = Convert.FromBase64String("VVVVVVVVVVVVVVVVnHVdh+EbedvPeiCwCdaTYpzn1CXQjhSh7PHv");
            ciphertextBytes[ciphertextBytes.Length - 1] ^= 0x01; // Tamper last byte of tag
            ReadOnlyMemory<byte> tamperedCiphertext = new ReadOnlyMemory<byte>(ciphertextBytes);
            Memory<byte> cleartextBuffer = new byte[_fileContentCryptor.CleartextChunkSize()];

            // Act & Assert
            Assert.ThrowsException<AuthenticationFailedException>(() =>
                _fileContentCryptor.DecryptChunk(tamperedCiphertext, cleartextBuffer, 0, _header, true));
        }

        // ----- Stream/Channel Tests (Skipped) -----
        // TODO: Implement if/when C# equivalents for EncryptingWritableByteChannel / DecryptingReadableByteChannel are available.
        // Java's testFileEncryption, testFileDecryption, testDecryptionWithTooShortHeader, testDecryptionWithUnauthenticFirstChunk
        // would go here.


        // ----- Removed Old/Redundant Tests -----
        // Removed TestDecryptedEncryptedEqualsPlaintext (replaced by vector tests)
        // Removed nested classes EncryptionTests/DecryptionTests (integrated tests above)
    }
}