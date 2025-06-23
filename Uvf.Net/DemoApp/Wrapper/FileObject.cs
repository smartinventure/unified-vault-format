namespace DemoApp.Wrapper
{
    /// <summary>
    /// Simple file information object for TitanVault operations
    /// Compatible with StorageLib.Abstractions.FileObject but standalone for AOT usage
    /// </summary>
    public class FileObject
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public string RealPath { get; set; } = string.Empty;
        public string VirtualPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime LastAccessTime { get; set; }
        public bool IsDirectory { get; set; }
        
        public FileObject(string fullPath)
        {
            FullPath = fullPath;
            RealPath = fullPath;
            VirtualPath = fullPath;
            Name = Path.GetFileName(fullPath);
            Filename = Name;
        }
    }
} 