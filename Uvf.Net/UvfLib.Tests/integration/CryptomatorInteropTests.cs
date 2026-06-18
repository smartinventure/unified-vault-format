using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Abstractions;
using UvfLib.Master;

namespace UvfLib.Tests.integration
{
    /// <summary>
    /// Interop against a vault created by the real Cryptomator app (Demo/_test-cryptomator-vault).
    /// Unlocks it, lists the tree, and byte-compares the decrypted files with the originals.
    /// </summary>
    [TestClass]
    public class CryptomatorInteropTests
    {
        [TestMethod]
        public async Task RealCryptomatorVault_UnlocksAndReadsFiles()
        {
            string? vaultDir = FindTestVaultDir();
            if (vaultDir == null) { Assert.Inconclusive("Real Cryptomator test vault not found (Demo/_test-cryptomator-vault)."); return; }
            string baseDir = Directory.GetParent(vaultDir)!.FullName;
            string origDir = Path.Combine(baseDir, "original-files");

            using VaultManager vault = await VaultManager.LoadCryptomatorVaultAsync(vaultDir, "smartinventure".ToCharArray());
            IStorage fs = vault.EncryptingStorage;

            // "See" the files: list the tree (decrypted names).
            var root = (await fs.ReadDirAsync("/")).Select(e => Path.GetFileName(e.Filename)).ToList();
            CollectionAssert.Contains(root, "Perfect-albums.txt", $"root listing: {string.Join(",", root)}");
            CollectionAssert.Contains(root, "mysubfolder1", $"root listing: {string.Join(",", root)}");

            // Byte-compare decrypted content with the originals.
            var cases = new (string vaultPath, string orig)[]
            {
                ("/Perfect-albums.txt", "Perfect-albums.txt"),
                ("/mysubfolder1/banana.jpg", "banana.jpg"),
                ("/mysubfolder1/mysubfolder2/Rubicon - Rivers - lyrics.txt", "Rubicon - Rivers - lyrics.txt"),
            };
            foreach (var (vaultPath, orig) in cases)
            {
                byte[] got = await ReadAllAsync(fs, vaultPath);
                byte[] want = await File.ReadAllBytesAsync(Path.Combine(origDir, orig));
                CollectionAssert.AreEqual(want, got, $"Decrypted {vaultPath} must match original {orig} ({want.Length} vs {got.Length} bytes)");
            }
        }

        private static string? FindTestVaultDir()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Demo", "_test-cryptomator-vault", "smartinventure");
                if (File.Exists(Path.Combine(candidate, "masterkey.cryptomator"))) return candidate;
            }
            return null;
        }

        private static async Task<byte[]> ReadAllAsync(IStorage fs, string path)
        {
            const int max = 16 << 20; // 16 MiB — plenty for the demo files
            IntPtr handle = await fs.OpenAsync(path, OpenFlags.ReadOnly);
            IntPtr buffer = Marshal.AllocHGlobal(max);
            try
            {
                int read = await fs.ReadAsync(path, handle, 0, max, buffer);
                byte[] outBytes = new byte[Math.Max(read, 0)];
                if (read > 0) Marshal.Copy(buffer, outBytes, 0, read);
                return outBytes;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                await fs.CloseAsync(path, handle);
            }
        }
    }
}
