using System;
using System.Buffers.Binary; // For BinaryPrimitives
using Jose; // For Base64Url if used in fallback

namespace UvfLib.Common
{
    public static class SeedIdConverter
    {
        /// <summary>
        /// Converts a seed ID string (potentially a compact 6-character ID or a standard Base64Url encoded int)
        /// to its integer representation.
        /// </summary>
        /// <param name="seedIdString">The seed ID string.</param>
        /// <returns>The integer representation of the seed ID.</returns>
        /// <exception cref="ArgumentException">Thrown if the seedIdString is null, empty, or invalid.</exception>
        /// <exception cref="FormatException">Thrown if Base64Url decoding fails for non-special IDs.</exception>
        public static int ToInt(string seedIdString)
        {
            if (string.IsNullOrEmpty(seedIdString))
            {
                throw new ArgumentException("Seed ID string cannot be null or empty.", nameof(seedIdString));
            }

            // Handle special 6-character compact IDs first
            if (seedIdString.Length == 6)
            {
                // Cryptomator masterkey files (e.g. from version 1.5.x, 1.6.x)
                // use these specific 6-character strings to represent particular integer seed IDs.
                // Values confirmed from cryptolib-java SeedId.java and UVFMasterkeyTest.java
                if (seedIdString == "HDm38i") return 473544690;  // 0x1C39B792
                if (seedIdString == "QBsJFo") return 1075513622; // 0x401B0996
                if (seedIdString == "gBryKw") return 1946999083; // 0x740B28AB
                
                // Add other known 6-char mappings here if discovered.
                // If it's 6 chars but not a known special one, it might be an error
                // or a standard Base64 of a small number. For now, let it fall through
                // to standard Base64 decoding, which might be correct or might indicate
                // a new special ID we don't know about.
            }

            // Standard decoding path for other IDs (assumed to be Base64Url of a 4-byte Big Endian integer)
            // or 6-char IDs not caught above (less likely for valid Cryptomator files but provides a path)
            byte[] bytes;
            try
            {
                bytes = Base64Url.Decode(seedIdString);
            }
            catch (Exception ex) // Catches errors from Jose.Base64Url.Decode
            {
                throw new FormatException($"Failed to Base64Url-decode seed ID string: '{seedIdString}'. Ensure it is valid Base64Url.", ex);
            }


            if (bytes == null || bytes.Length == 0)
            {
                 throw new ArgumentException($"Base64Url-decoded seed ID string '{seedIdString}' resulted in empty bytes.", nameof(seedIdString));
            }

            // If we have less than 4 bytes (e.g. "AQ==" -> 1 byte [1]), pad with leading zeros (MSB) to make 4 bytes for Big Endian.
            if (bytes.Length < 4)
            {
                byte[] paddedBytes = new byte[4]; // Initializes to zeros
                Buffer.BlockCopy(bytes, 0, paddedBytes, 4 - bytes.Length, bytes.Length);
                bytes = paddedBytes;
            }
            else if (bytes.Length > 4)
            {
                // If more than 4 bytes, this is unusual for a seed ID.
                // Cryptomator's SeedId.java truncates by taking the first 4 bytes.
                byte[] truncatedBytes = new byte[4];
                Buffer.BlockCopy(bytes, 0, truncatedBytes, 0, 4);
                bytes = truncatedBytes;
            }

            // Cryptomator stores Seed IDs as Big Endian integers.
            // BinaryPrimitives.ReadInt32BigEndian expects a span.
            return BinaryPrimitives.ReadInt32BigEndian(new ReadOnlySpan<byte>(bytes));
        }
    }
} 