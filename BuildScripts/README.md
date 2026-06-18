# UvfLib build scripts

One script, two flavors — pick the one for your shell:

- **`build.ps1`** — PowerShell (Windows PowerShell 5.1+ or PowerShell 7+).
- **`build.sh`** — Bash (Linux / macOS).

Both take the same `-Task` / `--task` switch and do the same things.

## Tasks

| Task | What it does |
|------|--------------|
| `test`     | Build the solution and run the test suite (`UvfLib.Tests`). |
| `aot`      | Publish the **native AOT** library `UvfLib.Master` → `TitanVault.{dll,so,dylib}` per platform RID, into `Dist/Native/<rid>/`, with a SHA-256 `build-manifest.json`. |
| `pack`     | Pack the **managed** NuGet packages (`UvfLib.Core`, `.Vault`, `.Master`) into `Dist/Packages/nuget/`. |
| `bindings` | Generate language-binding packages (delegates to `Scripts/package-bindings.ps1`; needs `pwsh` + a prior `aot` run). |
| `clean`    | Delete `Dist/` and all `bin`/`obj` under `Uvf.Net/`. |
| `all`      | `clean` (if requested) → `test` (unless skipped) → `aot` → `pack`. |

## Usage

```powershell
# PowerShell
./build.ps1 -Task aot
./build.ps1 -Task aot -Platforms win-x64,linux-x64
./build.ps1 -Task pack
./build.ps1 -Task test
./build.ps1 -Task all -Clean
```

```bash
# Bash
./build.sh --task aot
./build.sh --task aot --platforms linux-x64,linux-arm64
./build.sh --task pack
./build.sh --task all --clean
```

### Parameters

| PowerShell | Bash | Default | Notes |
|------------|------|---------|-------|
| `-Task`          | `--task`          | `aot` | One of the tasks above. |
| `-Platforms`     | `--platforms`     | host RID | Comma-separated RIDs for `aot` (e.g. `win-x64,linux-x64,osx-arm64`). |
| `-Configuration` | `--configuration` | `Release` | `Debug` or `Release`. |
| `-Languages`     | `--languages`     | `CSharp,Python,NodeJs` | For the `bindings` task. |
| `-SkipTests`     | `--skip-tests`    | off | Skip tests in the `all` task. |
| `-Clean`         | `--clean`         | off | Clean first (for non-`clean` tasks). |

The scripts resolve all paths from their own location, so they work from any working directory.

## Prerequisites

- **.NET 8 SDK** (all tasks).
- **`aot` only — a native C/C++ toolchain** (Native AOT links a real binary):
  - **Windows:** Visual Studio 2022 **Build Tools** with the *Desktop development with C++* workload
    (the MSVC linker `link.exe`). Without it the link step fails with `vswhere.exe` / `link.exe` not found.
  - **Linux:** `clang` + build/dev packages, e.g. `sudo apt-get install clang build-essential zlib1g-dev libicu-dev`.
  - **macOS:** Xcode command-line tools (`xcode-select --install`).
- **`bindings` only:** PowerShell 7+ (`pwsh`).

## Output

```
Dist/
├─ Native/
│  ├─ <rid>/TitanVault.{dll,so,dylib}   # native AOT library (aot task)
│  └─ build-manifest.json               # RIDs + SHA-256
└─ Packages/
   └─ nuget/UvfLib.{Core,Vault,Master}.<version>.nupkg   # managed packages (pack task)
```

## Notes

- `UvfLib.Master`'s assembly is already named **TitanVault**, so the AOT output needs no renaming.
- `Master` is configured to build native AOT in Release; `test` and `pack` pass overrides
  (`-p:PublishAot=false …`) so they get the plain **managed** assembly. The `aot` task sets
  `-p:PublishAot=true` to get the native one.
- `Scripts/package-bindings.ps1` is the only remaining helper script; the `bindings` task calls it.
  (It replaced the old `build-all/aot/clean/master/simple/windows/titanvault-aot` + `resolve-storagelib`
  scripts, which were redundant or stale.)
