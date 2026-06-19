# Dart demo (native `TitanVault` via `dart:ffi`)

Pure Dart calling the native `TitanVault` **C ABI** — the same flat `titan_vault_*` functions every
other binding uses, loaded at runtime with [`dart:ffi`](https://dart.dev/guides/libraries/c-interop)
and [`package:ffi`](https://pub.dev/packages/ffi). This is a **full-parity port of
[`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js)**: same sections, same
`… tests for <FORMAT>: PASSED/FAILED` lines, same flags, and (with no args) it runs everything —
both formats' functional sections, the real-Cryptomator-vault interop, and a benchmark.

The library is loaded at **runtime** (`DynamicLibrary.open`), so no link step is needed — it works
against any prebuilt `TitanVault.{dll,so,dylib}`. Each export is bound with `lookupFunction`, with
the native signatures taken from
[`../../Bindings/include/titan_vault.h`](../../Bindings/include/titan_vault.h).

> Verified to compile/run via CI / by the user — there is no Dart SDK on the original authoring
> machine, so the source is written carefully against the canonical header.

## 1. Build the native library (once)

```powershell
../../BuildScripts/build.ps1 -Task aot      # Windows  -> Dist/Native/win-x64/TitanVault.dll
```
```bash
../../BuildScripts/build.sh --task aot       # Linux/macOS -> Dist/Native/<rid>/libTitanVault.{so,dylib}
```

## 2. Install dependencies

```bash
dart pub get
```

## 3. Run

```bash
dart run                                       # FULL run: both formats + interop + a quick benchmark
dart run bin/vault_demo.dart --format uvf      # one format's functional sections
dart run bin/vault_demo.dart --benchmark --size 2   # throughput only, 2 GB
dart run bin/vault_demo.dart --cryptomator-interop
```

(`dart run` with no script argument runs `bin/vault_demo.dart`, the package's default entrypoint.)

**Switches** are identical across all demos — see the shared table in
[`../README.md`](../README.md#command-line-options-all-native-demos) (`--lib`, `--format`,
`--benchmark`, `--size <GB>`, `--cryptomator-interop`, `--vault`, `--password`).

Without `--lib`, the library is auto-discovered, **first hit wins**:

1. **`--lib <path>`** (explicit)
2. **`TITANVAULT_LIB`** environment variable
3. **next to the executable / script** and the **current working directory**
4. **`Dist/Native/<rid>/`** — the demo walks up from the script dir / cwd / exe dir to find a
   `Dist/Native/<rid>/` folder (where `build.ps1`/`build.sh -Task aot` writes the library).

`<rid>` is your OS (`win-`/`osx-`/`linux-`) plus the **process** architecture (`arm64` if
`Abi.current()` reports arm64, else `x64`); the binary must match it (an x64 runtime needs the
`win-x64` build, not `win-arm64`).

### ABI notes
Strings cross as a `Pointer<Uint8>` of UTF-8 bytes + an explicit `int` byte length (a small helper
allocates, copies, and frees each one). `read_file`'s size arg is an in/out `Pointer<Int32>`
(grow + retry). `list_directory` / `get_vault_users` fill a caller `Pointer<Pointer<Utf8>>` array of
length 256 and the count is the return value; each entry is freed with `titan_vault_free_string`.
Heap strings (`get_version`, `read_all_text`) are freed the same way. Stream offsets/lengths are
`Int64`; handles/streams are `Pointer<Void>`, closed with `close_vault` / `close_stream`. Every
`malloc`/`calloc` allocation is released in a `finally` block — `dart:ffi` has no GC for native
memory. The interop check byte-compares the decrypted output against the original files.
