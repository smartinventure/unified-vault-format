#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Master Windows build script for UVF.NET
.DESCRIPTION
    Complete build orchestration for Windows platforms (win-x64, win-arm64).
    Handles StorageLib resolution, managed builds, AOT compilation, and packaging.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Platforms
    Windows platforms to build. Default: win-x64,win-arm64
.PARAMETER SkipTests
    Skip running unit tests
.PARAMETER CreatePackages
    Create NuGet packages after building
.PARAMETER Clean
    Clean all outputs before building
.EXAMPLE
    .\build-master.ps1
    .\build-master.ps1 -Configuration Debug -Platforms "win-x64"
    .\build-master.ps1 -Clean -CreatePackages -SkipTests
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [string[]]$Platforms = @("win-x64", "win-arm64"),
    
    [switch]$SkipTests,
    
    [switch]$CreatePackages,
    
    [switch]$Clean
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"

Write-Host "=== UVF.NET Master Windows Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor White
Write-Host "Platforms: $($Platforms -join ', ')" -ForegroundColor White
Write-Host "Skip Tests: $SkipTests" -ForegroundColor White
Write-Host "Create Packages: $CreatePackages" -ForegroundColor White
Write-Host "Clean Build: $Clean" -ForegroundColor White

$startTime = Get-Date

try {
    # Step 1: Prerequisites
    Write-Host "`n🔍 Step 1: Checking Prerequisites" -ForegroundColor Yellow
    
    # Check .NET SDK
    try {
        $dotnetVersion = dotnet --version
        Write-Host "✅ .NET SDK: $dotnetVersion" -ForegroundColor Green
    }
    catch {
        Write-Host "❌ .NET SDK not found. Please install .NET 8.0 SDK" -ForegroundColor Red
        exit 1
    }
    
    # Check Windows
    if (-not ($IsWindows -or $PSVersionTable.PSEdition -eq "Desktop")) {
        Write-Host "❌ This script requires Windows" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Windows environment confirmed" -ForegroundColor Green
    
    # Step 2: Resolve StorageLib
    Write-Host "`n📦 Step 2: Resolving StorageLib Dependencies" -ForegroundColor Yellow
    $resolveScript = Join-Path $ScriptRoot "resolve-storagelib-simple.ps1"
    if (Test-Path $resolveScript) {
        Write-Host "Running StorageLib resolution..." -ForegroundColor Cyan
        & $resolveScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ StorageLib resolution failed" -ForegroundColor Red
            exit 1
        }
        Write-Host "✅ StorageLib resolved for AOT compilation" -ForegroundColor Green
    } else {
        Write-Host "⚠️  StorageLib resolution script not found" -ForegroundColor Yellow
    }
    
    # Step 3: Build managed libraries
    Write-Host "`n🔨 Step 3: Building Managed Libraries" -ForegroundColor Yellow
    
    $solutionPath = Join-Path $UvfNetRoot "Uvf.Net.sln"
    if (-not (Test-Path $solutionPath)) {
        Write-Host "❌ Solution not found: $solutionPath" -ForegroundColor Red
        exit 1
    }
    
    if ($Clean) {
        Write-Host "Cleaning solution..." -ForegroundColor Cyan
        & dotnet clean $solutionPath --configuration $Configuration --verbosity minimal
    }
    
    Write-Host "Restoring packages..." -ForegroundColor Cyan
    & dotnet restore $solutionPath --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Package restore failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Building solution..." -ForegroundColor Cyan
    & dotnet build $solutionPath --configuration $Configuration --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Solution build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Managed libraries built successfully" -ForegroundColor Green
    
    # Step 4: Run tests
    if (-not $SkipTests) {
        Write-Host "`n🧪 Step 4: Running Tests" -ForegroundColor Yellow
        
        $testProject = Join-Path $UvfNetRoot "UvfLib.Tests/UvfLib.Tests.csproj"
        if (Test-Path $testProject) {
            Write-Host "Running unit tests..." -ForegroundColor Cyan
            & dotnet test $testProject --configuration $Configuration --no-build --verbosity minimal
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✅ All tests passed" -ForegroundColor Green
            } else {
                Write-Host "❌ Tests failed" -ForegroundColor Red
                exit 1
            }
        } else {
            Write-Host "⚠️  Test project not found" -ForegroundColor Yellow
        }
    } else {
        Write-Host "`n⏭️  Step 4: Skipping Tests" -ForegroundColor Yellow
    }
    
    # Step 5: Build native libraries
    Write-Host "`n🏗️  Step 5: Building Native AOT Libraries" -ForegroundColor Yellow
    
    $windowsBuildScript = Join-Path $ScriptRoot "build-windows.ps1"
    if (-not (Test-Path $windowsBuildScript)) {
        Write-Host "❌ Windows build script not found: $windowsBuildScript" -ForegroundColor Red
        exit 1
    }
    
    $buildArgs = @(
        "-Configuration", $Configuration
        "-Platforms", ($Platforms -join ",")
    )
    
    if ($Clean) {
        $buildArgs += "-Clean"
    }
    
    if ($CreatePackages) {
        $buildArgs += "-CreatePackages"
    }
    
    Write-Host "Executing Windows build..." -ForegroundColor Cyan
    & $windowsBuildScript @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Native build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Native libraries built successfully" -ForegroundColor Green
    
    # Step 6: Generate build report
    Write-Host "`n📋 Step 6: Generating Build Report" -ForegroundColor Yellow
    
    $reportPath = Join-Path $ProjectRoot "Dist/windows-build-report.md"
    $distDir = Join-Path $ProjectRoot "Dist"
    
    if (-not (Test-Path $distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }
    
    $report = @"
# UVF.NET Windows Build Report

**Build Date:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")  
**Configuration:** $Configuration  
**Platforms:** $($Platforms -join ', ')  
**Skip Tests:** $SkipTests  
**Create Packages:** $CreatePackages  

## Environment

- **OS:** $($PSVersionTable.OS)
- **PowerShell:** $($PSVersionTable.PSVersion)
- **.NET SDK:** $(dotnet --version)
- **Machine:** $([Environment]::MachineName)

## Build Results

### Native Libraries

"@
    
    # Add platform-specific info
    $nativeDir = Join-Path $ProjectRoot "Dist/Native"
    if (Test-Path $nativeDir) {
        foreach ($platform in $Platforms) {
            $platformDir = Join-Path $nativeDir $platform
            if (Test-Path $platformDir) {
                $report += "`n#### $platform`n`n"
                
                $files = Get-ChildItem $platformDir -File | Sort-Object Name
                foreach ($file in $files) {
                    $sizeKB = [math]::Round($file.Length / 1024, 1)
                    $report += "- $($file.Name) ($sizeKB KB)`n"
                }
            }
        }
    }
    
    if ($CreatePackages) {
        $report += "`n### NuGet Packages`n`n"
        $packagesDir = Join-Path $ProjectRoot "Dist/Packages/nuget"
        if (Test-Path $packagesDir) {
            $packages = Get-ChildItem $packagesDir -Directory
            foreach ($package in $packages) {
                $report += "- $($package.Name)`n"
            }
        }
    }
    
    $report += @"

## Usage

The native libraries can be used directly or through NuGet packages:

### Direct Usage
```csharp
// Reference the native DLL directly
[DllImport("UvfLib.Core.dll")]
public static extern int SomeNativeFunction();
```

### NuGet Package Usage
```xml
<PackageReference Include="UvfLib.Native.win-x64" Version="1.0.0" />
```

---
*Generated by UVF.NET Windows Build System*
"@
    
    Set-Content $reportPath -Value $report -Encoding UTF8
    Write-Host "✅ Build report created: $reportPath" -ForegroundColor Green
    
    # Final summary
    $endTime = Get-Date
    $duration = $endTime - $startTime
    
    Write-Host "`n🎉 Build Complete!" -ForegroundColor Green
    Write-Host "Total time: $($duration.ToString('mm\:ss'))" -ForegroundColor Cyan
    Write-Host "Artifacts location: $(Join-Path $ProjectRoot 'Dist')" -ForegroundColor Green
    
    Write-Host "`nWhat was built:" -ForegroundColor White
    Write-Host "✅ Managed libraries (.NET assemblies)" -ForegroundColor Green
    Write-Host "✅ Native AOT libraries (win-x64, win-arm64)" -ForegroundColor Green
    if ($CreatePackages) {
        Write-Host "✅ NuGet packages" -ForegroundColor Green
    }
    if (-not $SkipTests) {
        Write-Host "✅ Unit tests executed" -ForegroundColor Green
    }
    
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "1. Test the native libraries with your applications" -ForegroundColor White
    Write-Host "2. Distribute via NuGet or direct file copy" -ForegroundColor White
    Write-Host "3. Create language bindings if needed" -ForegroundColor White
    
}
catch {
    Write-Host "`n❌ Build Failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} 