using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Core.Common;
using UvfLib.Core.Jwe;
using UvfLib.Master;

namespace UvfLib.Tests.integration
{
    /// <summary>
    /// Public-key (asymmetric) vault recipients: an admin grants access to a user's PUBLIC key
    /// (ECDH-ES+A256KW), and the user opens the vault with their PRIVATE key — no shared password.
    /// Exercised at the crypto layer (MultiUserJweVaultManager) using a real vault for setup.
    /// </summary>
    [TestClass]
    public class AsymmetricRecipientTests
    {
        [TestMethod]
        public async Task PublicKeyUser_AddedByPublicKey_CanLoadWithPrivateKey()
        {
            string vaultDir = NewDir();
            string vaultFile = Path.Combine(vaultDir, "vault.uvf");
            try
            {
                using (await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, "admin-pass-123".ToCharArray(), encryptFilenames: true, kdfParams: null)) { }

                string jwe = await File.ReadAllTextAsync(vaultFile);
                var adminPayload = MultiUserJweVaultManager.LoadMultiUserVault(jwe, "admin-pass-123".ToCharArray(), "admin");

                // Generate a user key pair; grant access using only the PUBLIC key (no user password).
                using var alice = EcdhKeyMaterial.GenerateKeyPair();
                byte[] alicePublic = EcdhKeyMaterial.ExportPublicKey(alice);
                string jwe2 = MultiUserJweVaultManager.AddPublicKeyUserToVault(jwe, "admin-pass-123".ToCharArray(), "alice", alicePublic);

                // Alice opens the vault with her PRIVATE key and gets the same vault keys as admin.
                var alicePayload = MultiUserJweVaultManager.LoadMultiUserVaultWithKey(jwe2, alice, "alice");
                Assert.AreEqual(adminPayload.RootDirId, alicePayload.RootDirId, "Public-key user must unwrap the same vault keys as admin.");

                // Alice is listed; admin access is unaffected.
                CollectionAssert.Contains(MultiUserJweVaultManager.GetVaultUsers(jwe2, "admin-pass-123".ToCharArray()), "alice");
                Assert.IsNotNull(MultiUserJweVaultManager.LoadMultiUserVault(jwe2, "admin-pass-123".ToCharArray(), "admin"));

                // Password-protected private key round-trips (export encrypted PKCS#8 -> import -> still loads).
                byte[] encryptedKey = EcdhKeyMaterial.ExportEncryptedPrivateKey(alice, "alice-key-pass".ToCharArray());
                using var aliceReloaded = EcdhKeyMaterial.ImportEncryptedPrivateKey(encryptedKey, "alice-key-pass".ToCharArray());
                var alicePayload2 = MultiUserJweVaultManager.LoadMultiUserVaultWithKey(jwe2, aliceReloaded, "alice");
                Assert.AreEqual(adminPayload.RootDirId, alicePayload2.RootDirId);
            }
            finally { TryDelete(vaultDir); }
        }

        [TestMethod]
        public async Task PublicKeyUser_WrongPrivateKey_CannotLoad()
        {
            string vaultDir = NewDir();
            string vaultFile = Path.Combine(vaultDir, "vault.uvf");
            try
            {
                using (await VaultManager.CreateMultiUserUvfVaultAsync(vaultDir, "admin-pass-123".ToCharArray(), encryptFilenames: true, kdfParams: null)) { }
                string jwe = await File.ReadAllTextAsync(vaultFile);

                using var alice = EcdhKeyMaterial.GenerateKeyPair();
                string jwe2 = MultiUserJweVaultManager.AddPublicKeyUserToVault(jwe, "admin-pass-123".ToCharArray(), "alice", EcdhKeyMaterial.ExportPublicKey(alice));

                // A different key pair must not be able to open Alice's recipient.
                using var stranger = EcdhKeyMaterial.GenerateKeyPair();
                Assert.ThrowsException<UnauthorizedAccessException>(
                    () => MultiUserJweVaultManager.LoadMultiUserVaultWithKey(jwe2, stranger, "alice"));
            }
            finally { TryDelete(vaultDir); }
        }

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), $"uvf-asym-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
