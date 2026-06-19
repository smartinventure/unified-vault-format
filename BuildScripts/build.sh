#!/usr/bin/env bash
# One build script for UvfLib, with task switches (bash port of build.ps1 for Linux/macOS).
#
# Tasks (--task):
#   test     Build the solution and run the test suite.
#   aot      Publish the native AOT library (UvfLib.Master -> libTitanVault.{so,dylib}) per --platforms RID,
#            into Dist/Native/<rid>/, with a SHA-256 manifest. Needs a native toolchain: clang + the
#            usual build/dev packages (e.g. build-essential, zlib1g-dev, libicu-dev on Debian/Ubuntu;
#            Xcode command line tools on macOS).
#   pack     Pack the managed NuGet packages (UvfLib.Core, .Vault, .Master) into Dist/Packages/nuget/.
#            Master defaults to a native AOT build in Release, so pack overrides those off (managed).
#   bindings Generate language-binding packages (delegates to Scripts/package-bindings.ps1; needs pwsh
#            and the AOT libraries from a prior 'aot' run).
#   clean    Delete Dist/ and all bin/obj under Uvf.Net/.
#   all      clean (if --clean) -> test (unless --skip-tests) -> aot -> pack.
#
# Examples:
#   ./BuildScripts/build.sh --task aot
#   ./BuildScripts/build.sh --task aot --platforms linux-x64,linux-arm64
#   ./BuildScripts/build.sh --task pack
#   ./BuildScripts/build.sh --task all --clean

set -euo pipefail

# ---- defaults ----
TASK="aot"
CONFIGURATION="Release"
PLATFORMS=""
LANGUAGES="CSharp,Python,NodeJs"
SKIP_TESTS=0
CLEAN=0

usage() { sed -n '2,21p' "$0"; exit "${1:-0}"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    -t|--task)          TASK="$2"; shift 2 ;;
    -c|--configuration) CONFIGURATION="$2"; shift 2 ;;
    -p|--platforms)     PLATFORMS="$2"; shift 2 ;;
    -l|--languages)     LANGUAGES="$2"; shift 2 ;;
    --skip-tests)       SKIP_TESTS=1; shift ;;
    --clean)            CLEAN=1; shift ;;
    -h|--help)          usage 0 ;;
    *) echo "Unknown option: $1" >&2; usage 1 ;;
  esac
done

case "$TASK" in test|aot|pack|bindings|clean|all) ;; *) echo "Invalid --task '$TASK'" >&2; usage 1 ;; esac
case "$CONFIGURATION" in Debug|Release) ;; *) echo "Invalid --configuration '$CONFIGURATION'" >&2; exit 1 ;; esac

# ---- paths (resolved from this script, so the CWD does not matter) ----
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
SOLUTION="$REPO_ROOT/Uvf.Net/Uvf.Net.sln"
MASTER_PROJ="$REPO_ROOT/Uvf.Net/UvfLib.Master/UvfLib.Master.csproj"
TEST_PROJ="$REPO_ROOT/Uvf.Net/UvfLib.Tests/UvfLib.Tests.csproj"
DIST_ROOT="$REPO_ROOT/Dist"
PACK_PROJECTS=(UvfLib.Core UvfLib.Vault UvfLib.Master)
# Force the managed build for test + pack (Master defaults to a native AOT build in Release).
MANAGED_OVERRIDES=(-p:PublishAot=false -p:NativeLib= -p:SelfContained=false -p:RuntimeIdentifier= -p:EnableDynamicLoading=false)

step() { printf '\n=== %s ===\n' "$1"; }

host_rid() {
  local s m; s="$(uname -s)"; m="$(uname -m)"
  case "$s" in
    Linux)  [[ "$m" == "aarch64" || "$m" == "arm64" ]] && echo "linux-arm64" || echo "linux-x64" ;;
    Darwin) [[ "$m" == "arm64" ]] && echo "osx-arm64" || echo "osx-x64" ;;
    *)      echo "linux-x64" ;;
  esac
}

sha256() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | awk '{print $1}';
  else shasum -a 256 "$1" | awk '{print $1}'; fi
}

[[ -z "$PLATFORMS" ]] && PLATFORMS="$(host_rid)"
IFS=',' read -r -a PLATFORM_ARR <<< "$PLATFORMS"

do_clean() {
  step "Clean"
  rm -rf "$DIST_ROOT" && echo "removed $DIST_ROOT" || true
  find "$REPO_ROOT/Uvf.Net" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true
  echo "removed bin/obj under Uvf.Net/"
}

do_test() {
  step "Build + test ($CONFIGURATION)"
  dotnet build "$SOLUTION" -c "$CONFIGURATION" "${MANAGED_OVERRIDES[@]}" --verbosity minimal
  dotnet test "$TEST_PROJ" -c "$CONFIGURATION" --no-build "${MANAGED_OVERRIDES[@]}" --verbosity minimal
}

do_aot() {
  step "AOT native build -> ${PLATFORMS}"
  mkdir -p "$DIST_ROOT/Native"
  local manifest="$DIST_ROOT/Native/build-manifest.json"
  local entries=()
  for rid in "${PLATFORM_ARR[@]}"; do
    local out="$DIST_ROOT/Native/$rid"
    echo "-- $rid -> $out"
    dotnet publish "$MASTER_PROJ" \
      -c "$CONFIGURATION" -r "$rid" --self-contained true \
      -p:PublishAot=true -p:OptimizationPreference=Speed -p:IlcOptimizationPreference=Speed \
      -p:IlcFoldIdenticalMethodBodies=true -p:DebugType=embedded \
      -o "$out" --verbosity minimal
    # .NET Native-AOT names the output after the assembly (TitanVault) with the platform
    # extension and NO "lib" prefix on Linux/macOS (i.e. TitanVault.so / TitanVault.dylib).
    # Normalize it to the conventional libTitanVault.{so,dylib} so FFI hosts and our demos
    # find it by the usual Unix name (Windows keeps TitanVault.dll, built by build.ps1).
    local lib
    lib="$(find "$out" -maxdepth 1 -type f \( -iname 'TitanVault*.so' -o -iname 'TitanVault*.dylib' -o -iname 'libTitanVault*.so' -o -iname 'libTitanVault*.dylib' \) | head -n1)"
    if [[ -z "$lib" ]]; then echo "ERROR: AOT build for $rid produced no TitanVault native library in $out" >&2; exit 1; fi
    local dir base ext
    dir="$(dirname "$lib")"; base="$(basename "$lib")"
    case "$base" in *.dylib) ext="dylib" ;; *) ext="so" ;; esac
    if [[ "$base" != lib* ]]; then
      mv -f "$lib" "$dir/libTitanVault.$ext"
      [[ -f "$dir/$base.dbg" ]] && mv -f "$dir/$base.dbg" "$dir/libTitanVault.$ext.dbg"   # keep the debug sidecar paired
      lib="$dir/libTitanVault.$ext"
    fi
    local h; h="$(sha256 "$lib")"
    echo "   $(basename "$lib")  sha256=$h"
    entries+=("{\"rid\":\"$rid\",\"file\":\"$(basename "$lib")\",\"sha256\":\"$h\"}")
  done
  { printf '{\n  "configuration": "%s",\n  "builtUtc": "%s",\n  "artifacts": [\n' \
      "$CONFIGURATION" "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    local i; for i in "${!entries[@]}"; do
      printf '    %s%s\n' "${entries[$i]}" "$([[ $i -lt $((${#entries[@]}-1)) ]] && echo ',')"
    done
    printf '  ]\n}\n'
  } > "$manifest"
  echo "manifest: $manifest"
}

do_pack() {
  step "Pack managed NuGet packages"
  local out="$DIST_ROOT/Packages/nuget"
  for proj in "${PACK_PROJECTS[@]}"; do
    dotnet pack "$REPO_ROOT/Uvf.Net/$proj/$proj.csproj" \
      -c Release "${MANAGED_OVERRIDES[@]}" \
      -o "$out" --verbosity minimal
  done
  echo "packages: $out"
  ls -1 "$out"/*.nupkg 2>/dev/null | sed 's/^/   /' || true
}

do_bindings() {
  step "Language-binding packages"
  local script="$SCRIPT_DIR/Scripts/package-bindings.ps1"
  [[ -f "$script" ]] || { echo "package-bindings.ps1 not found at $script" >&2; exit 1; }
  command -v pwsh >/dev/null 2>&1 || { echo "pwsh (PowerShell 7+) is required for the bindings task" >&2; exit 1; }
  pwsh "$script" -Languages "$LANGUAGES" -Configuration "$CONFIGURATION"
}

START=$(date +%s)
case "$TASK" in
  clean)    do_clean ;;
  test)     [[ $CLEAN -eq 1 ]] && do_clean; do_test ;;
  aot)      [[ $CLEAN -eq 1 ]] && do_clean; do_aot ;;
  pack)     [[ $CLEAN -eq 1 ]] && do_clean; do_pack ;;
  bindings) do_bindings ;;
  all)
    [[ $CLEAN -eq 1 ]] && do_clean
    [[ $SKIP_TESTS -eq 1 ]] || do_test
    do_aot
    do_pack ;;
esac
printf '\n✅ Task '\''%s'\'' completed in %ss.\n' "$TASK" "$(( $(date +%s) - START ))"
