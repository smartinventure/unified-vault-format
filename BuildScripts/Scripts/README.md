# BuildScripts/Scripts

The build entry point now lives one level up: use **`../build.ps1`** (PowerShell) or **`../build.sh`**
(Bash). See [`../README.md`](../README.md) for tasks and options.

This folder retains one helper:

- **`package-bindings.ps1`** — generates language-binding packages (NuGet / PyPI / NPM) from the native
  AOT libraries in `Dist/Native/`. It is invoked by the `bindings` task (`../build.ps1 -Task bindings`),
  or can be run directly:

  ```powershell
  ./package-bindings.ps1 -Languages "CSharp,Python,NodeJs"
  ```

The previous per-purpose scripts (`build-all`, `build-aot`, `build-clean`, `build-master`,
`build-simple`, `build-windows`, `build-titanvault-aot`, `resolve-storagelib-simple`) were consolidated
into `../build.ps1` / `../build.sh` and removed (several were redundant or stale — e.g. they targeted a
non-existent `UvfLib.Storage` project or a machine-hardcoded StorageLib path).
