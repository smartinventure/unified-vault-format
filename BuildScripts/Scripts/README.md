# UVF.NET Build System

This directory contains the streamlined build system for creating AOT-compiled native libraries and multi-language bindings for UVF.NET.

## Overview

The build system transforms the .NET UVF library into native libraries for multiple platforms and creates language-specific packages for easy distribution and consumption.

```
Source Code (.NET) → AOT Compilation → Native Libraries → Language Bindings → Distribution Packages
```

## Quick Start

### Build Everything (Recommended)
```powershell
# Build for current platform with all language packages
.\Scripts\build-all.ps1

# Build for specific platforms
.\Scripts\build-all.ps1 -Platforms "win-x64,linux-x64,osx-x64"

# Debug build with verbose output
.\Scripts\build-all.ps1 -Configuration Debug -Verbose -Clean
```

### AOT Libraries Only
```powershell
# Build native libraries for current platform
.\Scripts\build-aot.ps1

# Build for multiple platforms
.\Scripts\build-aot.ps1 -Platforms "win-x64,linux-x64" -Clean
```

### Language Packages Only
```powershell
# Create packages for all supported languages
.\Scripts\package-bindings.ps1

# Specific languages only
.\Scripts\package-bindings.ps1 -Languages "CSharp,Python"
```

## Build Scripts

### Essential Scripts

| Script | Purpose | Platforms |
|--------|---------|-----------|
| `build-all.ps1` | **Master build script** - orchestrates everything | All |
| `build-aot.ps1` | AOT compilation for native libraries | All |
| `package-bindings.ps1` | Creates language-specific packages | All |
| `resolve-storagelib-simple.ps1` | Resolves StorageLib dependencies | All |

## Output Structure

```
Dist/
├── Native/                    # AOT-compiled native libraries
│   ├── win-x64/
│   │   ├── UvfLib.Core.dll   # Main library (1.36 MB)
│   │   ├── UvfLib.Core.pdb   # Debug symbols
│   │   ├── UvfLib.Core.xml   # API documentation
│   │   ├── UvfLib.Storage.dll
│   │   └── UvfLib.Vault.dll
│   ├── linux-x64/
│   ├── osx-x64/
│   └── build-manifest.json   # Build metadata
└── Packages/                  # Language-specific packages
    ├── nuget/                # .NET NuGet packages
    ├── npm/                  # Node.js packages
    └── pip/                  # Python packages
```

## Supported Platforms

### Native Compilation Targets

| Platform | Architecture | Status | Notes |
|----------|-------------|---------|-------|
| `win-x64` | Windows x64 | ✅ Full | Optimized for Windows |
| `win-arm64` | Windows ARM64 | ✅ Full | Surface Pro X, etc. |
| `linux-x64` | Linux x64 | ✅ Full | Most Linux distributions |
| `linux-arm64` | Linux ARM64 | ✅ Full | Raspberry Pi, ARM servers |
| `osx-x64` | macOS Intel | ✅ Full | Intel Macs |
| `osx-arm64` | macOS Apple Silicon | ✅ Full | M1/M2/M3 Macs |

### Language Bindings

| Language | Package Manager | Status | Package Name |
|----------|----------------|---------|--------------|
| **C#/.NET** | NuGet | ✅ Ready | `UvfLib.Native` |
| **Python** | PyPI | ✅ Ready | `uvf-vault` |
| **Node.js** | NPM | ✅ Ready | `uvf-vault` |

## Prerequisites

### All Platforms
- **.NET 8.0 SDK** or later
- **PowerShell 7.0+** (cross-platform)
- **Git** (for version detection)

### Windows
- **Visual Studio 2022** or **Build Tools for Visual Studio 2022**
- **Windows SDK** (latest)

### Linux
- **GCC** or **Clang**
- **Build essentials**: `sudo apt-get install build-essential libc6-dev libicu-dev libssl-dev`

### macOS
- **Xcode Command Line Tools**: `xcode-select --install`

## Build Options

### Common Parameters

| Parameter | Description | Default | Example |
|-----------|-------------|---------|---------|
| `-Configuration` | Build configuration | `Release` | `-Configuration Debug` |
| `-Platforms` | Target platforms | Current platform | `-Platforms "win-x64,linux-x64"` |
| `-Languages` | Language packages to create | CSharp,Python,NodeJs | `-Languages "CSharp,Python"` |
| `-Clean` | Clean before building | `false` | `-Clean` |
| `-Verbose` | Detailed logging | `false` | `-Verbose` |

### Advanced Options

| Parameter | Description | Scripts | Example |
|-----------|-------------|---------|---------|
| `-SkipTests` | Skip running tests | `build-all.ps1` | `-SkipTests` |
| `-SkipNativeLibraries` | Skip AOT compilation | `build-all.ps1` | `-SkipNativeLibraries` |
| `-Projects` | Specific projects to build | `build-aot.ps1` | `-Projects "UvfLib.Core"` |
| `-OutputPath` | Custom output directory | `build-aot.ps1`, `package-bindings.ps1` | `-OutputPath "./MyDist"` |

## Usage Examples

### Development Workflow
```powershell
# Quick development build for current platform
.\Scripts\build-all.ps1 -Configuration Debug

# Full release build for distribution
.\Scripts\build-all.ps1 -Configuration Release -Clean

# Test specific project only
.\Scripts\build-aot.ps1 -Projects "UvfLib.Core" -Configuration Debug
```

### CI/CD Pipeline
```powershell
# Complete build for all platforms
.\Scripts\build-all.ps1 -Platforms "win-x64,win-arm64,linux-x64,linux-arm64,osx-x64,osx-arm64" -Clean

# Create and test packages
.\Scripts\package-bindings.ps1 -Languages "CSharp,Python,NodeJs"
```

### Cross-Platform Development
```powershell
# Build for multiple platforms from Windows
.\Scripts\build-aot.ps1 -Platforms "win-x64,linux-x64,osx-x64"

# Create Python packages only
.\Scripts\package-bindings.ps1 -Languages "Python"
```

## Performance Optimizations

### AOT Compilation Features
- **Native code generation** - No JIT overhead
- **Trimming** - Removes unused code
- **Speed optimization** - Optimized for performance
- **Small binary size** - 1.36 MB for UvfLib.Core

### Key Benefits
- **Fast startup** - No JIT compilation delay
- **Cross-platform** - Single codebase, multiple targets
- **Language agnostic** - C-compatible exports
- **Production ready** - Optimized release builds

## Troubleshooting

### Common Issues

1. **StorageLib not found**
   ```powershell
   .\Scripts\resolve-storagelib-simple.ps1
   ```

2. **Build failures**
   ```powershell
   # Clean and rebuild
   .\Scripts\build-all.ps1 -Clean -Verbose
   ```

3. **Missing platforms**
   ```powershell
   # Check available platforms
   dotnet --list-runtimes
   ```

### Getting Help

- Check build logs with `-Verbose` flag
- Ensure all prerequisites are installed
- Verify .NET 8.0 SDK is available
- Check platform-specific requirements

## Version Information

- **Build System Version**: 2.0 (Streamlined)
- **Target .NET Version**: 8.0
- **Supported PowerShell**: 7.0+
- **AOT Compatibility**: Full support

---
*Generated by UVF.NET Build System - Streamlined Edition* 