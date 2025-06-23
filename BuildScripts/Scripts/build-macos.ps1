#!/usr/bin/env pwsh
<#
.SYNOPSIS
    macOS-specific AOT build script for UVF.NET library
.DESCRIPTION
    Builds UVF.NET projects as AOT-compiled native libraries for macOS platforms.
    Supports both Intel (x64) and Apple Silicon (arm64) architectures.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Architecture
    Target architecture (x64, arm64, or Both). Default: Both
.PARAMETER Projects
    Specific projects to build. Default: All core projects
.PARAMETER OutputPath
    Output directory for built libraries. Default: ./Dist/Native
.PARAMETER CreateUniversalBinary
    Create universal binaries that work on both Intel and Apple Silicon
.PARAMETER CodeSign
    Code sign the binaries (requires developer certificate)
.EXAMPLE
    .\build-macos.ps1
    .\build-macos.ps1 -Architecture arm64 -CodeSign
    .\build-macos.ps1 -CreateUniversalBinary -Configuration Debug
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [ValidateSet("x64", "arm64", "Both")]
    [string]$Architecture = "Both",
    
    [string[]]$Projects = @("UvfLib.Core", "UvfLib.Storage", "UvfLib.Vault"),
    
    [string]$OutputPath = "./Dist/Native",
    
    [switch]$CreateUniversalBinary,
    
    [switch]$CodeSign,
    
    [switch]$Verbose
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"

# Determine if we're running on macOS
$IsMacOS = $PSVersionTable.Platform -eq "Unix" -and $PSVersionTable.OS -like "*Darwin*"

# Determine target platforms
$Platforms = @()
if ($Architecture -eq "Both") {
    $Platforms = @("osx-x64", "osx-arm64")
} else {
    $Platforms = @("osx-$Architecture")
}

# macOS-specific build optimizations
$MacOSOptimizations = @{
    # Performance optimizations
    "IlcOptimizationPreference" = "Speed"
    "IlcFoldIdenticalMethodBodies" = "true"
    "IlcGenerateStackTraceData" = if ($Configuration -eq "Debug") { "true" } else { "false" }
    
    # macOS-specific settings
    "PublishTrimmed" = "true"
    "TrimMode" = "link"
    "StripSymbols" = if ($Configuration -eq "Release") { "true" } else { "false" }
    
    # Apple-specific optimizations
    "UseAppHost" = "true"
    "PublishSingleFile" = "false"  # Better for dylib creation
    
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

# Check macOS build prerequisites
function Test-MacOSPrerequisites {
    Write-Header "Checking macOS Build Prerequisites"
    
    if (-not $IsMacOS) {
        Write-Warning "Not running on macOS. Cross-compilation from other platforms is limited."
        Write-Info "For best results, build on macOS with Xcode Command Line Tools installed."
    }
    
    if ($IsMacOS) {
        # Check macOS version
        $osVersion = & sw_vers -productVersion 2>$null
        if ($osVersion) {
            Write-Success "macOS version: $osVersion"
            
            # Check if version supports required features
            $versionParts = $osVersion.Split('.')
            $majorVersion = [int]$versionParts[0]
            $minorVersion = if ($versionParts.Length -gt 1) { [int]$versionParts[1] } else { 0 }
            
            if ($majorVersion -lt 10 -or ($majorVersion -eq 10 -and $minorVersion -lt 15)) {
                Write-Warning "macOS 10.15 (Catalina) or later recommended for best compatibility"
            }
        }
        
        # Check Xcode Command Line Tools
        try {
            $xcodeVersion = & xcode-select --version 2>$null
            if ($xcodeVersion) {
                Write-Success "Xcode Command Line Tools: $xcodeVersion"
            }
        }
        catch {
            Write-Warning "Xcode Command Line Tools not found. Install with: xcode-select --install"
        }
        
        # Check for required tools
        $tools = @("clang", "lipo", "otool", "install_name_tool")
        foreach ($tool in $tools) {
            try {
                $toolPath = & which $tool 2>$null
                if ($toolPath) {
                    Write-Success "Tool found: $tool at $toolPath"
                } else {
                    Write-Warning "Tool not found: $tool"
                }
            }
            catch {
                Write-Warning "Could not check tool: $tool"
            }
        }
        
        # Check architecture
        $machineArch = & uname -m 2>$null
        Write-Info "Machine architecture: $machineArch"
        
        if ($machineArch -eq "arm64") {
            Write-Info "Running on Apple Silicon"
        } elseif ($machineArch -eq "x86_64") {
            Write-Info "Running on Intel Mac"
        }
        
        # Check for code signing if requested
        if ($CodeSign) {
            Test-CodeSigningSetup
        }
    }
}

# Check code signing setup
function Test-CodeSigningSetup {
    Write-Header "Checking Code Signing Setup"
    
    if (-not $IsMacOS) {
        Write-Warning "Code signing only available on macOS"
        return
    }
    
    try {
        # Check for developer certificates
        $certs = & security find-identity -v -p codesigning 2>$null
        if ($certs -and $certs.Count -gt 0) {
            Write-Success "Code signing certificates found"
            if ($Verbose) {
                Write-Info "Available certificates:"
                $certs | ForEach-Object { Write-Info "  $_" }
            }
        } else {
            Write-Warning "No code signing certificates found. Binaries will not be signed."
            $script:CodeSign = $false
        }
    }
    catch {
        Write-Warning "Could not check code signing certificates"
        $script:CodeSign = $false
    }
}

# Build project for macOS
function Build-MacOSProject {
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
        "--verbosity", $(if ($Verbose) { "detailed" } else { "minimal" })
    )
    
    # Add macOS-specific optimizations
    foreach ($key in $MacOSOptimizations.Keys) {
        $buildArgs += "/p:$key=$($MacOSOptimizations[$key])"
    }
    
    # Architecture-specific settings
    if ($Platform -eq "osx-arm64") {
        $buildArgs += "/p:RuntimeIdentifier=osx-arm64"
    } elseif ($Platform -eq "osx-x64") {
        $buildArgs += "/p:RuntimeIdentifier=osx-x64"
    }
    
    try {
        Write-Info "Executing: dotnet $($buildArgs -join ' ')"
        & dotnet @buildArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Built $ProjectName for $Platform"
            
            # Post-build processing
            Invoke-MacOSPostBuild -ProjectName $ProjectName -Platform $Platform -OutputDir $outputDir
            
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

# macOS post-build processing
function Invoke-MacOSPostBuild {
    param(
        [string]$ProjectName,
        [string]$Platform,
        [string]$OutputDir
    )
    
    Write-Info "Post-build processing for $ProjectName ($Platform)"
    
    if ($IsMacOS) {
        # Fix dylib install names
        $dylibs = Get-ChildItem $OutputDir -Filter "*.dylib"
        foreach ($dylib in $dylibs) {
            try {
                # Set install name
                & install_name_tool -id "@rpath/$($dylib.Name)" $dylib.FullName
                Write-Info "Fixed install name for $($dylib.Name)"
            }
            catch {
                Write-Warning "Could not fix install name for $($dylib.Name)"
            }
        }
        
        # Code sign if requested
        if ($CodeSign) {
            Invoke-CodeSigning -OutputDir $OutputDir -ProjectName $ProjectName
        }
    }
    
    # Create package structure
    New-MacOSPackageStructure -ProjectName $ProjectName -Platform $Platform -OutputDir $OutputDir
}

# Code sign binaries
function Invoke-CodeSigning {
    param(
        [string]$OutputDir,
        [string]$ProjectName
    )
    
    Write-Info "Code signing binaries for $ProjectName"
    
    if (-not $IsMacOS) {
        Write-Warning "Code signing only available on macOS"
        return
    }
    
    # Find binaries to sign
    $binaries = @()
    $binaries += Get-ChildItem $OutputDir -Filter "*.dylib"
    $binaries += Get-ChildItem $OutputDir -Filter $ProjectName -File
    
    foreach ($binary in $binaries) {
        try {
            # Sign with ad-hoc signature (for local development)
            & codesign --sign - --force --deep $binary.FullName
            
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Signed: $($binary.Name)"
            } else {
                Write-Warning "Failed to sign: $($binary.Name)"
            }
        }
        catch {
            Write-Warning "Exception signing $($binary.Name): $_"
        }
    }
}

# Create universal binaries
function New-UniversalBinaries {
    param(
        [string]$ProjectName
    )
    
    if (-not $IsMacOS -or -not $CreateUniversalBinary) {
        return
    }
    
    Write-Header "Creating Universal Binaries for $ProjectName"
    
    $x64Dir = Join-Path $OutputPath "osx-x64" $ProjectName
    $arm64Dir = Join-Path $OutputPath "osx-arm64" $ProjectName
    $universalDir = Join-Path $OutputPath "osx-universal" $ProjectName
    
    if (-not (Test-Path $x64Dir) -or -not (Test-Path $arm64Dir)) {
        Write-Warning "Both x64 and arm64 builds required for universal binary"
        return
    }
    
    # Create universal directory
    if (-not (Test-Path $universalDir)) {
        New-Item -ItemType Directory -Path $universalDir -Force | Out-Null
    }
    
    # Find binaries to merge
    $x64Binaries = Get-ChildItem $x64Dir -File
    $arm64Binaries = Get-ChildItem $arm64Dir -File
    
    foreach ($x64Binary in $x64Binaries) {
        $arm64Binary = $arm64Binaries | Where-Object { $_.Name -eq $x64Binary.Name }
        
        if ($arm64Binary) {
            $universalPath = Join-Path $universalDir $x64Binary.Name
            
            try {
                # Create universal binary with lipo
                & lipo -create $x64Binary.FullName $arm64Binary.FullName -output $universalPath
                
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "Created universal binary: $($x64Binary.Name)"
                    
                    # Verify the universal binary
                    $archInfo = & lipo -info $universalPath 2>$null
                    Write-Info "Architecture info: $archInfo"
                } else {
                    Write-Warning "Failed to create universal binary for: $($x64Binary.Name)"
                }
            }
            catch {
                Write-Warning "Exception creating universal binary for $($x64Binary.Name): $_"
            }
        }
    }
    
    # Copy other files that don't need merging
    $otherFiles = @("*.json", "*.xml", "*.pdb")
    foreach ($pattern in $otherFiles) {
        $files = Get-ChildItem $x64Dir -Filter $pattern
        foreach ($file in $files) {
            Copy-Item $file.FullName $universalDir -Force
        }
    }
    
    # Create version info for universal build
    New-MacOSPackageStructure -ProjectName $ProjectName -Platform "osx-universal" -OutputDir $universalDir
}

# Create macOS-specific package structure
function New-MacOSPackageStructure {
    param(
        [string]$ProjectName,
        [string]$Platform,
        [string]$OutputDir
    )
    
    Write-Info "Creating macOS package structure for $ProjectName"
    
    # Create subdirectories
    $binDir = Join-Path $OutputDir "bin"
    $libDir = Join-Path $OutputDir "lib"
    $frameworksDir = Join-Path $OutputDir "Frameworks"
    $docsDir = Join-Path $OutputDir "docs"
    
    foreach ($dir in @($binDir, $libDir, $frameworksDir, $docsDir)) {
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }
    
    # Move files to appropriate locations
    $files = Get-ChildItem $OutputDir -File
    
    foreach ($file in $files) {
        switch ($file.Extension.ToLower()) {
            ".dylib" {
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
    
    # Create version info
    $versionInfo = @{
        ProjectName = $ProjectName
        Platform = $Platform
        Configuration = $Configuration
        BuildTime = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"
        DotNetVersion = (dotnet --version)
        Architecture = if ($Platform -eq "osx-universal") { "universal" } else { $Platform.Split('-')[1] }
        CodeSigned = $CodeSign
        UniversalBinary = $Platform -eq "osx-universal"
        Optimizations = $MacOSOptimizations
    }
    
    if ($IsMacOS) {
        $versionInfo.MacOSVersion = & sw_vers -productVersion 2>$null
        $versionInfo.XcodeVersion = & xcode-select --version 2>$null
    }
    
    $versionPath = Join-Path $OutputDir "version.json"
    $versionInfo | ConvertTo-Json -Depth 10 | Set-Content $versionPath -Encoding UTF8
    
    Write-Info "Package structure created for $ProjectName ($Platform)"
}

# Generate macOS documentation
function New-MacOSDocumentation {
    Write-Header "Generating macOS Documentation"
    
    $readmePath = Join-Path $OutputPath "README-macOS.md"
    $readme = @"
# UVF.NET macOS Native Libraries

This directory contains AOT-compiled native libraries for macOS platforms.

## Platforms

- **osx-x64**: macOS Intel (x86_64)
- **osx-arm64**: macOS Apple Silicon (ARM64)
$(if ($CreateUniversalBinary) { "`n- **osx-universal**: Universal binaries (Intel + Apple Silicon)" })

## Directory Structure

```
osx-x64/
├── UvfLib.Core/
│   ├── bin/          # Executables
│   ├── lib/          # Dynamic libraries (.dylib files)
│   ├── Frameworks/   # Framework bundles (if any)
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
// Load the dynamic library
void* handle = dlopen("./lib/UvfLib.Core.dylib", RTLD_LAZY);
// Use exported functions
```

### Frameworks (if available)

```objc
// Link against framework
#import <UvfLib/UvfLib.h>
```

## System Requirements

- macOS 10.15 (Catalina) or later
- For Intel Macs: x86_64 architecture
- For Apple Silicon: ARM64 architecture
$(if ($CreateUniversalBinary) { "- Universal binaries work on both architectures" })

## Installation

### Using Homebrew (if published)
```bash
brew install uvf-net
```

### Manual Installation
1. Download the appropriate platform package
2. Extract to desired location
3. Add lib directory to `DYLD_LIBRARY_PATH` if needed:
   ```bash
   export DYLD_LIBRARY_PATH=/path/to/uvf/lib:$DYLD_LIBRARY_PATH
   ```

## Code Signing

$(if ($CodeSign) {
"✅ Binaries are code signed for security and compatibility."
} else {
"⚠️  Binaries are not code signed. You may need to allow them in System Preferences > Security & Privacy."
})

To manually allow unsigned binaries:
```bash
sudo spctl --master-disable  # Disable Gatekeeper (not recommended)
# or
xattr -d com.apple.quarantine /path/to/library  # Remove quarantine flag
```

## Performance Notes

- Libraries are optimized for speed
- Native AOT provides excellent startup performance
- Universal binaries may be slightly larger but work on all Macs

## Build Information

- Configuration: $Configuration
- Build Date: $(Get-Date -Format "yyyy-MM-dd HH`:mm`:ss")
- .NET Version: $(dotnet --version)
$(if ($CreateUniversalBinary) { "- Universal Binary: Yes" })
$(if ($CodeSign) { "- Code Signed: Yes" })

## Troubleshooting

### Library Loading Issues

1. **Library not found**: Check `DYLD_LIBRARY_PATH`
   ```bash
   echo $DYLD_LIBRARY_PATH
   export DYLD_LIBRARY_PATH=/path/to/uvf/lib:$DYLD_LIBRARY_PATH
   ```

2. **Code signing issues**: 
   ```bash
   codesign -v /path/to/library  # Verify signature
   spctl -a -v /path/to/library  # Check Gatekeeper status
   ```

3. **Architecture mismatch**: Use universal binaries or correct architecture
   ```bash
   lipo -info /path/to/library  # Check supported architectures
   arch -x86_64 ./your-app     # Force Intel mode on Apple Silicon
   ```

### Performance Issues

- Use Instruments.app for profiling
- Check Activity Monitor for CPU usage
- Ensure SIP (System Integrity Protection) allows your use case

### Common Errors

- **"cannot be opened because the developer cannot be verified"**: Code signing issue
- **"no suitable image found"**: Architecture mismatch
- **"Library not loaded"**: Missing dependencies or incorrect path

"@

    Set-Content $readmePath -Value $readme -Encoding UTF8
    Write-Success "macOS documentation created: $readmePath"
}

# Main execution
function Main {
    $startTime = Get-Date
    
    Write-Header "UVF.NET macOS AOT Build"
    Write-Host "Configuration: $Configuration" -ForegroundColor White
    Write-Host "Architecture: $Architecture" -ForegroundColor White
    Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor White
    Write-Host "Projects: $($Projects -join ', ')" -ForegroundColor White
    Write-Host "Universal Binary: $CreateUniversalBinary" -ForegroundColor White
    Write-Host "Code Sign: $CodeSign" -ForegroundColor White
    
    try {
        # Prerequisites
        Test-MacOSPrerequisites
        
        # Build projects
        Write-Header "Building macOS Projects"
        $buildResults = @{}
        $totalBuilds = $Projects.Count * $Platforms.Count
        $currentBuild = 0
        
        foreach ($project in $Projects) {
            $buildResults[$project] = @{}
            
            foreach ($platform in $Platforms) {
                $currentBuild++
                Write-Host "`n[$currentBuild/$totalBuilds] Building $project for $platform..." -ForegroundColor Yellow
                
                $success = Build-MacOSProject -ProjectName $project -Platform $platform -Config $Configuration
                $buildResults[$project][$platform] = $success
            }
            
            # Create universal binaries if requested
            if ($CreateUniversalBinary -and $Architecture -eq "Both") {
                Write-Host "`nCreating universal binary for $project..." -ForegroundColor Yellow
                New-UniversalBinaries -ProjectName $project
            }
        }
        
        # Generate documentation
        New-MacOSDocumentation
        
        # Summary
        Write-Header "macOS Build Summary"
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
            
            if ($CreateUniversalBinary -and $Architecture -eq "Both") {
                $universalDir = Join-Path $OutputPath "osx-universal" $project
                if (Test-Path $universalDir) {
                    Write-Host "  ✅ osx-universal" -ForegroundColor Green
                } else {
                    Write-Host "  ❌ osx-universal" -ForegroundColor Red
                }
            }
        }
        
        $endTime = Get-Date
        $duration = $endTime - $startTime
        
        Write-Host "`nmacOS build completed in $($duration.ToString('mm`:ss'))" -ForegroundColor Cyan
        Write-Host "✅ Successful builds: $successCount" -ForegroundColor Green
        if ($failCount -gt 0) {
            Write-Host "❌ Failed builds: $failCount" -ForegroundColor Red
            exit 1
        }
        
        Write-Success "macOS libraries available in: $OutputPath"
        
        if ($CreateUniversalBinary) {
            Write-Success "Universal binaries created for maximum compatibility"
        }
        
        if ($CodeSign) {
            Write-Success "Binaries are code signed"
        } else {
            Write-Warning "Binaries are not code signed. Users may need to allow them manually."
        }
    }
    catch {
        Write-Error "macOS build failed: $_"
        exit 1
    }
}

# Execute main function
Main 