# Uvf.Net Library Documentation

## Overview

Uvf.Net is a C# / .NET implementation of the **Universal Vault Format (UVF)** — the
next-generation, open vault format designed by the team behind
[Cryptomator](https://cryptomator.org/). It provides secure client-side
encryption for files and folders stored locally or in the cloud, with everything
(file contents, file names, and the directory structure itself) encrypted before
it touches the storage backend.

The library delivers two things:

1. **A native UVF implementation** — full support for the Universal Vault Format
   (version 3): file-name encryption, file-content encryption, file headers,
   directory metadata, and master-key management.
2. **Cryptomator compatibility** — it can also read and write classic Cryptomator
   (vault format v8) vaults, so existing Cryptomator vaults can be used from .NET.

It is a port of the official Java [`cryptolib`](https://github.com/cryptomator/cryptolib),
and its cryptographic behavior is validated against that reference implementation
and the published UVF specification.

> ### ⚠️ Disclaimer / Warning
>
> The C# code was semi-automatically translated from the original Java
> implementation. While it passes an extensive test suite and has been reviewed
> against the Java reference, it has **not** undergone an independent professional
> security audit. Use in production or for securing sensitive information is at
> your own risk and should be preceded by your own thorough review.
>
> Under no circumstances shall the authors or contributors be held liable for any
> damages or losses (financial, data, health, or otherwise) resulting from the use
> of this software.

## License

This project is licensed under the **GNU Affero General Public License v3.0
(AGPL-3.0)** — see the [LICENSE](LICENSE) file for the full text.

In short: you are free to use, study, modify, and redistribute this software, but
if you distribute it or make it available over a network as part of a service, you
must release your corresponding source code under the same license.

**Alternative / commercial licenses are available upon request.** If the AGPL-3.0
terms do not fit your use case (for example, embedding the library in a
closed-source or proprietary product), please contact
**info@smartinventure.com** to arrange a commercial license.

## Getting Started

UvfLib can be consumed two ways:

### 1. .NET — managed NuGet package (no native dependency)

```xml
<PackageReference Include="UvfLib.Master" Version="1.0.0" />
```

`UvfLib.Master` provides the high-level vault API and pulls in `UvfLib.Core` + `UvfLib.Vault`. A minimal
example (UVF; Cryptomator has the equivalent `*CryptomatorVaultAsync` factories):

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

### 2. Other languages — native AOT library

The library also builds to a self-contained native **AOT** shared library (`TitanVault`) that exposes a
flat **C ABI**, so it can be used from any language with FFI (Python, Node.js, Java, Go, Rust, …). See
[Native AOT Build](#native-aot-build) and [Language Bindings](#language-bindings) below.

### Runnable examples — [`Demo/`](Demo/)

Each demo creates a vault, encrypts a file, reads it back as cleartext, and deletes it — for both UVF
and Cryptomator:

| Demo | Uses |
|------|------|
| [`Demo/DotNet`](Demo/DotNet)  | C# with the managed `UvfLib` package (no AOT) |
| [`Demo/Python`](Demo/Python)  | native `TitanVault` via `ctypes` (stdlib) |
| [`Demo/NodeJs`](Demo/NodeJs)  | native `TitanVault` via [`koffi`](https://koffi.dev) |
| [`Demo/Java`](Demo/Java)      | native `TitanVault` via [JNA](https://github.com/java-native-access/jna) |

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

Each directory in a Uvf vault has a unique ID. This ID is used as additional authenticated data during filename encryption to ensure that moving a file to a different directory changes its encrypted name.

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

The repository is intentionally layered so that the cryptographic library has **no
external dependencies** and can be built and audited on its own:

| Project | Role | External dependency |
|---------|------|---------------------|
| `UvfLib.Core` | Cryptographic core (UVF v3 + Cryptomator v8) | **None** (BouncyCastle/System.* NuGet only) |
| `UvfLib.Vault` | Vault streams & file-header orchestration | **None** (depends only on `UvfLib.Core`) |
| `UvfLib.Master` | Optional high-level + Native-AOT facade (`TitanVault`) | **`FolderMagic.StorageLib`** (separate MIT library) |
| [`Demo/`](Demo) (DotNet, Python, NodeJs, Java) | Runnable usage examples | `.NET` via the NuGet package; others via the native `TitanVault` C ABI |
| `UvfLib.Tests` | Test suite | references all of the above |

**The AGPL deliverable is `UvfLib.Core` + `UvfLib.Vault`.** They build cleanly with
nothing beyond public NuGet packages:

```powershell
dotnet build Uvf.Net/UvfLib.Core/UvfLib.Core.csproj   -c Release
dotnet build Uvf.Net/UvfLib.Vault/UvfLib.Vault.csproj  -c Release
```

`UvfLib.Master` (and therefore the apps, the full solution, and the test project)
additionally require **`FolderMagic.StorageLib`** — a separate, MIT-licensed storage
abstraction library that is *not* bundled here. The solution references it by
relative path (`..\..\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj`); provide
that library (or adjust the reference) if you want to build the storage-integrated
facade or run the full solution. Cloud connectors and other heavy dependencies live
in StorageLib, not in the crypto library, by design.

## Native AOT Build

In addition to the managed .NET assemblies, the library can be compiled
**Ahead-of-Time (AOT)** into a self-contained native shared library with no .NET
runtime dependency. The `UvfLib.Master` project exposes a flat **C-style ABI**
(via `[UnmanagedCallersOnly]` exports) and, in `Release` configuration, builds to
a native library (`TitanVault.dll` / `.so` / `.dylib`).

```powershell
# Produce a native AOT shared library for the current platform (win-x64 by default)
dotnet publish Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release -r win-x64 -p:PublishAot=true
```

Build automation for AOT and packaging lives under [`BuildScripts/Scripts/`](BuildScripts/Scripts)
(for example `build-aot.ps1` and `build-all.ps1`), with output collected under `Dist/`.
A prebuilt `win-x64` native library is included in [`Dist/Native/win-x64/`](Dist/Native/win-x64).

> **Note:** Native AOT imposes restrictions (no runtime reflection, trimming
> enabled). The cryptographic core and vault layers are AOT-compatible; JSON
> serialization uses source generators (`UvfJsonContext`) for that reason.

## Language Bindings

Because the AOT build exposes a stable C ABI, the library can be consumed from any
language with C FFI support (P/Invoke, `ctypes`, FFI, N-API, cgo, JNA, etc.). The
native exports cover vault create/open/close and file/directory operations, with
error codes and explicit memory management for marshaled buffers.

Packaging scripts for distributing the native library to several ecosystems are
provided in [`BuildScripts/Scripts/package-bindings.ps1`](BuildScripts/Scripts/package-bindings.ps1),
which can emit packages for:

- **.NET** — NuGet
- **Python** — PyPI (`ctypes` wrapper)
- **Node.js** — NPM (FFI/N-API wrapper)
- *(templates for Java/Maven and others)*

```powershell
.\BuildScripts\Scripts\package-bindings.ps1 -Languages "CSharp,Python,NodeJs" -Version "1.0.0"
```

> The C ABI and these packaging scripts are the supported integration surface for
> non-.NET languages. The bindings themselves are thin wrappers generated around
> the native exports; treat them as a starting point and test against your target
> runtime.

### Using From Other Languages

All native functions are exported with the `titan_vault_` prefix and follow a
consistent C calling convention:

- **Strings in** are passed as a UTF-8 byte pointer **plus an explicit length**
  (`byte* ptr, int len`) — they are *not* required to be NUL-terminated.
- **Integer results**: `0` (`TITAN_VAULT_SUCCESS`) means success; negative values
  are error codes (`-1` invalid parameter, `-2` not found, `-3` invalid password,
  `-5` corrupted, `-6` buffer too small, `-7` unsupported format, `-100` internal).
- **Handles** (vaults, streams) are returned as opaque pointers; `IntPtr.Zero`
  / `NULL` signals failure — call `titan_vault_get_last_error()` for the message.
- **Memory ownership**: strings returned by the library must be released with
  `titan_vault_free_string`; sensitive buffers can be wiped with
  `titan_vault_secure_zero_memory`.

Core entry points:

| Function | Purpose |
|----------|---------|
| `titan_vault_get_version()` | Library version string |
| `titan_vault_detect_vault_format(path,len,...)` | Detect UVF vs Cryptomator |
| `titan_vault_create_uvf_vault(...)` / `titan_vault_load_uvf_vault(...)` | Create / open a UVF vault |
| `titan_vault_create_cryptomator_vault(...)` / `titan_vault_load_cryptomator_vault(...)` | Create / open a Cryptomator vault |
| `titan_vault_read_file(h,path,len,buf,bufSize*)` / `titan_vault_write_file(h,path,len,buf,size)` | Whole-file read / write |
| `titan_vault_open_read_stream(...)` / `titan_vault_open_write_stream(...)` / `titan_vault_stream_read/write/seek/flush/close` | Streaming I/O for large files |
| `titan_vault_file_exists` / `titan_vault_delete_file` / `titan_vault_create_directory` / `titan_vault_directory_exists` / `titan_vault_delete_directory` | File & directory operations |
| `titan_vault_add_user` / `titan_vault_remove_user` | Multi-user (UVF) management |
| `titan_vault_close_vault(h)` / `titan_vault_free_string(p)` / `titan_vault_secure_zero_memory(p,size)` / `titan_vault_get_last_error()` | Lifecycle & memory |

#### Python (ctypes)

```python
import ctypes, os

lib = ctypes.CDLL("./TitanVault.dll")  # libTitanVault.so / .dylib on Linux/macOS

# int titan_vault_get_last_error() -> const char*  (do NOT free; static buffer)
lib.titan_vault_get_last_error.restype = ctypes.c_char_p

# void* titan_vault_load_uvf_vault(path, pathLen, pwd, pwdLen, userId, userIdLen)
lib.titan_vault_load_uvf_vault.restype = ctypes.c_void_p
lib.titan_vault_load_uvf_vault.argtypes = [
    ctypes.c_char_p, ctypes.c_int,   # vault path (UTF-8) + length
    ctypes.c_char_p, ctypes.c_int,   # password (UTF-8) + length
    ctypes.c_char_p, ctypes.c_int,   # user id (UTF-8) + length (may be NULL/0)
]

# int titan_vault_read_file(handle, path, pathLen, buffer, &bufferSize)
lib.titan_vault_read_file.restype = ctypes.c_int
lib.titan_vault_read_file.argtypes = [
    ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int,
    ctypes.c_void_p, ctypes.POINTER(ctypes.c_int),
]
lib.titan_vault_close_vault.argtypes = [ctypes.c_void_p]

def utf8(s):  # helper: bytes + length
    b = s.encode("utf-8")
    return b, len(b)

vault_path, vpl = utf8("/path/to/vault")
pwd, pl         = utf8("correct horse battery staple")

handle = lib.titan_vault_load_uvf_vault(vault_path, vpl, pwd, pl, None, 0)
if not handle:
    raise RuntimeError(lib.titan_vault_get_last_error().decode("utf-8"))

try:
    path, pthl = utf8("/documents/report.txt")
    size = ctypes.c_int(1024 * 1024)        # ask for up to 1 MiB
    buf  = ctypes.create_string_buffer(size.value)
    rc = lib.titan_vault_read_file(handle, path, pthl, buf, ctypes.byref(size))
    if rc != 0:                              # 0 == TITAN_VAULT_SUCCESS
        raise RuntimeError(lib.titan_vault_get_last_error().decode("utf-8"))
    data = buf.raw[:size.value]             # decrypted bytes
    print(f"Read {len(data)} bytes")
finally:
    lib.titan_vault_close_vault(handle)
```

#### Node.js (ffi-napi)

```js
const ffi = require('ffi-napi');
const ref = require('ref-napi');

const lib = ffi.Library('./TitanVault', {
  // returnType, [argTypes]
  'titan_vault_load_uvf_vault': ['pointer', ['string','int','string','int','string','int']],
  'titan_vault_read_file':      ['int',     ['pointer','string','int','pointer','pointer']],
  'titan_vault_close_vault':    ['int',     ['pointer']],
  'titan_vault_get_last_error': ['string',  []],
});

const u8 = (s) => Buffer.byteLength(s, 'utf8');           // UTF-8 byte length
const vaultPath = '/path/to/vault';
const password  = 'correct horse battery staple';

const handle = lib.titan_vault_load_uvf_vault(vaultPath, u8(vaultPath), password, u8(password), null, 0);
if (handle.isNull()) throw new Error(lib.titan_vault_get_last_error());

try {
  const filePath = '/documents/report.txt';
  const buf  = Buffer.alloc(1024 * 1024);
  const size = ref.alloc('int', buf.length);             // in/out buffer size
  const rc = lib.titan_vault_read_file(handle, filePath, u8(filePath), buf, size);
  if (rc !== 0) throw new Error(lib.titan_vault_get_last_error());
  const data = buf.subarray(0, size.deref());            // decrypted bytes
  console.log(`Read ${data.length} bytes`);
} finally {
  lib.titan_vault_close_vault(handle);
}
```

The same pattern applies to other FFI hosts — PHP FFI, Ruby Fiddle/FFI, Go cgo,
Java FFM/JNA, Rust `libloading`: declare the `titan_vault_*` signatures, pass
strings as UTF-8 pointer + length, check the `int`/handle result, and free returned
strings with `titan_vault_free_string`. For files larger than available memory, use
the streaming functions (`titan_vault_open_read_stream` / `..._stream_read` / etc.)
instead of `titan_vault_read_file`.

## Conclusion

The Uvf.Net library provides powerful, standards-based encryption for files, compatible with the Uvf and Cryptomator ecosystems. By following this documentation, you can integrate secure, client-side encryption into your .NET applications — either as a managed library or as a native AOT component callable from other languages.
