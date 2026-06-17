# Node.js demo (native `TitanVault` via `koffi`)

Uses [`koffi`](https://koffi.dev) (a modern, maintained FFI for Node) to call the native `TitanVault`
C ABI: create a vault, encrypt/decrypt a file, check existence, and delete it.

## 1. Build the native library (once, from the repo root)

```bash
dotnet publish Uvf.Net/UvfLib.Master/UvfLib.Master.csproj -c Release -r win-x64 -p:PublishAot=true
# -> TitanVault.dll  (or libTitanVault.so / .dylib)
```

## 2. Install + run

```bash
npm install
node vault-demo.js --lib /path/to/TitanVault.dll --format uvf
node vault-demo.js --lib /path/to/TitanVault.dll --format cryptomator
```

(`--lib` defaults to `./TitanVault.dll` or the `TITANVAULT_LIB` env var.)

Strings cross the ABI as a UTF-8 pointer **plus an explicit byte length** (`Buffer.byteLength`), and
`read_file`'s buffer-size argument is `_Inout_` (koffi writes the actual size back into the
single-element array you pass).
