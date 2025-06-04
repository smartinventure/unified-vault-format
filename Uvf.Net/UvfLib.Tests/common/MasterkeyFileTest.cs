using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using UvfLib._old.common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class MasterkeyFileTest
    {
        [TestMethod]
        [DisplayName("Test Read Masterkey File")]
        public void TestRead()
        {
            // Create a JSON string with Base64 encoded content
            string json = "{\"scryptSalt\": \"Zm9v\"}"; // "foo" Base64 encoded is "Zm9v"

            // Read the masterkey file from the JSON
            MasterkeyFile masterkeyFile = MasterkeyFile.FromJson(Encoding.UTF8.GetBytes(json));

            // Verify the scryptSalt was correctly decoded from Base64
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("foo"), masterkeyFile.ScryptSalt);
        }

        [TestMethod]
        [DisplayName("Test Write Masterkey File")]
        public void TestWrite()
        {
            // Create a masterkey file with a simple property
            MasterkeyFile masterkeyFile = new MasterkeyFile();
            masterkeyFile.ScryptSalt = Encoding.UTF8.GetBytes("foo");

            // Serialize to JSON
            byte[] jsonBytes = masterkeyFile.ToJson();
            string json = Encoding.UTF8.GetString(jsonBytes);

            // Verify the JSON contains the expected Base64 string
            StringAssert.Contains(json, "\"scryptSalt\": \"Zm9v\"");
        }

        [TestMethod]
        [DisplayName("Test Serialize And Deserialize")]
        public void TestSerializeAndDeserialize()
        {
            // Create a masterkey file with various properties
            MasterkeyFile original = new MasterkeyFile
            {
                Version = 3,
                ScryptSalt = Encoding.UTF8.GetBytes("salt"),
                ScryptCostParam = 16384,
                ScryptBlockSize = 8,
                ScryptParallelism = 1,
                PrimaryMasterkey = "encryptedKey",
                PrimaryMasterkeyNonce = "nonce",
                PrimaryMasterkeyMac = "mac",
                VaultVersion = 8,
                ContentEncryptionScheme = "SIV_GCM",
                FilenameEncryptionScheme = "SIV"
            };

            // Serialize to JSON
            byte[] jsonBytes = original.ToJson();

            // Deserialize back to object
            MasterkeyFile deserialized = MasterkeyFile.FromJson(jsonBytes);

            // Verify properties were preserved
            Assert.AreEqual(original.Version, deserialized.Version);
            CollectionAssert.AreEqual(original.ScryptSalt, deserialized.ScryptSalt);
            Assert.AreEqual(original.ScryptCostParam, deserialized.ScryptCostParam);
            Assert.AreEqual(original.ScryptBlockSize, deserialized.ScryptBlockSize);
            Assert.AreEqual(original.ScryptParallelism, deserialized.ScryptParallelism);
            Assert.AreEqual(original.PrimaryMasterkey, deserialized.PrimaryMasterkey);
            Assert.AreEqual(original.PrimaryMasterkeyNonce, deserialized.PrimaryMasterkeyNonce);
            Assert.AreEqual(original.PrimaryMasterkeyMac, deserialized.PrimaryMasterkeyMac);
            Assert.AreEqual(original.VaultVersion, deserialized.VaultVersion);
            Assert.AreEqual(original.ContentEncryptionScheme, deserialized.ContentEncryptionScheme);
            Assert.AreEqual(original.FilenameEncryptionScheme, deserialized.FilenameEncryptionScheme);
        }
    }
}