# Java demo (native `TitanVault` via JNA)

Uses [JNA](https://github.com/java-native-access/jna) to call the native `TitanVault` C ABI (no JNI to
write): create a vault, encrypt/decrypt a file, check existence, and delete it. Requires JDK 17+ and
Maven.

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
mvn -q compile exec:java -Dexec.args="--lib ../../Dist/Native/win-x64/TitanVault.dll --format uvf"
mvn -q compile exec:java -Dexec.args="--lib ../../Dist/Native/win-x64/TitanVault.dll --format cryptomator"
```

(`--lib` defaults to `./TitanVault.dll` or the `TITANVAULT_LIB` env var. Alternatively put the native
library on `-Djna.library.path=<dir>` and pass just its base name.)

Strings cross the ABI as UTF-8 `byte[]` **plus an explicit length**; `read_file`'s in/out buffer size
uses `com.sun.jna.ptr.IntByReference`. The version string is released via `titan_vault_free_string`;
`titan_vault_get_last_error()` is a static buffer (not freed).
