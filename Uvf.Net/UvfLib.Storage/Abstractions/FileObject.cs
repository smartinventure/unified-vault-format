using FolderMagicLib.StorageConnectors;
using System.IO;

namespace FolderMagicLib.Application
{
    public class FileObject
    {
        private byte[] _bytes = Array.Empty<byte>();

        public byte[] Bytes
        {
            get => _bytes;
            set => _bytes = value;
        }
        public string VirtualPath { get; set; }
        public string RealPath { get; set; }
        public string Filename { get; set; }
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
            VirtualPath = virtualPath;
            SC = sc;
        }
    }
}
