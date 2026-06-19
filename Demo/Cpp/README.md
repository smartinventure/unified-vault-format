# C / C++ demo (native `TitanVault` via `LoadLibrary`/`dlopen`)

Pure C++17 calling the native `TitanVault` **C ABI** — the same flat `titan_vault_*` functions every
other binding uses. This is a **full-parity port of [`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js)**:
same sections, same `… tests for <FORMAT>: PASSED/FAILED` lines, same flags, and (with no args) it runs
everything — both formats' functional sections, the real-Cryptomator-vault interop, and a benchmark.

The library is loaded at **runtime** (`LoadLibrary`/`dlopen`), so no import library is needed — it works
against any prebuilt `TitanVault.{dll,so,dylib}`. Function-pointer types come straight from
[`../../Bindings/include/titan_vault.h`](../../Bindings/include/titan_vault.h) via `decltype`, so they
can never drift from the ABI.

## 1. Build the native library (once)

```powershell
../../BuildScripts/build.ps1 -Task aot      # Windows  -> Dist/Native/win-x64/TitanVault.dll
```
```bash
../../BuildScripts/build.sh --task aot       # Linux/macOS -> Dist/Native/<rid>/libTitanVault.{so,dylib}
```

## 2. Build the demo

**CMake (any platform):**
```bash
cmake -S . -B build
cmake --build build --config Release
```

**Or compile directly:**
```bash
# Linux / macOS
g++  -std=c++17 -O2 vault_demo.cpp -I ../../Bindings/include -ldl -o vault_demo      # (clang++ works too)
# macOS uses -ldl implicitly; drop it if your toolchain complains.

# Windows (from a "x64 Native Tools Command Prompt", or after running vcvarsall)
cl /nologo /std:c++17 /EHsc /O2 /Fe:vault_demo.exe vault_demo.cpp /I ..\..\Bindings\include
```

## 3. Run

```bash
./vault_demo                      # FULL run: both formats + real-Cryptomator interop + a quick benchmark
./vault_demo --format uvf         # one format's functional sections
./vault_demo --benchmark --size 2 # throughput only, 2 GB
./vault_demo --cryptomator-interop
```

**Switches** are identical across all demos — see the shared table in
[`../README.md`](../README.md#command-line-options-all-native-demos) (`--lib`, `--format`,
`--benchmark`, `--size <GB>`, `--cryptomator-interop`, `--vault`, `--password`).

Without `--lib`, the library is auto-discovered: `--lib` → `TITANVAULT_LIB` env → **next to the
executable** → current dir → `../../Dist/Native/<rid>/` (the demo walks up from the exe/cwd to find a
`Dist/Native/<rid>/` folder). The binary must match the **executable's** architecture (an x64 build
needs `win-x64`, not `win-arm64`).

### ABI notes
Strings cross as a UTF-8 pointer + explicit byte length; `read_file`'s size arg is in/out (grow + retry);
`list_directory`/`get_vault_users` fill a caller `char*[]` and each entry is freed with
`titan_vault_free_string`; heap strings (`get_version`, `read_all_text`) are freed the same way; stream
offsets are 64-bit; handles close with `close_vault`/`close_stream`. The interop check byte-compares the
decrypted output against the original files (no md5 dependency).
