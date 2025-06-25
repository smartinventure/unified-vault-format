using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Text;
using UvfLib.Core.CryptomatorV8;
using System.Linq;

namespace UvfLib.Tests
{
    [TestClass]
    public class NameShorteningTests
    {
        [TestMethod]
        public void NeedsShortening_ShortFilename_ReturnsFalse()
        {
            // Arrange
            string shortFilename = "short.c9r";
            
            // Act
            bool result = NameShorteningHelper.NeedsShortening(shortFilename);
            
            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void NeedsShortening_LongFilename_ReturnsTrue()
        {
            // Arrange - Create a filename longer than 220 characters
            string longFilename = new string('A', 200) + ".c9r" + new string('B', 20);
            
            // Act
            bool result = NameShorteningHelper.NeedsShortening(longFilename);
            
            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(longFilename.Length > Constants.SHORTENING_THRESHOLD);
        }

        [TestMethod]
        public void NeedsShortening_ExactThreshold_ReturnsFalse()
        {
            // Arrange - Create a filename exactly 220 characters
            string exactFilename = new string('A', Constants.SHORTENING_THRESHOLD);
            
            // Act
            bool result = NameShorteningHelper.NeedsShortening(exactFilename);
            
            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(Constants.SHORTENING_THRESHOLD, exactFilename.Length);
        }

        [TestMethod]
        public void NeedsShortening_OneOverThreshold_ReturnsTrue()
        {
            // Arrange - Create a filename one character over the threshold
            string overFilename = new string('A', Constants.SHORTENING_THRESHOLD + 1);
            
            // Act
            bool result = NameShorteningHelper.NeedsShortening(overFilename);
            
            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(Constants.SHORTENING_THRESHOLD + 1, overFilename.Length);
        }

        [TestMethod]
        public void CreateShortenedDirectoryName_ValidInput_ReturnsCorrectFormat()
        {
            // Arrange
            string longFilename = "VGhpc19pc19hX3ZlcnlfdmVyeV92ZXJ5X2xvbmdfZmlsZW5hbWVfdGhhdF93aWxsX2V4Y2VlZF90aGVfMjIwX2NoYXJhY3Rlcl9saW1pdF93aGVuX2VuY3J5cHRlZF9hbmRfYmFzZTY0X2VuY29kZWQudHh0.c9r";
            
            // Act
            string result = NameShorteningHelper.CreateShortenedDirectoryName(longFilename);
            
            // Assert
            Assert.IsTrue(result.EndsWith(Constants.C9S_DIR_EXT));
            Assert.IsTrue(result.Length < Constants.SHORTENING_THRESHOLD);
            Assert.IsTrue(result.Length > Constants.C9S_DIR_EXT.Length);
        }

        [TestMethod]
        public void CreateShortenedDirectoryName_SameInput_ReturnsSameOutput()
        {
            // Arrange
            string longFilename = "very_long_encrypted_filename_that_exceeds_threshold.c9r";
            
            // Act
            string result1 = NameShorteningHelper.CreateShortenedDirectoryName(longFilename);
            string result2 = NameShorteningHelper.CreateShortenedDirectoryName(longFilename);
            
            // Assert
            Assert.AreEqual(result1, result2);
        }

        [TestMethod]
        public void CreateShortenedDirectoryName_DifferentInputs_ReturnsDifferentOutputs()
        {
            // Arrange
            string filename1 = "long_filename_1_that_exceeds_threshold.c9r";
            string filename2 = "long_filename_2_that_exceeds_threshold.c9r";
            
            // Act
            string result1 = NameShorteningHelper.CreateShortenedDirectoryName(filename1);
            string result2 = NameShorteningHelper.CreateShortenedDirectoryName(filename2);
            
            // Assert
            Assert.AreNotEqual(result1, result2);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateShortenedDirectoryName_NullInput_ThrowsException()
        {
            // Act
            NameShorteningHelper.CreateShortenedDirectoryName(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void CreateShortenedDirectoryName_EmptyInput_ThrowsException()
        {
            // Act
            NameShorteningHelper.CreateShortenedDirectoryName("");
        }

        [TestMethod]
        public void IsShortenedDirectory_ValidShortenedName_ReturnsTrue()
        {
            // Arrange
            string shortenedName = "ABC123DEF456.c9s";
            
            // Act
            bool result = NameShorteningHelper.IsShortenedDirectory(shortenedName);
            
            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsShortenedDirectory_RegularFilename_ReturnsFalse()
        {
            // Arrange
            string regularName = "regular_file.c9r";
            
            // Act
            bool result = NameShorteningHelper.IsShortenedDirectory(regularName);
            
            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsShortenedDirectory_NullInput_ReturnsFalse()
        {
            // Act
            bool result = NameShorteningHelper.IsShortenedDirectory(null);
            
            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsShortenedDirectory_EmptyInput_ReturnsFalse()
        {
            // Act
            bool result = NameShorteningHelper.IsShortenedDirectory("");
            
            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ExtractHashFromShortenedName_ValidInput_ReturnsHash()
        {
            // Arrange
            string shortenedName = "ABC123DEF456.c9s";
            string expectedHash = "ABC123DEF456";
            
            // Act
            string result = NameShorteningHelper.ExtractHashFromShortenedName(shortenedName);
            
            // Assert
            Assert.AreEqual(expectedHash, result);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ExtractHashFromShortenedName_InvalidInput_ThrowsException()
        {
            // Arrange
            string invalidName = "not_shortened.c9r";
            
            // Act
            NameShorteningHelper.ExtractHashFromShortenedName(invalidName);
        }

        [TestMethod]
        public void GetInflatedNameFilePath_ValidInput_ReturnsCorrectPath()
        {
            // Arrange
            string shortenedName = "ABC123.c9s";
            string expectedPath = "ABC123.c9s/name.c9s";
            
            // Act
            string result = NameShorteningHelper.GetInflatedNameFilePath(shortenedName);
            
            // Assert
            Assert.AreEqual(expectedPath, result);
        }

        [TestMethod]
        public void GetContentsFilePath_ValidInput_ReturnsCorrectPath()
        {
            // Arrange
            string shortenedName = "ABC123.c9s";
            string expectedPath = "ABC123.c9s/contents.c9r";
            
            // Act
            string result = NameShorteningHelper.GetContentsFilePath(shortenedName);
            
            // Assert
            Assert.AreEqual(expectedPath, result);
        }

        [TestMethod]
        public void GetDirectoryFilePath_ValidInput_ReturnsCorrectPath()
        {
            // Arrange
            string shortenedName = "ABC123.c9s";
            string expectedPath = "ABC123.c9s/dir.c9r";
            
            // Act
            string result = NameShorteningHelper.GetDirectoryFilePath(shortenedName);
            
            // Assert
            Assert.AreEqual(expectedPath, result);
        }

        [TestMethod]
        public void GetSymlinkFilePath_ValidInput_ReturnsCorrectPath()
        {
            // Arrange
            string shortenedName = "ABC123.c9s";
            string expectedPath = "ABC123.c9s/symlink.c9r";
            
            // Act
            string result = NameShorteningHelper.GetSymlinkFilePath(shortenedName);
            
            // Assert
            Assert.AreEqual(expectedPath, result);
        }

        [TestMethod]
        public void ValidateShortenedName_CorrectPair_ReturnsTrue()
        {
            // Arrange
            string originalFilename = "very_long_filename_that_needs_shortening.c9r";
            string shortenedName = NameShorteningHelper.CreateShortenedDirectoryName(originalFilename);
            
            // Act
            bool result = NameShorteningHelper.ValidateShortenedName(shortenedName, originalFilename);
            
            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ValidateShortenedName_IncorrectPair_ReturnsFalse()
        {
            // Arrange
            string originalFilename1 = "filename1.c9r";
            string originalFilename2 = "filename2.c9r";
            string shortenedName = NameShorteningHelper.CreateShortenedDirectoryName(originalFilename1);
            
            // Act
            bool result = NameShorteningHelper.ValidateShortenedName(shortenedName, originalFilename2);
            
            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ValidateShortenedName_NotShortenedName_ReturnsFalse()
        {
            // Arrange
            string regularFilename = "regular.c9r";
            string originalFilename = "original.c9r";
            
            // Act
            bool result = NameShorteningHelper.ValidateShortenedName(regularFilename, originalFilename);
            
            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Constants_ShorteningThreshold_IsCorrectValue()
        {
            // Assert
            Assert.AreEqual(220, Constants.SHORTENING_THRESHOLD);
        }

        [TestMethod]
        public void Constants_FileExtensions_AreCorrect()
        {
            // Assert
            Assert.AreEqual(".c9r", Constants.C9R_FILE_EXT);
            Assert.AreEqual(".c9s", Constants.C9S_DIR_EXT);
        }

        [TestMethod]
        public void Constants_ShorteningFileNames_AreCorrect()
        {
            // Assert
            Assert.AreEqual("name.c9s", Constants.INFLATED_NAME_FILE);
            Assert.AreEqual("contents.c9r", Constants.SHORTENED_CONTENTS_FILE);
            Assert.AreEqual("dir.c9r", Constants.SHORTENED_DIR_FILE);
            Assert.AreEqual("symlink.c9r", Constants.SHORTENED_SYMLINK_FILE);
        }

        [TestMethod]
        public void NameShortening_RealWorldExample_WorksCorrectly()
        {
            // Arrange - Simulate a very long filename that would be generated by encryption
            string longCleartext = "This_is_a_very_very_very_long_filename_that_will_definitely_exceed_the_220_character_limit_when_encrypted_and_base64_encoded_with_cryptomator_encryption_algorithm.pdf";
            
            // Simulate what an encrypted filename might look like (Base64URL encoded)
            byte[] cleartextBytes = Encoding.UTF8.GetBytes(longCleartext);
            string simulatedEncrypted = Convert.ToBase64String(cleartextBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=') + Constants.C9R_FILE_EXT;
            
            // Verify it's long enough to trigger shortening
            Assert.IsTrue(simulatedEncrypted.Length > Constants.SHORTENING_THRESHOLD);
            
            // Act
            bool needsShortening = NameShorteningHelper.NeedsShortening(simulatedEncrypted);
            string shortenedName = NameShorteningHelper.CreateShortenedDirectoryName(simulatedEncrypted);
            
            // Assert
            Assert.IsTrue(needsShortening);
            Assert.IsTrue(NameShorteningHelper.IsShortenedDirectory(shortenedName));
            Assert.IsTrue(shortenedName.Length < Constants.SHORTENING_THRESHOLD);
            Assert.IsTrue(NameShorteningHelper.ValidateShortenedName(shortenedName, simulatedEncrypted));
            
            // Verify the structure paths
            string nameFilePath = NameShorteningHelper.GetInflatedNameFilePath(shortenedName);
            string contentsFilePath = NameShorteningHelper.GetContentsFilePath(shortenedName);
            
            Assert.IsTrue(nameFilePath.EndsWith("/name.c9s"));
            Assert.IsTrue(contentsFilePath.EndsWith("/contents.c9r"));
        }

        [TestMethod]
        public void NameShortening_EdgeCase_Exactly220Characters()
        {
            // Arrange - Create a filename that's exactly 220 characters
            string exactly220 = new string('A', Constants.SHORTENING_THRESHOLD - 4) + ".c9r";
            Assert.AreEqual(Constants.SHORTENING_THRESHOLD, exactly220.Length);
            
            // Act
            bool needsShortening = NameShorteningHelper.NeedsShortening(exactly220);
            
            // Assert - Should NOT need shortening (threshold is exclusive)
            Assert.IsFalse(needsShortening);
        }

        [TestMethod]
        public void NameShortening_EdgeCase_221Characters()
        {
            // Arrange - Create a filename that's 221 characters (one over threshold)
            string over220 = new string('A', Constants.SHORTENING_THRESHOLD - 3) + ".c9r";
            Assert.AreEqual(Constants.SHORTENING_THRESHOLD + 1, over220.Length);
            
            // Act
            bool needsShortening = NameShorteningHelper.NeedsShortening(over220);
            
            // Assert - Should need shortening
            Assert.IsTrue(needsShortening);
        }

        [TestMethod]
        public void DecryptShortenedFilename_ValidInputs_ReturnsDecryptedName()
        {
            // This test simulates the decryption workflow but doesn't actually encrypt/decrypt
            // since we don't have a full cryptor setup in this test class
            
            // Arrange
            string originalLongFilename = "VGhpc19pc19hX3ZlcnlfdmVyeV92ZXJ5X2xvbmdfZmlsZW5hbWVfdGhhdF93aWxsX2V4Y2VlZF90aGVfMjIwX2NoYXJhY3Rlcl9saW1pdF93aGVuX2VuY3J5cHRlZF9hbmRfYmFzZTY0X2VuY29kZWQudHh0.c9r";
            string shortenedName = NameShorteningHelper.CreateShortenedDirectoryName(originalLongFilename);
            
            // Act & Assert - Verify the validation works
            bool isValid = NameShorteningHelper.ValidateShortenedName(shortenedName, originalLongFilename);
            Assert.IsTrue(isValid);
            
            // Verify the shortened name is actually shorter
            Assert.IsTrue(shortenedName.Length < Constants.SHORTENING_THRESHOLD);
            Assert.IsTrue(originalLongFilename.Length > Constants.SHORTENING_THRESHOLD);
        }

        [TestMethod]
        public void DecryptShortenedFilename_MismatchedInputs_ShouldFailValidation()
        {
            // Arrange
            string originalFilename1 = "long_filename_1_that_exceeds_threshold.c9r";
            string originalFilename2 = "long_filename_2_that_exceeds_threshold.c9r";
            string shortenedName = NameShorteningHelper.CreateShortenedDirectoryName(originalFilename1);
            
            // Act & Assert - Should fail validation
            bool isValid = NameShorteningHelper.ValidateShortenedName(shortenedName, originalFilename2);
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void ShorteningWorkflow_EncryptThenValidate_WorksCorrectly()
        {
            // Arrange - Simulate the complete workflow
            string longCleartext = "This_is_a_very_very_very_long_filename_that_will_definitely_exceed_the_220_character_limit_when_encrypted_and_base64_encoded.pdf";
            
            // Simulate encryption result (this would normally come from FileNameCryptor)
            byte[] cleartextBytes = Encoding.UTF8.GetBytes(longCleartext);
            string simulatedEncrypted = Convert.ToBase64String(cleartextBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=') + Constants.C9R_FILE_EXT;
            
            // Act - Apply shortening logic
            string result;
            if (NameShorteningHelper.NeedsShortening(simulatedEncrypted))
            {
                result = NameShorteningHelper.CreateShortenedDirectoryName(simulatedEncrypted);
            }
            else
            {
                result = simulatedEncrypted;
            }
            
            // Assert
            Assert.IsTrue(NameShorteningHelper.IsShortenedDirectory(result));
            Assert.IsTrue(result.Length < Constants.SHORTENING_THRESHOLD);
            
            // Verify we can validate the relationship
            Assert.IsTrue(NameShorteningHelper.ValidateShortenedName(result, simulatedEncrypted));
            
            // Verify the file structure paths
            string nameFilePath = NameShorteningHelper.GetInflatedNameFilePath(result);
            string contentsFilePath = NameShorteningHelper.GetContentsFilePath(result);
            
            Assert.AreEqual(result + "/name.c9s", nameFilePath);
            Assert.AreEqual(result + "/contents.c9r", contentsFilePath);
        }

        [TestMethod]
        public void ShorteningDecryptionWorkflow_CompleteRoundTrip_Simulation()
        {
            // This test simulates the complete encryption -> shortening -> decryption workflow
            
            // Step 1: Original cleartext filename
            string originalCleartext = "My_Very_Long_Document_Name_That_Will_Definitely_Exceed_The_Cryptomator_Filename_Length_Threshold_When_Encrypted_With_AES_SIV_And_Base64URL_Encoded.pdf";
            
            // Step 2: Simulate encryption (normally done by FileNameCryptor)
            byte[] cleartextBytes = Encoding.UTF8.GetBytes(originalCleartext);
            string longEncryptedFilename = Convert.ToBase64String(cleartextBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=') + Constants.C9R_FILE_EXT;
            
            // Step 3: Apply shortening if needed
            string storedFilename;
            if (NameShorteningHelper.NeedsShortening(longEncryptedFilename))
            {
                storedFilename = NameShorteningHelper.CreateShortenedDirectoryName(longEncryptedFilename);
            }
            else
            {
                storedFilename = longEncryptedFilename;
            }
            
            // Step 4: Verify shortening was applied
            Assert.IsTrue(NameShorteningHelper.IsShortenedDirectory(storedFilename));
            Assert.IsTrue(longEncryptedFilename.Length > Constants.SHORTENING_THRESHOLD);
            Assert.IsTrue(storedFilename.Length < Constants.SHORTENING_THRESHOLD);
            
            // Step 5: Simulate reading from storage (what would happen during decryption)
            // In real implementation, we would:
            // 1. Detect that storedFilename is a .c9s directory
            // 2. Read the original filename from storedFilename/name.c9s
            // 3. Validate that the shortened name matches the original
            // 4. Decrypt the original filename to get back the cleartext
            
            string nameFilePath = NameShorteningHelper.GetInflatedNameFilePath(storedFilename);
            string contentsFilePath = NameShorteningHelper.GetContentsFilePath(storedFilename);
            
            // Simulate reading the original filename from name.c9s file
            string retrievedOriginalFilename = longEncryptedFilename; // This would be read from storage
            
            // Step 6: Validate the relationship
            bool isValidRelationship = NameShorteningHelper.ValidateShortenedName(storedFilename, retrievedOriginalFilename);
            Assert.IsTrue(isValidRelationship);
            
            // Step 7: At this point, we would decrypt retrievedOriginalFilename to get back originalCleartext
            // (This would be done by FileNameCryptor.DecryptFilename())
            
            // Verify all the expected file paths exist in the shortened structure
            Assert.AreEqual(storedFilename + "/name.c9s", nameFilePath);
            Assert.AreEqual(storedFilename + "/contents.c9r", contentsFilePath);
        }

        [TestMethod]
        public void NameShortening_HashConsistency_SameInputProducesSameHash()
        {
            // Arrange
            string longFilename = "consistent_test_filename_that_exceeds_threshold.c9r";
            
            // Act - Generate hash multiple times
            string hash1 = NameShorteningHelper.CreateShortenedDirectoryName(longFilename);
            string hash2 = NameShorteningHelper.CreateShortenedDirectoryName(longFilename);
            string hash3 = NameShorteningHelper.CreateShortenedDirectoryName(longFilename);
            
            // Assert - All hashes should be identical
            Assert.AreEqual(hash1, hash2);
            Assert.AreEqual(hash2, hash3);
            Assert.AreEqual(hash1, hash3);
        }

        [TestMethod]
        public void NameShortening_HashUniqueness_DifferentInputsProduceDifferentHashes()
        {
            // Arrange
            var filenames = new[]
            {
                "filename_1_that_exceeds_threshold.c9r",
                "filename_2_that_exceeds_threshold.c9r", 
                "filename_3_that_exceeds_threshold.c9r",
                "completely_different_filename_that_also_exceeds_threshold.c9r"
            };
            
            // Act - Generate hashes for all filenames
            var hashes = filenames.Select(NameShorteningHelper.CreateShortenedDirectoryName).ToArray();
            
            // Assert - All hashes should be unique
            var uniqueHashes = hashes.Distinct().ToArray();
            Assert.AreEqual(filenames.Length, uniqueHashes.Length);
            
            // Verify each hash is properly formatted
            foreach (var hash in hashes)
            {
                Assert.IsTrue(NameShorteningHelper.IsShortenedDirectory(hash));
                Assert.IsTrue(hash.EndsWith(Constants.C9S_DIR_EXT));
                Assert.IsTrue(hash.Length < Constants.SHORTENING_THRESHOLD);
            }
        }
    }
} 