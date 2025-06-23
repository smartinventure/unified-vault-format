#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Windows-specific AOT build script for UVF.NET library
.DESCRIPTION
    Builds UVF.NET projects as AOT-compiled native libraries specifically for Windows platforms.
    Includes Windows-specific optimizations and configurations.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Architecture
    Target architecture (x64 or arm64). Default: Both
.PARAMETER Projects
    Specific projects to build. Default: All core projects
.PARAMETER OutputPath
    Output directory for built libraries. Default: ./Dist/Native
.PARAMETER IncludeSymbols
    Include debug symbols in release builds
.PARAMETER OptimizeForSize
    Optimize for size instead of speed
.EXAMPLE
    .\build-windows.ps1
    .\build-windows.ps1 -Architecture x64 -Configuration Debug
    .\build-windows.ps1 -OptimizeForSize -IncludeSymbols
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("x64", "arm64", "Both")]
    [string]$Architecture = "Both",
    
    [string[]]$Projects = @("UvfLib.Core", "UvfLib.Storage", "UvfLib.Vault"),
    
    [string]$OutputPath = "./Dist/Native",
    
    [switch]$IncludeSymbols,
    
    [switch]$OptimizeForSize,
    
    [switch]$Verbose
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"

# Determine target platforms
$Platforms = @()
if ($Architecture -eq "Both") {
    $Platforms = @("win-x64", "win-arm64")
} else {
    $Platforms = @("win-$Architecture")
}

# Windows-specific build optimizations
$WindowsOptimizations = @{
    # Performance optimizations
    "IlcOptimizationPreference" = if ($OptimizeForSize) { "Size" } else { "Speed" }
    "IlcFoldIdenticalMethodBodies" = "true"
    "IlcGenerateStackTraceData" = if ($Configuration -eq "Debug") { "true" } else { "false" }
    
    # Windows-specific settings
    "PublishReadyToRun" = "true"
    "ReadyToRunUseCrossgen2" = "true"
    "TieredCompilation" = "true"
    "TieredPGO" = "true"
    
    # Security and compatibility
    "PublishTrimmed" = "true"
    "TrimMode" = "link"
    "SuppressTrimAnalysisWarnings" = "true"
    "EnableUnsafeBinaryFormatterSerialization" = "false"
    
    # Debug settings
    "DebugType" = if ($IncludeSymbols -or $Configuration -eq "Debug") { "embedded" } else { "none" }
    "DebugSymbols" = if ($IncludeSymbols -or $Configuration -eq "Debug") { "true" } else { "false" }
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

# Check Windows-specific prerequisites
function Test-WindowsPrerequisites {
    Write-Header "Checking Windows Prerequisites"
    
    # Check Windows version
    $osVersion = [System.Environment]::OSVersion.Version
    Write-Info "Windows version: $osVersion"
    
    # Check for Windows SDK
    $windowsSdkPath = "${env:ProgramFiles(x86)}\Windows Kits\10"
    if (Test-Path $windowsSdkPath) {
        $sdkVersions = Get-ChildItem "$windowsSdkPath\bin" -Directory | Sort-Object Name -Descending
        if ($sdkVersions) {
            Write-Success "Windows SDK found: $($sdkVersions[0].Name)"
        }
    } else {
        Write-Warning "Windows SDK not found. Some features may not work optimally."
    }
    
    # Check for Visual Studio Build Tools
    $vsBuildToolsPath = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools"
    $vsPath = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional"
    $vsEnterprisePath = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise"
    
    if (Test-Path $vsBuildToolsPath) {
        Write-Success "Visual Studio Build Tools found"
    } elseif (Test-Path $vsPath) {
        Write-Success "Visual Studio Professional found"
    } elseif (Test-Path $vsEnterprisePath) {
        Write-Success "Visual Studio Enterprise found"
    } else {
        Write-Warning "Visual Studio Build Tools not found. Native compilation may be slower."
    }
    
    # Check available memory
    $memory = Get-CimInstance -ClassName Win32_ComputerSystem
    $memoryGB = [math]::Round($memory.TotalPhysicalMemory / 1GB, 1)
    Write-Info "Available memory: ${memoryGB}GB"
    
    if ($memoryGB -lt 8) {
        Write-Warning "Low memory detected. Consider building one platform at a time."
    }
}

# Build project with Windows-specific optimizations
function Build-WindowsProject {
    param(
        [string]$ProjectName,
        [string]$Platform,
        [string]$Config
    )
    
    $projectPath = Join-Path $UvfNetRoot "$ProjectName/$ProjectName.csproj"
    $outputDir = Join-Path $OutputPath $Platform $ProjectName
    
    Write-Info "Building $ProjectName for $Platform ($Config)"
    
    # Ensure output directory exists
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    
    # Build arguments with Windows optimizations
    $buildArgs = @(
        "publish"
        $projectPath
        "--configuration", $Config
        "--runtime", $Platform
        "--self-contained", "true"
        "--output", $outputDir
        "/p:PublishAot=true"
        "/p:PublishSingleFile=false"
        "--verbosity", ($Verbose ? "detailed" : "minimal")
    )
    
    # Add Windows-specific optimizations
    foreach ($key in $WindowsOptimizations.Keys) {
        $buildArgs += "/p:$key=$($WindowsOptimizations[$key])"
    }
    
    # Platform-specific optimizations
    if ($Platform -eq "win-x64") {
        $buildArgs += "/p:Prefer32Bit=false"
        $buildArgs += "/p:PlatformTarget=x64"
    } elseif ($Platform -eq "win-arm64") {
        $buildArgs += "/p:PlatformTarget=ARM64"
    }
    
    try {
        Write-Info "Executing: dotnet $($buildArgs -join ' ')"
        & dotnet @buildArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Built $ProjectName for $Platform"
            
            # Create package structure
            New-WindowsPackageStructure -ProjectName $ProjectName -Platform $Platform -OutputDir $outputDir
            
            return $true
        } else {
            Write-Error "Build failed with exit code: $LASTEXITCODE"
            return $false
        }
    }
    catch {
        Write-Error "Exception during build: $_"
        return $false
    }
}

# Create Windows-specific package structure
function New-WindowsPackageStructure {
    param(
        [string]$ProjectName,
        [string]$Platform,
        [string]$OutputDir
    )
    
    Write-Info "Creating Windows package structure for $ProjectName"
    
    # Create subdirectories
    $binDir = Join-Path $OutputDir "bin"
    $libDir = Join-Path $OutputDir "lib"
    $includeDir = Join-Path $OutputDir "include"
    $docsDir = Join-Path $OutputDir "docs"
    
    foreach ($dir in @($binDir, $libDir, $includeDir, $docsDir)) {
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }
    
    # Move files to appropriate locations
    $files = Get-ChildItem $OutputDir -File
    
    foreach ($file in $files) {
        switch ($file.Extension.ToLower()) {
            ".dll" {
                if ($file.Name -like "*$ProjectName*") {
                    # Main library goes to lib
                    Move-Item $file.FullName $libDir -Force
                } else {
                    # Dependencies go to bin
                    Move-Item $file.FullName $binDir -Force
                }
            }
            ".exe" {
                Move-Item $file.FullName $binDir -Force
            }
            ".pdb" {
                Move-Item $file.FullName $libDir -Force
            }
            ".xml" {
                Move-Item $file.FullName $docsDir -Force
            }
            ".json" {
                # Keep config files in root
            }
            default {
                # Keep other files in root
            }
        }
    }
    
    # Create version info file
    $versionInfo = @{
        ProjectName = $ProjectName
        Platform = $Platform
        Configuration = $Configuration
        BuildTime = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        DotNetVersion = (dotnet --version)
        WindowsVersion = [System.Environment]::OSVersion.VersionString
        Architecture = $Platform.Split('-')[1]
        Optimizations = $WindowsOptimizations
    }
    
    $versionPath = Join-Path $OutputDir "version.json"
    $versionInfo | ConvertTo-Json -Depth 10 | Set-Content $versionPath -Encoding UTF8
    
    Write-Info "Package structure created for $ProjectName ($Platform)"
}

# Generate Windows-specific documentation
function New-WindowsDocumentation {
    Write-Header "Generating Windows Documentation"
    
    $readmePath = Join-Path $OutputPath "README-Windows.md"
    $readme = @"
# UVF.NET Windows Native Libraries

This directory contains AOT-compiled native libraries for Windows platforms.

## Platforms

- **win-x64**: Windows 64-bit (Intel/AMD)
- **win-arm64**: Windows 64-bit (ARM)

## Directory Structure

```
win-x64/
├── UvfLib.Core/
│   ├── bin/          # Runtime dependencies
│   ├── lib/          # Main library files
│   ├── include/      # Header files (if any)
│   ├── docs/         # XML documentation
│   └── version.json  # Build information
├── UvfLib.Storage/
└── UvfLib.Vault/
```

## Usage

### C# Applications

```csharp
// Add reference to the appropriate platform library
// The library will be automatically loaded
using UvfLib.Core.Api;
using UvfLib.Storage;

// Create a vault
var vault = await VaultManager.CreateUvfVaultAsync("path/to/vault", "password");
```

### Native Applications

```cpp
// Load the native library
HMODULE uvfLib = LoadLibrary(L"UvfLib.Core.dll");
// Use P/Invoke or COM interop
```

## System Requirements

- Windows 10 version 1607 or later
- Windows Server 2016 or later
- .NET 8.0 Runtime (if using managed interop)

## Performance Notes

- Libraries are optimized for $(if ($OptimizeForSize) { "size" } else { "speed" })
- ReadyToRun compilation enabled for faster startup
- Tiered compilation and PGO enabled

## Troubleshooting

### Common Issues

1. **Library not found**: Ensure the library is in the same directory as your executable or in the system PATH.
2. **Access denied**: Run as administrator if accessing system directories.
3. **Compatibility issues**: Verify Windows version compatibility.

### Debug Symbols

$(if ($IncludeSymbols) {
"Debug symbols (.pdb files) are included for debugging support."
} else {
"Debug symbols are not included. Rebuild with -IncludeSymbols for debugging support."
})

## Build Information

- Configuration: $Configuration
- Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
- .NET Version: $(dotnet --version)
- Optimizations: $(if ($OptimizeForSize) { "Size" } else { "Speed" })

"@

    Set-Content $readmePath -Value $readme -Encoding UTF8
    Write-Success "Windows documentation created: $readmePath"
}

# Main execution
function Main {
    $startTime = Get-Date
    
    Write-Header "UVF.NET Windows AOT Build"
    Write-Host "Configuration: $Configuration" -ForegroundColor White
    Write-Host "Architecture: $Architecture" -ForegroundColor White
    Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor White
    Write-Host "Projects: $($Projects -join ', ')" -ForegroundColor White
    Write-Host "Optimize for: $(if ($OptimizeForSize) { 'Size' } else { 'Speed' })" -ForegroundColor White
    
    try {
        # Prerequisites
        Test-WindowsPrerequisites
        
        # Build projects
        Write-Header "Building Windows Projects"
        $buildResults = @{}
        $totalBuilds = $Projects.Count * $Platforms.Count
        $currentBuild = 0
        
        foreach ($project in $Projects) {
            $buildResults[$project] = @{}
            
            foreach ($platform in $Platforms) {
                $currentBuild++
                Write-Host "`n[$currentBuild/$totalBuilds] Building $project for $platform..." -ForegroundColor Yellow
                
                $success = Build-WindowsProject -ProjectName $project -Platform $platform -Config $Configuration
                $buildResults[$project][$platform] = $success
            }
        }
        
        # Generate documentation
        New-WindowsDocumentation
        
        # Summary
        Write-Header "Windows Build Summary"
        $successCount = 0
        $failCount = 0
        
        foreach ($project in $Projects) {
            Write-Host "`n$project:" -ForegroundColor White
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
        
        Write-Host "`nWindows build completed in $($duration.ToString('mm\:ss'))" -ForegroundColor Cyan
        Write-Host "✅ Successful builds: $successCount" -ForegroundColor Green
        if ($failCount -gt 0) {
            Write-Host "❌ Failed builds: $failCount" -ForegroundColor Red
            exit 1
        }
        
        Write-Success "Windows libraries available in: $OutputPath"
    }
    catch {
        Write-Error "Windows build failed: $_"
        exit 1
    }
}

# Execute main function
Main 