#requires -Version 5.1
<#
.SYNOPSIS
    One build script for UvfLib, with task switches. Replaces the old build-*.ps1 set.

.DESCRIPTION
    Tasks (-Task):
      test   Build the solution and run the test suite.
      aot    Publish the native AOT library (UvfLib.Master -> TitanVault.dll) for each -Platform RID,
             into Dist/Native/<rid>/, with a SHA-256 manifest. (Needs a native toolchain: the MSVC
             C/C++ build tools on Windows; clang + dev headers on Linux/macOS.)
      pack   Pack the managed NuGet packages (UvfLib.Core, .Vault, .Master) into Dist/Packages/nuget/.
             Master's csproj defaults to a native AOT build in Release, so pack overrides those off to
             emit the plain managed assembly (same as the publish-nuget.yml CI workflow).
      bindings  Generate language-binding packages (delegates to Scripts/package-bindings.ps1; needs
                the AOT libraries from a prior 'aot' run).
      clean  Delete Dist/ and all bin/obj under Uvf.Net/.
      all    clean (if -Clean) -> test (unless -SkipTests) -> aot -> pack.

.EXAMPLE
    ./BuildScripts/build.ps1 -Task aot
.EXAMPLE
    ./BuildScripts/build.ps1 -Task aot -Platforms win-x64,linux-x64
.EXAMPLE
    ./BuildScripts/build.ps1 -Task pack
.EXAMPLE
    ./BuildScripts/build.ps1 -Task all -Clean
#>
[CmdletBinding()]
param(
    [ValidateSet('test', 'aot', 'pack', 'bindings', 'clean', 'all')]
    [string]$Task = 'aot',

    [string[]]$Platforms,

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string[]]$Languages = @('CSharp', 'Python', 'NodeJs'),

    [switch]$SkipTests,

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---- paths (resolved from this script, so the CWD does not matter) ----
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$Solution   = Join-Path $RepoRoot 'Uvf.Net/Uvf.Net.sln'
$MasterProj = Join-Path $RepoRoot 'Uvf.Net/UvfLib.Master/UvfLib.Master.csproj'
$TestProj   = Join-Path $RepoRoot 'Uvf.Net/UvfLib.Tests/UvfLib.Tests.csproj'
$DistRoot   = Join-Path $RepoRoot 'Dist'
$PackProjects = @('UvfLib.Core', 'UvfLib.Vault', 'UvfLib.Master')

# UvfLib.Master defaults to a native AOT build in Release (NativeLib/PublishAot/SelfContained), which
# makes a plain Release solution build/test/pack emit a native TitanVault.dll instead of a loadable
# managed assembly. These overrides force the managed build for test + pack; the 'aot' task is the only
# one that wants the native output (and sets PublishAot=true explicitly).
$ManagedOverrides = @('-p:PublishAot=false', '-p:NativeLib=', '-p:SelfContained=false', '-p:RuntimeIdentifier=', '-p:EnableDynamicLoading=false')

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

function Invoke-Dotnet {
    param([string[]]$Arguments)
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): dotnet $($Arguments -join ' ')" }
}

function Get-HostRid {
    # Works on both Windows PowerShell 5.1 and PowerShell 7+ (no $IsWindows / ternary).
    $rt = [System.Runtime.InteropServices.RuntimeInformation]
    $os = [System.Runtime.InteropServices.OSPlatform]
    if ($rt::IsOSPlatform($os::Windows)) { return 'win-x64' }
    if ($rt::IsOSPlatform($os::OSX)) {
        if ($rt::ProcessArchitecture -eq 'Arm64') { return 'osx-arm64' } else { return 'osx-x64' }
    }
    if ($rt::IsOSPlatform($os::Linux)) { return 'linux-x64' }
    return 'win-x64'
}

if (-not $Platforms -or $Platforms.Count -eq 0) { $Platforms = @(Get-HostRid) }

# Native AOT linker setup for Windows-on-ARM64 hosts. The ILCompiler's findvcvarsall.bat requires the
# generic 'VC.Tools.ARM64' component and only handles AMD64 hosts, so it reports "Platform linker not
# found" on ARM64 even when the C++ tools are installed. We work around it by importing the VC build
# environment ourselves (vcvarsall) and telling ILC to use the tools from the environment.
function Get-WindowsHostArch {
    if ($env:PROCESSOR_ARCHITEW6432) { return $env:PROCESSOR_ARCHITEW6432 }
    return $env:PROCESSOR_ARCHITECTURE
}

function Get-VcArchArg($rid) {
    # vcvarsall arch: native (arm64/amd64/x86) when host==target, else "<host>_<target>".
    $h = switch ((Get-WindowsHostArch)) { 'ARM64' { 'arm64' } 'AMD64' { 'amd64' } default { 'x86' } }
    $t = switch (($rid -split '-')[-1]) { 'arm64' { 'arm64' } 'x64' { 'amd64' } 'x86' { 'x86' } default { 'amd64' } }
    if ($h -eq $t) { return $t } else { return "${h}_${t}" }
}

function Find-Vcvarsall {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { return $null }
    # Prefer an install that actually has the C++ tools (-latest alone can return e.g. SSMS, which has
    # no vcvarsall). Fall back to any install that ships vcvarsall.bat.
    $candidates = @()
    $candidates += & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    $candidates += & $vswhere -all -prerelease -products * -property installationPath 2>$null
    foreach ($base in $candidates) {
        if (-not $base) { continue }
        $vc = Join-Path $base 'VC\Auxiliary\Build\vcvarsall.bat'
        if (Test-Path $vc) { return $vc }
    }
    return $null
}

function Initialize-VcEnvironment($archArg) {
    $vcvars = Find-Vcvarsall
    if (-not $vcvars) { return $false }
    Write-Host "   setting up VC build env: vcvarsall $archArg" -ForegroundColor DarkGray
    $lines = cmd /c "`"$vcvars`" $archArg >NUL 2>&1 && set" 2>$null
    if ($LASTEXITCODE -ne 0) { return $false }
    foreach ($line in $lines) {
        $i = $line.IndexOf('=')
        if ($i -gt 0) { Set-Item -Path ("env:" + $line.Substring(0, $i)) -Value $line.Substring($i + 1) }
    }
    return $true
}

function Invoke-Clean {
    Write-Step "Clean"
    if (Test-Path $DistRoot) { Remove-Item $DistRoot -Recurse -Force; Write-Host "removed $DistRoot" }
    Get-ChildItem (Join-Path $RepoRoot 'Uvf.Net') -Directory -Recurse -Include 'bin', 'obj' -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Host "removed bin/obj under Uvf.Net/"
}

function Invoke-Test {
    Write-Step "Build + test ($Configuration)"
    Invoke-Dotnet (@('build', $Solution, '-c', $Configuration, '--verbosity', 'minimal') + $ManagedOverrides)
    Invoke-Dotnet (@('test', $TestProj, '-c', $Configuration, '--no-build', '--verbosity', 'minimal') + $ManagedOverrides)
}

function Invoke-Aot {
    Write-Step "AOT native build -> $($Platforms -join ', ')"
    $manifest = [ordered]@{ configuration = $Configuration; builtUtc = (Get-Date).ToUniversalTime().ToString('o'); artifacts = @() }
    foreach ($rid in $Platforms) {
        $outDir = Join-Path $DistRoot "Native/$rid"
        Write-Host "-- $rid -> $outDir" -ForegroundColor Yellow
        # On ARM64 Windows hosts the default linker detection is broken; set up the VC env ourselves.
        $extra = @()
        if ((Get-WindowsHostArch) -eq 'ARM64') {
            if (Initialize-VcEnvironment (Get-VcArchArg $rid)) { $extra = @('-p:IlcUseEnvironmentalTools=true') }
            else { Write-Host "   (no VC env found; relying on default linker detection)" -ForegroundColor Yellow }
        }
        Invoke-Dotnet (@(
            'publish', $MasterProj,
            '-c', $Configuration,
            '-r', $rid,
            '--self-contained', 'true',
            '-p:PublishAot=true',
            '-p:OptimizationPreference=Speed',
            '-p:IlcOptimizationPreference=Speed',
            '-p:IlcFoldIdenticalMethodBodies=true',
            '-p:DebugType=embedded',
            '-o', $outDir,
            '--verbosity', 'minimal'
        ) + $extra)
        # The assembly is named TitanVault (UvfLib.Master.csproj AssemblyName); the native lib uses the
        # platform extension.
        $lib = Get-ChildItem $outDir -Filter '*TitanVault*' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.dll', '.so', '.dylib' } | Select-Object -First 1
        if (-not $lib) { throw "AOT build for $rid produced no TitanVault native library in $outDir" }
        $hash = (Get-FileHash $lib.FullName -Algorithm SHA256).Hash
        Write-Host "   $($lib.Name)  sha256=$hash" -ForegroundColor Green
        $manifest.artifacts += [ordered]@{ rid = $rid; file = $lib.Name; sha256 = $hash }
    }
    $manifestPath = Join-Path $DistRoot 'Native/build-manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content $manifestPath
    Write-Host "manifest: $manifestPath"
}

function Invoke-Pack {
    Write-Step "Pack managed NuGet packages"
    $outDir = Join-Path $DistRoot 'Packages/nuget'
    foreach ($proj in $PackProjects) {
        $csproj = Join-Path $RepoRoot "Uvf.Net/$proj/$proj.csproj"
        # Override the native/AOT settings off so Master packs as a plain managed assembly.
        Invoke-Dotnet (@('pack', $csproj, '-c', 'Release', '-o', $outDir, '--verbosity', 'minimal') + $ManagedOverrides)
    }
    Write-Host "packages: $outDir"
    Get-ChildItem $outDir -Filter '*.nupkg' | ForEach-Object { Write-Host "   $($_.Name)" }
}

function Invoke-Bindings {
    Write-Step "Language-binding packages"
    $script = Join-Path $PSScriptRoot 'Scripts/package-bindings.ps1'
    if (-not (Test-Path $script)) { throw "package-bindings.ps1 not found at $script" }
    & $script -Languages $Languages -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "package-bindings.ps1 failed (exit $LASTEXITCODE)" }
}

# ---- dispatch ----
$sw = [System.Diagnostics.Stopwatch]::StartNew()
switch ($Task) {
    'clean'    { Invoke-Clean }
    'test'     { if ($Clean) { Invoke-Clean }; Invoke-Test }
    'aot'      { if ($Clean) { Invoke-Clean }; Invoke-Aot }
    'pack'     { if ($Clean) { Invoke-Clean }; Invoke-Pack }
    'bindings' { Invoke-Bindings }
    'all'      {
        if ($Clean) { Invoke-Clean }
        if (-not $SkipTests) { Invoke-Test }
        Invoke-Aot
        Invoke-Pack
    }
}
$sw.Stop()
Write-Host "`n✅ Task '$Task' completed in $([math]::Round($sw.Elapsed.TotalSeconds,1))s." -ForegroundColor Green
