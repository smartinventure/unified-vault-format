using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UvfLib.Common
{
    /// <summary>
    /// Utility for JSON serialization and deserialization.
    /// </summary>
    public static class JsonSerialization
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        /// <summary>
        /// Serializes an object to a JSON string.
        /// </summary>
        /// <typeparam name="T">The type of the object</typeparam>
        /// <param name="obj">The object to serialize</param>
        /// <returns>The JSON string</returns>
        public static string ToJson<T>(T obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            return JsonSerializer.Serialize(obj, _options);
        }

        /// <summary>
        /// Deserializes a JSON string to an object.
        /// </summary>
        /// <typeparam name="T">The type of the object</typeparam>
        /// <param name="json">The JSON string</param>
        /// <returns>The deserialized object</returns>
        public static T FromJson<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            return JsonSerializer.Deserialize<T>(json, _options) ??
                throw new InvalidOperationException("Deserialization returned null");
        }

        /// <summary>
        /// Deserializes a JSON string to an object.
        /// </summary>
        /// <param name="json">The JSON string</param>
        /// <param name="type">The type of the object</param>
        /// <returns>The deserialized object</returns>
        public static object FromJson(string json, Type type)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return JsonSerializer.Deserialize(json, type, _options) ??
                throw new InvalidOperationException("Deserialization returned null");
        }

        /// <summary>
        /// Clones an object by serializing and deserializing it.
        /// </summary>
        /// <typeparam name="T">The type of the object</typeparam>
        /// <param name="obj">The object to clone</param>
        /// <returns>A clone of the object</returns>
        public static T Clone<T>(T obj)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            string json = ToJson(obj);
            return FromJson<T>(json);
        }
    }
}