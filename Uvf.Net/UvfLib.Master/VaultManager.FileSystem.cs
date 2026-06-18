using StorageLib.Abstractions;
using StorageLib.Streaming;
using UvfLib.Master.Decorators;
using UvfLib.Master.PathTranslators;
using UvfLib.Vault;
using System.Text;
using System.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Buffers.Binary;
using UvfLib.Core.Jwe;
using UvfLib.Core.Api;

namespace UvfLib.Master
{
    public partial class VaultManager
    {
        #region File Operations (Forward Slash Paths)

        /// <summary>
        /// Opens a file for reading. Path uses forward slashes (e.g., "/documents/file.txt")
        /// </summary>
        public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.OpenReadAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Opens a file for writing. Path uses forward slashes (e.g., "/documents/file.txt")
        /// </summary>
        public async Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.OpenWriteAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Opens a file for both reading and writing. Path uses forward slashes.
        /// </summary>
        public async Task<Stream> OpenReadWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.OpenReadWriteAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Opens a file with specific flags. Path uses forward slashes.
        /// </summary>
        public async Task<Stream> OpenAsync(string path, OpenFlags flags, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await ((IStreamStorage)_vaultStorage!).OpenAsync(NormalizePath(path), flags, cancellationToken);
        }

        /// <summary>
        /// Reads all bytes from a file. Path uses forward slashes.
        /// </summary>
        public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.ReadAllBytesAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Writes all bytes to a file. Path uses forward slashes.
        /// </summary>
        public async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.WriteAllBytesAsync(NormalizePath(path), data, cancellationToken);
        }

        /// <summary>
        /// Reads all text from a file. Path uses forward slashes.
        /// </summary>
        public async Task<string> ReadAllTextAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.ReadAllTextAsync(NormalizePath(path), encoding, cancellationToken);
        }

        /// <summary>
        /// Writes all text to a file. Path uses forward slashes.
        /// </summary>
        public async Task WriteAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.WriteAllTextAsync(NormalizePath(path), content, encoding, cancellationToken);
        }

        /// <summary>
        /// Reads all lines from a text file. Path uses forward slashes.
        /// </summary>
        public async Task<string[]> ReadAllLinesAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.ReadAllLinesAsync(NormalizePath(path), encoding, cancellationToken);
        }

        /// <summary>
        /// Writes all lines to a text file. Path uses forward slashes.
        /// </summary>
        public async Task WriteAllLinesAsync(string path, IEnumerable<string> lines, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.WriteAllLinesAsync(NormalizePath(path), lines, encoding, cancellationToken);
        }

        /// <summary>
        /// Appends text to a file. Path uses forward slashes.
        /// </summary>
        public async Task AppendAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.AppendAllTextAsync(NormalizePath(path), content, encoding, cancellationToken);
        }

        /// <summary>
        /// Copies data from a stream to a file. Path uses forward slashes.
        /// </summary>
        public async Task CopyFromStreamAsync(string destinationPath, Stream sourceStream, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.CopyFromStreamAsync(NormalizePath(destinationPath), sourceStream, cancellationToken);
        }

        /// <summary>
        /// Copies a file to a stream. Path uses forward slashes.
        /// </summary>
        public async Task CopyToStreamAsync(string sourcePath, Stream destinationStream, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.CopyToStreamAsync(NormalizePath(sourcePath), destinationStream, cancellationToken);
        }

        #endregion

        #region Directory Operations

        /// <summary>
        /// Lists directory contents. Path uses forward slashes (e.g., "/documents" or "/" for root)
        /// </summary>
        public async Task<IEnumerable<FileObject>> ListDirectoryAsync(string path = "/", CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.ReadDirAsync(NormalizePath(path), true, cancellationToken);
        }

        /// <summary>
        /// Creates a directory. Path uses forward slashes.
        /// </summary>
        public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.CreateDirectoryAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Deletes a file. Path uses forward slashes.
        /// </summary>
        public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.DeleteAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Deletes a directory. Path uses forward slashes.
        /// </summary>
        public async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.DeleteDirectoryAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Checks if a file exists. Path uses forward slashes.
        /// </summary>
        public async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.FileExistsAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Checks if a directory exists. Path uses forward slashes.
        /// </summary>
        public async Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.DirectoryExistsAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Gets file information. Path uses forward slashes.
        /// </summary>
        public async Task<FileObject> GetFileInfoAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            return await _vaultStorage!.GetFileInfoAsync(NormalizePath(path), cancellationToken);
        }

        /// <summary>
        /// Moves a file. Paths use forward slashes.
        /// </summary>
        public async Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default)
        {
            EnsureOpen();
            await _vaultStorage!.MoveAsync(NormalizePath(sourceFilePath), NormalizePath(destinationFilePath), overwrite, cancellationToken);
        }

        #endregion

        #region IStreamStorage Implementation (Delegate to vault storage)

        /// <summary>
        /// Gets the underlying IStorage instance that this stream storage wraps
        /// </summary>
        public IStorage UnderlyingStorage
        {
            get
            {
                EnsureOpen();
                if (_vaultStorage is CryptomatorStorageDecorator csd)
                {
                    return csd.UnderlyingStorage;
                }
                if (_vaultStorage is UvfStorageDecorator usd)
                {
                    return usd.UnderlyingStorage;
                }
                throw new InvalidOperationException("The underlying storage cannot be accessed for the current vault type.");
            }
        }

        /// <summary>
        /// Gets the encrypting <see cref="IStorage"/> decorator for this vault — the handle-based storage
        /// that transparently encrypts content + filenames over the underlying connector. Callers that
        /// drive the handle-based IStorage API directly (e.g. a virtual filesystem) wrap this, while the
        /// physical backend (via <see cref="UnderlyingStorage"/>) only ever sees ciphertext.
        /// </summary>
        public IStorage EncryptingStorage
        {
            get
            {
                EnsureOpen();
                if (_vaultStorage is IStorage storage)
                {
                    return storage;
                }
                throw new InvalidOperationException("The encrypting storage is not available for the current vault type.");
            }
        }

        // Delegate remaining IStreamStorage methods to _vaultStorage
        Task<IEnumerable<FileObject>> IStreamStorage.ReadDirAsync(string directoryPath, bool readOnly, CancellationToken cancellationToken)
            => ListDirectoryAsync(directoryPath, cancellationToken);

        Task IStreamStorage.DeleteAsync(string filePath, CancellationToken cancellationToken)
            => DeleteFileAsync(filePath, cancellationToken);

        Task IStreamStorage.ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken)
        {
            EnsureOpen();
            return _vaultStorage!.ChownAsync(NormalizePath(path), uid, gid, cancellationToken);
        }

        Task IStreamStorage.SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken)
        {
            EnsureOpen();
            return _vaultStorage!.SetTimesAsync(NormalizePath(path), accessTime, modifiedTime, cancellationToken);
        }

        Task<bool> IStreamStorage.TestReadAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return ((IStreamStorage)_vaultStorage!).TestReadAsync(cancellationToken);
        }

        Task<bool> IStreamStorage.TestWriteAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return ((IStreamStorage)_vaultStorage!).TestWriteAsync(cancellationToken);
        }

        Task<long> IStreamStorage.GetTotalCapacityAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return ((IStreamStorage)_vaultStorage!).GetTotalCapacityAsync(cancellationToken);
        }

        Task<long> IStreamStorage.GetAvailableFreeSpaceAsync(CancellationToken cancellationToken)
        {
            EnsureOpen();
            return ((IStreamStorage)_vaultStorage!).GetAvailableFreeSpaceAsync(cancellationToken);
        }

        #endregion
    }
}
