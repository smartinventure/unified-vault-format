using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Abstractions;
using UvfLib.Master;

namespace UvfLib.Tests.integration
{
    /// <summary>
    /// Two-levels-deep directory create/write/read for both formats. Guards the fix where a child
    /// directory's reference entry must be placed in its parent's CONTENT directory (and an existing
    /// parent navigated, not recreated) — previously only one level deep was exercised.
    /// </summary>
    [TestClass]
    public class NestedDirectoryTests
    {
        [TestMethod]
        public async Task Cryptomator_NestedDirectories_WriteReadRoundTrips() => await RunAsync(cryptomator: true);

        [TestMethod]
        public async Task Uvf_NestedDirectories_WriteReadRoundTrips() => await RunAsync(cryptomator: false);

        private static async Task RunAsync(bool cryptomator)
        {
            string vaultDir = Path.Combine(Path.GetTempPath(), $"uvf-nested-{(cryptomator ? "c" : "u")}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(vaultDir);
            char[] pw = "nested-pass-123".ToCharArray();
            const string deep = "deep two-level content — the quick brown fox";
            const string shallow = "one level content";
            try
            {
                using VaultManager vm = cryptomator
                    ? await VaultManager.CreateCryptomatorVaultAsync(vaultDir, pw)
                    : await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, pw, encryptFilenames: true, kdfParams: null);
                IStorage fs = vm.EncryptingStorage;

                await fs.CreateDirectoryAsync("/a");
                await fs.CreateDirectoryAsync("/a/b");
                await WriteAllAsync(fs, "/a/shallow.txt", shallow);
                await WriteAllAsync(fs, "/a/b/deep.txt", deep);

                Assert.IsTrue(await fs.DirectoryExistsAsync("/a/b"), "/a/b should exist");
                Assert.AreEqual(shallow, await ReadAllAsync(fs, "/a/shallow.txt", shallow.Length * 2));
                Assert.AreEqual(deep, await ReadAllAsync(fs, "/a/b/deep.txt", deep.Length * 2), "two-level-deep file must round-trip");
            }
            finally { try { Directory.Delete(vaultDir, recursive: true); } catch { } }
        }

        private static async Task WriteAllAsync(IStorage storage, string path, string content)
        {
            byte[] data = Encoding.UTF8.GetBytes(content);
            IntPtr handle = await storage.OpenAsync(path, OpenFlags.Create | OpenFlags.ReadWrite);
            IntPtr buffer = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buffer, data.Length);
                int written = 0;
                while (written < data.Length)
                {
                    int n = await storage.WriteAsync(path, handle, written, data.Length - written, IntPtr.Add(buffer, written));
                    if (n <= 0) throw new IOException($"short write for {path}");
                    written += n;
                }
                await storage.FlushAsync(path, handle);
            }
            finally { Marshal.FreeHGlobal(buffer); await storage.CloseAsync(path, handle); }
        }

        private static async Task<string> ReadAllAsync(IStorage storage, string path, int maxBytes)
        {
            IntPtr handle = await storage.OpenAsync(path, OpenFlags.ReadOnly);
            IntPtr buffer = Marshal.AllocHGlobal(maxBytes);
            try
            {
                int read = await storage.ReadAsync(path, handle, 0, maxBytes, buffer);
                if (read <= 0) return string.Empty;
                byte[] outBytes = new byte[read];
                Marshal.Copy(buffer, outBytes, 0, read);
                return Encoding.UTF8.GetString(outBytes);
            }
            finally { Marshal.FreeHGlobal(buffer); await storage.CloseAsync(path, handle); }
        }
    }
}
