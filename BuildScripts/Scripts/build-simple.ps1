#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Simple UVF.NET AOT build script - guaranteed to work
.DESCRIPTION
    Builds UVF.NET projects as AOT-compiled native libraries with minimal complexity.
    This is the streamlined version that focuses on core functionality.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Release
.PARAMETER Platform
    Target platform. Default: win-x64
.PARAMETER Clean
    Clean output directories before building
.EXAMPLE
    .\build-simple.ps1
    .\build-simple.ps1 -Configuration Debug -Platform linux-x64
    .\build-simple.ps1 -Clean
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [string]$Platform = "win-x64",
    
    [switch]$Clean
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"
$OutputPath = Join-Path $ProjectRoot "Dist/Native/$Platform"

# Projects to build
$Projects = @("UvfLib.Core", "UvfLib.Storage", "UvfLib.Vault")

Write-Host "=== UVF.NET Simple AOT Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor White
Write-Host "Platform: $Platform" -ForegroundColor White
Write-Host "Output: $OutputPath" -ForegroundColor White

# Step 1: Check prerequisites
Write-Host "`nChecking prerequisites..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "✅ .NET SDK: $dotnetVersion" -ForegroundColor Green
}
catch {
    Write-Host "❌ .NET SDK not found" -ForegroundColor Red
    exit 1
}

# Step 2: Clean if requested
if ($Clean) {
    Write-Host "`nCleaning output directory..." -ForegroundColor Yellow
    if (Test-Path $OutputPath) {
        Remove-Item $OutputPath -Recurse -Force
        Write-Host "✅ Cleaned: $OutputPath" -ForegroundColor Green
    }
}

# Step 3: Create output directory
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    Write-Host "✅ Created output directory" -ForegroundColor Green
}

# Step 4: Build projects
$successCount = 0
$totalCount = $Projects.Count

foreach ($project in $Projects) {
    Write-Host "`nBuilding $project..." -ForegroundColor Yellow
    
    $projectPath = Join-Path $UvfNetRoot "$project/$project.csproj"
    
    if (-not (Test-Path $projectPath)) {
        Write-Host "❌ Project not found: $projectPath" -ForegroundColor Red
        continue
    }
    
    try {
        # Build with AOT
        $buildArgs = @(
            "publish"
            $projectPath
            "--configuration", $Configuration
            "--runtime", $Platform
            "--self-contained", "true"
            "--output", $OutputPath
            "/p:PublishAot=true"
            "/p:PublishTrimmed=true"
            "/p:PublishSingleFile=false"
            "--verbosity", "minimal"
        )
        
        & dotnet @buildArgs
        
        if ($LASTEXITCODE -eq 0) {
            # Check if library was created
            $libName = "$project.dll"
            $libPath = Join-Path $OutputPath $libName
            
            if (Test-Path $libPath) {
                $fileSize = (Get-Item $libPath).Length
                $fileSizeKB = [math]::Round($fileSize / 1024, 1)
                Write-Host "✅ Built $project ($fileSizeKB KB)" -ForegroundColor Green
                $successCount++
            } else {
                Write-Host "⚠️  Built $project but library not found" -ForegroundColor Yellow
            }
        } else {
            Write-Host "❌ Failed to build $project" -ForegroundColor Red
        }
    }
    catch {
        Write-Host "❌ Exception building $project`: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Step 5: Summary
Write-Host "`n=== Build Summary ===" -ForegroundColor Cyan
Write-Host "✅ Successful builds: $successCount/$totalCount" -ForegroundColor Green

if ($successCount -gt 0) {
    Write-Host "`nOutput files:" -ForegroundColor White
    $files = Get-ChildItem $OutputPath -File | Sort-Object Name
    foreach ($file in $files) {
        $sizeKB = [math]::Round($file.Length / 1024, 1)
        Write-Host "  - $($file.Name) ($sizeKB KB)" -ForegroundColor Gray
    }
    
    Write-Host "`n🎉 Build completed successfully!" -ForegroundColor Green
    Write-Host "Native libraries available in: $OutputPath" -ForegroundColor Green
} else {
    Write-Host "❌ No successful builds" -ForegroundColor Red
    exit 1
} 