#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Linux-specific AOT build script for UVF.NET library
.DESCRIPTION
    Builds UVF.NET projects as AOT-compiled native libraries for Linux platforms.
    Supports cross-compilation from Windows and native Linux builds.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Architecture
    Target architecture (x64 or arm64). Default: Both
.PARAMETER Projects
    Specific projects to build. Default: All core projects
.PARAMETER OutputPath
    Output directory for built libraries. Default: ./Dist/Native
.PARAMETER CrossCompile
    Enable cross-compilation from Windows
.PARAMETER UseContainer
    Use container for isolated build environment
.EXAMPLE
    .\build-linux.ps1
    .\build-linux.ps1 -Architecture x64 -CrossCompile
    .\build-linux.ps1 -UseContainer -Configuration Debug
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("x64", "arm64", "Both")]
    [string]$Architecture = "Both",
    
    [string[]]$Projects = @("UvfLib.Core", "UvfLib.Storage", "UvfLib.Vault"),
    
    [string]$OutputPath = "./Dist/Native",
    
    [switch]$CrossCompile,
    
    [switch]$UseContainer,
    
    [switch]$Verbose
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"

# Determine if we're running on Linux
$IsLinux = $PSVersionTable.Platform -eq "Unix" -and $PSVersionTable.OS -like "*Linux*"
$IsWindows = $PSVersionTable.Platform -eq "Win32NT" -or $PSVersionTable.PSEdition -eq "Desktop"

# Determine target platforms
$Platforms = @()
if ($Architecture -eq "Both") {
    $Platforms = @("linux-x64", "linux-arm64")
} else {
    $Platforms = @("linux-$Architecture")
}

# Linux-specific build optimizations
$LinuxOptimizations = @{
    # Performance optimizations
    "IlcOptimizationPreference" = "Speed"
    "IlcFoldIdenticalMethodBodies" = "true"
    "IlcGenerateStackTraceData" = if ($Configuration -eq "Debug") { "true" } else { "false" }
    
    # Linux-specific settings
    "PublishTrimmed" = "true"
    "TrimMode" = "link"
    "StripSymbols" = if ($Configuration -eq "Release") { "true" } else { "false" }
    
    # Security settings
    "EnableUnsafeBinaryFormatterSerialization" = "false"
    "UseSystemResourceKeys" = "true"
    
    # Debug settings
    "DebugType" = if ($Configuration -eq "Debug") { "portable" } else { "none" }
    "DebugSymbols" = if ($Configuration -eq "Debug") { "true" } else { "false" }
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

# Check Linux build prerequisites
function Test-LinuxPrerequisites {
    Write-Header "Checking Linux Build Prerequisites"
    
    if ($IsLinux) {
        # Native Linux build
        Write-Info "Running on native Linux"
        
        # Check for required packages
        $requiredPackages = @("build-essential", "libc6-dev", "libicu-dev", "libssl-dev")
        
        foreach ($package in $requiredPackages) {
            try {
                $result = & dpkg -l $package 2>$null
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "Package found: $package"
                } else {
                    Write-Warning "Package not found: $package. Install with: sudo apt-get install $package"
                }
            }
            catch {
                Write-Warning "Could not check package: $package"
            }
        }
        
        # Check GCC/Clang
        try {
            $gccVersion = & gcc --version 2>$null | Select-Object -First 1
            if ($gccVersion) {
                Write-Success "GCC found: $gccVersion"
            }
        }
        catch {
            Write-Warning "GCC not found"
        }
        
        try {
            $clangVersion = & clang --version 2>$null | Select-Object -First 1
            if ($clangVersion) {
                Write-Success "Clang found: $clangVersion"
            }
        }
        catch {
            Write-Info "Clang not found (optional)"
        }
    }
    elseif ($CrossCompile -and $IsWindows) {
        # Cross-compilation from Windows
        Write-Info "Cross-compiling from Windows to Linux"
        
        # Check for cross-compilation tools
        Write-Warning "Cross-compilation requires additional setup. Consider using WSL or containers."
        
        if ($UseContainer) {
            Test-ContainerPrerequisites
        }
    }
    else {
        Write-Error "Unsupported platform for Linux builds. Use -CrossCompile on Windows or run on Linux."
        exit 1
    }
}

# Check container prerequisites
function Test-ContainerPrerequisites {
    Write-Header "Checking Container Prerequisites"
    
    # Check Docker
    try {
        $dockerVersion = & docker --version 2>$null
        if ($dockerVersion) {
            Write-Success "Docker found: $dockerVersion"
        } else {
            Write-Error "Docker not found. Install Docker Desktop for container builds."
            exit 1
        }
    }
    catch {
        Write-Error "Docker not available. Install Docker Desktop for container builds."
        exit 1
    }
    
    # Check if Docker is running
    try {
        & docker info >$null 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Docker daemon is running"
        } else {
            Write-Error "Docker daemon is not running. Start Docker Desktop."
            exit 1
        }
    }
    catch {
        Write-Error "Cannot connect to Docker daemon"
        exit 1
    }
}

# Build project for Linux
function Build-LinuxProject {
    param(
        [string]$ProjectName,
        [string]$Platform,
        [string]$Config
    )
    
    if ($UseContainer) {
        return Build-LinuxProjectInContainer -ProjectName $ProjectName -Platform $Platform -Config $Config
    } else {
        return Build-LinuxProjectNative -ProjectName $ProjectName -Platform $Platform -Config $Config
    }
}

# Build project natively (on Linux or cross-compile)
function Build-LinuxProjectNative {
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
    
    # Build arguments
    $buildArgs = @(
        "publish"
        $projectPath
        "--configuration", $Config
        "--runtime", $Platform
        "--self-contained", "true"
        "--output", $outputDir
        "/p:PublishAot=true"
        "/p:PublishSingleFile=false"
        "--verbosity", (if ($Verbose) { "detailed" } else { "minimal" })
    )
    
    # Add Linux-specific optimizations
    foreach ($key in $LinuxOptimizations.Keys) {
        $buildArgs += "/p:$key=$($LinuxOptimizations[$key])"
    }
    
    # Cross-compilation settings
    if ($CrossCompile -and $IsWindows) {
        # Add cross-compilation specific settings
        $buildArgs += "/p:CrossGen=false"  # Disable CrossGen for cross-compilation
        $buildArgs += "/p:UseAppHost=true"
    }
    
    try {
        Write-Info "Executing: dotnet $($buildArgs -join ' ')"
        & dotnet @buildArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Built $ProjectName for $Platform"
            
            # Create Linux package structure
            New-LinuxPackageStructure -ProjectName $ProjectName -Platform $Platform -OutputDir $outputDir
            
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

# Build project in container
function Build-LinuxProjectInContainer {
    param(
        [string]$ProjectName,
        [string]$Platform,
        [string]$Config
    )
    
    Write-Info "Building $ProjectName for $Platform in container"
    
    $containerName = "uvf-linux-build-$($Platform.Replace('-', ''))"
    $outputDir = Join-Path $OutputPath $Platform $ProjectName
    
    # Ensure output directory exists
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    
    # Create Dockerfile for build
    $dockerfileContent = @"
FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build

# Install native dependencies
RUN apt-get update && apt-get install -y \
    build-essential \
    libc6-dev \
    libicu-dev \
    libssl-dev \
    zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# Set working directory
WORKDIR /src

# Copy project files
COPY . .

# Build the project
RUN dotnet publish Uvf.Net/$ProjectName/$ProjectName.csproj \
    --configuration $Config \
    --runtime $Platform \
    --self-contained true \
    --output /app \
    /p:PublishAot=true \
    /p:PublishTrimmed=true \
    /p:StripSymbols=$(if ($Config -eq "Release") { "true" } else { "false" })

FROM scratch AS export
COPY --from=build /app /
"@
    
    $dockerfilePath = Join-Path $ProjectRoot "Dockerfile.linux-build"
    Set-Content $dockerfilePath -Value $dockerfileContent -Encoding UTF8
    
    try {
        # Build in container
        $dockerArgs = @(
            "build"
            "--file", $dockerfilePath
            "--target", "export"
            "--output", $outputDir
            $ProjectRoot
        )
        
        Write-Info "Building in container: docker $($dockerArgs -join ' ')"
        & docker @dockerArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Container build completed for $ProjectName ($Platform)"
            
            # Clean up dockerfile
            Remove-Item $dockerfilePath -Force -ErrorAction SilentlyContinue
            
            # Create package structure
            New-LinuxPackageStructure -ProjectName $ProjectName -Platform $Platform -OutputDir $outputDir
            
            return $true
        } else {
            Write-Error "Container build failed with exit code: $LASTEXITCODE"
            return $false
        }
    }
    catch {
        Write-Error "Exception during container build: $_"
        return $false
    }
    finally {
        # Clean up dockerfile
        Remove-Item $dockerfilePath -Force -ErrorAction SilentlyContinue
    }
}

# Create Linux-specific package structure
function New-LinuxPackageStructure {
    param(
        [string]$ProjectName,
        [string]$Platform,
        [string]$OutputDir
    )
    
    Write-Info "Creating Linux package structure for $ProjectName"
    
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
            ".so" {
                Move-Item $file.FullName $libDir -Force
            }
            ".dll" {
                if ($file.Name -like "*$ProjectName*") {
                    Move-Item $file.FullName $libDir -Force
                } else {
                    Move-Item $file.FullName $binDir -Force
                }
            }
            ".xml" {
                Move-Item $file.FullName $docsDir -Force
            }
            ".pdb" {
                if ($Configuration -eq "Debug") {
                    Move-Item $file.FullName $libDir -Force
                }
            }
            "" {
                # Executable without extension
                if ($file.Name -eq $ProjectName) {
                    Move-Item $file.FullName $binDir -Force
                }
            }
            default {
                # Keep other files in root
            }
        }
    }
    
    # Set executable permissions on Linux
    if ($IsLinux) {
        $binFiles = Get-ChildItem $binDir -File
        foreach ($binFile in $binFiles) {
            & chmod +x $binFile.FullName
        }
    }
    
    # Create version info
    $versionInfo = @{
        ProjectName = $ProjectName
        Platform = $Platform
        Configuration = $Configuration
        BuildTime = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        DotNetVersion = (dotnet --version)
        BuildEnvironment = if ($UseContainer) { "Container" } elseif ($CrossCompile) { "Cross-compile" } else { "Native" }
        Architecture = $Platform.Split('-')[1]
        Optimizations = $LinuxOptimizations
    }
    
    $versionPath = Join-Path $OutputDir "version.json"
    $versionInfo | ConvertTo-Json -Depth 10 | Set-Content $versionPath -Encoding UTF8
    
    Write-Info "Package structure created for $ProjectName ($Platform)"
}

# Generate Linux documentation
function New-LinuxDocumentation {
    Write-Header "Generating Linux Documentation"
    
    $readmePath = Join-Path $OutputPath "README-Linux.md"
    $readme = @"
# UVF.NET Linux Native Libraries

This directory contains AOT-compiled native libraries for Linux platforms.

## Platforms

- **linux-x64**: Linux 64-bit (Intel/AMD)
- **linux-arm64**: Linux 64-bit (ARM)

## Directory Structure

```
linux-x64/
├── UvfLib.Core/
│   ├── bin/          # Executables
│   ├── lib/          # Shared libraries (.so files)
│   ├── include/      # Header files (if any)
│   ├── docs/         # XML documentation
│   └── version.json  # Build information
├── UvfLib.Storage/
└── UvfLib.Vault/
```

## Usage

### .NET Applications

```csharp
using UvfLib.Core.Api;
using UvfLib.Storage;

var vault = await VaultManager.CreateUvfVaultAsync("path/to/vault", "password");
```

### Native Applications

```c
// Load the shared library
void* handle = dlopen("./lib/UvfLib.Core.so", RTLD_LAZY);
// Use exported functions
```

## System Requirements

- Linux kernel 3.17 or later
- glibc 2.17 or later (most distributions since ~2013)
- libicu (usually pre-installed)
- libssl (OpenSSL 1.1 or later)

### Ubuntu/Debian
```bash
sudo apt-get update
sudo apt-get install libc6 libicu70 libssl3
```

### CentOS/RHEL/Fedora
```bash
sudo yum install glibc libicu openssl-libs
# or
sudo dnf install glibc libicu openssl-libs
```

## Performance Notes

- Libraries are optimized for speed
- Symbols are $(if ($Configuration -eq "Debug") { "included" } else { "stripped" }) for $(if ($Configuration -eq "Debug") { "debugging" } else { "smaller size" })
- Native AOT provides near-zero startup time

## Build Information

- Configuration: $Configuration
- Build Method: $(if ($UseContainer) { "Container" } elseif ($CrossCompile) { "Cross-compile" } else { "Native" })
- Build Date: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
- .NET Version: $(dotnet --version)

## Troubleshooting

### Library Loading Issues

1. **Library not found**: Ensure `LD_LIBRARY_PATH` includes the lib directory
   ```bash
   export LD_LIBRARY_PATH=/path/to/uvf/lib:$LD_LIBRARY_PATH
   ```

2. **Missing dependencies**: Install required system packages
   ```bash
   ldd lib/UvfLib.Core.so  # Check dependencies
   ```

3. **Permission denied**: Ensure execute permissions
   ```bash
   chmod +x bin/*
   ```

### Performance Issues

- Use `perf` to profile: `perf record -g ./your-app`
- Check CPU governor: `cat /sys/devices/system/cpu/cpu0/cpufreq/scaling_governor`
- For best performance, use `performance` governor

"@

    Set-Content $readmePath -Value $readme -Encoding UTF8
    Write-Success "Linux documentation created: $readmePath"
}

# Main execution
function Main {
    $startTime = Get-Date
    
    Write-Header "UVF.NET Linux AOT Build"
    Write-Host "Configuration: $Configuration" -ForegroundColor White
    Write-Host "Architecture: $Architecture" -ForegroundColor White
    Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor White
    Write-Host "Projects: $($Projects -join ', ')" -ForegroundColor White
    Write-Host "Cross-compile: $CrossCompile" -ForegroundColor White
    Write-Host "Use container: $UseContainer" -ForegroundColor White
    
    try {
        # Prerequisites
        Test-LinuxPrerequisites
        
        # Build projects
        Write-Header "Building Linux Projects"
        $buildResults = @{}
        $totalBuilds = $Projects.Count * $Platforms.Count
        $currentBuild = 0
        
        foreach ($project in $Projects) {
            $buildResults[$project] = @{}
            
            foreach ($platform in $Platforms) {
                $currentBuild++
                Write-Host "`n[$currentBuild/$totalBuilds] Building $project for $platform..." -ForegroundColor Yellow
                
                $success = Build-LinuxProject -ProjectName $project -Platform $platform -Config $Configuration
                $buildResults[$project][$platform] = $success
            }
        }
        
        # Generate documentation
        New-LinuxDocumentation
        
        # Summary
        Write-Header "Linux Build Summary"
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
        
        Write-Host "`nLinux build completed in $($duration.ToString('mm\:ss'))" -ForegroundColor Cyan
        Write-Host "✅ Successful builds: $successCount" -ForegroundColor Green
        if ($failCount -gt 0) {
            Write-Host "❌ Failed builds: $failCount" -ForegroundColor Red
            exit 1
        }
        
        Write-Success "Linux libraries available in: $OutputPath"
    }
    catch {
        Write-Error "Linux build failed: $_"
        exit 1
    }
}

# Execute main function
Main 