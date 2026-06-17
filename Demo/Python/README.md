# Python demo (native `TitanVault` via `ctypes`)

No third-party packages — uses the standard-library `ctypes`. Calls the native `TitanVault` C ABI to
create a vault, encrypt/decrypt a file, check existence, and delete it.

## 1. Build the native library (once, from the repo root)

```bash
dotnet publish Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release -r win-x64 -p:PublishAot=true
# -> bin/Release/net8.0/win-x64/publish/TitanVault.dll  (or libTitanVault.so / .dylib)
```

## 2. Run

```bash
python vault_demo.py --lib /path/to/TitanVault.dll --format uvf
python vault_demo.py --lib /path/to/TitanVault.dll --format cryptomator
```

(`--lib` defaults to `./TitanVault.dll` or the `TITANVAULT_LIB` env var. `--vault` / `--password`
override the location and passphrase.)

Strings cross the ABI as a UTF-8 byte pointer **plus an explicit length**; `read_file` takes an in/out
buffer-size argument (`ctypes.byref`). Returned strings (e.g. the version) are released with
`titan_vault_free_string`; `titan_vault_get_last_error()` is a static buffer and must not be freed.
