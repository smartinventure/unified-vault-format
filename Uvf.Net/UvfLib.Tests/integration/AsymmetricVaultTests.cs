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
    /// End-to-end public-key multi-user flows via the path-based <see cref="VaultManager"/> API:
    /// generate a user key pair, grant access by PUBLIC key, open the vault with the (encrypted) PRIVATE
    /// key, rotate the key for public-key members without any member password, and revoke a member.
    /// </summary>
    [TestClass]
    public class AsymmetricVaultTests
    {
        private const string Content = "shared secret — the quick brown fox jumps over the lazy dog";

        [TestMethod]
        public async Task PublicKeyUser_GrantedByPublicKey_OpensWithPrivateKeyAndReads()
        {
            string vaultDir = NewDir();
            char[] admin = "admin-pass-123".ToCharArray();
            char[] keyPw = "alice-key-pass".ToCharArray();
            try
            {
                using (var vmAdmin = await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, admin, encryptFilenames: true, kdfParams: null))
                {
                    await WriteAllAsync(vmAdmin.EncryptingStorage, "/secret.txt", Content);
                }

                var (publicKey, encryptedPrivateKey) = VaultManager.GenerateUserKeyPair(keyPw);
                await VaultManager.AddPublicKeyUserAsync(vaultDir, "admin-pass-123".ToCharArray(), "alice", publicKey);

                using (var vmAlice = await VaultManager.LoadUvfVaultWithEncryptedKeyAsync(vaultDir, encryptedPrivateKey, "alice-key-pass".ToCharArray(), "alice"))
                {
                    string readBack = await ReadAllAsync(vmAlice.EncryptingStorage, "/secret.txt", Content.Length * 2);
                    Assert.AreEqual(Content, readBack, "Public-key user must read the admin-written file via their private key.");
                }
            }
            finally { TryDelete(vaultDir); }
        }

        [TestMethod]
        public async Task PublicKeyMember_RotationWithoutMemberPassword_KeepsAccessToOldContent()
        {
            string vaultDir = NewDir();
            char[] admin = "admin-pass-123".ToCharArray();
            char[] keyPw = "alice-key-pass".ToCharArray();
            string vaultFile = Path.Combine(vaultDir, "vault.uvf");
            try
            {
                using (var vmAdmin = await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, admin, encryptFilenames: true, kdfParams: null))
                {
                    await WriteAllAsync(vmAdmin.EncryptingStorage, "/old.txt", Content);
                }

                var (publicKey, encryptedPrivateKey) = VaultManager.GenerateUserKeyPair(keyPw);
                await VaultManager.AddPublicKeyUserAsync(vaultDir, "admin-pass-123".ToCharArray(), "alice", publicKey);

                byte[] before = await File.ReadAllBytesAsync(vaultFile);
                // Admin rotates the key WITHOUT Alice's password (re-wraps to her public key).
                await VaultManager.RotateForPublicKeyMembersAsync(vaultDir, "admin-pass-123".ToCharArray());
                byte[] after = await File.ReadAllBytesAsync(vaultFile);
                Assert.IsFalse(before.SequenceEqual(after), "Rotation should rewrite vault.uvf.");

                // Alice still opens the vault with her private key and reads content from before rotation.
                using (var vmAlice = await VaultManager.LoadUvfVaultWithEncryptedKeyAsync(vaultDir, encryptedPrivateKey, "alice-key-pass".ToCharArray(), "alice"))
                {
                    string readBack = await ReadAllAsync(vmAlice.EncryptingStorage, "/old.txt", Content.Length * 2);
                    Assert.AreEqual(Content, readBack, "Member retains access after password-free rotation.");
                }
            }
            finally { TryDelete(vaultDir); }
        }

        [TestMethod]
        public async Task RemovedPublicKeyUser_CanNoLongerOpenVault()
        {
            string vaultDir = NewDir();
            char[] admin = "admin-pass-123".ToCharArray();
            char[] keyPw = "alice-key-pass".ToCharArray();
            try
            {
                using (await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, admin, encryptFilenames: true, kdfParams: null)) { }

                var (publicKey, encryptedPrivateKey) = VaultManager.GenerateUserKeyPair(keyPw);
                await VaultManager.AddPublicKeyUserAsync(vaultDir, "admin-pass-123".ToCharArray(), "alice", publicKey);
                // Sanity: alice can open before removal.
                using (await VaultManager.LoadUvfVaultWithEncryptedKeyAsync(vaultDir, encryptedPrivateKey, "alice-key-pass".ToCharArray(), "alice")) { }

                await VaultManager.RemoveUserFromVaultAsync(vaultDir, "admin-pass-123".ToCharArray(), "alice");

                await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(
                    () => VaultManager.LoadUvfVaultWithEncryptedKeyAsync(vaultDir, encryptedPrivateKey, "alice-key-pass".ToCharArray(), "alice"));
            }
            finally { TryDelete(vaultDir); }
        }

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"uvf-asymv-{Guid.NewGuid():N}");
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
