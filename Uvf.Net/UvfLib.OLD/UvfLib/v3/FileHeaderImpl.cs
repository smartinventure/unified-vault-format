using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using UvfLib.Api;
using UvfLib.Common;

namespace UvfLib.V3;

/// <summary>
/// Implementation of the FileHeader interface for v3 format.
/// </summary>
public sealed class FileHeaderImpl : FileHeader
{
    // Header structure constants
    internal const int UVF_MAGIC_BYTES_LENGTH = 4; // 'u', 'v', 'f', 0x00
    internal const int UVF_GENERAL_HEADERS_LEN = UVF_MAGIC_BYTES_LENGTH + sizeof(int); // Magic bytes + seed ID
    public const int NONCE_LEN = 12; // 96 bits
    internal const int CONTENT_KEY_LEN = 32; // 256 bits
    internal const int TAG_LEN = 16; // 128 bits
    internal const int NONCE_POS = UVF_GENERAL_HEADERS_LEN;
    internal const int CONTENT_KEY_POS = NONCE_POS + NONCE_LEN;
    internal const int SIZE = CONTENT_KEY_POS + CONTENT_KEY_LEN + TAG_LEN;

    private readonly int _seedId;
    private readonly byte[] _nonce;
    private readonly DestroyableSecretKey _contentKey;
    private bool _destroyed;

    /// <summary>
    /// Creates a new file header.
    /// </summary>
    /// <param name="seedId">The seed ID</param>
    /// <param name="nonce">The nonce</param>
    /// <param name="contentKey">The content key</param>
    public FileHeaderImpl(int seedId, byte[] nonce, DestroyableSecretKey contentKey)
    {
        if (nonce == null || nonce.Length != NONCE_LEN)
        {
            throw new ArgumentException($"Nonce must be {NONCE_LEN} bytes long", nameof(nonce));
        }
        if (contentKey == null)
        {
            throw new ArgumentNullException(nameof(contentKey));
        }

        _seedId = seedId;
        _nonce = new byte[NONCE_LEN];
        Buffer.BlockCopy(nonce, 0, _nonce, 0, NONCE_LEN);
        _contentKey = contentKey;
        _destroyed = false;
    }

    /// <summary>
    /// Gets the seed ID.
    /// </summary>
    public int GetSeedId()
    {
        ThrowIfDestroyed();
        return _seedId;
    }

    /// <summary>
    /// Gets a copy of the nonce.
    /// </summary>
    /// <returns>A copy of the nonce</returns>
    public byte[] GetNonce()
    {
        ThrowIfDestroyed();
        byte[] nonceCopy = new byte[NONCE_LEN];
        Buffer.BlockCopy(_nonce, 0, nonceCopy, 0, NONCE_LEN);
        return nonceCopy;
    }

    /// <summary>
    /// Gets the content key.
    /// </summary>
    /// <returns>The content key</returns>
    internal DestroyableSecretKey GetContentKey()
    {
        ThrowIfDestroyed();
        return _contentKey;
    }

    /// <summary>
    /// Destroys this file header, securely erasing all key material.
    /// </summary>
    public void Destroy()
    {
        if (!_destroyed)
        {
            UvfLib.Common.CryptographicOperations.ZeroMemory(_nonce);
            _contentKey?.Destroy();
            _destroyed = true;
        }
    }

    /// <summary>
    /// Checks if this file header has been destroyed.
    /// </summary>
    /// <returns>True if destroyed, false otherwise</returns>
    public bool IsDestroyed()
    {
        return _destroyed;
    }

    /// <summary>
    /// Casts a FileHeader to a FileHeaderImpl.
    /// </summary>
    /// <param name="header">The file header to cast</param>
    /// <returns>The cast file header</returns>
    /// <exception cref="ArgumentNullException">If the header is null</exception>
    /// <exception cref="InvalidCastException">If the header is not a FileHeaderImpl</exception>
    internal static FileHeaderImpl Cast(FileHeader header)
    {
        if (header == null)
        {
            throw new ArgumentNullException(nameof(header));
        }

        if (header is FileHeaderImpl headerImpl)
        {
            return headerImpl;
        }

        throw new InvalidCastException($"Cannot cast {header.GetType().Name} to FileHeaderImpl");
    }

    /// <summary>
    /// Disposes this file header, calling Destroy.
    /// </summary>
    public void Dispose()
    {
        Destroy();
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDestroyed()
    {
        if (_destroyed)
        {
            throw new ObjectDisposedException(nameof(FileHeaderImpl));
        }
    }
}