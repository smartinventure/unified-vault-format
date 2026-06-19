# Rust demo (native `TitanVault` via `libloading`)

Pure Rust calling the native `TitanVault` **C ABI** — the same flat `titan_vault_*` functions every
other binding uses. This is a **full-parity port of [`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js)**
(and structurally mirrors [`../Cpp/vault_demo.cpp`](../Cpp/vault_demo.cpp)): same sections, same
`… tests for <FORMAT>: PASSED/FAILED` lines, same flags, and (with no args) it runs everything — both
formats' functional sections, the real-Cryptomator-vault interop, and a benchmark.

The library is loaded at **runtime** with the [`libloading`](https://crates.io/crates/libloading)
crate (`Library::new`), so no link-time import library is needed — it works against any prebuilt
`TitanVault.{dll,so,dylib}`. Each export is resolved into a typed function pointer whose signature
matches the canonical header
[`../../Bindings/include/titan_vault.h`](../../Bindings/include/titan_vault.h).

## 1. Build the native library (once)

```powershell
../../BuildScripts/build.ps1 -Task aot      # Windows  -> Dist/Native/<rid>/TitanVault.dll
```
```bash
../../BuildScripts/build.sh --task aot       # Linux/macOS -> Dist/Native/<rid>/libTitanVault.{so,dylib}
```

## 2. Build & run the demo

The only dependency is `libloading` (everything else is std). With Cargo:

```bash
cargo run                          # FULL run: both formats + real-Cryptomator interop + a quick benchmark
cargo run -- --format uvf          # one format's functional sections
cargo run -- --benchmark --size 2  # throughput only, 2 GB
cargo run -- --cryptomator-interop
cargo run -- --lib /path/to/TitanVault.dll
```

Use `cargo run --release` for representative benchmark numbers.

**Switches** are identical across all demos — see the shared table in
[`../README.md`](../README.md#command-line-options-all-native-demos) (`--lib`, `--format`,
`--benchmark`, `--size <GB>`, `--cryptomator-interop`, `--vault`, `--password`).

## How the library is found (when you don't pass `--lib`)

In order, first hit wins:

1. **`--lib <path>`** (explicit)
2. **`TITANVAULT_LIB`** environment variable
3. **next to the executable** (`target/<profile>/`) — drop the DLL there for the simplest setup
4. **the current working directory**
5. **`Dist/Native/<rid>/`** — the demo walks up from both the executable's dir and the cwd looking
   for a `Dist/Native/<rid>/` folder (where `build.ps1`/`build.sh -Task aot` writes the library)

`<rid>` is your OS + the **build** architecture: `win-`/`osx-`/`linux-` + `arm64` (on `aarch64`) or
`x64`. The native binary must match the **executable's** architecture. This crate's build target is
`aarch64-pc-windows-msvc`, so the demo resolves **`win-arm64`** and needs the `win-arm64` DLL — not
`win-x64`.

### ABI notes
Strings cross as a UTF-8 pointer (`s.as_bytes().as_ptr()`) + explicit `i32` byte length; `read_file`'s
size arg is in/out (grow + retry); `list_directory`/`get_vault_users` fill a caller `[*mut c_char; 256]`
and each entry is freed with `titan_vault_free_string` (read via `CStr::from_ptr`); heap strings
(`get_version`, `read_all_text`) are freed the same way; stream offsets/lengths are 64-bit (`i64`);
handles/streams are `*mut c_void` and close with `close_vault`/`close_stream`. The interop check
byte-compares the decrypted output against the original files (no hashing dependency).
