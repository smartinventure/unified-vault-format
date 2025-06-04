using System;
using System.Text;

namespace UvfLib.Common
{
    /// <summary>
    /// Utility class for Base64URL encoding and decoding.
    /// Base64URL is a URL and filename safe variant of Base64 that replaces '+' with '-', '/' with '_' and omits padding '='.
    /// </summary>
    public static class Base64Url
    {
        /// <summary>
        /// Encodes binary data to a Base64URL string without padding.
        /// </summary>
        /// <param name="data">The binary data to encode</param>
        /// <returns>A Base64URL encoded string</returns>
        public static string Encode(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return Convert.ToBase64String(data)
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Decodes a Base64URL string to binary data.
        /// </summary>
        /// <param name="base64Url">The Base64URL encoded string</param>
        /// <returns>The decoded binary data</returns>
        public static byte[] Decode(string base64Url)
        {
            if (string.IsNullOrEmpty(base64Url))
                throw new ArgumentException("Input cannot be null or empty", nameof(base64Url));

            string padded = base64Url
                .Replace('-', '+')
                .Replace('_', '/');

            // Add padding if needed
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            return Convert.FromBase64String(padded);
        }

        /// <summary>
        /// Decodes a Base64URL string to an UTF-8 string.
        /// </summary>
        /// <param name="base64Url">The Base64URL encoded string</param>
        /// <returns>The decoded UTF-8 string</returns>
        public static string DecodeToString(string base64Url)
        {
            byte[] bytes = Decode(base64Url);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Encodes a UTF-8 string to a Base64URL string.
        /// </summary>
        /// <param name="text">The UTF-8 string to encode</param>
        /// <returns>A Base64URL encoded string</returns>
        public static string EncodeString(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return Encode(bytes);
        }
    }
}