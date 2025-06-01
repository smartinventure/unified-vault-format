// Copyright (c) Smart In Venture GmbH 2025 of the C# Porting


using System.Security.Cryptography;
using System.Text;

namespace UvfLib.VaultHelpers
{
    public class FastHash
    {
        private const int NumBytes = 1024;
        private const long FullHashThreshold = 5 * 1024 * 1024; // 5 MB

        /// <summary>
        /// Calculates a hash for a given file stream.
        /// If the file is smaller than FullHashThreshold, the entire file is hashed.
        /// Otherwise, it takes 1024 bytes at the beginning, middle, and end of the stream.
        /// The file length is always concatenated with the bytes before hashing with SHA256.
        /// </summary>
        /// <param name="stream">A stream of data for the file.</param>
        /// <returns>A hexadecimal string representation of the SHA256 hash.</returns>
        public static string GetHash(Stream stream)
        {
            var streamLength = stream.Length;
            var lengthBytes = BitConverter.GetBytes(streamLength);

            byte[] bytesToHash;

            if (streamLength < FullHashThreshold)
            {
                stream.Position = 0;
                var fileBytes = new byte[streamLength];
                stream.Read(fileBytes, 0, (int)streamLength); // Read the entire file

                bytesToHash = new byte[streamLength + lengthBytes.Length];
                Array.Copy(fileBytes, 0, bytesToHash, 0, streamLength);
                Array.Copy(lengthBytes, 0, bytesToHash, streamLength, lengthBytes.Length);
            }
            else
            {
                bytesToHash = new byte[(3 * NumBytes) + lengthBytes.Length];

                stream.Position = 0;
                stream.Read(bytesToHash, 0, NumBytes); // Read first 1024 bytes

                stream.Position = (streamLength / 2) - (NumBytes / 2); // Seek to middle
                stream.Read(bytesToHash, NumBytes, NumBytes); // Read middle 1024 bytes

                stream.Position = streamLength - NumBytes; // Seek to end
                stream.Read(bytesToHash, 2 * NumBytes, NumBytes); // Read last 1024 bytes

                Array.Copy(lengthBytes, 0, bytesToHash, 3 * NumBytes, lengthBytes.Length); // Append length
            }

            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(bytesToHash);
                // Convert byte array to a hexadecimal string
                var sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}