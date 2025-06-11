using StorageLib.Abstractions;
using UvfLib.Core.Api;

namespace UvfLib.Storage.Abstractions
{
    /// <summary>
    /// Translates between virtual vault paths and encrypted storage paths.
    /// Handles both encrypted (complex directory structure) and unencrypted (simple .uvf appending) modes.
    /// </summary>
    public interface IVaultPathTranslator : IDisposable
    {
        /// <summary>
        /// Gets the vault format (UVF or CryptomatorV8)
        /// </summary>
        VaultFormat Format { get; }

        /// <summary>
        /// Gets whether filename/directory encryption is enabled
        /// </summary>
        bool IsEncryptionEnabled { get; }

        /// <summary>
        /// Gets the base storage path where files are stored
        /// </summary>
        string BaseStoragePath { get; }

        /// <summary>
        /// Translates a virtual path to the actual storage path where content is stored.
        /// 
        /// ENCRYPTED MODE:
        /// /folder/file.txt -> d/XX/YYYYYYYY/encrypted_file.c9r (Cryptomator)
        /// /folder/file.txt -> d/XX/YYYYYYYY/encrypted_file.uvf (UVF)
        /// 
        /// UNENCRYPTED MODE:
        /// /folder/file.txt -> folder/file.txt.uvf
        /// </summary>
        /// <param name="virtualPath">Virtual path relative to vault root</param>
        /// <returns>Storage path translation result</returns>
        Task<VaultPathResult> TranslateToStoragePathAsync(string virtualPath);

        /// <summary>
        /// Translates a storage path back to virtual path.
        /// Reverse of TranslateToStoragePathAsync.
        /// </summary>
        /// <param name="storagePath">Storage path</param>
        /// <returns>Virtual path or null if not found</returns>
        Task<string?> TranslateToVirtualPathAsync(string storagePath);

        // Storage operations removed - these belong in the storage decorator, not path translator

        /// <summary>
        /// Gets the encrypted file extension for this vault format
        /// </summary>
        string GetEncryptedFileExtension();

        /// <summary>
        /// Gets the metadata filename for this vault format
        /// </summary>
        string GetMetadataFileName();
    }

    /// <summary>
    /// Result of path translation
    /// </summary>
    public class VaultPathResult
    {
        /// <summary>
        /// Final storage path where content should be read/written
        /// </summary>
        public string StoragePath { get; set; } = "";

        /// <summary>
        /// Encrypted filename (only relevant in encrypted mode)
        /// </summary>
        public string EncryptedFilename { get; set; } = "";

        /// <summary>
        /// Directory where content is stored (may differ from parent directory in encrypted mode)
        /// </summary>
        public string ContentDirectoryPath { get; set; } = "";

        /// <summary>
        /// Whether directory structure needs to be created
        /// </summary>
        public bool RequiresDirectoryCreation { get; set; }

        /// <summary>
        /// Parent directory metadata (for encrypted mode)
        /// </summary>
        public DirectoryMetadata? ParentMetadata { get; set; }

        /// <summary>
        /// Whether this is in encrypted or unencrypted mode
        /// </summary>
        public bool IsEncrypted { get; set; }
    }

    /// <summary>
    /// Result of directory structure creation
    /// </summary>
    public class VaultDirectoryResult
    {
        /// <summary>
        /// Path where directory content should be stored
        /// </summary>
        public string ContentStoragePath { get; set; } = "";

        /// <summary>
        /// Reference directory path (for Cryptomator encrypted mode)
        /// </summary>
        public string ReferenceDirectoryPath { get; set; } = "";

        /// <summary>
        /// Directory metadata (for encrypted mode)
        /// </summary>
        public DirectoryMetadata? DirectoryMetadata { get; set; }

        /// <summary>
        /// Whether this was a new directory creation
        /// </summary>
        public bool IsNewDirectory { get; set; }

        /// <summary>
        /// Whether this directory is in encrypted mode
        /// </summary>
        public bool IsEncrypted { get; set; }
    }

    /// <summary>
    /// Vault format enumeration
    /// </summary>
    public enum VaultFormat
    {
        UVF,
        CryptomatorV8
    }
} 