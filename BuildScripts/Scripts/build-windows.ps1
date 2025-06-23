#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Windows-focused UVF.NET AOT build script
.DESCRIPTION
    Builds UVF.NET projects as AOT-compiled native libraries for Windows platforms.
    Supports win-x64 and win-arm64 architectures.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Platforms
    Target Windows platforms. Default: win-x64,win-arm64
.PARAMETER Projects
    Projects to build. Default: All core projects
.PARAMETER Clean
    Clean output directories before building
.PARAMETER CreatePackages
    Create NuGet packages after building
.EXAMPLE
    .\build-windows.ps1
    .\build-windows.ps1 -Configuration Debug -Platforms "win-x64"
    .\build-windows.ps1 -Clean -CreatePackages
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [switch]$IncludeArm64 = $false,
    
    [switch]$CreatePackages = $false,
    
    [switch]$Verbose = $false
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"
$OutputRoot = Join-Path $ProjectRoot "Dist"
$NativeRoot = Join-Path $OutputRoot "Native"
$PackagesRoot = Join-Path $OutputRoot "Packages"

# Set verbosity
$VerbosityLevel = if ($Verbose) { "normal" } else { "minimal" }

Write-Host "=== UVF.NET Windows AOT Build ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Include ARM64: $IncludeArm64" -ForegroundColor Yellow
Write-Host "Create Packages: $CreatePackages" -ForegroundColor Yellow

# Define platforms
$Platforms = @("win-x64")
if ($IncludeArm64) {
    Write-Host ""
    Write-Host "WARNING: ARM64 build requires C++ ARM64 build tools!" -ForegroundColor Red
    Write-Host "Install via Visual Studio Installer > Individual Components:" -ForegroundColor Yellow
    Write-Host "- MSVC v143 - VS 2022 C++ ARM64 build tools (Latest)" -ForegroundColor Yellow
    Write-Host "- Windows 11 SDK (latest version)" -ForegroundColor Yellow
    Write-Host ""
    $Platforms += "win-arm64"
}

# Define projects to build
$Projects = @(
    @{ Name = "UvfLib.Core"; Path = "Uvf.Net/UvfLib.Core/UvfLib.Core.csproj" },
    @{ Name = "UvfLib.Storage"; Path = "Uvf.Net/UvfLib.Storage/UvfLib.Storage.csproj" },
    @{ Name = "UvfLib.Vault"; Path = "Uvf.Net/UvfLib.Vault/UvfLib.Vault.csproj" }
)

# Get version from project
function Get-ProjectVersion {
    try {
        $csprojPath = Join-Path $UvfNetRoot "UvfLib.Core/UvfLib.Core.csproj"
        if (Test-Path $csprojPath) {
            $content = Get-Content $csprojPath -Raw
            if ($content -match '<Version>([^<]+)</Version>') {
                return $Matches[1]
            }
            if ($content -match '<VersionPrefix>([^<]+)</VersionPrefix>') {
                return $Matches[1]
            }
        }
    }
    catch {
        Write-Warning "Could not detect version: $($_.Exception.Message)"
    }
    return "1.0.0"
}

Write-Host "Version: $(Get-ProjectVersion)" -ForegroundColor White

# Step 1: Prerequisites
Write-Host "`n🔍 Checking Prerequisites" -ForegroundColor Yellow
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
    Write-Host "❌ This script is designed for Windows only" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Windows environment detected" -ForegroundColor Green

# Step 2: Clean if requested
if ($Clean) {
    Write-Host "`n🧹 Cleaning Output Directories" -ForegroundColor Yellow
    
    if (Test-Path $NativeRoot) {
        Remove-Item $NativeRoot -Recurse -Force
        Write-Host "✅ Cleaned native libraries" -ForegroundColor Green
    }
    
    if (Test-Path $PackagesRoot) {
        Remove-Item $PackagesRoot -Recurse -Force
        Write-Host "✅ Cleaned packages" -ForegroundColor Green
    }
    
    # Clean project outputs
    foreach ($project in $Projects) {
        $projectDir = Join-Path $UvfNetRoot $project.Name
        @("bin", "obj") | ForEach-Object {
            $dir = Join-Path $projectDir $_
            if (Test-Path $dir) {
                Remove-Item $dir -Recurse -Force
                Write-Host "✅ Cleaned $project.Name/$_" -ForegroundColor Green
            }
        }
    }
}

# Step 3: Create output directories
Write-Host "`n📁 Creating Output Structure" -ForegroundColor Yellow
foreach ($platform in $Platforms) {
    $platformDir = Join-Path $NativeRoot $platform
    if (-not (Test-Path $platformDir)) {
        New-Item -ItemType Directory -Path $platformDir -Force | Out-Null
        Write-Host "✅ Created: $platform" -ForegroundColor Green
    }
}

# Step 4: Build projects for each platform
Write-Host "`n🔨 Building Native Libraries" -ForegroundColor Yellow

$buildResults = @{}
$totalBuilds = $Projects.Count * $Platforms.Count
$currentBuild = 0

foreach ($project in $Projects) {
    $buildResults[$project.Name] = @{}
    
    foreach ($platform in $Platforms) {
        $currentBuild++
        Write-Host "`n[$currentBuild/$totalBuilds] Building $($project.Name) for $platform..." -ForegroundColor Green
        
        $projectPath = Join-Path $UvfNetRoot $project.Name/$project.Name.csproj
        $outputDir = Join-Path $NativeRoot $platform
        
        if (-not (Test-Path $projectPath)) {
            Write-Host "❌ Project not found: $projectPath" -ForegroundColor Red
            $buildResults[$project.Name][$platform] = $false
            continue
        }
        
        try {
            $buildArgs = @(
                "publish"
                $projectPath
                "-c", $Configuration
                "-r", $platform
                "--self-contained", "false"
                "-p:PublishAot=true"
                "-p:PublishTrimmed=true"
                "-p:OptimizationPreference=Speed"
                "-o", $outputDir
                "--verbosity", $VerbosityLevel
            )
            
            $process = Start-Process -FilePath "dotnet" -ArgumentList $buildArgs -Wait -PassThru -NoNewWindow
            
            if ($process.ExitCode -ne 0) {
                throw "Build failed with exit code $($process.ExitCode)"
            }
            
            # Check if DLL was created
            $dllPath = Join-Path $outputDir "$($project.Name).dll"
            if (Test-Path $dllPath) {
                $fileSize = (Get-Item $dllPath).Length
                $fileSizeKB = [math]::Round($fileSize / 1024, 0)
                Write-Host "  ✓ Success: $($project.Name).dll ($fileSizeKB KB)" -ForegroundColor Green
                $buildResults[$project.Name][$platform] = $true
            } else {
                Write-Host "  ✗ Warning: DLL not found at $dllPath" -ForegroundColor Yellow
                $buildResults[$project.Name][$platform] = $false
            }
        }
        catch {
            Write-Host "  ✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
            
            # For ARM64, provide helpful guidance
            if ($platform -eq "win-arm64") {
                Write-Host ""
                Write-Host "ARM64 Build Failed - This is expected if C++ ARM64 tools are not installed." -ForegroundColor Yellow
                Write-Host "To fix this:" -ForegroundColor Yellow
                Write-Host "1. Open Visual Studio Installer" -ForegroundColor White
                Write-Host "2. Modify your VS installation" -ForegroundColor White
                Write-Host "3. Go to Individual Components" -ForegroundColor White
                Write-Host "4. Install: MSVC v143 - VS 2022 C++ ARM64 build tools" -ForegroundColor White
                Write-Host "5. Install: Windows 11 SDK (latest)" -ForegroundColor White
                Write-Host ""
            }
            
            # Continue with other builds
            $buildResults[$project.Name][$platform] = $false
        }
    }
}

# Step 5: Create NuGet packages if requested
if ($CreatePackages) {
    Write-Host "`n📦 Creating NuGet Packages" -ForegroundColor Yellow
    
    $nugetDir = Join-Path $PackagesRoot "nuget"
    if (-not (Test-Path $nugetDir)) {
        New-Item -ItemType Directory -Path $nugetDir -Force | Out-Null
    }
    
    $packageVersion = Get-ProjectVersion
    
    foreach ($platform in $Platforms) {
        $platformDir = Join-Path $NativeRoot $platform
        if (Test-Path $platformDir) {
            Write-Host "Creating NuGet package for $platform..." -ForegroundColor Cyan
            
            # Create package structure
            $packageDir = Join-Path $nugetDir "UvfLib.Native.$platform"
            $runtimeDir = Join-Path $packageDir "runtimes/$platform/native"
            
            if (-not (Test-Path $runtimeDir)) {
                New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
            }
            
            # Copy libraries
            $files = Get-ChildItem $platformDir -File -Include "*.dll", "*.pdb", "*.xml"
            foreach ($file in $files) {
                Copy-Item $file.FullName $runtimeDir -Force
            }
            
            # Create nuspec
            $nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>UvfLib.Native.$platform</id>
    <version>$packageVersion</version>
    <authors>UVF.NET Team</authors>
    <description>Native AOT libraries for UVF.NET vault format - $platform</description>
    <projectUrl>https://github.com/your-org/uvf.net</projectUrl>
    <license type="expression">MIT</license>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <tags>uvf cryptomator vault encryption native aot windows $platform</tags>
    <dependencies>
      <group targetFramework="net8.0" />
    </dependencies>
  </metadata>
  <files>
    <file src="runtimes\**\*" target="runtimes" />
  </files>
</package>
"@
            
            $nuspecPath = Join-Path $packageDir "UvfLib.Native.$platform.nuspec"
            Set-Content $nuspecPath -Value $nuspecContent -Encoding UTF8
            
            Write-Host "✅ Package structure created for $platform" -ForegroundColor Green
        }
    }
}

# Step 6: Build Summary
Write-Host "`n📊 Build Summary" -ForegroundColor Cyan

        $successCount = 0
$totalCount = 0
        
        foreach ($project in $Projects) {
    Write-Host "`n$project.Name" -ForegroundColor White
            foreach ($platform in $Platforms) {
        $totalCount++
        $success = $buildResults[$project.Name][$platform]
        if ($success) {
            Write-Host "  ✓ $platform" -ForegroundColor Green
                    $successCount++
                } else {
            Write-Host "  ✗ $platform" -ForegroundColor Red
        }
    }
}

Write-Host "`nResults:" -ForegroundColor White
Write-Host "✅ Successful: $successCount/$totalCount" -ForegroundColor Green

if ($successCount -gt 0) {
    Write-Host "`n📁 Output Structure:" -ForegroundColor White
    foreach ($platform in $Platforms) {
        $platformDir = Join-Path $NativeRoot $platform
        if (Test-Path $platformDir) {
            Write-Host "`n$platform/" -ForegroundColor Yellow
            $files = Get-ChildItem $platformDir -File | Sort-Object Name
            foreach ($file in $files) {
                $sizeKB = [math]::Round($file.Length / 1024, 1)
                Write-Host "  $($file.Name) (${sizeKB} KB)" -ForegroundColor Gray
            }
        }
    }
    
    Write-Host "`n🎉 Build Completed Successfully!" -ForegroundColor Green
    Write-Host "Native libraries: $NativeRoot" -ForegroundColor Green
    
    if ($CreatePackages) {
        Write-Host "NuGet packages: $PackagesRoot" -ForegroundColor Green
    }
    
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "1. Test the native libraries" -ForegroundColor White
    Write-Host "2. Create language bindings (optional)" -ForegroundColor White
    Write-Host "3. Distribute packages" -ForegroundColor White
    
} else {
    Write-Host "`n❌ No successful builds!" -ForegroundColor Red
        exit 1
    }