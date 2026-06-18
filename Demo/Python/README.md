# Python demo (native `TitanVault` via `ctypes`)

No third-party packages — uses the standard-library `ctypes`. Calls the native `TitanVault` C ABI to
create a vault, encrypt/decrypt a file, check existence, and delete it.

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
python vault_demo.py --lib ../../Dist/Native/win-x64/TitanVault.dll --format uvf
python vault_demo.py --lib ../../Dist/Native/win-x64/TitanVault.dll --format cryptomator
```

(`--lib` defaults to `./TitanVault.dll` or the `TITANVAULT_LIB` env var. `--vault` / `--password`
override the location and passphrase.)

Strings cross the ABI as a UTF-8 byte pointer **plus an explicit length**; `read_file` takes an in/out
buffer-size argument (`ctypes.byref`). Returned strings (e.g. the version) are released with
`titan_vault_free_string`; `titan_vault_get_last_error()` is a static buffer and must not be freed.
