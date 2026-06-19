# Go demo (native `TitanVault` via `purego`, no cgo)

Pure Go calling the native `TitanVault` **C ABI** — the same flat `titan_vault_*` functions every other
binding uses. This is a **full-parity port of [`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js)**
(and its C++ twin [`../Cpp/vault_demo.cpp`](../Cpp/vault_demo.cpp)): same sections, same
`… tests for <FORMAT>: PASSED/FAILED` lines, same flags, and (with no args) it runs everything — both
formats' functional sections, the real-Cryptomator-vault interop, and a benchmark.

The library is loaded at **runtime** via [`purego`](https://github.com/ebitengine/purego)
(`purego.Dlopen` → `LoadLibrary`/`dlopen`), so **no cgo and no C compiler** are needed — build with
`CGO_ENABLED=0`. Each export is bound to a typed Go func with `purego.RegisterLibFunc`, so the binding
works against any prebuilt `TitanVault.{dll,so,dylib}`. The canonical signatures are in
[`../../Bindings/include/titan_vault.h`](../../Bindings/include/titan_vault.h).

## 1. Build the native library (once)

```powershell
../../BuildScripts/build.ps1 -Task aot      # Windows  -> Dist/Native/win-x64/TitanVault.dll
```
```bash
../../BuildScripts/build.sh --task aot       # Linux/macOS -> Dist/Native/<rid>/libTitanVault.{so,dylib}
```

## 2. Run

No build step is required — `go run` compiles and runs in one shot (purego needs no C toolchain):

```bash
go run .                      # FULL run: both formats + real-Cryptomator interop + a quick benchmark
go run . --format uvf         # one format's functional sections
go run . --benchmark --size 2 # throughput only, 2 GB
go run . --cryptomator-interop
```

Or build a standalone binary:

```bash
go build -o vault-demo .      # CGO_ENABLED=0 by default; purego needs no C compiler
./vault-demo
```

`go.mod` requires **Go 1.21+** and `github.com/ebitengine/purego` (v0.8.x). The build host targeted here
is **windows/amd64** with `CGO_ENABLED=0`. On first run, `go` fetches `purego` automatically (or run
`go mod download`).

## 3. Command-line options

**Switches** are identical across all native demos — see the shared table in
[`../README.md`](../README.md#command-line-options-all-native-demos) (`--lib`, `--format`,
`--benchmark`, `--size <GB>`, `--cryptomator-interop`/`--interop`, `--vault`, `--password`).

## Library discovery

Without `--lib`, the library is auto-discovered with this precedence:

1. **`--lib <path>`** (highest)
2. **`TITANVAULT_LIB`** environment variable
3. **search**: next to the executable → current directory → walking up from both the exe dir and the
   cwd looking for a `Dist/Native/<rid>/<file>` folder.

`<file>` is chosen by `runtime.GOOS`: `TitanVault.dll` (windows) · `libTitanVault.so` (linux) ·
`libTitanVault.dylib` (darwin). `<rid>` is the OS prefix (`win-`/`linux-`/`osx-`) plus the architecture
(`arm64` when `runtime.GOARCH == "arm64"`, otherwise `x64`).

> **GOARCH → DLL note:** the resolver maps Go's `GOARCH` to the RID's architecture token. On the standard
> **windows/amd64** build host, `runtime.GOARCH` is `amd64`, which maps to **`x64`** → it loads
> `Dist/Native/win-x64/TitanVault.dll`. The loaded library must match the Go binary's architecture (an
> amd64 build needs `win-x64`, not `win-arm64`).

## ABI / purego mapping notes

The `purego.RegisterLibFunc` typed funcs map the C ABI as follows:

- **UTF-8 string + length** → `[]byte` (purego passes the slice's data pointer) + an `int32` byte length.
  The optional `userId` is passed as `nil` + `0`.
- **Output byte buffers** → `[]byte`.
- **in/out size (`int*`)** → `*int32` (`read_file` grows + retries on a too-small buffer).
- **int64 stream offsets/lengths** → `int64`.
- **handles / streams (`void*`)** → `uintptr` (close with `close_vault` / `close_stream`).
- **returned `char*`** (`get_version`, `get_last_error`, `read_all_text`) → `uintptr`, read with a small
  `unsafe` NUL-terminated copy helper; heap strings are freed with `free_string` (the static
  `get_last_error` buffer is **not** freed).
- **`char*[]`** (`list_directory`, `get_vault_users`) → a `[]uintptr` of length 256 (its data pointer is
  the `char*[]`) + an `int32` capacity; the return value is the count, and each entry is read then freed
  with `free_string`.

The interop check byte-compares the decrypted output against the original files (no md5 dependency).
