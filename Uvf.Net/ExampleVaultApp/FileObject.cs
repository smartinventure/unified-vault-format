namespace StorageLib.Abstractions
{
    /*
    /// <summary>
    /// Represents a file or directory object with metadata and storage connection.
    /// </summary>
    public class FileObject
    {
        private byte[] _bytes = Array.Empty<byte>();

        public byte[] Bytes
        {
            get => _bytes;
            set => _bytes = value;
        }
        
        public string VirtualPath { get; set; } = string.Empty;
        public string RealPath { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public IStorage? SC { get; set; }
        public long Size { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime LastAccessTime { get; set; }
        public int Mode { get; set; }
        public int Uid { get; set; }
        public int Gid { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsDirectory { get; set; }
        
        public FileObject(string virtualPath, IStorage? sc = null)
        {
            VirtualPath = virtualPath ?? string.Empty;
            SC = sc;
        }
    }
    */
} 