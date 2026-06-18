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

The non-.NET demos call the native `TitanVault` shared library through its C ABI. Build it once with
the build script (full reference: [`../BuildScripts/README.md`](../BuildScripts/README.md)):

```powershell
# Windows (PowerShell) — needs VS 2022 Build Tools + the "Desktop development with C++" workload
../BuildScripts/build.ps1 -Task aot
```

```bash
# Linux / macOS — needs clang + build tools (e.g. build-essential, zlib1g-dev, libicu-dev)
../BuildScripts/build.sh --task aot
```

This Native-AOT-publishes `UvfLib.Master` and writes the library to **`Dist/Native/<rid>/`**:
`TitanVault.dll` (Windows) · `libTitanVault.so` (Linux) · `libTitanVault.dylib` (macOS), plus a
`build-manifest.json` with SHA-256 hashes. Add `-Platforms`/`--platforms` for other RIDs (e.g.
`linux-x64,osx-arm64`). Under the hood it runs
`dotnet publish Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release -r <rid> -p:PublishAot=true`.

> **Native AOT needs a C/C++ toolchain** (the platform linker). Without it the link step fails with
> e.g. `link.exe`/`vswhere.exe` not found on Windows. A plain managed build won't work for FFI — the
> demos need the *native* library.

Then point each demo at the produced file with `--lib <path>` (or the `TITANVAULT_LIB` env var). The
`.NET` demo needs none of this — it uses the managed package.

## What each demo does

Every demo reports the library version, then **runs the full flow for both formats** (UVF and
Cryptomator). The core flow: create + open a vault, write a file, read it back as cleartext, verify the
backing folder holds only ciphertext (the plaintext name never appears on disk), then check existence
and delete.

The **Node.js** demo goes further and exercises most of the C ABI, printing a `… tests for <FORMAT>:
PASSED/FAILED` line per area:

- **File** round-trip + filename-leak check.
- **Directory**: create, write into, `list_directory`, `get_file_info`, `move`/rename.
- **Streaming**: write a multi-chunk file via `open_write_stream`/`stream_write`, then random-access
  read with `stream_seek`/`stream_read`.
- **Persistence**: close the vault and reopen it with the passphrase, then re-read.
- **UVF only**: key rotation; password multi-user (`add_user` / `get_vault_users`); and **public-key
  multi-user** — generate a key pair, grant access by public key, open with the private key, and rotate
  the key without any member's password (Cryptomator Hub-style).

> The C ABI exposes the full surface — including `titan_vault_list_directory` (the native demos *do*
> list directories). Two current library limitations the Node demo surfaces: `rotate_keys` reports
> "not implemented", and opening a vault as a *secondary* (non-admin) UVF user fails.

See each folder's `README.md` for exact run commands.
