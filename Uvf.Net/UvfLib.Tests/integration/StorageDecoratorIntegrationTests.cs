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
    /// End-to-end tests for the handle-based <see cref="IStorage"/> vault decorators (the path a virtual
    /// filesystem like FolderMagic drives): create a vault over a real LocalStorage, then mkdir → write →
    /// read-back → list through the encrypting decorator, and confirm the backend holds only ciphertext.
    /// Covers both Cryptomator v8 and UVF v3.
    /// </summary>
    [TestClass]
    public class StorageDecoratorIntegrationTests
    {
        [TestMethod]
        public async Task Cryptomator_HandleApi_MkdirWriteReadList_RoundTrips()
        {
            await RunRoundTripAsync(VaultFormatKind.Cryptomator);
        }

        [TestMethod]
        public async Task Uvf_HandleApi_MkdirWriteReadList_RoundTrips()
        {
            await RunRoundTripAsync(VaultFormatKind.Uvf);
        }

        private enum VaultFormatKind { Cryptomator, Uvf }

        private static async Task RunRoundTripAsync(VaultFormatKind kind)
        {
            string vaultDir = Path.Combine(Path.GetTempPath(), $"uvf-it-{kind}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(vaultDir);
            char[] password = "integration-pass-123".ToCharArray();
            const string subDir = "/sub";
            const string filePath = "/sub/note.txt";
            const string content = "hello encrypted world — the quick brown fox jumps over the lazy dog";

            VaultManager? vm = null;
            try
            {
                var storage = await UvfLib.Master.StorageFactory.CreateInitializedLocalStorageAsync(vaultDir);
                vm = kind == VaultFormatKind.Cryptomator
                    ? await VaultManager.CreateCryptomatorVaultAsync(storage, password, vaultDir, ownsStorage: true)
                    : await VaultManager.CreateMultiUserUvfVaultAsync(storage, password, vaultDir, encryptFilenames: true, kdfParams: null, ownsStorage: true);

                IStorage enc = vm.EncryptingStorage;

                // Root must be listable on a fresh vault (empty).
                var rootEntries = (await enc.ReadDirAsync("/")).ToList();
                Assert.AreEqual(0, rootEntries.Count, "Fresh vault root should be empty.");

                // mkdir
                await enc.CreateDirectoryAsync(subDir);
                Assert.IsTrue(await enc.DirectoryExistsAsync(subDir), "Subdirectory should exist after CreateDirectory.");

                // write (handle-based, native buffer)
                await WriteAllAsync(enc, filePath, content);

                // read-your-writes through the mount-style API
                string readBack = await ReadAllAsync(enc, filePath, content.Length * 2);
                Assert.AreEqual(content, readBack, "Decrypted content should match what was written.");

                // list shows the cleartext name
                var entries = (await enc.ReadDirAsync(subDir)).Select(e => Path.GetFileName(e.Filename)).ToList();
                CollectionAssert.Contains(entries, "note.txt", $"Directory listing should contain the cleartext name. Got: {string.Join(",", entries)}");

                // GetFileInfo reports the cleartext size
                var info = await enc.GetFileInfoAsync(filePath);
                Assert.AreEqual(Encoding.UTF8.GetByteCount(content), info.Size, "GetFileInfo should report the cleartext size.");

                // Backend confidentiality: cleartext name + bytes never appear on disk.
                AssertBackendIsCiphertext(vaultDir, "note.txt", content, kind);
            }
            finally
            {
                vm?.Dispose();
                try { Directory.Delete(vaultDir, recursive: true); } catch { /* best effort */ }
            }
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

        private static void AssertBackendIsCiphertext(string vaultDir, string plaintextName, string content, VaultFormatKind kind)
        {
            var markers = new[] { "vault.uvf", "vault.cryptomator", "masterkey.cryptomator" };
            byte[] secret = Encoding.UTF8.GetBytes(content);

            foreach (var file in Directory.GetFiles(vaultDir, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                Assert.AreNotEqual(plaintextName, name, $"Cleartext name leaked to backend: {file}");
                if (markers.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                byte[] bytes = File.ReadAllBytes(file);
                Assert.IsTrue(IndexOf(bytes, secret) < 0, $"Cleartext content leaked into backend file: {file}");
            }
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && haystack[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
