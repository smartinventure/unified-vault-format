using StorageLib.Abstractions;
using UvfLib.Vault;
using System.Runtime.InteropServices;

namespace UvfLib.Storage.Decorators
{
    /// <summary>
    /// Wrapper for encrypted file handles that combines virtual/physical paths with crypto streams.
    /// Supports lazy creation of both encrypting and decrypting streams for ReadWrite access.
    /// </summary>
    public class CryptoHandle : IDisposable
    {
        public string VirtualPath { get; }
        public string PhysicalPath { get; }
        public IntPtr UnderlyingHandle { get; }
        public OpenFlags Flags { get; }
        
        private readonly VaultHandler _vault;
        private readonly CryptorStorageDecoratorBase _parent;
        private Stream? _encryptingStream;
        private Stream? _decryptingStream;
        
        private GCHandle _gcHandle;
        private bool _disposed = false;

        public CryptoHandle(string virtualPath, string physicalPath, IntPtr underlyingHandle, VaultHandler vault, OpenFlags flags, CryptorStorageDecoratorBase parent)
        {
            VirtualPath = virtualPath;
            PhysicalPath = physicalPath;
            UnderlyingHandle = underlyingHandle;
            _vault = vault;
            Flags = flags;
            _parent = parent;
        }

        public IntPtr CreateContext()
        {
            _gcHandle = GCHandle.Alloc(this);
            return GCHandle.ToIntPtr(_gcHandle);
        }

        public async Task<Stream> GetDecryptingStreamAsync(CancellationToken cancellationToken)
        {
            if (_decryptingStream == null)
            {
                // Lazy creation of decrypting stream
                _decryptingStream = await _parent.CreateCryptoStreamAsync(UnderlyingHandle, FileAccess.Read, VirtualPath, PhysicalPath, cancellationToken);
            }
            return _decryptingStream;
        }

        public async Task<Stream> GetEncryptingStreamAsync(CancellationToken cancellationToken)
        {
            if (_encryptingStream == null)
            {
                // Lazy creation of encrypting stream
                _encryptingStream = await _parent.CreateCryptoStreamAsync(UnderlyingHandle, FileAccess.Write, VirtualPath, PhysicalPath, cancellationToken);
            }
            return _encryptingStream;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Dispose both crypto streams if they exist
                _encryptingStream?.Dispose();
                _decryptingStream?.Dispose();
                
                if (_gcHandle.IsAllocated)
                    _gcHandle.Free();
                    
                _disposed = true;
            }
        }
    }
} 