# PHP demo (native `TitanVault` via the `FFI` extension)

No third-party packages — uses PHP's built-in [`FFI`](https://www.php.net/manual/en/book.ffi.php)
extension to call the native `TitanVault` **C ABI** (the same flat `titan_vault_*` functions every
other binding uses). This is a **full-parity port of [`../NodeJs/vault-demo.js`](../NodeJs/vault-demo.js)**:
it runs the same sections, prints the same `… tests for <FORMAT>: PASSED/FAILED` lines, honors the same
flags, and (with no arguments) runs everything — both formats' functional sections (**Detect format**,
**File** + filename-leak check, **Text helpers**, **Directory**, **Streaming**, **Persistence**,
**Maintenance**, and **UVF-only** key rotation, password multi-user, and public-key multi-user), then
the real-Cryptomator-vault **interop** (md5-compares 3 files), then a quick throughput **benchmark**.

> **Intended for WSL / Linux.** PHP's `FFI` works on any platform, but this demo is meant to be run
> from a Linux shell (e.g. WSL) against the Linux `libTitanVault.so` build.

## Requirements

- **PHP 7.4 or newer** (8.x recommended) with the **FFI extension enabled**. FFI is bundled with PHP
  but disabled by default, so either set `ffi.enable=1` in your `php.ini`, or pass it on the command
  line:
  ```bash
  php -d ffi.enable=1 vault_demo.php
  ```
  Check that FFI is available with `php -m | grep -i ffi`. On Debian/Ubuntu (WSL) install it with
  `sudo apt install php-cli php-ffi`.

## 1. Build the native library (once)

In **WSL/Linux**, from the repo root:

```bash
../../BuildScripts/build.sh --task aot       # Linux  -> Dist/Native/linux-x64/libTitanVault.so
```

On Windows (if you build there instead):

```powershell
../../BuildScripts/build.ps1 -Task aot       # Windows -> Dist/Native/win-x64/TitanVault.dll
```

Native AOT needs a C/C++ toolchain — see [`../../BuildScripts/README.md`](../../BuildScripts/README.md).

## 2. Run

```bash
php vault_demo.php                 # FULL run: both formats + real-Cryptomator-vault interop + a quick benchmark
php vault_demo.php --format uvf    # just one format's functional sections (no interop/benchmark)
php vault_demo.php --benchmark              # throughput only (default 1 GB; --size <GB> to change)
php vault_demo.php --benchmark --size 2     # throughput only, 2 GB
php vault_demo.php --cryptomator-interop    # real-Cryptomator-app vault read + md5 verify only
```

If FFI is disabled in your `php.ini`, prefix any of the above with `-d ffi.enable=1`, e.g.
`php -d ffi.enable=1 vault_demo.php`.

**Switches** (`--lib`, `--format`, `--benchmark`, `--size <GB>`, `--cryptomator-interop`, `--vault`,
`--password`) are identical across all demos — see the shared table in
[`../README.md`](../README.md#command-line-options-all-native-demos). With no `--format`, both formats
run.

## Library discovery

Without `--lib`, the library is auto-discovered in this order (first match wins):

1. **`--lib <path>`** (explicit)
2. **`TITANVAULT_LIB`** environment variable
3. **this folder** (drop the library next to `vault_demo.php`)
4. the **current working directory**
5. walking **up** from this script's directory and the cwd, looking for
   `Dist/Native/<rid>/<library>` (the usual location after a build)

The library file is chosen by `PHP_OS_FAMILY` (Windows → `TitanVault.dll`, macOS → `libTitanVault.dylib`,
otherwise → `libTitanVault.so`), and `<rid>` is the OS prefix (`linux-`/`win-`/`osx-`) plus the
architecture from `php_uname('m')` (`aarch64`/`arm64` → `arm64`, otherwise `x64`) — e.g.
`linux-x64`. The native binary must match the **PHP interpreter's** architecture.

## ABI notes

- Strings cross the ABI as a **UTF-8 byte pointer + an explicit byte length** (`strlen`, since PHP
  strings are byte strings). A small `buf()` helper copies the bytes into an `FFI::new("unsigned
  char[N]")` buffer (kept referenced for the duration of the call) and passes it (the array decays to a
  pointer) plus the length.
- `read_file`'s buffer-size argument is **in/out** (`int*`): we pass `FFI::addr($size)` and grow-and-retry
  on `INSUFFICIENT_BUFFER`.
- 64-bit stream offsets/lengths (`stream_seek`, `stream_get_position`, `stream_get_length`,
  `stream_set_length`, `get_file_info`) are declared as **`long long`** in the cdef; PHP's native `int`
  maps to it directly on 64-bit builds.
- `list_directory` / `get_vault_users` fill a caller `FFI::new("char*[256]")` plus an `int*` capacity;
  the return value is the count, each entry is read with `FFI::string()` and freed with
  `titan_vault_free_string`. Heap strings (`get_version`, `read_all_text`, `get_last_error`) are freed
  the same way.
- Handles and streams are opaque `void*`; close them with `close_vault` / `close_stream`.

The function prototypes are transcribed (clean — no `#define`, comments, `#include`, or `extern "C"`,
which `FFI::cdef` cannot parse) into a single cdef string at the top of `vault_demo.php`. The interop
check md5-compares the decrypted output against the original files in
`Demo/_test-cryptomator-vault/original-files/`.
