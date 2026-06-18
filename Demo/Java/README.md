# Java demo (native `TitanVault` via JNA)

Uses [JNA](https://github.com/java-native-access/jna) to call the native `TitanVault` C ABI (no JNI to
write). This is a **full-parity port of [`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js)**: it runs
the same sections, prints the same `… tests for <FORMAT>: PASSED/FAILED` lines, honors the same flags,
and (with no arguments) runs everything — both formats' functional sections (**File**, **Directory**,
**Streaming**, **Persistence**, and **UVF-only** key rotation, password multi-user, and public-key
multi-user), then the real-Cryptomator-vault **interop** (md5-compares 3 files), then a quick
throughput **benchmark**. Requires JDK 17+ and Maven.

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
mvn -q compile exec:java                                            # FULL run: both formats + interop + benchmark
mvn -q compile exec:java -Dexec.args="--format uvf"                 # one format's functional sections
mvn -q compile exec:java -Dexec.args="--benchmark"                  # throughput only (default 1 GB; --size <GB>)
mvn -q compile exec:java -Dexec.args="--cryptomator-interop"        # real-vault read + md5 verify only
```

(With no `--lib`, the demo auto-resolves `../../Dist/Native/<rid>/TitanVault.dll` from the project dir
for your OS/arch — so run the `aot` build first. Override with `--lib <path>`, the `TITANVAULT_LIB` env
var, or put the library on `-Djna.library.path=<dir>`. With no `--format`, both run.) The native binary
must match your **JVM architecture** (`os.arch`): an x64 JVM needs the `win-x64` build, not `win-arm64`.

Strings cross the ABI as UTF-8 `byte[]` **plus an explicit length**; `read_file`'s in/out buffer size
uses `com.sun.jna.ptr.IntByReference` (grow-and-retry); directory listings come back as a `char*[]`
read via a `Memory` block and freed per entry. The version string is released via
`titan_vault_free_string`. stdout/stderr are forced to UTF-8 so the `✅`/`❌` status emoji print on
legacy Windows codepages.
