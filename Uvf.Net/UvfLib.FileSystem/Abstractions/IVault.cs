namespace UvfLib.FileSystem.Abstractions
{
    // UvfLib.FileSystem/Abstractions/IVault.cs
    public interface IVault : IDisposable
    {
        VaultInfo GetInfo();
        Task<Stream> OpenReadAsync(string path);
        Task<VaultEntry[]> ListDirectoryAsync(string path);
    }
}
