using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UvfLib.Tests.integration;
using UvfLib.Vault;

namespace UvfLib.Tests.ChunkAware
{
    /// <summary>
    /// A flush in the middle of a file must make the bytes written so far decryptable without losing or
    /// misplacing anything written after it. The sequential encrypting stream keeps a partly filled 32 KB
    /// chunk in memory, so a flush has to write that chunk and still let later writes complete it.
    /// </summary>
    [TestClass]
    public class EncryptingStreamFlushTests
    {
        private const string Password = "flush-pass-123";
        private const int ChunkSize = 32 * 1024;
        private string _directory = null!;

        [TestInitialize]
        public void Setup()
        {
            _directory = Path.Combine(Path.GetTempPath(), "EncryptingStreamFlushTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A handle the OS has not released yet only leaves a temp folder behind.
            }
        }

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public void Flush_ThenMoreWritesInTheSameChunk_KeepsBothParts(TestVaultFormat format)
        {
            using var vault = CreateVault(format);
            byte[] first = Encoding.UTF8.GetBytes("written before the flush;");
            byte[] second = Encoding.UTF8.GetBytes(" written after it.");
            var output = new MemoryStream();

            using (var encrypting = vault.GetEncryptingStream(output, leaveOpen: true))
            {
                encrypting.Write(first);
                encrypting.Flush();
                encrypting.Write(second);
            }

            CollectionAssert.AreEqual(first.Concat(second).ToArray(), Decrypt(vault, output.ToArray()));
        }

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public void Flush_ThenWritesPastTheChunkBoundary_KeepsAllData(TestVaultFormat format)
        {
            using var vault = CreateVault(format);
            byte[] first = CreateData(20 * 1024, seed: 1);
            byte[] second = CreateData(ChunkSize + 20 * 1024, seed: 2);
            var output = new MemoryStream();

            using (var encrypting = vault.GetEncryptingStream(output, leaveOpen: true))
            {
                encrypting.Write(first);
                encrypting.Flush();
                encrypting.Write(second);
                encrypting.Flush();
            }

            CollectionAssert.AreEqual(first.Concat(second).ToArray(), Decrypt(vault, output.ToArray()));
        }

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public void Flush_PartialChunk_MakesTheWrittenBytesDecryptableBeforeClose(TestVaultFormat format)
        {
            using var vault = CreateVault(format);
            byte[] data = CreateData(ChunkSize + 100, seed: 3);
            var output = new MemoryStream();
            using var encrypting = vault.GetEncryptingStream(output, leaveOpen: true);

            encrypting.Write(data);
            encrypting.Flush();

            CollectionAssert.AreEqual(data, Decrypt(vault, output.ToArray()));
        }

        [TestMethod]
        [DataRow(TestVaultFormat.Cryptomator)]
        [DataRow(TestVaultFormat.Uvf)]
        public void Flush_NonSeekableOutput_KeepsTheFileDecryptable(TestVaultFormat format)
        {
            using var vault = CreateVault(format);
            byte[] first = Encoding.UTF8.GetBytes("before the flush;");
            byte[] second = Encoding.UTF8.GetBytes(" after it.");
            var output = new NonSeekableStream();

            using (var encrypting = vault.GetEncryptingStream(output, leaveOpen: true))
            {
                encrypting.Write(first);
                encrypting.Flush();
                encrypting.Write(second);
            }

            CollectionAssert.AreEqual(first.Concat(second).ToArray(), Decrypt(vault, output.ToArray()));
        }

        private VaultHandler CreateVault(TestVaultFormat format)
        {
            byte[] password = Encoding.UTF8.GetBytes(Password);
            if (format == TestVaultFormat.Cryptomator)
            {
                VaultHandler.CreateNewCryptomatorV8VaultComplete(_directory, password);
                return VaultHandler.LoadCryptomatorV8Vault(File.ReadAllBytes(Path.Combine(_directory, "masterkey.cryptomator")), password);
            }

            string vaultFile = Path.Combine(_directory, "vault.uvf");
            VaultHandler.CreateNewUvfVault(vaultFile, password, encryptFilenames: true);
            return VaultHandler.LoadUvfVault(File.ReadAllBytes(vaultFile), password);
        }

        private static byte[] Decrypt(VaultHandler vault, byte[] ciphertext)
        {
            using var decrypting = vault.GetDecryptingStream(new MemoryStream(ciphertext));
            using var cleartext = new MemoryStream();
            decrypting.CopyTo(cleartext);
            return cleartext.ToArray();
        }

        private static byte[] CreateData(int length, int seed)
        {
            var data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }
    }
}
