using System.Runtime.InteropServices;
using System.Text;
using StorageLib.Abstractions;
using UvfLib.Master;

namespace UvfLib.Demo.DotNet;

/// <summary>
/// .NET demo for the managed UvfLib package (no native AOT DLL involved). It creates a vault, encrypts
/// files into it, reads them back as cleartext, lists the directory, shows that the backing folder holds
/// only ciphertext, and deletes a file — for both UVF (v3) and Cryptomator (v8) formats.
///
/// Run:   dotnet run -- uvf            (or)   dotnet run -- cryptomator   (or)   dotnet run -- both
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string which = (args.FirstOrDefault() ?? "both").ToLowerInvariant();
        const string password = "correct horse battery staple";

        try
        {
            if (which is "uvf" or "both") await RunAsync(VaultKind.Uvf, password);
            if (which is "cryptomator" or "both") await RunAsync(VaultKind.Cryptomator, password);
            Console.WriteLine("\n✅ Demo completed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n❌ Demo failed: {ex.Message}\n{ex}");
            return 1;
        }
    }

    private enum VaultKind { Uvf, Cryptomator }

    private static async Task RunAsync(VaultKind kind, string password)
    {
        // A fresh vault location per run (delete to start clean).
        string vaultDir = Path.Combine(Path.GetTempPath(), $"uvf-dotnet-demo-{kind}".ToLowerInvariant());
        if (Directory.Exists(vaultDir)) Directory.Delete(vaultDir, recursive: true);
        Directory.CreateDirectory(vaultDir);

        Console.WriteLine($"\n===== {kind} vault @ {vaultDir} =====");

        // 1. Create the vault. The path-based factories create the backing LocalStorage for us.
        using VaultManager vault = kind == VaultKind.Uvf
            ? await VaultManager.CreateUvfVaultAsync(vaultDir, password.ToCharArray(), encryptFilenames: true)
            : await VaultManager.CreateCryptomatorVaultAsync(vaultDir, password.ToCharArray());

        // EncryptingStorage is the handle-based IStorage that transparently encrypts content + names.
        IStorage fs = vault.EncryptingStorage;

        // 2. Encrypt: write files through the vault (cleartext in, ciphertext on disk).
        await fs.CreateDirectoryAsync("/notes");
        await WriteTextAsync(fs, "/hello.txt", "Hello, encrypted world!");
        await WriteTextAsync(fs, "/notes/todo.txt", "1. buy milk\n2. rotate keys");
        Console.WriteLine("Wrote /hello.txt and /notes/todo.txt");

        // 3. Decrypt: read them back as cleartext.
        Console.WriteLine($"  /hello.txt      -> \"{await ReadTextAsync(fs, "/hello.txt")}\"");
        Console.WriteLine($"  /notes/todo.txt -> \"{(await ReadTextAsync(fs, "/notes/todo.txt")).Replace("\n", " / ")}\"");

        // 4. List a directory (decrypted names).
        var listing = await fs.ReadDirAsync("/");
        Console.WriteLine("  / contains: " + string.Join(", ", listing.Select(e => Path.GetFileName(e.Filename))));

        // 5. Show the backend is ciphertext: the cleartext name never appears on disk.
        bool leaked = Directory.GetFiles(vaultDir, "*", SearchOption.AllDirectories)
            .Any(f => Path.GetFileName(f) == "hello.txt");
        Console.WriteLine($"  backend stores plaintext name 'hello.txt'? {leaked}  (expected: False)");

        // 6. Delete a file.
        await fs.DeleteAsync("/hello.txt");
        Console.WriteLine($"  after delete, /hello.txt exists? {await fs.FileExistsAsync("/hello.txt")}  (expected: False)");
    }

    // ---- small helpers over the handle-based IStorage API ----

    private static async Task WriteTextAsync(IStorage fs, string path, string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        IntPtr handle = await fs.OpenAsync(path, OpenFlags.Create | OpenFlags.ReadWrite);
        IntPtr buffer = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, buffer, data.Length);
            int written = 0;
            while (written < data.Length)
            {
                int n = await fs.WriteAsync(path, handle, written, data.Length - written, IntPtr.Add(buffer, written));
                if (n <= 0) throw new IOException($"short write for {path}");
                written += n;
            }
            await fs.FlushAsync(path, handle);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            await fs.CloseAsync(path, handle);
        }
    }

    private static async Task<string> ReadTextAsync(IStorage fs, string path)
    {
        const int max = 1 << 20;
        IntPtr handle = await fs.OpenAsync(path, OpenFlags.ReadOnly);
        IntPtr buffer = Marshal.AllocHGlobal(max);
        try
        {
            int read = await fs.ReadAsync(path, handle, 0, max, buffer);
            if (read <= 0) return string.Empty;
            byte[] outBytes = new byte[read];
            Marshal.Copy(buffer, outBytes, 0, read);
            return Encoding.UTF8.GetString(outBytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            await fs.CloseAsync(path, handle);
        }
    }
}
