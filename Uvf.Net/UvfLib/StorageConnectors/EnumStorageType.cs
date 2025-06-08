namespace FolderMagicLib.StorageConnectors
{
    /// <summary>
    /// Defines the type of storage backend to use.
    /// </summary>
    public enum EnumStorageType
    {
        /// <summary>
        /// Local filesystem storage
        /// </summary>
        Local = 0,

        /// <summary>
        /// In-memory storage (volatile)
        /// </summary>
        Memory = 1,

        /// <summary>
        /// Amazon Web Services S3 storage
        /// </summary>
        AWS = 2,

        /// <summary>
        /// Microsoft Azure Blob Storage
        /// </summary>
        Azure = 3,

        /// <summary>
        /// Google Drive cloud storage
        /// </summary>
        GoogleDrive = 4,

        /// <summary>
        /// Microsoft OneDrive cloud storage
        /// </summary>
        OneDrive = 5,

        /// <summary>
        /// FTP (File Transfer Protocol) and FTPS (FTP over SSL/TLS)
        /// </summary>
        FTP = 6,

        /// <summary>
        /// SSH/SFTP (SSH File Transfer Protocol)
        /// </summary>
        SSH = 7,

        /// <summary>
        /// OpenSlide virtual whole-slide imaging system
        /// </summary>
        OpenSlide = 8
    }
}
