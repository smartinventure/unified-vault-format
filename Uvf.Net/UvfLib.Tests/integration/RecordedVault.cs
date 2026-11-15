using System;
using System.IO;
using System.Threading.Tasks;
using StorageLib.Abstractions;
using UvfLib.Master;

namespace UvfLib.Tests.integration
{
    /// <summary>
    /// A fresh vault in a temp folder whose decorator sits on a <see cref="RecordingStorageProxy"/> over a real
    /// LocalStorage. Disposing it closes the vault and removes the folder.
    /// </summary>
    internal sealed class RecordedVault : IAsyncDisposable
    {
        private const string Password = "recorded-vault-pass-123";
        private readonly string _directory;

        private RecordedVault(string directory, VaultManager manager, RecordingStorageProxy recorder)
        {
            _directory = directory;
            Manager = manager;
            Recorder = recorder;
        }

        public VaultManager Manager { get; }

        public RecordingStorageProxy Recorder { get; }

        public static async Task<RecordedVault> CreateAsync(TestVaultFormat format)
        {
            string directory = Path.Combine(Path.GetTempPath(), $"uvf-recorded-{format}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            IStorage localStorage = await UvfLib.Master.StorageFactory.CreateInitializedLocalStorageAsync(directory);
            IStorage recordedStorage = RecordingStorageProxy.Wrap(localStorage, out var recorder);
            char[] password = Password.ToCharArray();

            VaultManager manager = format == TestVaultFormat.Cryptomator
                ? await VaultManager.CreateCryptomatorVaultAsync(recordedStorage, password, directory, ownsStorage: true)
                : await VaultManager.CreateMultiUserUvfVaultAsync(recordedStorage, password, directory, encryptFilenames: true, kdfParams: null, ownsStorage: true);

            return new RecordedVault(directory, manager, recorder);
        }

        public ValueTask DisposeAsync()
        {
            Manager.Dispose();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A handle the OS has not released yet only leaves a temp folder behind.
            }

            return ValueTask.CompletedTask;
        }
    }
}
