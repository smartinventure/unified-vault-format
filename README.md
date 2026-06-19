# Unified Vault Format (UVF) Library

## Overview

A **universal implementation of the [Unified Vault Format (UVF)](https://github.com/encryption-alliance/unified-vault-format)** — the next-generation, open vault format developed by the [Encryption Alliance](https://github.com/encryption-alliance) (teams behind Cryptomator and Cyberduck) with full **[Cryptomator](https://cryptomator.org/)** compatibility. It provides secure client-side encryption for files and folders stored locally or in the cloud, kept in a secure "vault" where file contents, file names, and the directory structure are all encrypted before anything touches the storage backend.

The **core is written in C# / .NET** and compiles to a self-contained **native library** (`TitanVault`) that exposes a flat C ABI — so the *same* engine is callable from many languages. This repository ships runnable demos and bindings for **.NET, Python, Node.js, Java, C/C++, Rust, Go, Swift, Dart and PHP** (see [`Demo/`](Demo/)).

The library delivers two things:

1. **A native UVF implementation** — full support for the Unified Vault Format (version 3): file-name encryption, file-content encryption, file headers, directory metadata, master-key management, and multi-user access (password and public-key recipients).
2. **Cryptomator compatibility** — it also reads and writes classic Cryptomator (vault format v8) vaults. This has been **tested for full round-trip compatibility with Cryptomator**: it unlocks and decrypts vaults created by the Cryptomator app, and vaults *it* creates open and verify correctly **in** the Cryptomator app.

The C# core was **ported from the official Java reference implementation** ([`cryptolib`](https://github.com/cryptomator/cryptolib) / `cryptofs`); its cryptographic behavior is validated against that reference and the published UVF specification — but please read the disclaimer below on **how** that port was produced.

> ### ⚠️ Disclaimer / Warning — please read
>
> **The port from the original Java code to C# was produced with extensive use of AI (large-language-model) assistance.** The result passes an extensive automated test suite and has been reviewed against the Java reference, but it has **not** undergone an independent, professional, human security audit. AI-assisted translation can introduce subtle cryptographic or memory-handling defects that automated tests do not catch.
>
> **Use it entirely at your own discretion and risk.** Do not rely on it to protect sensitive or high-value data without first performing your own thorough review and, ideally, a professional audit. **We accept no liability whatsoever:** under no circumstances shall the authors or contributors be held liable for any damages or losses (financial, data, privacy, health, or otherwise) arising from the use of this software, which is provided "as is", without warranty of any kind.

## Security model & known limitations

UvfLib has had a structured source-level self-review (see [`Audit/`](Audit/)) but **no independent professional audit**. Two properties are worth calling out explicitly:

- **Truncation of trailing data is not detected.** Each file chunk is individually authenticated (AES-GCM) and bound to its position (chunk index + header nonce as additional authenticated data), so chunks cannot be reordered, swapped, duplicated, or forged. However, the *total* file length is not authenticated — an attacker with write access to the ciphertext can drop whole trailing chunks and the decrypted output will simply be shorter, undetected. (This matches the Cryptomator format's design.) Any content that *is* returned is always authentic.
- **Key material in memory cannot be guaranteed erased.** The library zeroizes keys, passwords, and derived buffers (`CryptographicOperations.ZeroMemory` / `Destroy()`), but on a managed runtime the garbage collector may relocate/copy `byte[]`/`char[]` before they are wiped, and OS swap/hibernation can persist secrets. Treat process memory as a residual risk on a compromised host.

See [`Audit/`](Audit/) for the full findings and the remediation log.

## License

This project is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)** — see the [LICENSE](LICENSE) file for the full text.

In short: you are free to use, study, modify, and redistribute this software, but if you distribute it or make it available over a network as part of a service, you must release your corresponding source code under the same license.

**Alternative / commercial licenses are available upon request.** If the AGPL-3.0 terms do not fit your use case (for example, embedding the library in a closed-source or proprietary product), please reach out to arrange a commercial license: **info `[at]` smartinventure `[dot]` com** (write the address normally — it is shown this way to deter spam bots: replace `[at]` → `@` and `[dot]` → `.`).

## Getting Started

UvfLib can be consumed two ways — as a managed .NET package, or as the native library from any other language.

### 1. .NET — managed NuGet package (no native dependency)

```xml
<PackageReference Include="UvfLib.Master" Version="1.0.4" />
```

`UvfLib.Master` provides the high-level vault API and pulls in `UvfLib.Core` + `UvfLib.Vault`. A minimal example (UVF; Cryptomator has the equivalent `*CryptomatorVaultAsync` factories):

```csharp
using UvfLib.Master;
using StorageLib.Abstractions;

// Create (or VaultManager.LoadUvfVaultAsync to open) a vault.
using var vault = await VaultManager.CreateUvfVaultAsync(vaultDir, password.ToCharArray());
IStorage fs = vault.EncryptingStorage;          // transparently encrypts content + file names

await fs.CreateDirectoryAsync("/notes");
// write/read through fs.OpenAsync / WriteAsync / ReadAsync; list with fs.ReadDirAsync("/")
await fs.DeleteAsync("/hello.txt");
```

### 2. Any other language — the native library

The core also compiles to a self-contained **Native-AOT** shared library (`TitanVault.dll` / `.so` / `.dylib`) exposing a flat **C ABI**, so it works from any language with FFI. The quickest way to see this is the runnable demos below; the contract and conventions are documented under [Native library & C ABI](#native-library--c-abi).

### Runnable examples — [`Demo/`](Demo/)

Each demo exercises the **full API for both UVF and Cryptomator** — files, directories, streaming, persistence, key rotation, password + public-key multi-user, and maintenance — then unlocks a real Cryptomator vault (proving interop) and runs a throughput benchmark. See [`Demo/README.md`](Demo/README.md) for the shared command-line switches.

| Demo | Uses |
|------|------|
| [`Demo/DotNet`](Demo/DotNet)  | C# with the managed `UvfLib` package (no native DLL) |
| [`Demo/NodeJs`](Demo/NodeJs)  | native `TitanVault` via [`koffi`](https://koffi.dev) — the reference demo |
| [`Demo/Python`](Demo/Python)  | native `TitanVault` via `ctypes` (stdlib) |
| [`Demo/Java`](Demo/Java)      | native `TitanVault` via [JNA](https://github.com/java-native-access/jna) |
| [`Demo/Cpp`](Demo/Cpp)        | native `TitanVault` via `LoadLibrary`/`dlopen` (runtime) |
| [`Demo/Rust`](Demo/Rust)      | native `TitanVault` via [`libloading`](https://crates.io/crates/libloading) |
| [`Demo/Go`](Demo/Go)          | native `TitanVault` via [`purego`](https://github.com/ebitengine/purego) (no cgo) |
| [`Demo/Swift`](Demo/Swift)    | native `TitanVault` via a C module map + `dlopen` |
| [`Demo/Dart`](Demo/Dart)      | native `TitanVault` via `dart:ffi` |
| [`Demo/Php`](Demo/Php)        | native `TitanVault` via PHP `FFI` |

#### Getting the native library (for the non-.NET demos)

The non-.NET demos need the `TitanVault` shared library. Two ways to get it:

- **Download a prebuilt binary** from the [**Releases**](https://github.com/smartinventure/unified-vault-format/releases) page: each release attaches a `TitanVault-<rid>.zip` for every platform — `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`. Unzip to get `TitanVault.dll` (Windows) / `libTitanVault.so` (Linux) / `libTitanVault.dylib` (macOS), then drop it next to the demo (it is auto-discovered there) or point at it with `--lib <path>`.
- **Build it yourself:** `BuildScripts/build.ps1 -Task aot` (Windows) or `BuildScripts/build.sh --task aot` (Linux/macOS) → written to `Dist/Native/<rid>/`. Needs a C toolchain (Linux: `clang` + `zlib1g-dev`; Windows: VS Build Tools, "Desktop development with C++"). See [`BuildScripts/README.md`](BuildScripts/README.md).

## Architecture

The library is organized into three main components:

1. **API** - Interfaces that define the contract for the library
2. **Common** - Utility classes and implementations shared across versions
3. **V3** - Implementation of the Universal Vault Format (version 3)

### Key Components

#### Masterkey Management

1. **Masterkey** - Base interface for encryption keys
2. **UVFMasterkey** - Interface for the Universal Vault Format masterkey
3. **DestroyableMasterkey** - Interface for keys that can be securely destroyed
4. **RevolvingMasterkey** - Interface for keys that support rotation (multiple revisions)

```csharp
// Create a new masterkey from a passphrase
MasterkeyFile file = MasterkeyFileAccess.CreateFromPassphrase("your-secure-passphrase");

// Save to disk
MasterkeyFileAccess.Save(file, "masterkey.Uvf");

// Load from disk with passphrase
MasterkeyFile loadedFile = MasterkeyFileAccess.Load("masterkey.Uvf");
byte[] rawKey = MasterkeyFileAccess.LoadRawMasterkey(loadedFile, "your-secure-passphrase");

// Create UVF masterkey and use it
UVFMasterkey masterkey = UVFMasterkey.CreateFromRaw(rawKey);

// Securely destroy the key when finished
if (masterkey is DestroyableMasterkey destroyable)
{
    destroyable.Destroy();
}
```

#### Cryptographic Operations

1. **CryptoFactory** - Creates cryptors for specific vault versions
2. **Cryptor** - Main entry point for cryptographic operations
3. **FileNameCryptor** - Handles encryption/decryption of filenames
4. **FileContentCryptor** - Handles encryption/decryption of file contents
5. **FileHeaderCryptor** - Manages file headers containing metadata

```csharp
// Create cryptor from masterkey
CryptoFactory factory = CryptoFactory.GetFactory();
Cryptor cryptor = factory.Create(masterkey);

// File name encryption
string encryptedName = cryptor.FileNameCryptor().EncryptFilename("document.txt", directoryId);

// File content encryption/decryption
FileHeader header = cryptor.FileHeaderCryptor().Create();
byte[] headerBytes = cryptor.FileHeaderCryptor().HeaderBytes(header);

// Write file with header followed by encrypted content
using (var outputStream = File.Create("encrypted.bin"))
{
    outputStream.Write(headerBytes, 0, headerBytes.Length);
    
    // Encrypt content
    byte[] encryptedContent = cryptor.FileContentCryptor().EncryptWithoutHeader(content, header);
    outputStream.Write(encryptedContent, 0, encryptedContent.Length);
}
```

## Working with Files

### File Name Encryption

Each directory in a UVF vault has a unique ID. This ID is used as additional authenticated data during filename encryption to ensure that moving a file to a different directory changes its encrypted name.

```csharp
// Generate a unique ID for each directory
string directoryId = Guid.NewGuid().ToString();

// Encrypt a file name
string encryptedName = cryptor.FileNameCryptor().EncryptFilename("document.txt", directoryId);

// Decrypt a file name
string decryptedName = cryptor.FileNameCryptor().DecryptFilename(encryptedName, directoryId);
```

### File Content Encryption

File content encryption involves two steps:
1. Creating a file header with encryption parameters
2. Encrypting the content using those parameters

```csharp
// Create file header
FileHeader header = cryptor.FileHeaderCryptor().Create();

// Encrypt content with header
byte[] encryptedContent = cryptor.FileContentCryptor().Encrypt(content, header);

// Or for more control:
byte[] headerBytes = cryptor.FileHeaderCryptor().HeaderBytes(header);
byte[] encryptedWithoutHeader = cryptor.FileContentCryptor().EncryptWithoutHeader(content, header);

// Decrypt content
FileHeader decryptedHeader = cryptor.FileHeaderCryptor().DecryptHeader(headerBytes);
byte[] decryptedContent = cryptor.FileContentCryptor().DecryptWithoutHeader(encryptedWithoutHeader, decryptedHeader);
```

## Key Derivation and Security

The library uses HKDF (HMAC-based Key Derivation Function) for deriving keys from the master key, and scrypt for passphrase-based key derivation:

```csharp
// HKDF example (internal library use)
byte[] derivedKey = HKDFHelper.DeriveKey(
    masterKey,     // Input key material
    salt,          // Optional salt
    contextInfo,   // Context information
    outputLength   // Length of output key
);

// Secure passphrase handling (when loading masterkey files)
byte[] rawKey = MasterkeyFileAccess.LoadRawMasterkey(masterkeyFile, passphrase);
try {
    // Use the key
    UVFMasterkey masterkey = UVFMasterkey.CreateFromRaw(rawKey);
    // ...operations...
}
finally {
    // Securely erase from memory
    CryptographicOperations.ZeroMemory(rawKey);
}
```

## Best Practices

1. **Always destroy keys when finished**: Use the `Destroy()` method on `DestroyableMasterkey` implementations to securely erase sensitive data.

2. **Use strong passphrases**: When creating masterkey files, use strong, random passphrases.

3. **Handle encrypted data securely**: Keep encrypted data and key material separate, and never store keys unencrypted.

4. **Use directory IDs consistently**: For each directory, generate a unique ID and use it consistently for all files in that directory.

5. **Error handling**: Catch specific exceptions like `InvalidPassphraseException`, `AuthenticationFailedException`, and `CryptoException` to handle different error scenarios gracefully.

## Building & Repository Layout

The repository is intentionally layered so that the cryptographic library has **no external dependencies** and can be built and audited on its own:

| Project | Role | External dependency |
|---------|------|---------------------|
| `UvfLib.Core` | Cryptographic core (UVF v3 + Cryptomator v8) | **None** (BouncyCastle/System.* NuGet only) |
| `UvfLib.Vault` | Vault streams & file-header orchestration | **None** (depends only on `UvfLib.Core`) |
| `UvfLib.Master` | Optional high-level + Native-AOT facade (`TitanVault`) | **`FolderMagic.StorageLib`** (separate MIT library) |
| [`Demo/`](Demo) (.NET, Python, Node.js, Java, C/C++, Rust, Go, Swift, Dart, PHP) | Runnable usage examples | `.NET` via the NuGet package; others via the native `TitanVault` C ABI |
| `UvfLib.Tests` | Test suite | references all of the above |

**The AGPL deliverable is `UvfLib.Core` + `UvfLib.Vault`.** They build cleanly with nothing beyond public NuGet packages:

```powershell
dotnet build Uvf.Net/UvfLib.Core/UvfLib.Core.csproj   -c Release
dotnet build Uvf.Net/UvfLib.Vault/UvfLib.Vault.csproj  -c Release
```

`UvfLib.Master` (and therefore the demos, the full solution, and the test project) additionally require **`FolderMagic.StorageLib`** — a separate, MIT-licensed storage abstraction library that is *not* bundled here. The solution references it by relative path (`..\..\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj`); provide that library (or adjust the reference) if you want to build the storage-integrated facade or run the full solution. Cloud connectors and other heavy dependencies live in StorageLib, not in the crypto library, by design.

## Native library & C ABI

`UvfLib.Master` compiles **Ahead-of-Time (AOT)** to a self-contained native shared library with no .NET runtime dependency, exposing a flat **C-style ABI** via `[UnmanagedCallersOnly]` exports (`TitanVault.dll` / `.so` / `.dylib`). Get it from [Releases or by building it](#getting-the-native-library-for-the-non-net-demos).

Because the ABI is a stable flat C interface, the library is consumable from **any language with C FFI**. **The fastest way to see how is the runnable [`Demo/`](Demo/) — ten languages, all calling the same exports** (P/Invoke, `ctypes`, koffi, JNA, `dlopen`, `libloading`, `purego`, `dart:ffi`, PHP `FFI`). The canonical, compiler-checked contract is the C header [`Bindings/include/titan_vault.h`](Bindings/include/titan_vault.h) (all 46 functions, grouped, with ownership/in-out docs); [`Demo/README.md`](Demo/README.md) lists the full function table and the conventions below.

### ABI conventions (the essentials)

- **Strings in** are passed as a UTF-8 byte pointer **plus an explicit length** (`const unsigned char* ptr, int len`) — they are *not* required to be NUL-terminated.
- **Integer results**: `0` (`TITAN_VAULT_SUCCESS`) means success; negative values are error codes (`-1` invalid parameter, `-2` not found, `-3` invalid password, `-5` corrupted, `-6` buffer too small, `-7` unsupported format, `-100` internal). Call `titan_vault_get_last_error()` for the message.
- **In/out sizes** (`int*`): for `read_file` / `generate_user_keypair`, pass the buffer capacity; on `-6` the required size is written back — grow and retry.
- **Handles** (vaults, streams) are opaque pointers; `NULL` signals failure. Close them with `titan_vault_close_vault` / `titan_vault_close_stream`.
- **Memory ownership**: every string the library returns — `get_version`, `get_last_error`, `read_all_text`, and each entry filled into `list_directory` / `get_vault_users` — is heap-allocated and must be released with `titan_vault_free_string`. Wipe sensitive buffers with `titan_vault_secure_zero_memory`.
- **Architecture must match the calling process** (an x64 runtime needs the `win-x64` build, not `win-arm64` — relevant under Windows-on-ARM emulation).

> **Native AOT note:** AOT disallows runtime reflection and enables trimming; the cryptographic core and vault layers are AOT-compatible, and JSON serialization uses source generators (`UvfJsonContext`) for that reason.

## Conclusion

The UVF library provides powerful, standards-based, client-side encryption for files — fully interoperable with the UVF and Cryptomator ecosystems. Use it as a managed .NET library, or as a single native component callable from a dozen languages; the runnable [`Demo/`](Demo/) shows every one end to end.
