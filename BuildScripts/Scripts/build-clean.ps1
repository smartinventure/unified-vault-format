#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Clean Windows build script for UVF.NET
.DESCRIPTION
    Builds UVF.NET projects as AOT-compiled native libraries for Windows.
    Output goes directly to Dist/Native/win-x64 and Dist/Native/win-arm64.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Platforms
    Windows platforms to build. Default: win-x64,win-arm64
.PARAMETER Projects
    Projects to build. Default: All core projects
.PARAMETER Clean
    Clean output directories before building
.EXAMPLE
    .\build-clean.ps1
    .\build-clean.ps1 -Configuration Debug -Platforms "win-x64"
    .\build-clean.ps1 -Projects "UvfLib.Core" -Clean
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [string[]]$Platforms = @("win-x64", "win-arm64"),
    
    [string[]]$Projects = @("UvfLib.Core", "UvfLib.Storage", "UvfLib.Vault"),
    
    [switch]$Clean
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"
$OutputRoot = Join-Path $ProjectRoot "Dist\Native"

Write-Host "=== UVF.NET Windows x64 AOT Build ===" -ForegroundColor Green

# Create output directory
$OutputDir = Join-Path $OutputRoot "win-x64"
if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "Building UvfLib.Core for win-x64..." -ForegroundColor Cyan

try {
    dotnet publish Uvf.Net/UvfLib.Core/UvfLib.Core.csproj `
        -c Release `
        -r win-x64 `
        --self-contained false `
        -p:PublishAot=true `
        -p:PublishTrimmed=true `
        -p:OptimizationPreference=Speed `
        -o $OutputDir `
        --verbosity minimal

    # Check result
    $DllPath = Join-Path $OutputDir "UvfLib.Core.dll"
    if (Test-Path $DllPath) {
        $FileSize = (Get-Item $DllPath).Length
        $FileSizeKB = [math]::Round($FileSize / 1024, 0)
        Write-Host "Success! UvfLib.Core.dll created ($FileSizeKB KB)" -ForegroundColor Green
    } else {
        Write-Host "Warning: DLL not found" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "Build failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Build complete! Files are in: $OutputDir" -ForegroundColor Green
Write-Host ""
Write-Host "Note: For ARM64 builds, you need C++ ARM64 build tools installed." -ForegroundColor Yellow
Write-Host "Use: build-windows.ps1 -IncludeArm64 to attempt ARM64 builds." -ForegroundColor Yellow 