# UvfLib Demos

Runnable examples that create a vault, encrypt files into it, read them back as cleartext, and
delete them — for both **UVF** (Universal Vault Format v3) and **Cryptomator** (vault format v8).

UvfLib can be consumed two ways, and there is a demo for each:

| Folder | Uses | How |
|--------|------|-----|
| [`DotNet/`](DotNet/) | the **managed** `UvfLib` NuGet package | C# / .NET, no native DLL |
| [`Python/`](Python/) | the **native AOT** library (`TitanVault`) | `ctypes` (stdlib) |
| [`NodeJs/`](NodeJs/) | the **native AOT** library (`TitanVault`) | [`koffi`](https://koffi.dev) |
| [`Java/`](Java/) | the **native AOT** library (`TitanVault`) | [JNA](https://github.com/java-native-access/jna) |

## Building the native library (for the Python / Node.js / Java demos)

The non-.NET demos call the native `TitanVault` shared library through its C ABI. Build it once from
the repository root for your platform:

```bash
dotnet publish Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release -r win-x64 -p:PublishAot=true
# -> TitanVault.dll (Windows). Use -r linux-x64 / osx-arm64 etc. for libTitanVault.so / .dylib
```

Then point each demo at the produced file with `--lib <path>` (or the `TITANVAULT_LIB` env var). The
`.NET` demo needs none of this — it uses the managed package.

## What each demo does (same flow everywhere)

1. Report the library version.
2. Create a vault (UVF or Cryptomator) in a temp folder and open it.
3. **Encrypt:** write `/hello.txt` into the vault.
4. **Decrypt:** read it back and verify it matches the original cleartext.
5. Show that the **backing folder contains only ciphertext** (the plaintext name `hello.txt` never
   appears on disk).
6. Check existence, then delete the file.

> The C ABI used by the native demos exposes file/dir operations (create/open vault, read/write/delete,
> exists, streams) but **not** directory listing; the managed `.NET` demo additionally lists a
> directory via `ReadDirAsync` to show decrypted names.

See each folder's `README.md` for exact run commands.
