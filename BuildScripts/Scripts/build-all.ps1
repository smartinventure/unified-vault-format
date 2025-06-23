#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Master build script for UVF.NET - builds everything from source to packages
.DESCRIPTION
    Orchestrates the complete build process: AOT compilation, packaging, and distribution.
    This is the main entry point for building the entire UVF.NET distribution.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Platforms
    Target platforms to build. Default: Current platform only
.PARAMETER SkipTests
    Skip running tests
.PARAMETER SkipNativeLibraries
    Skip building AOT native libraries (useful for testing managed code only)
.PARAMETER Clean
    Clean all output directories before building
.PARAMETER Parallel
    Build platforms in parallel (faster but uses more resources)
.EXAMPLE
    .\build-all.ps1
    .\build-all.ps1 -Configuration Debug -Platforms "win-x64,linux-x64"
    .\build-all.ps1 -Clean -Parallel -SkipTests
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [string[]]$Platforms = @(),  # Empty means current platform only
    
    [switch]$SkipTests,
    
    [switch]$SkipNativeLibraries,
    
    [switch]$Clean,
    
    [switch]$Parallel,
    
    [switch]$Verbose
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)

# Determine current platform
$CurrentPlatform = ""
if ($IsWindows -or $PSVersionTable.PSEdition -eq "Desktop") {
    $arch = if ([Environment]::Is64BitProcess) { "x64" } else { "x86" }
    $CurrentPlatform = "win-$arch"
} elseif ($IsLinux) {
    $arch = if ((uname -m) -eq "x86_64") { "x64" } else { "arm64" }
    $CurrentPlatform = "linux-$arch"
} elseif ($IsMacOS) {
    $arch = if ((uname -m) -eq "x86_64") { "x64" } else { "arm64" }
    $CurrentPlatform = "osx-$arch"
}

# Default to current platform if none specified
if ($Platforms.Count -eq 0) {
    $Platforms = @($CurrentPlatform)
    Write-Host "No platforms specified, building for current platform: $CurrentPlatform" -ForegroundColor Yellow
}

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

function Write-Step {
    param([string]$Message)
    Write-Host "`n🔨 $Message" -ForegroundColor Magenta
}

# Check prerequisites
function Test-BuildPrerequisites {
    Write-Header "Checking Build Prerequisites"
    
    # Check .NET SDK
    try {
        $dotnetVersion = dotnet --version
        Write-Success ".NET SDK: $dotnetVersion"
    }
    catch {
        Write-Error ".NET SDK not found. Please install .NET 8.0 SDK or later."
        exit 1
    }
    
    # Check PowerShell version
    $psVersion = $PSVersionTable.PSVersion
    Write-Success "PowerShell: $psVersion"
    
    # Check Git (for version info)
    try {
        $gitVersion = git --version 2>$null
        Write-Success "Git: $gitVersion"
    }
    catch {
        Write-Warning "Git not found. Version info may be limited."
    }
    
    # Platform-specific checks
    if ($CurrentPlatform.StartsWith("win")) {
        Write-Info "Windows build environment detected"
    } elseif ($CurrentPlatform.StartsWith("linux")) {
        Write-Info "Linux build environment detected"
        
        # Check for build tools
        $tools = @("gcc", "make")
        foreach ($tool in $tools) {
            try {
                $toolPath = which $tool 2>$null
                if ($toolPath) {
                    Write-Success "Tool: $tool"
                } else {
                    Write-Warning "Tool not found: $tool (may be needed for native compilation)"
                }
            }
            catch {
                Write-Warning "Could not check tool: $tool"
            }
        }
    } elseif ($CurrentPlatform.StartsWith("osx")) {
        Write-Info "macOS build environment detected"
        
        # Check Xcode tools
        try {
            $xcodeVersion = xcode-select --version 2>$null
            Write-Success "Xcode Command Line Tools: $xcodeVersion"
        }
        catch {
            Write-Warning "Xcode Command Line Tools not found. May be needed for native compilation."
        }
    }
}

# Build managed libraries first
function Build-ManagedLibraries {
    Write-Step "Building Managed Libraries"
    
    $projectsPath = Join-Path $ProjectRoot "Uvf.Net"
    
    # Build solution
    try {
        $solutionPath = Join-Path $projectsPath "Uvf.Net.sln"
        
        if ($Clean) {
            Write-Info "Cleaning solution..."
            & dotnet clean $solutionPath --configuration $Configuration --verbosity minimal
        }
        
        Write-Info "Restoring packages..."
        & dotnet restore $solutionPath --verbosity minimal
        
        Write-Info "Building solution..."
        & dotnet build $solutionPath --configuration $Configuration --no-restore --verbosity minimal
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Managed libraries built successfully"
        } else {
            Write-Error "Failed to build managed libraries"
            exit 1
        }
    }
    catch {
        Write-Error "Exception building managed libraries: $_"
        exit 1
    }
}

# Run tests
function Invoke-Tests {
    if ($SkipTests) {
        Write-Warning "Skipping tests (--SkipTests specified)"
        return
    }
    
    Write-Step "Running Tests"
    
    try {
        $testProject = Join-Path $ProjectRoot "Uvf.Net/UvfLib.Tests/UvfLib.Tests.csproj"
        
        Write-Info "Running unit tests..."
        & dotnet test $testProject --configuration $Configuration --no-build --verbosity minimal
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "All tests passed"
        } else {
            Write-Error "Tests failed"
            exit 1
        }
    }
    catch {
        Write-Error "Exception running tests: $_"
        exit 1
    }
}

# Build AOT libraries  
function Build-AotLibraries {
    if ($SkipNativeLibraries) {
        Write-Warning "Skipping AOT native libraries (--SkipNativeLibraries specified)"
        return
    }
    
    Write-Step "Building AOT Native Libraries"
    
    $buildScript = Join-Path $ScriptRoot "build-aot.ps1"
    
    if (-not (Test-Path $buildScript)) {
        Write-Error "AOT build script not found: $buildScript"
        exit 1
    }
    
    try {
        $buildArgs = @(
            "-Configuration", $Configuration
            "-Platforms", ($Platforms -join ",")
        )
        
        if ($Clean) {
            $buildArgs += "-Clean"
        }
        
        if ($Verbose) {
            $buildArgs += "-Verbose"
        }
        
        Write-Info "Executing AOT build: $buildScript $($buildArgs -join ' ')"
        
        if ($Parallel -and $Platforms.Count -gt 1) {
            # Build platforms in parallel
            Write-Info "Building platforms in parallel..."
            
            $jobs = @()
            foreach ($platform in $Platforms) {
                $job = Start-Job -ScriptBlock {
                    param($ScriptPath, $Config, $Platform, $CleanFlag, $VerboseFlag)
                    
                    $args = @("-Configuration", $Config, "-Platforms", $Platform)
                    if ($CleanFlag) { $args += "-Clean" }
                    if ($VerboseFlag) { $args += "-Verbose" }
                    
                    & $ScriptPath @args
                } -ArgumentList $buildScript, $Configuration, $platform, $Clean, $Verbose
                
                $jobs += $job
            }
            
            # Wait for all jobs to complete
            $jobs | Wait-Job | Receive-Job
            $jobs | Remove-Job
        } else {
            # Sequential build
            & $buildScript @buildArgs
        }
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "AOT libraries built successfully"
        } else {
            Write-Error "Failed to build AOT libraries"
            exit 1
        }
    }
    catch {
        Write-Error "Exception building AOT libraries: $_"
        exit 1
    }
}

# Note: Language binding creation is separate from AOT compilation
# Use .\package-bindings.ps1 separately to create language-specific wrappers

# Generate build report
function New-BuildReport {
    Write-Step "Generating Build Report"
    
    $reportPath = Join-Path $ProjectRoot "Dist/build-report.md"
    $distDir = Join-Path $ProjectRoot "Dist"
    
    if (-not (Test-Path $distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }
    
    $report = @"
# UVF.NET Build Report

**Build Date:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC")  
**Configuration:** $Configuration  
**Platforms:** $($Platforms -join ', ')  

## Environment

- **OS:** $($PSVersionTable.OS)
- **PowerShell:** $($PSVersionTable.PSVersion)
- **.NET SDK:** $(dotnet --version)
- **Machine:** $([Environment]::MachineName)

## Build Results

### Native Libraries

"@
    
    # Add native library info
    $nativeDir = Join-Path $ProjectRoot "Dist/Native"
    if (Test-Path $nativeDir) {
        foreach ($platform in $Platforms) {
            $platformDir = Join-Path $nativeDir $platform
            if (Test-Path $platformDir) {
                $report += "`n#### $platform`n`n"
                
                $projects = Get-ChildItem $platformDir -Directory
                foreach ($project in $projects) {
                    $report += "- **$($project.Name)**`n"
                    
                    $libDir = Join-Path $project.FullName "lib"
                    if (Test-Path $libDir) {
                        $libs = Get-ChildItem $libDir -File
                        foreach ($lib in $libs) {
                            $sizeKB = [math]::Round($lib.Length / 1024, 1)
                            $report += "  - $($lib.Name) ($sizeKB KB)`n"
                        }
                    }
                }
            }
        }
    }
    
    # Note: Language packages created separately by package-bindings.ps1
    
    $report += @"

## Usage

The AOT libraries provide C-compatible exports that can be used from any language.

### Direct C-style usage
```c
// Example C header usage (generated during AOT build)
#include "uvflib_native.h"

int result = uvf_create_vault("/path/to/vault", "password");
```

### Language Bindings
Create language-specific wrappers using:
```bash
.\Scripts\package-bindings.ps1 -Languages "Python,NodeJs,Java"
```

---
*Generated by UVF.NET Build System*
"@
    
    Set-Content $reportPath -Value $report -Encoding UTF8
    Write-Success "Build report created: $reportPath"
}

# Main execution
function Main {
    $startTime = Get-Date
    
    Write-Header "UVF.NET Complete Build System"
    Write-Host "Configuration: $Configuration" -ForegroundColor White
    Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor White
    Write-Host "Parallel Build: $Parallel" -ForegroundColor White
    Write-Host "Skip Tests: $SkipTests" -ForegroundColor White
    Write-Host "Skip Native Libraries: $SkipNativeLibraries" -ForegroundColor White
    
    try {
        # Step 1: Prerequisites
        Test-BuildPrerequisites
        
        # Step 2: Resolve StorageLib for AOT
        $resolveScript = Join-Path $ScriptRoot "resolve-storagelib.ps1"
        if (Test-Path $resolveScript) {
            Write-Step "Resolving StorageLib for AOT Compilation"
            & $resolveScript
            if ($LASTEXITCODE -ne 0) {
                Write-Error "StorageLib resolution failed"
                exit 1
            }
        }
        
        # Step 3: Build managed libraries
        Build-ManagedLibraries
        
        # Step 4: Run tests
        Invoke-Tests
        
        # Step 5: Build AOT libraries
        Build-AotLibraries
        
        # Step 6: Generate report
        New-BuildReport
        
        # Summary
        $endTime = Get-Date
        $duration = $endTime - $startTime
        
        Write-Header "Build Complete! 🎉"
        Write-Host "Total build time: $($duration.ToString('mm\:ss'))" -ForegroundColor Cyan
        Write-Success "All artifacts available in: $(Join-Path $ProjectRoot 'Dist')"
        
        Write-Host "`nNext steps:" -ForegroundColor Yellow
        Write-Host "1. Test the native AOT libraries" -ForegroundColor White
        Write-Host "2. Create language bindings: .\Scripts\package-bindings.ps1" -ForegroundColor White
        Write-Host "3. Test language bindings with example applications" -ForegroundColor White
        
    }
    catch {
        Write-Error "Build failed: $_"
        exit 1
    }
}

# Execute main function
Main 