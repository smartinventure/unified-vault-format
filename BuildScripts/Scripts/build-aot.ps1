#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Main AOT build script for UVF.NET library
.DESCRIPTION
    Builds all UVF.NET projects as AOT-compiled native libraries for multiple platforms.
    Supports Windows (x64, ARM64), Linux (x64, ARM64), and macOS (x64, ARM64).
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Platforms
    Target platforms to build. Default: All platforms
.PARAMETER Projects
    Specific projects to build. Default: All core projects
.PARAMETER OutputPath
    Output directory for built libraries. Default: ./Dist/Native
.PARAMETER Clean
    Clean output directories before building
.PARAMETER Verbose
    Enable verbose logging
.EXAMPLE
    .\build-aot.ps1
    .\build-aot.ps1 -Configuration Debug -Platforms "win-x64,linux-x64"
    .\build-aot.ps1 -Projects "UvfLib.Core,UvfLib.Storage" -Clean
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [string[]]$Platforms = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"),
    
    [string[]]$Projects = @("UvfLib.Core", "UvfLib.Storage", "UvfLib.Vault"),
    
    [string]$OutputPath = "./Dist/Native",
    
    [switch]$Clean,
    
    [switch]$Verbose
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"

# Logging functions
function Write-Header {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✅ $Message" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "⚠️  $Message" -ForegroundColor Yellow
}

function Write-Error {
    param([string]$Message)
    Write-Host "❌ $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    if ($Verbose) {
        Write-Host "ℹ️  $Message" -ForegroundColor Blue
    }
}

# Resolve StorageLib for AOT compilation
function Resolve-StorageLibForAOT {
    Write-Header "Resolving StorageLib Source for AOT"
    
    $resolveScript = Join-Path $ScriptRoot "resolve-storagelib.ps1"
    
    if (Test-Path $resolveScript) {
        Write-Info "Running StorageLib resolution..."
        try {
            & $resolveScript
            if ($LASTEXITCODE -ne 0) {
                Write-Error "StorageLib resolution failed"
                exit 1
            }
            Write-Success "StorageLib source resolved for AOT compilation"
        }
        catch {
            Write-Error "Exception during StorageLib resolution: $_"
            exit 1
        }
    } else {
        Write-Warning "StorageLib resolution script not found: $resolveScript"
        Write-Warning "AOT compilation may fail if StorageLib source is not available"
    }
}

# Prerequisite checks
function Test-Prerequisites {
    Write-Header "Checking Prerequisites"
    
    # Check .NET SDK
    try {
        $dotnetVersion = dotnet --version
        Write-Success ".NET SDK found: $dotnetVersion"
    }
    catch {
        Write-Error ".NET SDK not found. Please install .NET 8.0 SDK or later."
        exit 1
    }
    
    # Check for required workloads
    $workloads = dotnet workload list --machine-readable | ConvertFrom-Json
    $requiredWorkloads = @("microsoft-net-sdk-blazorwebassembly-aot")
    
    foreach ($workload in $requiredWorkloads) {
        if ($workloads.installed -notcontains $workload) {
            Write-Warning "Installing required workload: $workload"
            dotnet workload install $workload
        }
    }
    
    # Verify project paths
    foreach ($project in $Projects) {
        $projectPath = Join-Path $UvfNetRoot "$project/$project.csproj"
        if (-not (Test-Path $projectPath)) {
            Write-Error "Project not found: $projectPath"
            exit 1
        }
        Write-Info "Project found: $project"
    }
}

# Clean output directories
function Clear-OutputDirectories {
    if ($Clean) {
        Write-Header "Cleaning Output Directories"
        
        if (Test-Path $OutputPath) {
            Remove-Item $OutputPath -Recurse -Force
            Write-Success "Cleaned output directory: $OutputPath"
        }
        
        # Clean project bin/obj directories
        foreach ($project in $Projects) {
            $projectDir = Join-Path $UvfNetRoot $project
            $binDir = Join-Path $projectDir "bin"
            $objDir = Join-Path $projectDir "obj"
            
            if (Test-Path $binDir) {
                Remove-Item $binDir -Recurse -Force
                Write-Info "Cleaned bin directory: $project"
            }
            
            if (Test-Path $objDir) {
                Remove-Item $objDir -Recurse -Force
                Write-Info "Cleaned obj directory: $project"
            }
        }
    }
}

# Create output directories
function New-OutputDirectories {
    Write-Header "Creating Output Directories"
    
    foreach ($platform in $Platforms) {
        $platformDir = Join-Path $OutputPath $platform
        if (-not (Test-Path $platformDir)) {
            New-Item -ItemType Directory -Path $platformDir -Force | Out-Null
            Write-Info "Created directory: $platformDir"
        }
    }
}

# Build project for specific platform
function Build-ProjectForPlatform {
    param(
        [string]$ProjectName,
        [string]$Platform,
        [string]$Config
    )
    
    $projectPath = Join-Path $UvfNetRoot "$ProjectName/$ProjectName.csproj"
    $outputDir = Join-Path $OutputPath $Platform
    
    Write-Info "Building $ProjectName for $Platform ($Config)"
    
    $buildArgs = @(
        "publish"
        $projectPath
        "--configuration", $Config
        "--runtime", $Platform
        "--self-contained", "true"
        "--output", $outputDir
        "/p:PublishAot=true"
        "/p:PublishTrimmed=true"
        "/p:PublishSingleFile=false"
        "/p:DebugType=embedded"
        "/p:DebugSymbols=true"
        "/p:OptimizationPreference=Speed"
        "/p:IlcOptimizationPreference=Speed"
        "/p:IlcFoldIdenticalMethodBodies=true"
        "--verbosity", (if ($Verbose) { "detailed" } else { "minimal" })
    )
    
    try {
        & dotnet @buildArgs
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Built $ProjectName for $Platform"
            
            # Copy important files to organized structure
            $libName = "$ProjectName.dll"
            $nativeLibName = "$ProjectName.so"
            if ($Platform.StartsWith("win")) {
                $nativeLibName = "$ProjectName.dll"
            } elseif ($Platform.StartsWith("osx")) {
                $nativeLibName = "$ProjectName.dylib"
            }
            
            # Create organized output structure
            $platformOutputDir = Join-Path $OutputPath $Platform
            $projectOutputDir = Join-Path $platformOutputDir $ProjectName
            
            if (-not (Test-Path $projectOutputDir)) {
                New-Item -ItemType Directory -Path $projectOutputDir -Force | Out-Null
            }
            
            # Copy native library if it exists
            $nativeLibPath = Join-Path $outputDir $nativeLibName
            if (Test-Path $nativeLibPath) {
                Copy-Item $nativeLibPath $projectOutputDir -Force
                Write-Info "Copied native library: $nativeLibName"
            }
            
            # Copy managed assembly
            $managedLibPath = Join-Path $outputDir $libName
            if (Test-Path $managedLibPath) {
                Copy-Item $managedLibPath $projectOutputDir -Force
                Write-Info "Copied managed library: $libName"
            }
            
            # Copy PDB files for debugging
            $pdbPath = Join-Path $outputDir "$ProjectName.pdb"
            if (Test-Path $pdbPath) {
                Copy-Item $pdbPath $projectOutputDir -Force
                Write-Info "Copied debug symbols: $ProjectName.pdb"
            }
            
            # Copy XML documentation
            $xmlPath = Join-Path $outputDir "$ProjectName.xml"
            if (Test-Path $xmlPath) {
                Copy-Item $xmlPath $projectOutputDir -Force
                Write-Info "Copied documentation: $ProjectName.xml"
            }
        }
        else {
            Write-Error "Failed to build $ProjectName for $Platform"
            return $false
        }
    }
    catch {
        Write-Error "Exception building $ProjectName for $Platform: $($_.Exception.Message)"
        return $false
    }
    
    return $true
}

# Generate build manifest
function New-BuildManifest {
    Write-Header "Generating Build Manifest"
    
    $manifest = @{
        BuildTime = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        Configuration = $Configuration
        Platforms = $Platforms
        Projects = $Projects
        DotNetVersion = (dotnet --version)
        Libraries = @{}
    }
    
    foreach ($platform in $Platforms) {
        $platformDir = Join-Path $OutputPath $platform
        if (Test-Path $platformDir) {
            $manifest.Libraries[$platform] = @{}
            
            foreach ($project in $Projects) {
                $projectDir = Join-Path $platformDir $project
                if (Test-Path $projectDir) {
                    $files = Get-ChildItem $projectDir -File | ForEach-Object {
                        @{
                            Name = $_.Name
                            Size = $_.Length
                            Hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
                        }
                    }
                    $manifest.Libraries[$platform][$project] = $files
                }
            }
        }
    }
    
    $manifestPath = Join-Path $OutputPath "build-manifest.json"
    $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath -Encoding UTF8
    Write-Success "Build manifest created: $manifestPath"
}

# Main execution
function Main {
    $startTime = Get-Date
    
    Write-Header "UVF.NET AOT Build Script"
    Write-Host "Configuration: $Configuration" -ForegroundColor White
    Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor White
    Write-Host "Projects: $($Projects -join ', ')" -ForegroundColor White
    Write-Host "Output: $OutputPath" -ForegroundColor White
    
    try {
        # Step 1: Prerequisites
        Test-Prerequisites
        
        # Step 2: Resolve StorageLib
        Resolve-StorageLibForAOT
        
        # Step 3: Clean
        Clear-OutputDirectories
        
        # Step 3: Create output structure
        New-OutputDirectories
        
        # Step 4: Build all projects for all platforms
        Write-Header "Building Projects"
        $buildResults = @{}
        $totalBuilds = $Projects.Count * $Platforms.Count
        $currentBuild = 0
        
        foreach ($project in $Projects) {
            $buildResults[$project] = @{}
            
            foreach ($platform in $Platforms) {
                $currentBuild++
                Write-Host "`n[$currentBuild/$totalBuilds] Building $project for $platform..." -ForegroundColor Yellow
                
                $success = Build-ProjectForPlatform -ProjectName $project -Platform $platform -Config $Configuration
                $buildResults[$project][$platform] = $success
                
                if (-not $success) {
                    Write-Warning "Build failed for $project on $platform, continuing with other builds..."
                }
            }
        }
        
        # Step 5: Generate manifest
        New-BuildManifest
        
        # Step 6: Summary
        Write-Header "Build Summary"
        $successCount = 0
        $failCount = 0
        
        foreach ($project in $Projects) {
            Write-Host "`n${project}:" -ForegroundColor White
            foreach ($platform in $Platforms) {
                $status = $buildResults[$project][$platform]
                if ($status) {
                    Write-Host "  ✅ $platform" -ForegroundColor Green
                    $successCount++
                } else {
                    Write-Host "  ❌ $platform" -ForegroundColor Red
                    $failCount++
                }
            }
        }
        
        $endTime = Get-Date
        $duration = $endTime - $startTime
        
        $durationStr = $duration.ToString("mm\:ss")
        Write-Host "`nBuild completed in $durationStr" -ForegroundColor Cyan
        Write-Host "✅ Successful builds: $successCount" -ForegroundColor Green
        if ($failCount -gt 0) {
            Write-Host "❌ Failed builds: $failCount" -ForegroundColor Red
        }
        
        Write-Success "AOT libraries available in: $OutputPath"
        
        if ($failCount -gt 0) {
            Write-Warning "Some builds failed. Check the output above for details."
            exit 1
        }
    }
    catch {
        Write-Error "Build script failed: $($_.Exception.Message)"
        exit 1
    }
}

# Execute main function
Main 