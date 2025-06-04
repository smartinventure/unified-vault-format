namespace UvfLib.Api
{
    /// <summary>
    /// Factory for creating cryptographic components.
    /// </summary>
    public interface CryptoFactory : IDisposable
    {
        /// <summary>
        /// Creates a cryptor instance.
        /// </summary>
        /// <returns>A cryptor instance</returns>
        Cryptor Create();

        /// <summary>
        /// Creates a file name cryptor.
        /// </summary>
        /// <returns>A file name cryptor</returns>
        FileNameCryptor CreateFileNameCryptor();

        /// <summary>
        /// Creates a file content cryptor.
        /// </summary>
        /// <returns>A file content cryptor</returns>
        IFileContentCryptor CreateFileContentCryptor();
    }
}