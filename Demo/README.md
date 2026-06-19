# UvfLib Demos

Runnable examples that create a vault, encrypt files into it, read them back as cleartext, and
delete them — for both **UVF** (Universal Vault Format v3) and **Cryptomator** (vault format v8).

UvfLib can be consumed two ways — the **managed** NuGet package (C#) or the **native AOT** `TitanVault`
C ABI (everything else). Every demo runs the same full flow (all 46 functions for the native ones) and
the same `--format/--benchmark/--size/--cryptomator-interop` switches:

| Folder | Uses | How (FFI) |
|--------|------|-----------|
| [`DotNet/`](DotNet/) | the **managed** `UvfLib` NuGet package | C# / .NET, no native DLL |
| [`NodeJs/`](NodeJs/) | the native `TitanVault` C ABI | [`koffi`](https://koffi.dev) — **the reference demo** |
| [`Python/`](Python/) | the native `TitanVault` C ABI | `ctypes` (stdlib) |
| [`Java/`](Java/) | the native `TitanVault` C ABI | [JNA](https://github.com/java-native-access/jna) |
| [`Cpp/`](Cpp/) | the native `TitanVault` C ABI | `LoadLibrary`/`dlopen` (runtime), header via `decltype` |
| [`Rust/`](Rust/) | the native `TitanVault` C ABI | [`libloading`](https://crates.io/crates/libloading) |
| [`Go/`](Go/) | the native `TitanVault` C ABI | [`purego`](https://github.com/ebitengine/purego) (no cgo) |
| [`Swift/`](Swift/) | the native `TitanVault` C ABI | C module map + `dlopen` (macOS/Linux) |
| [`Dart/`](Dart/) | the native `TitanVault` C ABI | `dart:ffi` |
| [`Php/`](Php/) | the native `TitanVault` C ABI | PHP `FFI` (WSL/Linux) |

The native demos all consume the canonical C header
[`../Bindings/include/titan_vault.h`](../Bindings/include/titan_vault.h) — the single source of truth.

The native demos all call the same flat C ABI (`titan_vault_*`, cdecl). The canonical C header is
[`../Bindings/include/titan_vault.h`](../Bindings/include/titan_vault.h) — the single source of truth
for every binding. **[`NodeJs/vault-demo.js`](NodeJs/vault-demo.js) is the reference demo**: it
exercises the whole API surface, and the other languages mirror it.

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
`linux-x64,osx-arm64`).

> **Native AOT needs a C/C++ toolchain** (the platform linker). A plain managed build won't work for
> FFI — the demos need the *native* library. The `.NET` demo needs none of this (managed package).

## ABI conventions (every binding follows these)

- **Strings cross as a UTF-8 byte pointer + an explicit byte length** — never NUL-terminated input.
- **`0` = success; negative = error** (codes in the header); call `titan_vault_get_last_error()` for the
  message. Functions returning a handle return `NULL` on failure.
- **In/out size args** (`int*`): pass the buffer capacity; on `INSUFFICIENT_BUFFER` the required size is
  written back — grow and retry (`read_file`, `generate_user_keypair`).
- **`list_directory` / `get_vault_users`** fill a caller-allocated `char*[]`; the count is the return
  value; **free each returned string** with `titan_vault_free_string`.
- **Heap strings** (`get_version`, `get_last_error`, `read_all_text`) and all the array entries above
  must be released with `titan_vault_free_string`. **Handles** are closed with `close_vault` /
  `close_stream`. **Stream offsets/lengths are 64-bit.**
- The native binary must match the **calling process's architecture** (an x64 runtime needs the
  `win-x64` build, not `win-arm64` — relevant under Windows-on-ARM emulation).

## Command-line options (all native demos)

The Python, Node.js, Java, C/C++, Rust and Go demos take the **same switches** — only the launcher
differs (`python vault_demo.py …`, `node vault-demo.js …`, `mvn … -Dexec.args="…"`, the compiled
`vault_demo` exe, `cargo run -- …`, `go run . …`):

| Switch | Meaning |
|--------|---------|
| `--lib <path>` | Use this native library explicitly. Needed when it isn't in one of the auto-searched locations below. (Same as the `TITANVAULT_LIB` env var.) |
| `--format uvf` \| `cryptomator` | Run just one format's functional sections (default: **both**). |
| `--benchmark` (`--bench`) | Throughput benchmark only (default **1 GB**). |
| `--size <GB>` | Benchmark dataset size, e.g. `--size 10`. A size **larger than your RAM** gives disk-bound numbers (otherwise the disk rows mostly reflect the OS cache). |
| `--cryptomator-interop` (`--interop`) | Only unlock the bundled real Cryptomator vault and verify its files. |
| `--vault <path>` | Where to create the scratch demo vault (default: a temp dir). |
| `--password <pw>` | Passphrase for the scratch vault. |

With no arguments the demo runs **everything**: both formats' sections, the real-Cryptomator interop,
and a quick benchmark.

### How the library is found (when you don't pass `--lib`)

In order, first hit wins:

1. **`--lib <path>`** (explicit)
2. **`TITANVAULT_LIB`** environment variable
3. **the demo's own folder** — drop `TitanVault.dll` / `libTitanVault.so` / `libTitanVault.dylib` next to the demo for the simplest setup
4. **the current working directory**
5. **`../../Dist/Native/<rid>/`** — where `build.ps1`/`build.sh -Task aot` writes it

`<rid>` is your OS + the **process** architecture (e.g. `win-x64`); the binary must match it (an x64
runtime needs the `win-x64` build, not `win-arm64` — relevant under Windows-on-ARM emulation).

## The full native C ABI — all 46 functions

"Demoed" = exercised by [`NodeJs/vault-demo.js`](NodeJs/vault-demo.js) (the reference); the
Python/Java/… ports mirror it.

### Library & utility
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_get_version` | Library version string (heap; free it) | ✓ |
| `titan_vault_get_last_error` | Last error message for the calling thread | ✓ |
| `titan_vault_free_string` | Release a heap string returned by the library | ✓ |
| `titan_vault_secure_zero_memory` | Wipe a sensitive buffer (passwords/keys) | ✓ |
| `titan_vault_detect_vault_format` | Detect UVF vs Cryptomator at a path | ✓ |

### Vault lifecycle — Cryptomator
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_create_cryptomator_vault` | Create a Cryptomator v8 vault | ✓ |
| `titan_vault_load_cryptomator_vault` | Open a Cryptomator vault → handle | ✓ |
| `titan_vault_change_cryptomator_password` | Change the vault password | ✓ |

### Vault lifecycle — UVF
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_create_uvf_vault` | Create a UVF vault (filename-enc + KDF options) | ✓ |
| `titan_vault_load_uvf_vault` | Open a UVF vault (admin or, with userId, a member) → handle | ✓ |
| `titan_vault_change_uvf_admin_password` | Change the admin password | ✓ |

### Multi-user — password recipients (UVF)
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_add_user` | Add a password-based member | ✓ |
| `titan_vault_remove_user` | Remove a member (revoke future access) | ✓ |
| `titan_vault_change_uvf_user_password` | Change a member's password (admin-driven) | ✓ |
| `titan_vault_get_vault_users` | List members | ✓ |

### Multi-user — public-key recipients (UVF, Cryptomator Hub-style)
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_generate_user_keypair` | Generate a key pair (public + password-wrapped private) | ✓ |
| `titan_vault_add_user_by_public_key` | Grant access by public key (no member password) | ✓ |
| `titan_vault_load_uvf_vault_with_key` | Open the vault with a private key | ✓ |
| `titan_vault_rotate_keys_pubkey` | Rotate keys for public-key members (admin alone) | ✓ |

### Key management
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_rotate_keys` | Add a new key generation (admin-only vault) | ✓ |

### File operations
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_write_file` | Write a whole file (bytes) | ✓ |
| `titan_vault_read_file` | Read a whole file (in/out size buffer) | ✓ |
| `titan_vault_file_exists` | Test file existence | ✓ |
| `titan_vault_delete_file` | Delete a file | ✓ |
| `titan_vault_move` | Move/rename a file or directory | ✓ |

### Text convenience (UTF-8)
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_write_all_text` | Write a UTF-8 string (overwrite) | ✓ |
| `titan_vault_append_all_text` | Append a UTF-8 string | ✓ |
| `titan_vault_read_all_text` | Read a file as a UTF-8 string (heap; free it) | ✓ |

### Directory operations
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_create_directory` | Create a directory (and parents) | ✓ |
| `titan_vault_directory_exists` | Test directory existence | ✓ |
| `titan_vault_delete_directory` | Delete a directory | ✓ |
| `titan_vault_list_directory` | List entries (→ `char*[]`) | ✓ |
| `titan_vault_get_file_info` | Size + last-modified for an entry | ✓ |

### Streams (random access, large files)
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_open_read_stream` | Open a file for reading → stream | ✓ |
| `titan_vault_open_write_stream` | Open a file for writing → stream | ✓ |
| `titan_vault_open_stream_with_flags` | Open with explicit flags (read/write/append/…) | ✓ |
| `titan_vault_stream_read` | Read bytes from a stream | ✓ |
| `titan_vault_stream_write` | Write bytes to a stream | ✓ |
| `titan_vault_stream_seek` | Seek (begin/current/end) | ✓ |
| `titan_vault_stream_get_position` | Current offset | ✓ |
| `titan_vault_stream_get_length` | Total length | ✓ |
| `titan_vault_stream_set_length` | Truncate/extend (backend-dependent) | ✓ |
| `titan_vault_stream_flush` | Flush pending writes | ✓ |
| `titan_vault_close_stream` | Close a stream | ✓ |

### Handle & maintenance
| Function | Purpose | Demoed |
|----------|---------|:------:|
| `titan_vault_close_vault` | Close a vault handle | ✓ |
| `titan_vault_backup_files` | Copy the vault's config/key files to a backup dir | ✓ |

## What the reference demo runs

`node vault-demo.js` (no args) runs, for **both** formats, sections that each print
`… tests for <FORMAT>: PASSED/FAILED`: **File**, **Text helpers**, **Directory**, **Streaming**,
**Persistence**, plus **UVF-only** key rotation, password & public-key multi-user, and a
**Maintenance** section (password changes, `backup_files`, `detect_vault_format`). It then unlocks a
**real Cryptomator vault** and byte-verifies 3 files, and runs a quick throughput **benchmark**. See
each folder's `README.md` for run commands and flags (`--format`, `--benchmark`, `--cryptomator-interop`).

> Known library limitation surfaced by the demo: opening a vault as a *secondary* (non-admin) UVF user
> is not yet supported (reported, not failed). `rotate_keys` works for admin-only vaults.
