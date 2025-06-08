using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FolderMagicLib.StorageConnectors
{
    public class FileHandle : IDisposable
    {
        public string Path { get; }
        public Stream Stream { get; }
        public bool IsReadOnly { get; }
        public IStorage Storage { get; }
        private GCHandle _gcHandle;
        private bool _disposed = false;
        public string? RealPath { get; set; }
        public string? VirtualPath { get; set; }

        public FileHandle(string path, Stream stream, bool isReadOnly, IStorage storage)
        {
            Path = path;
            Stream = stream;
            IsReadOnly = isReadOnly;
            Storage = storage;
        }

        public IntPtr CreateContext()
        {
            // Pin this object so it won't be moved by the garbage collector
            _gcHandle = GCHandle.Alloc(this);
            return GCHandle.ToIntPtr(_gcHandle);
        }

        public static FileHandle FromContext(IntPtr context)
        {
            if (context == IntPtr.Zero)
                return null;

            GCHandle handle = GCHandle.FromIntPtr(context);
            return handle.Target as FileHandle;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    Stream?.Dispose();
                }

                // Free unmanaged resources
                if (_gcHandle.IsAllocated)
                    _gcHandle.Free();

                _disposed = true;
            }
        }

        ~FileHandle()
        {
            Dispose(false);
        }
    }
}