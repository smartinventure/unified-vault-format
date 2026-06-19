# Swift demo (native `TitanVault` via `dlopen`)

Pure Swift calling the native `TitanVault` **C ABI** — the same flat `titan_vault_*` functions every
other binding uses. This is a **full-parity port of [`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js)**
(and structurally mirrors [`../Cpp/vault_demo.cpp`](../Cpp/vault_demo.cpp)): same sections, the same
`… tests for <FORMAT>: PASSED/FAILED` lines, the same flags, and (with no args) it runs everything — both
formats' functional sections, the real-Cryptomator-vault interop, and a quick benchmark.

The native library is loaded at **runtime** with `dlopen`/`dlsym` (from `Glibc` on Linux / `Darwin` on
macOS), so no link-time import library is needed — it works against any prebuilt
`libTitanVault.{so,dylib}`. The C ABI **types** come from the canonical header
[`../../Bindings/include/titan_vault.h`](../../Bindings/include/titan_vault.h) through the `CTitanVault`
module (a thin system-library target wrapping the header); each export is then resolved with `dlsym` and
reinterpreted (`unsafeBitCast`) into a typed `@convention(c)` function pointer.

> **Platform note.** This demo is verified on **macOS and Linux**. A Swift toolchain on Windows-on-ARM
> isn't practical, so there is no Windows build here — use the [.NET](../DotNet), [C/C++](../Cpp),
> [Rust](../Rust), or [Go](../Go) demos on Windows.

## 1. Build the native library (once)

```bash
../../BuildScripts/build.sh --task aot       # Linux/macOS -> Dist/Native/<rid>/libTitanVault.{so,dylib}
```

(The library must exist before the demo can load it. The SwiftPM package itself builds fine without it —
the binding is resolved at runtime — but the program exits early if it can't find the `.so`/`.dylib`.)

## 2. Build & run the demo

The package has no external dependencies (Foundation + the platform C library only). With SwiftPM:

```bash
swift run                              # FULL run: both formats + real-Cryptomator interop + a quick benchmark
swift run vault-demo --format uvf      # one format's functional sections
swift run vault-demo --benchmark --size 2   # throughput only, 2 GB
swift run vault-demo --cryptomator-interop
swift run vault-demo --lib /path/to/libTitanVault.dylib
```

Use `swift run -c release` for representative benchmark numbers.

**Switches** are identical across all demos — see the shared table in
[`../README.md`](../README.md#command-line-options-all-native-demos) (`--lib`, `--format`,
`--benchmark`, `--size <GB>`, `--cryptomator-interop`, `--vault`, `--password`).

## How the library is found (when you don't pass `--lib`)

In order, first hit wins:

1. **`--lib <path>`** (explicit)
2. **`TITANVAULT_LIB`** environment variable
3. **next to the executable** (`.build/<config>/`)
4. **the current working directory**
5. **`Dist/Native/<rid>/`** — the demo walks up from both the executable's dir and the cwd looking
   for a `Dist/Native/<rid>/` folder (where `build.sh --task aot` writes the library)

`<rid>` is your OS + the **build** architecture: `osx-`/`linux-` + `arm64` (on `aarch64`/Apple Silicon)
or `x64`. The native binary must match the executable's architecture.

### ABI notes
Strings cross as a UTF-8 byte pointer (`[UInt8]` / `UnsafePointer<UInt8>`) + explicit `Int32` byte
length; `read_file`'s size arg is in/out (grow + retry); `list_directory` / `get_vault_users` fill a
caller `[UnsafeMutablePointer<CChar>?]` of capacity 256 and each entry is freed with
`titan_vault_free_string` (read via `String(cString:)`); heap strings (`get_version`, `read_all_text`)
are freed the same way; stream offsets/lengths are 64-bit (`Int64`); handles/streams are
`OpaquePointer`/raw pointers and close with `close_vault` / `close_stream`. The interop check
byte-compares the decrypted output against the original files (no hashing dependency).
