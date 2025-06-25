#!/usr/bin/env pwsh
#Requires -Version 5.1

<#
.SYNOPSIS
    Builds TitanVault AOT native library for Windows x64
    
.DESCRIPTION
    This script builds the complete UVF.NET stack (UvfLib.Master) as a single 
    AOT-compiled native library named TitanVault.dll. The library includes:
    - UvfLib.Core (cryptographic operations)
    - UvfLib.Vault (vault management)
    - UvfLib.Master (high-level API)
    - FolderMagic.StorageLib (cloud storage support)
    - C-style exports for cross-language compatibility
    
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
    
.PARAMETER Runtime
    Target runtime. Default: win-x64
    
.PARAMETER OutputDir
    Output directory for the native library. Default: ../../Dist/Native/win-x64
    
.PARAMETER Clean
    Clean before building
    
.PARAMETER Verbose
    Enable verbose output
    
.EXAMPLE
    .\build-titanvault-aot.ps1
    
.EXAMPLE
    .\build-titanvault-aot.ps1 -Configuration Debug -Clean -VerboseOutput
    
.NOTES
    Requires .NET 8.0 SDK or later
    Requires StorageLib to be resolved to project references (run resolve-storagelib-simple.ps1 first)
#>

param(
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [Parameter()]
    [string]$Runtime = "win-x64",
    
    [Parameter()]
    [string]$OutputDir = "../../Dist/Native/win-x64",
    
    [Parameter()]
    [switch]$Clean,
    
    [Parameter()]
    [switch]$VerboseOutput
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Script location and paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Join-Path $ScriptDir "../.."
$UvfNetDir = Join-Path $RootDir "Uvf.Net"
$MasterProjectPath = Join-Path $UvfNetDir "UvfLib.Master/UvfLib.Master.csproj"

# Resolve paths
$RootDir = Resolve-Path $RootDir
$UvfNetDir = Resolve-Path $UvfNetDir
$MasterProjectPath = Resolve-Path $MasterProjectPath

# Create absolute output directory
if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $ScriptDir $OutputDir
}
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

Write-Host "Building TitanVault AOT Native Library" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Runtime: $Runtime" -ForegroundColor Cyan
Write-Host "Output: $OutputDir" -ForegroundColor Cyan
Write-Host ""

# Check .NET version
try {
    $dotnetVersion = dotnet --version
    $majorVersion = [int]($dotnetVersion.Split('.')[0])
    
    if ($majorVersion -lt 8) {
        Write-Host ".NET 8.0 or later is required. Found: $dotnetVersion" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ".NET SDK version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host ".NET SDK not found. Please install .NET 8.0 SDK or later." -ForegroundColor Red
    exit 1
}

# Check project exists
if (-not (Test-Path $MasterProjectPath)) {
    Write-Host "UvfLib.Master project not found: $MasterProjectPath" -ForegroundColor Red
    exit 1
}

# Check StorageLib resolution
$projectContent = Get-Content $MasterProjectPath -Raw

if ($projectContent -match 'ProjectReference.*StorageLib\.csproj') {
    Write-Host "StorageLib resolved to project reference" -ForegroundColor Green
} elseif ($projectContent -match 'PackageReference.*FolderMagic\.StorageLib') {
    Write-Host "StorageLib is still a NuGet package reference" -ForegroundColor Yellow
    Write-Host "Run resolve-storagelib-simple.ps1 first to enable AOT compilation" -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "StorageLib reference not found in project" -ForegroundColor Yellow
    exit 1
}

# Change to Uvf.Net directory
Push-Location $UvfNetDir

try {
    # Clean if requested
    if ($Clean) {
        Write-Host "Cleaning previous builds..." -ForegroundColor Cyan
        dotnet clean UvfLib.Master/UvfLib.Master.csproj --configuration $Configuration --verbosity minimal
        
        if (Test-Path $OutputDir) {
            Remove-Item $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host "Clean completed" -ForegroundColor Green
    }
    
    # Create output directory
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
        Write-Host "Created output directory: $OutputDir" -ForegroundColor Cyan
    }
    
    # Build AOT library
    Write-Host "Building TitanVault AOT library..." -ForegroundColor Cyan
    Write-Host "This may take several minutes..." -ForegroundColor Cyan
    
    $buildArgs = @(
        "publish"
        "UvfLib.Master/UvfLib.Master.csproj"
        "--configuration", $Configuration
        "--runtime", $Runtime
        "--self-contained", "true"
        "/p:PublishAot=true"
        "--output", $OutputDir
    )
    
    if ($VerboseOutput) {
        $buildArgs += "--verbosity", "normal"
    } else {
        $buildArgs += "--verbosity", "minimal"
    }
    
    $startTime = Get-Date
    & dotnet @buildArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "AOT compilation failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    
    $buildTime = (Get-Date) - $startTime
    Write-Host "AOT compilation completed in $($buildTime.TotalSeconds.ToString('F1')) seconds" -ForegroundColor Green
    
    # Rename UvfLib.Master.dll to TitanVault.dll
    $originalDll = Join-Path $OutputDir "UvfLib.Master.dll"
    $newDll = Join-Path $OutputDir "TitanVault.dll"
    
    if (Test-Path $originalDll) {
        Move-Item $originalDll $newDll -Force
        Write-Host "Renamed UvfLib.Master.dll -> TitanVault.dll" -ForegroundColor Green
    } else {
        Write-Host "UvfLib.Master.dll not found for renaming" -ForegroundColor Yellow
    }
    
    # Also rename PDB file if it exists
    $originalPdb = Join-Path $OutputDir "UvfLib.Master.pdb"
    $newPdb = Join-Path $OutputDir "TitanVault.pdb"
    
    if (Test-Path $originalPdb) {
        Move-Item $originalPdb $newPdb -Force
        Write-Host "Renamed debug symbols: TitanVault.pdb" -ForegroundColor Cyan
    }
    
    # Display results
    Write-Host ""
    Write-Host "TitanVault AOT library build completed successfully!" -ForegroundColor Green
    Write-Host ""
    
    # Show output files
    if (Test-Path $OutputDir) {
        $files = Get-ChildItem $OutputDir | Where-Object { $_.Extension -in @('.dll', '.exe', '.pdb', '.xml') }
        
        Write-Host "Output files:" -ForegroundColor Cyan
        foreach ($file in $files) {
            $sizeKB = [math]::Round($file.Length / 1024, 1)
            Write-Host "   $($file.Name) ($sizeKB KB)" -ForegroundColor White
        }
    }
    
    Write-Host ""
    Write-Host "TitanVault.dll is ready for:" -ForegroundColor Cyan
    Write-Host "   * C-style exports for cross-language bindings" -ForegroundColor White
    Write-Host "   * PHP (FFI), Python (ctypes), Go (cgo)" -ForegroundColor White
    Write-Host "   * C++, Rust, Node.js, and other languages" -ForegroundColor White
    Write-Host "   * Complete UVF and Cryptomator V8 support" -ForegroundColor White
    Write-Host "   * Cloud storage integration (AWS S3, Azure, SSH)" -ForegroundColor White
    
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Build script completed successfully!" -ForegroundColor Green 