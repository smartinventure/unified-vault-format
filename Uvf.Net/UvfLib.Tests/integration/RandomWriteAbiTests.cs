using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Master;

namespace UvfLib.Tests.integration
{
    /// <summary>
    /// Exercises the random-access WRITE path exposed to the native C ABI via
    /// VaultManager.OpenWriteAsync(path, requestRandomWrite: true) (the plumbing behind
    /// titan_vault_open_random_write_stream): seek to an arbitrary offset and overwrite bytes there,
    /// across multiple chunks. Covers both same-session (write then seek back) and patch-an-existing-file.
    /// </summary>
    [TestClass]
    public class RandomWriteAbiTests
    {
        private const int Size = 100 * 1024;          // 100 KiB -> spans several 32 KiB chunks
        private const long Offset = 40_000;           // lands mid-file, across a chunk boundary
        private static readonly byte[] Patch = Encoding.ASCII.GetBytes("PATCHED-RANDOM-WRITE!");

        [TestMethod] public Task Uvf_RandomWrite_SameSession_SeekOverwrite() => SameSessionAsync(cryptomator: false);
        [TestMethod] public Task Cryptomator_RandomWrite_SameSession_SeekOverwrite() => SameSessionAsync(cryptomator: true);
        [TestMethod] public Task Uvf_RandomWrite_PatchExistingFile() => PatchExistingAsync(cryptomator: false);
        [TestMethod] public Task Cryptomator_RandomWrite_PatchExistingFile() => PatchExistingAsync(cryptomator: true);

        private static byte[] MakeData()
        {
            var d = new byte[Size];
            for (int i = 0; i < d.Length; i++) d[i] = (byte)(i % 251);
            return d;
        }

        private static async Task<VaultManager> CreateAsync(bool cryptomator, string dir, char[] pw) =>
            cryptomator
                ? await VaultManager.CreateCryptomatorVaultAsync(dir, pw)
                : await VaultManager.CreateMultiUserUvfVaultAsync(dir, pw, encryptFilenames: true, kdfParams: null);

        private static void AssertPatched(byte[] original, byte[] readBack)
        {
            Assert.AreEqual(original.Length, readBack.Length, "length must be unchanged");
            for (int i = 0; i < Patch.Length; i++)
                Assert.AreEqual(Patch[i], readBack[Offset + i], $"patched byte at {Offset + i}");
            Assert.AreEqual(original[0], readBack[0], "byte before the patch must be unchanged");
            Assert.AreEqual(original[Offset - 1], readBack[Offset - 1], "byte just before the patch must be unchanged");
            Assert.AreEqual(original[Offset + Patch.Length], readBack[Offset + Patch.Length], "byte just after the patch must be unchanged");
        }

        private static async Task SameSessionAsync(bool cryptomator)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"uvf-rw-ss-{(cryptomator ? "c" : "u")}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try
            {
                using VaultManager vm = await CreateAsync(cryptomator, dir, "rw-pass-123".ToCharArray());
                byte[] data = MakeData();
                await using (Stream ws = await vm.OpenWriteAsync("/data.bin", requestRandomWrite: true))
                {
                    Assert.IsTrue(ws.CanSeek, "random-write stream must be seekable");
                    Assert.IsTrue(ws.CanWrite, "random-write stream must be writable");
                    await ws.WriteAsync(data, 0, data.Length);
                    ws.Seek(Offset, SeekOrigin.Begin);
                    await ws.WriteAsync(Patch, 0, Patch.Length);
                    await ws.FlushAsync();
                }
                AssertPatched(data, await vm.ReadAllBytesAsync("/data.bin"));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static async Task PatchExistingAsync(bool cryptomator)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"uvf-rw-pe-{(cryptomator ? "c" : "u")}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            try
            {
                using VaultManager vm = await CreateAsync(cryptomator, dir, "rw-pass-123".ToCharArray());
                byte[] data = MakeData();
                await vm.WriteAllBytesAsync("/data.bin", data);   // sequential write, fully closed
                await using (Stream ws = await vm.OpenWriteAsync("/data.bin", requestRandomWrite: true))
                {
                    ws.Seek(Offset, SeekOrigin.Begin);
                    await ws.WriteAsync(Patch, 0, Patch.Length);
                    await ws.FlushAsync();
                }
                AssertPatched(data, await vm.ReadAllBytesAsync("/data.bin"));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
