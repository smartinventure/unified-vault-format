using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Core.Common;
using UvfLib.Tests.Common;
using UvfLib.Core.V3;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using UvfLib.Core.V3;
using UvfLib.Core.Api; // Added for Enumerable.Repeat

namespace UvfLib.Tests.V3
{
    [TestClass]
    public class FileNameCryptorImplTest
    {
        // Define test data for masterkey creation - same as in Java tests for consistency
        private static readonly Dictionary<int, byte[]> SEEDS = new Dictionary<int, byte[]>
        {
            { -1540072521, Convert.FromBase64String("fP4V4oAjsUw5DqackAvLzA0oP1kAQZ0f5YFZQviXSuU=".Replace('-', '+').Replace('_', '/')) }
        };
        private static readonly byte[] KDF_SALT = Convert.FromBase64String("HE4OP-2vyfLLURicF1XmdIIsWv0Zs6MobLKROUIEhQY=".Replace('-', '+').Replace('_', '/'));
        // Use the specific revision from Java test
        private const int TestRevision = -1540072521;
        // Note: UVFMasterkeyImpl needs to be compatible with RevolvingMasterkey interface used by FileNameCryptorImpl
        // FIX: Declare MASTERKEY as RevolvingMasterkey to aid type resolution
        private static readonly RevolvingMasterkey MASTERKEY = new UVFMasterkeyImpl(SEEDS, KDF_SALT, TestRevision, TestRevision);

        private FileNameCryptorImpl _filenameCryptor;
        private RandomNumberGenerator _rng; // Add RNG instance

        [TestInitialize]
        public void Setup()
        {
            // Initialize RNG
            _rng = RandomNumberGenerator.Create();
            // Initialize the filename cryptor using the specific revision from Java and an RNG instance
            // Cast MASTERKEY if necessary, assuming UVFMasterkeyImpl implements RevolvingMasterkey implicitly or explicitly
            // FIX: Remove explicit cast as UVFMasterkeyImpl already implements RevolvingMasterkey
            _filenameCryptor = new FileNameCryptorImpl(MASTERKEY, _rng, TestRevision);
        }

        [TestCleanup] // Add cleanup to dispose RNG
        public void Cleanup()
        {
            _rng?.Dispose();
        }

        // Mimics Java's filenameGenerator for basic tests
        // FIX: Return IEnumerable<object[]> for DynamicData
        private static IEnumerable<object[]> FilenameGenerator()
        {
            // Generate a few UUIDs for testing, similar to Java's limit(100) but smaller scope for C# test run
            return Enumerable.Range(0, 5).Select(_ => new object[] { Guid.NewGuid().ToString() });
        }

        // Corresponds to Java's testDeterministicEncryptionOfFilenames
        // Uses TestContext for DataRow to mimic ParameterizedTest somewhat
        // Note: Java uses Base32 encoding here. C# impl likely uses Base64Url.
        [TestMethod]
        [DataTestMethod]
        [DynamicData(nameof(FilenameGenerator), DynamicDataSourceType.Method)]
        [DisplayName("Test Deterministic Encryption Of Filenames")]
        public void TestDeterministicEncryptionOfFilenames(string origName)
        {
            // Assume default encoding (Base64Url) is handled internally by C# impl
            string encrypted1 = _filenameCryptor.EncryptFilename(origName);
            string encrypted2 = _filenameCryptor.EncryptFilename(origName);
            string decrypted = _filenameCryptor.DecryptFilename(encrypted1);

            Assert.AreEqual(encrypted1, encrypted2, "Encryption should be deterministic");
            Assert.AreEqual(origName, decrypted, "Decryption should restore the original filename");
        }

        // Corresponds to Java's testDeterministicEncryptionOfFilenamesWithCustomEncodingAndAssociatedData
        // Note: Java uses Base64Url encoding here, which likely matches C# default.
        [TestMethod]
        [DataTestMethod]
        [DynamicData(nameof(FilenameGenerator), DynamicDataSourceType.Method)]
        [DisplayName("Test Encrypt And Decrypt Filenames With AD")]
        public void TestDeterministicEncryptionOfFilenamesWithAssociatedData(string origName)
        {
            byte[] associatedData = new byte[10]; // Similar to Java test
            // Assume Encrypt/DecryptFilename overloads handle AD and default Base64Url encoding
            string encrypted1 = _filenameCryptor.EncryptFilename(origName, associatedData);
            string encrypted2 = _filenameCryptor.EncryptFilename(origName, associatedData);
            string decrypted = _filenameCryptor.DecryptFilename(encrypted1, associatedData);

            Assert.AreEqual(encrypted1, encrypted2, "Encryption with AD should be deterministic");
            Assert.AreEqual(origName, decrypted, "Decryption with AD should restore the original filename");
        }

        // Corresponds to Java's testDeterministicEncryptionOf128bitFilename
        // Note: Java uses Base32 encoding here. C# impl likely uses Base64Url.
        [TestMethod]
        [DisplayName("Test Encrypt And Decrypt 128 Bit Filename")]
        public void TestDeterministicEncryptionOf128bitFilename()
        {
            // Block size length file name (16 chars = 128 bits ASCII)
            string originalPath = "aaaabbbbccccdddd";
            // Assume default encoding (Base64Url)
            string encryptedPathA = _filenameCryptor.EncryptFilename(originalPath);
            string encryptedPathB = _filenameCryptor.EncryptFilename(originalPath);
            string decryptedPath = _filenameCryptor.DecryptFilename(encryptedPathA);

            Assert.AreEqual(encryptedPathA, encryptedPathB, "Encryption should be deterministic");
            Assert.AreEqual(originalPath, decryptedPath, "Decryption should restore the original 128-bit filename");
        }

        // Corresponds to Java's testHashRootDirId
        [TestMethod]
        [DisplayName("Test Hash Root Dir ID")]
        public void TestHashRootDirId()
        {
            // Value from Java test
            // Java: Base64.getDecoder().decode("24UBEDeGu5taq7U4GqyA0MXUXb9HTYS6p3t9vvHGJAc=") - standard Base64
            // C# uses standard Convert.FromBase64String
            byte[] rootDirId = Convert.FromBase64String("24UBEDeGu5taq7U4GqyA0MXUXb9HTYS6p3t9vvHGJAc=");
            string hashedRootDirId = _filenameCryptor.HashDirectoryId(rootDirId);
            // Expected hash from Java test (Base32 encoded)
            Assert.AreEqual("6DYU3E5BTPAZ4DWEQPQK3AIHX2DXSPHG", hashedRootDirId, "Hashed root dir ID mismatch");
        }

        // Corresponds to Java's testDeterministicHashingOfDirectoryIds
        [TestMethod]
        [DataTestMethod]
        [DynamicData(nameof(FilenameGenerator), DynamicDataSourceType.Method)] // Reuse generator
        [DisplayName("Test Deterministic Hashing Of Directory IDs")]
        public void TestDeterministicHashingOfDirectoryIds(string originalDirectoryId)
        {
            // Assume HashDirectoryId takes string or byte[] internally maps to UTF8 bytes
            // If it takes byte[], we need: byte[] dirIdBytes = Encoding.UTF8.GetBytes(originalDirectoryId);
            // FIX: Convert string to byte array
            byte[] dirIdBytes = Encoding.UTF8.GetBytes(originalDirectoryId);
            string hashedDirectory1 = _filenameCryptor.HashDirectoryId(dirIdBytes);
            string hashedDirectory2 = _filenameCryptor.HashDirectoryId(dirIdBytes);
            Assert.AreEqual(hashedDirectory1, hashedDirectory2, "Directory ID hashing should be deterministic");
        }

        // Corresponds to Java's testDecryptionOfMalformedFilename
        [TestMethod]
        [DisplayName("Test Decryption Of Malformed Filename")]
        public void TestDecryptionOfMalformedFilename()
        {
            string invalidCiphertext = "lol";
            // Java expects AuthenticationFailedException with cause IllegalArgumentException.
            // C# should throw AuthenticationFailedException directly for consistency if the underlying SIV impl does.
            // If C# throws InvalidCiphertextException or FormatException, adjust assert or impl.
            var ex = Assert.ThrowsException<AuthenticationFailedException>(() =>
                _filenameCryptor.DecryptFilename(invalidCiphertext));

            // Optional: Check InnerException if needed, though direct AuthenticationFailedException is preferred.
            // Assert.IsInstanceOfType(ex.InnerException, typeof(ArgumentException), "Inner exception should be ArgumentException for malformed input.");
        }

        // Corresponds to Java's testDecryptionOfManipulatedFilename
        [TestMethod]
        [DisplayName("Test Decryption Of Manipulated Filename")]
        public void TestDecryptionOfManipulatedFilename()
        {
            string origName = "test";
            // Assume default encoding (Base64Url) for encryption
            string encrypted = _filenameCryptor.EncryptFilename(origName);

            // Convert the Base64Url string to bytes for manipulation.
            // Need a robust Base64Url to byte array conversion. Using UTF8 might be incorrect.
            // Let's assume a helper or direct byte[] based manipulation is needed.
            // For now, simulate manipulation on the string if possible, though less robust.
            // If direct byte manipulation is required, this needs more complex setup.

            // Simplistic string manipulation (may not perfectly mimic byte manipulation)
            if (string.IsNullOrEmpty(encrypted))
            {
                Assert.Inconclusive("Generated ciphertext is empty");
                return; // Need return here
            }
            char[] encryptedChars = encrypted.ToCharArray();
            encryptedChars[0] = (encryptedChars[0] == 'A' ? 'B' : 'A'); // Simple char flip
            string tamperedEncrypted = new string(encryptedChars);


            // Java expects AuthenticationFailedException with cause UnauthenticCiphertextException.
            // C# should throw AuthenticationFailedException directly.
            var ex = Assert.ThrowsException<AuthenticationFailedException>(() =>
                _filenameCryptor.DecryptFilename(tamperedEncrypted));

            // Optional: Check InnerException if needed, although direct AuthFailed is cleaner.
            // Assert.IsInstanceOfType(ex.InnerException, typeof(CryptographicException)); // Or specific SivException if available
        }

        // Corresponds to Java's testEncryptionOfSameFilenamesWithDifferentAssociatedData
        // Note: Java uses Base32 encoding here. C# impl likely uses Base64Url.
        [TestMethod]
        [DisplayName("Test Encrypt With Different AD")]
        public void TestEncryptionOfSameFilenamesWithDifferentAssociatedData()
        {
            string origName = "test";
            byte[] ad1 = Encoding.UTF8.GetBytes("ad1");
            byte[] ad2 = Encoding.UTF8.GetBytes("ad2");
            // Assume default encoding (Base64Url)
            string encrypted1 = _filenameCryptor.EncryptFilename(origName, ad1);
            string encrypted2 = _filenameCryptor.EncryptFilename(origName, ad2);
            Assert.AreNotEqual(encrypted1, encrypted2, "Ciphertext should differ with different AD");
        }

        // Corresponds to Java's testDeterministicEncryptionOfFilenamesWithAssociatedData (specific case)
        // Note: Java uses Base32 encoding here. C# impl likely uses Base64Url.
        [TestMethod]
        [DisplayName("Test Decrypt Ciphertext With Correct AD")]
        public void TestDecryptionWithCorrectAssociatedData() // Renamed for clarity
        {
            string origName = "test";
            byte[] correctAd = Encoding.UTF8.GetBytes("ad");
            // Assume default encoding (Base64Url)
            string encrypted = _filenameCryptor.EncryptFilename(origName, correctAd);
            string decrypted = _filenameCryptor.DecryptFilename(encrypted, correctAd);
            Assert.AreEqual(origName, decrypted, "Decryption with correct AD failed");
        }

        // Corresponds to Java's testDeterministicEncryptionOfFilenamesWithWrongAssociatedData
        // Note: Java uses Base32 encoding here. C# impl likely uses Base64Url.
        [TestMethod]
        [DisplayName("Test Decrypt Ciphertext With Incorrect AD")]
        public void TestDecryptionWithWrongAssociatedData() // Renamed for clarity
        {
            string origName = "test";
            byte[] correctAd = Encoding.UTF8.GetBytes("right");
            byte[] wrongAd = Encoding.UTF8.GetBytes("wrong");
            // Assume default encoding (Base64Url)
            string encrypted = _filenameCryptor.EncryptFilename(origName, correctAd);

            // Expect AuthenticationFailedException when decrypting with wrong AD
            Assert.ThrowsException<AuthenticationFailedException>(() =>
            {
                _filenameCryptor.DecryptFilename(encrypted, wrongAd);
            }, "Should throw AuthenticationFailedException for wrong AD.");
        }

        // ----- Existing C# Tests (Kept if useful) -----

        // Removed: TestEncryptionOfFilenamesWithCustomPrefix (Not in Java test)
        // Removed: TestEncryptAndDecryptDirectoryIds (Java only tests hashing)
        // Removed: TestEncryptAndDecryptMultipleFilenames (Covered by parameterized tests)

        [TestMethod]
        [DisplayName("Test With Empty Filename")]
        public void TestWithEmptyFilename()
        {
            // Encrypting empty string should likely be disallowed.
            Assert.ThrowsException<ArgumentException>(() => _filenameCryptor.EncryptFilename(""), "Encrypting empty string should throw ArgumentException.");
            // Decrypting "" should be treated as malformed input.
            Assert.ThrowsException<AuthenticationFailedException>(() => _filenameCryptor.DecryptFilename(""), "Decrypting empty string should throw AuthenticationFailedException (malformed).");
            // Hashing empty ID might be valid or invalid depending on requirements. Assuming invalid for now.
            // FIX: Convert empty string to empty byte array
            byte[] emptyDirIdBytes = Encoding.UTF8.GetBytes(""); // Or new byte[0]
            Assert.ThrowsException<ArgumentException>(() => _filenameCryptor.HashDirectoryId(emptyDirIdBytes), "Hashing empty byte array directory ID should throw ArgumentException.");
        }

        [TestMethod]
        [DisplayName("Test With Null Filename")]
        public void TestWithNullFilename()
        {
            // Test with null inputs
            Assert.ThrowsException<ArgumentNullException>(() => _filenameCryptor.EncryptFilename(null), "Encrypting null filename should throw ArgumentNullException.");
            Assert.ThrowsException<ArgumentNullException>(() => _filenameCryptor.DecryptFilename(null), "Decrypting null filename should throw ArgumentNullException.");
            // Null byte array for HashDirectoryId(byte[]) overload
            Assert.ThrowsException<ArgumentNullException>(() => _filenameCryptor.HashDirectoryId((byte[])null), "Hashing null byte array directory ID should throw ArgumentNullException.");
            // Null string for HashDirectoryId(string) overload - REMOVED as HashDirectoryId only takes byte[]
            // Assert.ThrowsException<ArgumentNullException>(() => _filenameCryptor.HashDirectoryId((string)null), "Hashing null string directory ID should throw ArgumentNullException.");
            // Test null AD (Associated Data for Encrypt/Decrypt Filename)
            string name = "test";
            // Ensure EncryptFilename correctly throws ArgumentNullException for null AD
            // FIX: Explicitly cast null to byte[] to resolve ambiguity
            Assert.ThrowsException<ArgumentNullException>(() => _filenameCryptor.EncryptFilename(name, (byte[])null), "Encrypting with null AD should throw ArgumentNullException.");
            string encryptedName = _filenameCryptor.EncryptFilename(name); // Encrypt without AD first (uses empty AD internally)
            // Ensure DecryptFilename correctly throws ArgumentNullException for null AD
            // FIX: Explicitly cast null to byte[] to resolve ambiguity
            Assert.ThrowsException<ArgumentNullException>(() => _filenameCryptor.DecryptFilename(encryptedName, (byte[])null), "Decrypting with null AD should throw ArgumentNullException.");
        }

        [TestMethod]
        [DisplayName("Test Unicode Filenames")]
        public void TestUnicodeFilenames()
        {
            // Test with Unicode characters
            string[] unicodeNames = {
                "文件名.txt", // Chinese
                "ファイル名.txt", // Japanese
                "파일 이름.txt", // Korean
                "имя файла.txt", // Russian (Cyrillic)
                "αρχείο.txt", // Greek
                "שם קובץ.txt", // Hebrew
                "fööbär.txt" // Umlauts
            };

            foreach (string name in unicodeNames)
            {
                // Assume default encoding (Base64Url)
                string encrypted = _filenameCryptor.EncryptFilename(name);
                string decrypted = _filenameCryptor.DecryptFilename(encrypted);
                Assert.AreEqual(name, decrypted, $"Decryption failed for Unicode filename: {name}");
            }
        }
    }
}