# Python demo (native `TitanVault` via `ctypes`)

No third-party packages — uses the standard-library `ctypes`. This is a **full-parity port of
[`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js)**: it runs the same sections, prints the same
`… tests for <FORMAT>: PASSED/FAILED` lines, honors the same flags, and (with no arguments) runs
everything — both formats' functional sections (**File**, **Directory**, **Streaming**,
**Persistence**, and **UVF-only** key rotation, password multi-user, and public-key multi-user), then
the real-Cryptomator-vault **interop** (md5-compares 3 files), then a quick throughput **benchmark**.

## 1. Build the native library (once)

```powershell
../../BuildScripts/build.ps1 -Task aot      # Windows  -> Dist/Native/win-x64/TitanVault.dll
```

```bash
../../BuildScripts/build.sh --task aot       # Linux/macOS -> Dist/Native/<rid>/libTitanVault.{so,dylib}
```

Native AOT needs a C/C++ toolchain — see [`../../BuildScripts/README.md`](../../BuildScripts/README.md).

## 2. Run

```bash
python vault_demo.py                 # FULL run: both formats + real-Cryptomator-vault interop + a quick benchmark
python vault_demo.py --format uvf    # just one format's functional sections (no interop/benchmark)
python vault_demo.py --benchmark              # throughput only (default 1 GB; --size <GB> to change)
python vault_demo.py --cryptomator-interop    # real-Cryptomator-app vault read + md5 verify only
```

(With no `--lib`, the demo auto-resolves `../../Dist/Native/<rid>/TitanVault.dll` for your OS/arch — so
run the `aot` build first. Override with `--lib <path>` or the `TITANVAULT_LIB` env var. With no
`--format`, both run.)

The native binary must match the **Python interpreter's** architecture. On **Windows-on-ARM** an x64
(emulated) Python reports the *host* arch via `platform.machine()` (`ARM64`), so the demo instead reads
`PROCESSOR_ARCHITECTURE` (the *process* arch, `AMD64`) to pick the right `win-x64` build.

Strings cross the ABI as a UTF-8 byte pointer **plus an explicit length**; `read_file` takes an in/out
buffer-size argument (`ctypes.byref`, grow-and-retry). Returned heap strings (e.g. the version) are
released with `titan_vault_free_string`. stdout/stderr are forced to UTF-8 so the `✅`/`❌` status
emoji print on legacy Windows codepages.
