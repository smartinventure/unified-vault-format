using System.Security.Cryptography;
using System.Text;
using UvfLib.Core.Common;

namespace UvfLib.Core.CryptomatorV8
{
    /// <summary>
    /// Helper class for Cryptomator filename shortening functionality.
    /// Implements the name shortening algorithm as described in Cryptomator documentation.
    /// </summary>
    public static class NameShorteningHelper
    {
        /// <summary>
        /// Checks if a filename needs shortening based on the threshold.
        /// </summary>
        /// <param name="filename">The filename to check</param>
        /// <param name="threshold">The length threshold (default: 220)</param>
        /// <returns>True if the filename exceeds the threshold</returns>
        public static bool NeedsShortening(string filename, int threshold = Constants.SHORTENING_THRESHOLD)
        {
            return filename.Length > threshold;
        }

        /// <summary>
        /// Creates a shortened directory name from a long encrypted filename.
        /// Uses SHA-1 hash of the long filename, encoded as Base64URL.
        /// </summary>
        /// <param name="longEncryptedFilename">The long encrypted filename that needs shortening</param>
        /// <returns>The shortened directory name (hash + .c9s extension)</returns>
        public static string CreateShortenedDirectoryName(string longEncryptedFilename)
        {
            if (string.IsNullOrEmpty(longEncryptedFilename))
                throw new ArgumentException("Filename cannot be null or empty", nameof(longEncryptedFilename));

            // Create SHA-1 hash of the long encrypted filename
            byte[] filenameBytes = Encoding.UTF8.GetBytes(longEncryptedFilename);
            byte[] hashBytes = SHA1.HashData(filenameBytes);
            
            // Encode as Base64URL (no padding)
            string hashBase64Url = Base64Url.Encode(hashBytes);
            
            // Return with .c9s extension
            return hashBase64Url + Constants.C9S_DIR_EXT;
        }

        /// <summary>
        /// Checks if a filename represents a shortened directory.
        /// </summary>
        /// <param name="filename">The filename to check</param>
        /// <returns>True if the filename ends with .c9s extension</returns>
        public static bool IsShortenedDirectory(string filename)
        {
            return !string.IsNullOrEmpty(filename) && filename.EndsWith(Constants.C9S_DIR_EXT);
        }

        /// <summary>
        /// Extracts the hash part from a shortened directory name.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name (e.g., "ABC123.c9s")</param>
        /// <returns>The hash part without the .c9s extension</returns>
        public static string ExtractHashFromShortenedName(string shortenedDirectoryName)
        {
            if (!IsShortenedDirectory(shortenedDirectoryName))
                throw new ArgumentException("Not a shortened directory name", nameof(shortenedDirectoryName));

            return shortenedDirectoryName.Substring(0, shortenedDirectoryName.Length - Constants.C9S_DIR_EXT.Length);
        }

        /// <summary>
        /// Creates the full path for the inflated name file within a shortened directory.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name</param>
        /// <returns>The path to the name.c9s file</returns>
        public static string GetInflatedNameFilePath(string shortenedDirectoryName)
        {
            return shortenedDirectoryName + "/" + Constants.INFLATED_NAME_FILE;
        }

        /// <summary>
        /// Creates the full path for the contents file within a shortened directory.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name</param>
        /// <returns>The path to the contents.c9r file</returns>
        public static string GetContentsFilePath(string shortenedDirectoryName)
        {
            return shortenedDirectoryName + "/" + Constants.SHORTENED_CONTENTS_FILE;
        }

        /// <summary>
        /// Creates the full path for the directory file within a shortened directory.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name</param>
        /// <returns>The path to the dir.c9r file</returns>
        public static string GetDirectoryFilePath(string shortenedDirectoryName)
        {
            return shortenedDirectoryName + "/" + Constants.SHORTENED_DIR_FILE;
        }

        /// <summary>
        /// Creates the full path for the symlink file within a shortened directory.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name</param>
        /// <returns>The path to the symlink.c9r file</returns>
        public static string GetSymlinkFilePath(string shortenedDirectoryName)
        {
            return shortenedDirectoryName + "/" + Constants.SHORTENED_SYMLINK_FILE;
        }

        /// <summary>
        /// Validates that a shortened directory name was created from the given long filename.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name to validate</param>
        /// <param name="originalLongFilename">The original long filename</param>
        /// <returns>True if the shortened name matches the original filename</returns>
        public static bool ValidateShortenedName(string shortenedDirectoryName, string originalLongFilename)
        {
            if (!IsShortenedDirectory(shortenedDirectoryName))
                return false;

            string expectedShortenedName = CreateShortenedDirectoryName(originalLongFilename);
            return string.Equals(shortenedDirectoryName, expectedShortenedName, StringComparison.Ordinal);
        }
    }
} 