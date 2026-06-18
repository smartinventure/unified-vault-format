using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Abstractions;
using UvfLib.Master;

namespace UvfLib.Tests.integration
{
    /// <summary>
    /// Multi-user + key-rotation behaviour for UVF vaults (the path-based <see cref="VaultManager"/> API).
    /// Regression coverage for two fixes: a non-admin user can open the vault (filename-encryption
    /// detection is now user-aware), and admin-only key rotation actually rotates (adds a seed) instead
    /// of throwing NotImplemented.
    /// </summary>
    [TestClass]
    public class MultiUserVaultTests
    {
        private const string Content = "shared secret — the quick brown fox jumps over the lazy dog";

        [TestMethod]
        public async Task Uvf_SecondaryUser_CanOpenVaultAndReadAdminFile()
        {
            string vaultDir = NewVaultDir("multiuser");
            char[] admin = "admin-pass-123".ToCharArray();
            char[] alicePw = "alice-pass-456".ToCharArray();
            try
            {
                using (var vmAdmin = await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, admin, encryptFilenames: true, kdfParams: null))
                {
                    await WriteAllAsync(vmAdmin.EncryptingStorage, "/secret.txt", Content);
                }

                // Grant a second (non-admin) user, then open the vault AS that user.
                await VaultManager.AddUserToVaultAsync(vaultDir, admin, "alice", alicePw);

                using (var vmAlice = await VaultManager.LoadUvfVaultAsync(vaultDir, alicePw, "alice"))
                {
                    string readBack = await ReadAllAsync(vmAlice.EncryptingStorage, "/secret.txt", Content.Length * 2);
                    Assert.AreEqual(Content, readBack, "Secondary user should read the admin-written file.");
                }
            }
            finally { TryDelete(vaultDir); }
        }

        [TestMethod]
        public async Task Uvf_AdminOnly_RotateKeys_RewritesVaultAndKeepsFilesReadable()
        {
            string vaultDir = NewVaultDir("rotate");
            char[] admin = "admin-pass-123".ToCharArray();
            string vaultFile = Path.Combine(vaultDir, "vault.uvf");
            try
            {
                using (var vm = await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, admin, encryptFilenames: true, kdfParams: null))
                {
                    await WriteAllAsync(vm.EncryptingStorage, "/before.txt", Content);
                }

                byte[] before = await File.ReadAllBytesAsync(vaultFile);
                await VaultManager.RotateVaultKeysAsync(vaultDir, "admin-pass-123".ToCharArray());
                byte[] after = await File.ReadAllBytesAsync(vaultFile);

                Assert.IsFalse(before.SequenceEqual(after), "Rotation should rewrite vault.uvf (a new seed is added).");

                using (var vm2 = await VaultManager.LoadUvfVaultAsync(vaultDir, "admin-pass-123".ToCharArray(), null))
                {
                    string readBack = await ReadAllAsync(vm2.EncryptingStorage, "/before.txt", Content.Length * 2);
                    Assert.AreEqual(Content, readBack, "Files written before rotation must remain readable (old seed retained).");
                }
            }
            finally { TryDelete(vaultDir); }
        }

        [TestMethod]
        public async Task Uvf_MultiUser_RotateKeys_IsBlockedWithClearError()
        {
            string vaultDir = NewVaultDir("rotate-blocked");
            char[] admin = "admin-pass-123".ToCharArray();
            try
            {
                using (await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, admin, encryptFilenames: true, kdfParams: null)) { }
                await VaultManager.AddUserToVaultAsync(vaultDir, "admin-pass-123".ToCharArray(), "bob", "bob-pass-789".ToCharArray());

                // With a non-admin user present, rotation is refused with a clear InvalidOperationException
                // (not a generic NotImplementedException).
                await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                    () => VaultManager.RotateVaultKeysAsync(vaultDir, "admin-pass-123".ToCharArray()));
            }
            finally { TryDelete(vaultDir); }
        }

        private static string NewVaultDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"uvf-mu-{tag}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
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
                    if (n <= 0) throw new IOException($"short write at {written} for {path}");
                    written += n;
                }
                await storage.FlushAsync(path, handle);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                await storage.CloseAsync(path, handle);
            }
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
            finally
            {
                Marshal.FreeHGlobal(buffer);
                await storage.CloseAsync(path, handle);
            }
        }
    }
}
