# Node.js demo (native `TitanVault` via `koffi`)

Uses [`koffi`](https://koffi.dev) (a modern, maintained FFI for Node) to call the native `TitanVault`
C ABI. This is the **reference demo** — it exercises the **entire 46-function ABI** (see the table in
[`../README.md`](../README.md)). It **runs the full flow for both formats** (UVF then Cryptomator) and
prints a `… tests for <FORMAT>: PASSED/FAILED` line per area: **Detect format**, **File** (+ filename-leak
check), **Text helpers** (`write`/`append`/`read_all_text`), **Directory** (create / `list_directory` /
`get_file_info` / `move`), **Streaming** (multi-chunk write, `stream_seek`/`stream_get_position`
random access, `open_stream_with_flags`, `stream_flush`/`stream_set_length`), **Persistence** (close +
reopen), **Maintenance** (`backup_files`, `secure_zero_memory`, password change + reopen —
`change_uvf_admin_password` / `change_cryptomator_password`), and **UVF-only** key rotation, password
multi-user (`add_user` / `get_vault_users` / `change_uvf_user_password` / `remove_user`), and
**public-key multi-user** (generate a key pair, grant by public key, open with the private key, rotate
with no member password).

## 1. Build the native library (once)

```powershell
../../BuildScripts/build.ps1 -Task aot      # Windows  -> Dist/Native/win-x64/TitanVault.dll
```

```bash
../../BuildScripts/build.sh --task aot       # Linux/macOS -> Dist/Native/<rid>/libTitanVault.{so,dylib}
```

Native AOT needs a C/C++ toolchain — see [`../../BuildScripts/README.md`](../../BuildScripts/README.md).

## 2. Install + run

```bash
npm install
node vault-demo.js                 # FULL run: both formats + real-Cryptomator-vault interop + a quick benchmark
node vault-demo.js --format uvf    # just one format's functional sections (no interop/benchmark)
node vault-demo.js --benchmark              # throughput only (default 1 GB; --size <GB> to change)
node vault-demo.js --cryptomator-interop    # real-Cryptomator-app vault read + md5 verify only
```

(With no `--lib`, the demo auto-resolves `../../Dist/Native/<rid>/TitanVault.dll` for your OS/arch — so
run the `aot` build first. Override with `--lib <path>` or the `TITANVAULT_LIB` env var. With no
`--format`, both run. The native binary must match your **Node architecture** — x64 Node needs the
`win-x64` build, not `win-arm64`.)

Strings cross the ABI as a UTF-8 pointer **plus an explicit byte length** (`Buffer.byteLength`), and
`read_file`'s buffer-size argument is `_Inout_` (koffi writes the actual size back into the
single-element array you pass).
