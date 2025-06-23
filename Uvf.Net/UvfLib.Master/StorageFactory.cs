using StorageLib.Abstractions;
using StorageLib.Connectors;

namespace UvfLib.Master
{
    /// <summary>
    /// Factory for creating storage instances without requiring direct dependencies on StorageLib.Connectors.
    /// This allows consuming applications to only depend on UvfLib.Storage while still accessing
    /// the underlying StorageLib implementations.
    /// </summary>
    public static class StorageFactory
    {
        /// <summary>
        /// Creates a new LocalStorage instance for file system operations.
        /// </summary>
        /// <returns>A new IStorage instance configured for local file system access</returns>
        public static IStorage CreateLocalStorage()
        {
            return new LocalStorage();
        }
        
        /// <summary>
        /// Creates and initializes a LocalStorage instance for the specified base path.
        /// </summary>
        /// <param name="basePath">The base directory path for the storage</param>
        /// <returns>An initialized IStorage instance ready for use</returns>
        public static async Task<IStorage> CreateInitializedLocalStorageAsync(string basePath)
        {
            var storage = new LocalStorage();
            await storage.InitializeAsync("file://", basePath);
            return storage;
        }
        
        /// <summary>
        /// Creates a LocalStorage instance and initializes it synchronously.
        /// Use this when you need immediate initialization without async/await.
        /// </summary>
        /// <param name="basePath">The base directory path for the storage</param>
        /// <returns>An initialized IStorage instance ready for use</returns>
        public static IStorage CreateInitializedLocalStorage(string basePath)
        {
            var storage = new LocalStorage();
            storage.InitializeAsync("file://", basePath).Wait();
            return storage;
        }
        
        /// <summary>
        /// Creates a LocalStorage instance with custom connection string and base path.
        /// </summary>
        /// <param name="connectionString">The connection string for the storage</param>
        /// <param name="basePath">The base directory path for the storage</param>
        /// <returns>An initialized IStorage instance ready for use</returns>
        public static async Task<IStorage> CreateCustomLocalStorageAsync(string connectionString, string basePath)
        {
            var storage = new LocalStorage();
            await storage.InitializeAsync(connectionString, basePath);
            return storage;
        }
    }
} 