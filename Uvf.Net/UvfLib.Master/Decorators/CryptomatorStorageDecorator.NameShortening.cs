using StorageLib.Abstractions;
using UvfLib.Master.PathTranslators;
using UvfLib.Master.Common;
using UvfLib.Vault;
using Microsoft.Extensions.Logging;
using System.IO;
using UvfLib.Core.CryptomatorV8;
using System.Text;
using UvfLib.Master.Abstractions;

namespace UvfLib.Master.Decorators
{
    public partial class CryptomatorStorageDecorator
    {
        #region Name Shortening Support

        /// <summary>
        /// Reads the original encrypted filename from a shortened directory's name.c9s file.
        /// </summary>
        /// <param name="contentDirectoryPath">The content directory path where the shortened directory is located</param>
        /// <param name="shortenedDirectoryName">The shortened directory name (e.g., "ABC123.c9s")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The original long encrypted filename</returns>
        private async Task<string> ReadOriginalFilenameFromShortenedDirectoryAsync(
            string contentDirectoryPath, 
            string shortenedDirectoryName, 
            CancellationToken cancellationToken)
        {
            string shortenedDirectoryPath = Path.Combine(contentDirectoryPath, shortenedDirectoryName);
            string nameFilePath = Path.Combine(shortenedDirectoryPath, "name.c9s");

            if (!await _underlyingStorage.FileExistsAsync(nameFilePath, cancellationToken))
            {
                throw new FileNotFoundException($"Name file not found in shortened directory: {nameFilePath}");
            }

            // Read the original filename from the name.c9s file as PLAINTEXT
            // According to Cryptomator spec, name.c9s contains the original encrypted filename as plaintext
            IntPtr fileHandle = await _underlyingStorage.OpenAsync(nameFilePath, OpenFlags.ReadOnly, cancellationToken);
            try
            {
                var fileInfo = await _underlyingStorage.GetFileInfoAsync(nameFilePath, cancellationToken);
                long fileSize = fileInfo.Size;

                if (fileSize <= 0)
                {
                    throw new InvalidDataException($"Name file is empty: {nameFilePath}");
                }

                // Read file content as plaintext (no decryption needed)
                IntPtr dataPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)fileSize);
                try
                {
                    await _underlyingStorage.ReadAsync(nameFilePath, fileHandle, 0, fileSize, dataPtr, cancellationToken);
                    
                    // Copy to managed array
                    byte[] fileBytes = new byte[fileSize];
                    System.Runtime.InteropServices.Marshal.Copy(dataPtr, fileBytes, 0, (int)fileSize);
                    
                    // Convert to string and trim
                    string originalFilename = Encoding.UTF8.GetString(fileBytes).Trim();
                    
                    if (string.IsNullOrEmpty(originalFilename))
                    {
                        throw new InvalidDataException($"Name file contains empty filename: {nameFilePath}");
                    }

                    return originalFilename;
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(dataPtr);
                }
            }
            finally
            {
                await _underlyingStorage.CloseAsync(nameFilePath, fileHandle, cancellationToken);
            }
        }

        /// <summary>
        /// Decrypts a shortened filename using the original encrypted filename.
        /// </summary>
        /// <param name="shortenedDirectoryName">The shortened directory name</param>
        /// <param name="originalEncryptedFilename">The original long encrypted filename</param>
        /// <param name="directoryMetadata">The directory metadata for decryption context</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The decrypted filename</returns>
        private async Task<string> DecryptShortenedFilenameAsync(
            string shortenedDirectoryName,
            string originalEncryptedFilename,
            UvfLib.Core.Api.DirectoryMetadata directoryMetadata,
            CancellationToken cancellationToken)
        {
            // Note: We don't validate the shortened name against the original filename
            // because Cryptomator's shortening algorithm uses a hash/truncation method
            // that doesn't allow for simple reverse validation

            // The originalEncryptedFilename should include the .c9r extension
            if (!originalEncryptedFilename.EndsWith(".c9r"))
            {
                throw new InvalidDataException(
                    $"Original encrypted filename should end with .c9r extension: '{originalEncryptedFilename}'");
            }

            // Decrypt the original filename - the VaultHandler should properly handle the .c9r extension
            return _vault.DecryptFilename(originalEncryptedFilename, directoryMetadata);
        }

        /// <summary>
        /// Writes the original encrypted filename to a shortened directory's name.c9s file.
        /// </summary>
        /// <param name="contentDirectoryPath">The content directory path</param>
        /// <param name="shortenedDirectoryName">The shortened directory name</param>
        /// <param name="originalEncryptedFilename">The original long encrypted filename to store</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task WriteOriginalFilenameToShortenedDirectoryAsync(
            string contentDirectoryPath,
            string shortenedDirectoryName,
            string originalEncryptedFilename,
            CancellationToken cancellationToken)
        {
            string shortenedDirectoryPath = Path.Combine(contentDirectoryPath, shortenedDirectoryName);
            
            // Ensure the shortened directory exists
            if (!await _underlyingStorage.DirectoryExistsAsync(shortenedDirectoryPath, cancellationToken))
            {
                await _underlyingStorage.CreateDirectoryAsync(shortenedDirectoryPath, cancellationToken);
            }

            string nameFilePath = NameShorteningHelper.GetInflatedNameFilePath(shortenedDirectoryName);
            string fullNameFilePath = Path.Combine(contentDirectoryPath, nameFilePath);

            // Write the original filename to the name.c9s file
            byte[] filenameBytes = Encoding.UTF8.GetBytes(originalEncryptedFilename);
            
            IntPtr fileHandle = await _underlyingStorage.OpenAsync(fullNameFilePath, OpenFlags.Create | OpenFlags.WriteOnly, cancellationToken);
            try
            {
                IntPtr dataPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(filenameBytes.Length);
                try
                {
                    System.Runtime.InteropServices.Marshal.Copy(filenameBytes, 0, dataPtr, filenameBytes.Length);
                    await _underlyingStorage.WriteAsync(nameFilePath, fileHandle, 0, filenameBytes.Length, dataPtr, cancellationToken);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(dataPtr);
                }
            }
            finally
            {
                await _underlyingStorage.CloseAsync(fullNameFilePath, fileHandle, cancellationToken);
            }
        }

        /// <summary>
        /// Extracts the directory ID from directory metadata for use with DirectoryContentCryptor methods.
        /// </summary>
        /// <param name="directoryMetadata">The directory metadata</param>
        /// <returns>The Base64Url encoded directory ID</returns>
        private string GetDirectoryIdFromMetadata(UvfLib.Core.Api.DirectoryMetadata directoryMetadata)
        {
            // For Cryptomator V8, we need to extract the directory ID
            // This is implementation-specific and may need adjustment based on the actual metadata structure
            if (directoryMetadata is UvfLib.Core.CryptomatorV8.DirectoryMetadataImpl cryptomatorMetadata)
            {
                return Convert.ToBase64String(cryptomatorMetadata.DirIdBytes())
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .TrimEnd('=');
            }
            
            throw new NotSupportedException("Directory metadata type not supported for name shortening");
        }

        #endregion
    }
}
