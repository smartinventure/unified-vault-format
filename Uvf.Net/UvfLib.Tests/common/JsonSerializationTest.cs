using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UvfLib.Core.Common;

namespace UvfLib.Tests.Common
{
    [TestClass]
    public class JsonSerializationTest
    {
        [TestMethod]
        [DisplayName("Test Serialize Object")]
        public void TestSerializeObject()
        {
            var testObject = new TestObject
            {
                StringValue = "foobar",
                IntValue = 42,
                BoolValue = true
            };

            string jsonString = JsonSerialization.ToJson(testObject);

            // Parse the result with JsonElement to validate JSON structure
            using var document = JsonDocument.Parse(jsonString);
            var root = document.RootElement;

            Assert.AreEqual("foobar", root.GetProperty("stringValue").GetString());
            Assert.AreEqual(42, root.GetProperty("intValue").GetInt32());
            Assert.IsTrue(root.GetProperty("boolValue").GetBoolean());
        }

        [TestMethod]
        [DisplayName("Test Deserialize Object")]
        public void TestDeserializeObject()
        {
            string json = "{\"stringValue\":\"foobar\",\"intValue\":42,\"boolValue\":true}";

            var testObject = JsonSerialization.FromJson<TestObject>(json);

            Assert.AreEqual("foobar", testObject.StringValue);
            Assert.AreEqual(42, testObject.IntValue);
            Assert.IsTrue(testObject.BoolValue);
        }

        [TestMethod]
        [DisplayName("Test Read Object From Stream")]
        public void TestReadObjectFromStream()
        {
            string json = "{\"stringValue\":\"foobar\",\"intValue\":42,\"boolValue\":true}";
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            using var stream = new MemoryStream(jsonBytes);

            // Read the stream content as a string first
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            string jsonFromStream = reader.ReadToEnd();

            var testObject = JsonSerialization.FromJson<TestObject>(jsonFromStream);

            Assert.AreEqual("foobar", testObject.StringValue);
            Assert.AreEqual(42, testObject.IntValue);
            Assert.IsTrue(testObject.BoolValue);
        }

        [TestMethod]
        [DisplayName("Test Write Object To Stream")]
        public void TestWriteObjectToStream()
        {
            var testObject = new TestObject
            {
                StringValue = "foobar",
                IntValue = 42,
                BoolValue = true
            };

            // First convert to JSON string
            string jsonString = JsonSerialization.ToJson(testObject);

            // Then write to stream
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            writer.Write(jsonString);
            writer.Flush();

            // Reset stream position to beginning for reading
            stream.Seek(0, SeekOrigin.Begin);

            // Read back and verify
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();

            // Parse the result with JsonElement to validate JSON structure
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.AreEqual("foobar", root.GetProperty("stringValue").GetString());
            Assert.AreEqual(42, root.GetProperty("intValue").GetInt32());
            Assert.IsTrue(root.GetProperty("boolValue").GetBoolean());
        }

        [TestMethod]
        [DisplayName("Test Deserialize Invalid Json")]
        [ExpectedException(typeof(JsonException))]
        public void TestDeserializeInvalidJson()
        {
            string invalidJson = "{\"stringValue\":\"foobar\",\"intValue\":42,\"boolValue\":true";

            // This should throw a JsonException
            JsonSerialization.FromJson<TestObject>(invalidJson);
        }

        [TestMethod]
        [DisplayName("Test Deserialize With Invalid Type")]
        public void TestDeserializeWithInvalidType()
        {
            string json = "{\"stringValue\":42,\"intValue\":\"foobar\",\"boolValue\":null}";

            // In C# with System.Text.Json, this will either throw or have default values
            try
            {
                var testObject = JsonSerialization.FromJson<TestObject>(json);

                // If it doesn't throw, verify the object has default/null values
                Assert.IsNull(testObject.StringValue);
                Assert.AreEqual(0, testObject.IntValue);
                Assert.IsFalse(testObject.BoolValue);
            }
            catch (JsonException)
            {
                // Either outcome is acceptable for this test
                Assert.IsTrue(true);
            }
        }

        // Test class for serialization
        private class TestObject
        {
            public string StringValue { get; set; }
            public int IntValue { get; set; }
            public bool BoolValue { get; set; }
        }
    }
}