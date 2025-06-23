# UVF.NET Build Scripts

This directory contains PowerShell scripts for building UVF.NET as AOT-compiled native libraries and creating language bindings.

## 🚨 Important: StorageLib Dependency

UVF.NET depends on **StorageLib** from your **FolderMagic** solution. For AOT compilation to work, we need **source code**, not just NuGet packages.

### Quick Setup (Local Development)

```bash
# 1. Resolve StorageLib source code
.\Scripts\resolve-storagelib.ps1

# 2. Build everything
.\Scripts\build-all.ps1
```

This will:
- ✅ Find your local StorageLib at `D:\__PROGRAMMING\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj`
- ✅ Replace NuGet references with project references
- ✅ Enable full AOT compilation with StorageLib source

### Restore NuGet References (After AOT Build)

```bash
# Restore original NuGet references for normal development
.\Scripts\resolve-storagelib.ps1 -Restore
```

## Build Scripts Overview

### Core Scripts

| Script | Purpose | When to Use |
|--------|---------|-------------|
| `resolve-storagelib.ps1` | Resolve StorageLib source for AOT | Before any AOT build |
| `build-all.ps1` | Complete build pipeline | Main entry point |
| `build-aot.ps1` | AOT native libraries only | AOT compilation only |
| `package-bindings.ps1` | Language-specific packages | After AOT build |

### Platform-Specific Scripts

| Script | Purpose | Platforms |
|--------|---------|-----------|
| `build-windows.ps1` | Windows optimizations | win-x64, win-arm64 |
| `build-linux.ps1` | Linux builds | linux-x64, linux-arm64 |
| `build-macos.ps1` | macOS builds | osx-x64, osx-arm64 |

## Usage Examples

### 🏃‍♂️ Quick Start (Local Development)

```bash
# Build for current platform only
.\Scripts\build-all.ps1

# Build with specific configuration
.\Scripts\build-all.ps1 -Configuration Debug
```

### 🌍 Cross-Platform Build

```bash
# Build for multiple platforms
.\Scripts\build-all.ps1 -Platforms "win-x64,linux-x64,osx-x64" -Parallel

# Build for all platforms
.\Scripts\build-aot.ps1 -Platforms "win-x64,win-arm64,linux-x64,linux-arm64,osx-x64,osx-arm64"
```

### 🧪 Development Workflow

```bash
# 1. Set up StorageLib for AOT
.\Scripts\resolve-storagelib.ps1

# 2. Build and test
.\Scripts\build-all.ps1 -Configuration Debug

# 3. Create language bindings
.\Scripts\package-bindings.ps1 -Languages "Python,NodeJs"

# 4. Restore NuGet for normal development
.\Scripts\resolve-storagelib.ps1 -Restore
```

### 🏗️ CI/CD Pipeline

For Azure DevOps, use multi-repo checkout:

```yaml
# azure-pipelines.yml
resources:
  repositories:
  - repository: FolderMagic
    type: git
    name: YourProject/FolderMagic

steps:
- checkout: self
- checkout: FolderMagic
  path: FolderMagic

- pwsh: |
    .\Scripts\resolve-storagelib.ps1 -UseAzureDevOps
    .\Scripts\build-all.ps1 -Platforms "win-x64,linux-x64"
```

## StorageLib Resolution Details

### How It Works

1. **Local Development**: Uses `D:\__PROGRAMMING\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj`
2. **Azure DevOps**: Looks for `../FolderMagic/StorageLib/StorageLib.csproj` (multi-repo checkout)
3. **Git Submodule**: Falls back to `External/FolderMagic/StorageLib/StorageLib.csproj`

### What It Does

**Before (NuGet Reference)**:
```xml
<PackageReference Include="FolderMagic.StorageLib" Version="1.0.250613.1337-dev" />
```

**After (Project Reference)**:
```xml
<ProjectReference Include="..\..\..\..\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj" />
```

This enables the AOT compiler to:
- ✅ Inline StorageLib methods
- ✅ Perform whole-program optimization
- ✅ Generate truly native libraries
- ✅ Eliminate reflection dependencies

### Affected Projects

- `UvfLib.Storage/UvfLib.Storage.csproj`
- `UvfLib.FileSystem/UvfLib.FileSystem.csproj`

## Output Structure

```
Dist/
├── Native/                    # AOT-compiled libraries
│   ├── win-x64/
│   │   ├── UvfLib.Core/
│   │   │   ├── lib/          # .dll files
│   │   │   ├── bin/          # executables
│   │   │   └── docs/         # XML documentation
│   │   ├── UvfLib.Storage/   # ← Includes StorageLib code!
│   │   └── UvfLib.Vault/
│   ├── linux-x64/           # .so files
│   └── osx-x64/              # .dylib files
└── build-report.md           # Build summary
```

## Troubleshooting

### ❌ "StorageLib source not found"

**Problem**: `resolve-storagelib.ps1` can't find StorageLib source code.

**Solutions**:
1. **Check local path**: Ensure `D:\__PROGRAMMING\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj` exists
2. **Use Azure DevOps**: Run with `-UseAzureDevOps` flag
3. **Set up submodule**: Add FolderMagic as git submodule

### ❌ "AOT compilation failed"

**Problem**: Build fails during AOT compilation.

**Solutions**:
1. **Resolve StorageLib first**: Run `.\Scripts\resolve-storagelib.ps1`
2. **Check dependencies**: Ensure all NuGet packages support AOT
3. **Clean build**: Use `-Clean` flag

### ❌ "Project reference not found"

**Problem**: Build can't find StorageLib project reference.

**Solutions**:
1. **Verify path**: Check that relative path to StorageLib is correct
2. **Restore NuGet**: Run `.\Scripts\resolve-storagelib.ps1 -Restore`
3. **Rebuild**: Clean and rebuild solution

## Advanced Usage

### Custom StorageLib Path

```bash
# If StorageLib is in a different location
$env:STORAGELIB_PATH = "C:\MyProjects\StorageLib\StorageLib.csproj"
.\Scripts\resolve-storagelib.ps1
```

### Container Builds

```bash
# Use Docker for isolated Linux builds
.\Scripts\build-linux.ps1 -UseContainer
```

### Performance Optimization

```bash
# Optimize for speed vs size
.\Scripts\build-windows.ps1 -OptimizeFor Speed
.\Scripts\build-windows.ps1 -OptimizeFor Size
```

## Next Steps

1. **Test AOT libraries**: Verify native libraries work correctly
2. **Create bindings**: Use `package-bindings.ps1` for language wrappers  
3. **Deploy**: Distribute native libraries and language packages
4. **CI/CD**: Set up automated builds with Azure DevOps

---

💡 **Pro Tip**: Always run `resolve-storagelib.ps1 -Restore` after AOT builds to return to normal NuGet-based development workflow. 