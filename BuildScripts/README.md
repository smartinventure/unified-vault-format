# UVF.NET Build System

This directory contains the complete build system for creating AOT-compiled native libraries and multi-language bindings for UVF.NET.

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
.\Scripts\build-all.ps1 -Configuration Debug -Verbose
```

### Platform-Specific Builds
```powershell
# Windows only
.\Scripts\build-windows.ps1

# Linux only (supports cross-compilation and containers)
.\Scripts\build-linux.ps1 -CrossCompile

# macOS only (with universal binaries)
.\Scripts\build-macos.ps1 -CreateUniversalBinary
```

### Language Packages Only
```powershell
# Create packages for all languages
.\Scripts\package-bindings.ps1

# Specific languages only
.\Scripts\package-bindings.ps1 -Languages "CSharp,Python"
```

## Build Scripts

### Core Scripts

| Script | Purpose | Platforms |
|--------|---------|-----------|
| `build-all.ps1` | **Master build script** - orchestrates everything | All |
| `build-aot.ps1` | Main AOT compilation script | All |
| `package-bindings.ps1` | Creates language-specific packages | All |

### Platform-Specific Scripts

| Script | Purpose | Best Used On |
|--------|---------|--------------|
| `build-windows.ps1` | Windows-optimized AOT builds | Windows |
| `build-linux.ps1` | Linux builds with cross-compilation support | Linux, Windows (cross-compile) |
| `build-macos.ps1` | macOS builds with universal binary support | macOS |

## Output Structure

```
Dist/
├── Native/                    # AOT-compiled native libraries
│   ├── win-x64/
│   │   ├── UvfLib.Core/
│   │   │   ├── bin/          # Executables
│   │   │   ├── lib/          # Main libraries (.dll, .so, .dylib)
│   │   │   ├── docs/         # XML documentation
│   │   │   └── version.json  # Build metadata
│   │   ├── UvfLib.Storage/
│   │   └── UvfLib.Vault/
│   ├── linux-x64/
│   ├── osx-x64/
│   └── ...
└── Packages/                  # Language-specific packages
    ├── nuget/                # .NET NuGet packages
    ├── npm/                  # Node.js packages
    ├── pip/                  # Python packages
    ├── maven/                # Java Maven packages
    └── gem/                  # Ruby gems (future)
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
| `osx-universal` | macOS Universal | ✅ Full | Works on both Intel and Apple Silicon |

### Language Bindings

| Language | Package Manager | Status | Package Name |
|----------|----------------|---------|--------------|
| **C#/.NET** | NuGet | ✅ Ready | `UvfLib.Native` |
| **Python** | PyPI | ✅ Ready | `uvf-vault` |
| **Node.js** | NPM | ✅ Ready | `uvf-vault` |
| **Java** | Maven | ✅ Ready | `net.uvf:uvf-vault-native` |
| **C++** | Manual | 🚧 Planned | Header files + libraries |
| **Go** | Go Modules | 🚧 Planned | `github.com/uvf/go-uvf` |
| **PHP** | Composer | 🚧 Planned | `uvf/vault` |

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
- **macOS 10.15+** recommended

### Cross-Compilation
- **Docker** (for containerized Linux builds)
- **WSL2** (for Linux builds on Windows)

## Build Options

### Common Parameters

| Parameter | Description | Default | Example |
|-----------|-------------|---------|---------|
| `-Configuration` | Build configuration | `Release` | `-Configuration Debug` |
| `-Platforms` | Target platforms | Current platform | `-Platforms "win-x64,linux-x64"` |
| `-Languages` | Language packages to create | All supported | `-Languages "CSharp,Python"` |
| `-Clean` | Clean before building | `false` | `-Clean` |
| `-Verbose` | Detailed logging | `false` | `-Verbose` |

### Advanced Options

| Parameter | Description | Scripts | Example |
|-----------|-------------|---------|---------|
| `-Parallel` | Build platforms in parallel | `build-all.ps1` | `-Parallel` |
| `-CrossCompile` | Enable cross-compilation | `build-linux.ps1` | `-CrossCompile` |
| `-UseContainer` | Use Docker containers | `build-linux.ps1` | `-UseContainer` |
| `-CreateUniversalBinary` | Create macOS universal binaries | `build-macos.ps1` | `-CreateUniversalBinary` |
| `-CodeSign` | Code sign macOS binaries | `build-macos.ps1` | `-CodeSign` |
| `-OptimizeForSize` | Optimize for size vs speed | `build-windows.ps1` | `-OptimizeForSize` |

## Usage Examples

### Development Workflow
```powershell
# Quick development build for current platform
.\Scripts\build-all.ps1 -Configuration Debug

# Full release build for distribution
.\Scripts\build-all.ps1 -Configuration Release -Clean

# Test specific platform
.\Scripts\build-windows.ps1 -Architecture x64 -Configuration Debug
```

### CI/CD Pipeline
```powershell
# Complete build for all platforms
.\Scripts\build-all.ps1 -Platforms "win-x64,win-arm64,linux-x64,linux-arm64,osx-x64,osx-arm64" -Clean

# Create and publish packages
.\Scripts\package-bindings.ps1 -PublishToRegistry
```

### Cross-Platform Development
```powershell
# Build Linux binaries from Windows
.\Scripts\build-linux.ps1 -CrossCompile -UseContainer

# Build universal macOS binaries
.\Scripts\build-macos.ps1 -CreateUniversalBinary -CodeSign
```

## Performance Optimizations

### AOT Compilation Features
- **Native code generation** - No JIT overhead
- **Trimming** - Removes unused code
- **ReadyToRun** - Pre-compiled IL for faster startup
- **Profile-Guided Optimization** - Runtime profiling data used for optimization

### Platform-Specific Optimizations
- **Windows**: ReadyToRun, Tiered PGO, optimized for Windows APIs
- **Linux**: Stripped symbols, size optimization, glibc compatibility
- **macOS**: Universal binaries, install name fixing, code signing

## Troubleshooting

### Common Issues

#### Build Failures
```powershell
# Clean everything and rebuild
.\Scripts\build-all.ps1 -Clean -Verbose

# Check prerequisites
.\Scripts\build-all.ps1 -Verbose  # Will show detailed prerequisite checks
```

#### Platform-Specific Issues

**Windows:**
- Install Visual Studio Build Tools if missing
- Ensure Windows SDK is installed
- Run as Administrator if needed

**Linux:**
- Install build-essential: `sudo apt-get install build-essential`
- Check glibc version compatibility
- Use containers for consistent environment

**macOS:**
- Install Xcode Command Line Tools: `xcode-select --install`
- Allow unsigned binaries in Security & Privacy settings
- Use universal binaries for maximum compatibility

### Memory Requirements
- **Minimum**: 8GB RAM
- **Recommended**: 16GB+ RAM for parallel builds
- **Disk Space**: ~5GB for full build output

### Build Time Estimates
- **Single platform**: 2-5 minutes
- **All platforms (sequential)**: 15-30 minutes
- **All platforms (parallel)**: 5-10 minutes (with sufficient RAM)

## Contributing

### Adding New Platforms
1. Add platform identifier to `$Platforms` arrays
2. Update platform-specific build logic
3. Add platform-specific optimizations
4. Update documentation

### Adding New Language Bindings
1. Create binding source in `Bindings/{Language}/`
2. Add package creation logic to `package-bindings.ps1`
3. Create examples in `Bindings/{Language}/examples/`
4. Update this documentation

### Testing Changes
```powershell
# Test on current platform
.\Scripts\build-all.ps1 -Configuration Debug -SkipPackaging

# Test specific changes
.\Scripts\build-aot.ps1 -Platforms "win-x64" -Verbose
```

## License

This build system is part of the UVF.NET project and is licensed under the same terms as the main project.

---

**Need Help?** 
- Check the verbose output: add `-Verbose` to any script
- Review the build report: `Dist/build-report.md`
- Open an issue with the full build log 