# UvfLib.Storage - Vault Storage Decorators

This project provides storage decorators that implement transparent encryption/decryption for vault files while maintaining full compatibility with the StorageLib `IStorage` interface.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                        │
├─────────────────────────────────────────────────────────────┤
│                 UvfLib.Storage                              │
│  ┌─────────────────┐  ┌─────────────────────────────────┐   │
│  │ UvfStorageDecorator │  │  CryptomatorV8StorageDecorator│   │
│  │ (IStorage)      │  │  (IStorage)                     │   │
│  └─────────────────┘  └─────────────────────────────────┘   │
│            │                          │                     │
│  ┌─────────▼────────┐       ┌─────────▼──────────┐         │
│  │ UvfPathTranslator│       │CryptomatorPathTranslator │     │
│  │ (IVaultPathTranslator)   │ (IVaultPathTranslator)   │     │
│  └──────────────────┘       └──────────────────────────┘     │
├─────────────────────────────────────────────────────────────┤
│                    StorageLib (NuGet)                       │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────┐ │
│  │  LocalStorage   │  │  AzureStorage   │  │MemoryStorage │ │
│  │   (IStorage)    │  │   (IStorage)    │  │  (IStorage)  │ │
│  └─────────────────┘  └─────────────────┘  └─────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Key Components

### 1. Path Translators (`IVaultPathTranslator`)

Handle the complex mapping between virtual paths and encrypted storage paths:

- **Virtual Path**: `/documents/readme.txt` (what the application sees)
- **Storage Path**: `d/XX/YYYYYYYY/encrypted_filename.uvf` (what's stored on disk)

### 2. Storage Decorators (`VaultStorageDecoratorBase`)

Implement the `IStorage` interface while providing transparent encryption:

- **UvfStorageDecorator**: Handles UVF format with both encrypted/unencrypted modes
- **CryptomatorV8StorageDecorator**: Handles Cryptomator V8 compatibility

### 3. File Handles (`VaultFileHandle`)

Manage encryption/decryption streams for open files based on the working patterns from `Program.cs`.

## Usage Examples

### Simple UVF Encryption (Unencrypted Filenames)

```csharp
// Files stored as: ReadMe.txt -> ReadMe.txt.uvf
IStorage localStorage = new LocalStorage();
await localStorage.InitializeAsync("file://", @"D:\vault");

IFileContentCryptor cryptor = GetCryptorFromVault();
using IStorage vaultStorage = localStorage.WithSimpleUvfEncryption(cryptor);

// Use like any IStorage - encryption is transparent!
await vaultStorage.WriteAllBytesAsync("/readme.txt", data);
byte[] decrypted = await vaultStorage.ReadAllBytesAsync("/readme.txt");
```

### Full UVF Encryption (Encrypted Filenames)

```csharp
// Files stored in complex structure like Program.cs testrun --cryptomator
using IStorage vaultStorage = UvfStorageDecorator.CreateEncrypted(localStorage, cryptor);

await vaultStorage.CreateDirectoryAsync("/documents");
await vaultStorage.WriteAllBytesAsync("/documents/secret.txt", data);
```

### Composable with StorageLib Decorators

```csharp
IStorage localStorage = new LocalStorage();
IStorage cachedStorage = new CachingStorage(localStorage);      // From StorageLib
IStorage throttledStorage = new ThrottledStorage(cachedStorage); // From StorageLib
using IStorage vaultStorage = throttledStorage.WithUvfEncryption(cryptor); // Our decorator

// Now: LocalStorage -> Caching -> Throttling -> UVF Encryption
```

## Integration with UvfLib

The storage decorators integrate with your existing UvfLib classes:

```csharp
// Load vault using existing UvfLib patterns
byte[] vaultContent = File.ReadAllBytes("vault.uvf");
using var vault = Vault.LoadUvfVault(vaultContent, password);

// Get cryptor from vault
IFileContentCryptor cryptor = vault.FileContentCryptor;

// Create storage decorator
IStorage localStorage = new LocalStorage();
using IStorage vaultStorage = localStorage.WithUvfEncryption(cryptor);

// Use transparently
await vaultStorage.WriteAllBytesAsync("/myfile.txt", data);
```

## Benefits

1. **✅ Transparent Encryption**: Applications use standard `IStorage` interface
2. **✅ Compatible with StorageLib**: Works with any `IStorage` implementation
3. **✅ Composable**: Can be combined with caching, throttling, logging, etc.
4. **✅ Multiple Backends**: Local, Azure, S3, Memory, etc.
5. **✅ Multiple Formats**: UVF and Cryptomator V8 support
6. **✅ Follows Working Patterns**: Based on your successful `Program.cs` implementation

## File Extensions

- **UVF Format**: `.uvf` files
- **Cryptomator Format**: `.c9r` files
- **Metadata Files**: `dir.uvf` (UVF) or `dir.c9r`/`dirid.c9r` (Cryptomator)

## Thread Safety

All storage decorators are thread-safe and can be used concurrently. File handles are managed per-thread to avoid conflicts. 