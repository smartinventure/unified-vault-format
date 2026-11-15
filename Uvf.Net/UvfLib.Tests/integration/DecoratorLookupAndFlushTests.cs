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
    /// The vault decorators must hand the two lookup/flush variants StorageLib 2.2 added to <see cref="IStorage"/>
    /// on to the storage below: TryGetFileInfoAsync so a missing name costs no exception, and FlushToDiskAsync so
    /// the fsync guarantee is not silently weakened to a plain flush.
    /// </summary>
    [TestClass]
    public class DecoratorLookupAndFlushTests
    {
        private const string ExistingFilePath = "/note.txt";
        private const string MissingFilePath = "/missing.txt";
        private const string Content = "flushed to disk, found without exceptions";

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public async Task FlushToDiskAsync_OpenFile_FlushesStorageBelowToDisk(TestVaultFormat format)
        {
            await using var vault = await RecordedVault.CreateAsync(format);
            IStorage storage = vault.Manager.EncryptingStorage;
            IntPtr handle = await storage.OpenAsync(ExistingFilePath, OpenFlags.Create | OpenFlags.ReadWrite);
            try
            {
                await WriteAsync(storage, ExistingFilePath, handle, Content);
                vault.Recorder.ClearCalls();

                await storage.FlushToDiskAsync(ExistingFilePath, handle);

                CollectionAssert.Contains(vault.Recorder.Calls, nameof(IStorage.FlushToDiskAsync));
            }
            finally
            {
                await storage.CloseAsync(ExistingFilePath, handle);
            }
        }

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public async Task TryGetFileInfoAsync_MissingFile_ReturnsNullWithoutThrowingLookupBelow(TestVaultFormat format)
        {
            await using var vault = await RecordedVault.CreateAsync(format);
            IStorage storage = vault.Manager.EncryptingStorage;
            vault.Recorder.ClearCalls();

            FileObject? info = await storage.TryGetFileInfoAsync(MissingFilePath);

            Assert.IsNull(info);
            CollectionAssert.DoesNotContain(vault.Recorder.Calls, nameof(IStorage.GetFileInfoAsync));
        }

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public async Task TryGetFileInfoAsync_ExistingFile_ReturnsCleartextSize(TestVaultFormat format)
        {
            await using var vault = await RecordedVault.CreateAsync(format);
            IStorage storage = vault.Manager.EncryptingStorage;
            await WriteFileAsync(storage, ExistingFilePath, Content);

            FileObject? info = await storage.TryGetFileInfoAsync(ExistingFilePath);

            Assert.IsNotNull(info);
            Assert.IsFalse(info.IsDirectory);
            Assert.AreEqual(Encoding.UTF8.GetByteCount(Content), info.Size);
        }

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public async Task TryGetFileInfoAsync_Directory_ReturnsDirectory(TestVaultFormat format)
        {
            await using var vault = await RecordedVault.CreateAsync(format);
            IStorage storage = vault.Manager.EncryptingStorage;
            await storage.CreateDirectoryAsync("/sub");

            FileObject? info = await storage.TryGetFileInfoAsync("/sub");

            Assert.IsNotNull(info);
            Assert.IsTrue(info.IsDirectory);
        }

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public async Task GetFileInfoAsync_MissingFile_ThrowsFileNotFound(TestVaultFormat format)
        {
            await using var vault = await RecordedVault.CreateAsync(format);
            IStorage storage = vault.Manager.EncryptingStorage;

            await Assert.ThrowsExceptionAsync<FileNotFoundException>(() => storage.GetFileInfoAsync(MissingFilePath));
        }

        private static async Task WriteFileAsync(IStorage storage, string path, string content)
        {
            IntPtr handle = await storage.OpenAsync(path, OpenFlags.Create | OpenFlags.ReadWrite);
            try
            {
                await WriteAsync(storage, path, handle, content);
            }
            finally
            {
                await storage.CloseAsync(path, handle);
            }
        }

        private static async Task WriteAsync(IStorage storage, string path, IntPtr handle, string content)
        {
            byte[] data = Encoding.UTF8.GetBytes(content);
            IntPtr buffer = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buffer, data.Length);
                await storage.WriteAsync(path, handle, 0, data.Length, buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
