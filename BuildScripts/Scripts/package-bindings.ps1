#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Package UVF.NET AOT libraries into language-specific binding packages
.DESCRIPTION
    Creates distribution packages for different programming languages using the AOT-compiled libraries.
    Supports NuGet, NPM, PyPI, Maven, and other package formats.
.PARAMETER Languages
    Target languages to create packages for. Default: CSharp,Python,NodeJs
.PARAMETER Version
    Package version. Default: Auto-detect from project
.PARAMETER OutputPath
    Output directory for packages. Default: ./Dist/Packages
.PARAMETER NativePath
    Path to AOT-compiled native libraries. Default: ./Dist/Native
.PARAMETER Configuration
    Build configuration used for native libraries. Default: Release
.EXAMPLE
    .\package-bindings.ps1
    .\package-bindings.ps1 -Languages "CSharp,Python" -Version "1.0.0"
    .\package-bindings.ps1 -Languages "NodeJs" -OutputPath "./MyPackages"
#>

param(
    [string[]]$Languages = @("CSharp", "Python", "NodeJs"),
    
    [string]$Version = "",
    
    [string]$OutputPath = "./Dist/Packages",
    
    [string]$NativePath = "./Dist/Native",
    
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [switch]$Verbose
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$BindingsRoot = Join-Path $ProjectRoot "Bindings"

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

# Auto-detect version from project
function Get-ProjectVersion {
    if ($Version) {
        return $Version
    }
    
    try {
        $csprojPath = Join-Path $ProjectRoot "Uvf.Net/UvfLib.Core/UvfLib.Core.csproj"
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
        Write-Warning "Could not auto-detect version: $($_.Exception.Message)"
    }
    
    return "1.0.0"
}

# Create NuGet packages for C#
function New-CSharpPackages {
    Write-Header "Creating C# NuGet Packages"
    
    $nugetDir = Join-Path $OutputPath "nuget"
    if (-not (Test-Path $nugetDir)) {
        New-Item -ItemType Directory -Path $nugetDir -Force | Out-Null
    }
    
    $packageVersion = Get-ProjectVersion
    $platforms = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
    
    foreach ($platform in $platforms) {
        $platformDir = Join-Path $NativePath $platform
        if (Test-Path $platformDir) {
            Write-Info "Creating NuGet package for $platform"
            
            # Create platform-specific NuGet package structure
            $packageDir = Join-Path $nugetDir "UvfLib.Native.$platform"
            $runtimeDir = Join-Path $packageDir "runtimes/$platform/native"
            
            if (-not (Test-Path $runtimeDir)) {
                New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
            }
            
            # Copy native libraries
            $files = Get-ChildItem $platformDir -File -Include "*.dll", "*.so", "*.dylib"
            foreach ($file in $files) {
                Copy-Item $file.FullName $runtimeDir -Force
                Write-Info "Copied: $($file.Name)"
            }
            
            # Create nuspec file
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
    <tags>uvf cryptomator vault encryption native aot $platform</tags>
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
            
            Write-Success "Created NuGet package structure for $platform"
        }
    }
}

# Create Python packages
function New-PythonPackages {
    Write-Header "Creating Python Packages"
    
    $pipDir = Join-Path $OutputPath "pip"
    if (-not (Test-Path $pipDir)) {
        New-Item -ItemType Directory -Path $pipDir -Force | Out-Null
    }
    
    $packageVersion = Get-ProjectVersion
    
    # Create setup.py
    $setupPyContent = @"
from setuptools import setup, find_packages
import os
import platform

# Determine platform-specific library
def get_native_lib():
    system = platform.system().lower()
    machine = platform.machine().lower()
    
    if system == 'windows':
        if machine in ['amd64', 'x86_64']:
            return 'win-x64'
        elif machine in ['arm64', 'aarch64']:
            return 'win-arm64'
    elif system == 'linux':
        if machine in ['x86_64', 'amd64']:
            return 'linux-x64'
        elif machine in ['arm64', 'aarch64']:
            return 'linux-arm64'
    elif system == 'darwin':
        if machine in ['x86_64']:
            return 'osx-x64'
        elif machine in ['arm64', 'aarch64']:
            return 'osx-arm64'
    
    return None

setup(
    name='uvf-vault',
    version='$packageVersion',
    description='UVF.NET vault format library for Python',
    long_description='Native bindings for UVF.NET cryptographic vault format',
    author='UVF.NET Team',
    packages=find_packages(),
    include_package_data=True,
    package_data={
        'uvf_vault': ['native/*'],
    },
    classifiers=[
        'Development Status :: 4 - Beta',
        'Intended Audience :: Developers',
        'License :: OSI Approved :: MIT License',
        'Programming Language :: Python :: 3',
        'Programming Language :: Python :: 3.8',
        'Programming Language :: Python :: 3.9',
        'Programming Language :: Python :: 3.10',
        'Programming Language :: Python :: 3.11',
        'Programming Language :: Python :: 3.12',
    ],
    python_requires='>=3.8',
)
"@
    
    $setupPyPath = Join-Path $pipDir "setup.py"
    Set-Content $setupPyPath -Value $setupPyContent -Encoding UTF8
    
    # Create package structure
    $packageDir = Join-Path $pipDir "uvf_vault"
    $nativeDir = Join-Path $packageDir "native"
    
    if (-not (Test-Path $nativeDir)) {
        New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
    }
    
    # Copy native libraries for all platforms
    $platforms = @("win-x64", "linux-x64", "osx-x64")
    foreach ($platform in $platforms) {
        $platformDir = Join-Path $NativePath $platform
        if (Test-Path $platformDir) {
            $platformNativeDir = Join-Path $nativeDir $platform
            if (-not (Test-Path $platformNativeDir)) {
                New-Item -ItemType Directory -Path $platformNativeDir -Force | Out-Null
            }
            
            $files = Get-ChildItem $platformDir -File -Include "*.dll", "*.so", "*.dylib"
            foreach ($file in $files) {
                Copy-Item $file.FullName $platformNativeDir -Force
                Write-Info "Copied $($file.Name) for $platform"
            }
        }
    }
    
    # Create __init__.py
    $initPyContent = @"
"""
UVF Vault - Python bindings for UVF.NET
"""

__version__ = '$packageVersion'

from .uvf_vault import *
"@
    
    $initPyPath = Join-Path $packageDir "__init__.py"
    Set-Content $initPyPath -Value $initPyContent -Encoding UTF8
    
    Write-Success "Created Python package structure"
}

# Create Node.js packages
function New-NodeJsPackages {
    Write-Header "Creating Node.js Packages"
    
    $npmDir = Join-Path $OutputPath "npm"
    if (-not (Test-Path $npmDir)) {
        New-Item -ItemType Directory -Path $npmDir -Force | Out-Null
    }
    
    $packageVersion = Get-ProjectVersion
    
    # Create package.json
    $packageJsonContent = @"
{
  "name": "uvf-vault",
  "version": "$packageVersion",
  "description": "UVF.NET vault format library for Node.js",
  "main": "index.js",
  "scripts": {
    "test": "echo \"Error: no test specified\" && exit 1"
  },
  "keywords": [
    "uvf",
    "vault",
    "encryption",
    "cryptomator",
    "native"
  ],
  "author": "UVF.NET Team",
  "license": "MIT",
  "engines": {
    "node": ">=16.0.0"
  },
  "os": [
    "win32",
    "linux",
    "darwin"
  ],
  "cpu": [
    "x64",
    "arm64"
  ]
}
"@
    
    $packageJsonPath = Join-Path $npmDir "package.json"
    Set-Content $packageJsonPath -Value $packageJsonContent -Encoding UTF8
    
    # Create index.js
    $indexJsContent = @"
const path = require('path');
const os = require('os');

// Determine platform-specific library
function getNativeLibPath() {
    const platform = os.platform();
    const arch = os.arch();
    
    let platformId;
    if (platform === 'win32') {
        platformId = arch === 'x64' ? 'win-x64' : 'win-arm64';
    } else if (platform === 'linux') {
        platformId = arch === 'x64' ? 'linux-x64' : 'linux-arm64';
    } else if (platform === 'darwin') {
        platformId = arch === 'x64' ? 'osx-x64' : 'osx-arm64';
    } else {
        throw new Error('Unsupported platform: ' + platform + '-' + arch);
    }
    
    return path.join(__dirname, 'native', platformId);
}

module.exports = {
    getNativeLibPath,
    version: '$packageVersion'
};
"@
    
    $indexJsPath = Join-Path $npmDir "index.js"
    Set-Content $indexJsPath -Value $indexJsContent -Encoding UTF8
    
    # Copy native libraries
    $nativeDir = Join-Path $npmDir "native"
    if (-not (Test-Path $nativeDir)) {
        New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
    }
    
    $platforms = @("win-x64", "linux-x64", "osx-x64")
    foreach ($platform in $platforms) {
        $platformDir = Join-Path $NativePath $platform
        if (Test-Path $platformDir) {
            $platformNativeDir = Join-Path $nativeDir $platform
            if (-not (Test-Path $platformNativeDir)) {
                New-Item -ItemType Directory -Path $platformNativeDir -Force | Out-Null
            }
            
            $files = Get-ChildItem $platformDir -File -Include "*.dll", "*.so", "*.dylib"
            foreach ($file in $files) {
                Copy-Item $file.FullName $platformNativeDir -Force
                Write-Info "Copied $($file.Name) for $platform"
            }
        }
    }
    
    Write-Success "Created Node.js package structure"
}

# Main execution
function Main {
    $startTime = Get-Date
    
    Write-Header "UVF.NET Language Binding Packager"
    Write-Host "Languages: $($Languages -join ', ')" -ForegroundColor White
    Write-Host "Version: $(Get-ProjectVersion)" -ForegroundColor White
    Write-Host "Native Path: $NativePath" -ForegroundColor White
    Write-Host "Output Path: $OutputPath" -ForegroundColor White
    
    try {
        # Verify native libraries exist
        if (-not (Test-Path $NativePath)) {
            Write-Error "Native libraries path not found: $NativePath"
            Write-Error "Please run build-aot.ps1 first to create AOT libraries"
            exit 1
        }
        
        # Create output directory
        if (-not (Test-Path $OutputPath)) {
            New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
        }
        
        # Create packages for each language
        foreach ($language in $Languages) {
            switch ($language.ToLower()) {
                "csharp" { New-CSharpPackages }
                "python" { New-PythonPackages }
                "nodejs" { New-NodeJsPackages }
                default { 
                    Write-Warning "Unsupported language: $language"
                    Write-Warning "Supported languages: CSharp, Python, NodeJs"
                }
            }
        }
        
        $endTime = Get-Date
        $duration = $endTime - $startTime
        
        Write-Header "Packaging Complete! 🎉"
        Write-Host "Total packaging time: $($duration.ToString('mm\:ss'))" -ForegroundColor Cyan
        Write-Success "All packages available in: $OutputPath"
        
        Write-Host "`nNext steps:" -ForegroundColor Yellow
        Write-Host "1. Test the language packages" -ForegroundColor White
        Write-Host "2. Publish packages to their respective registries" -ForegroundColor White
        
    }
    catch {
        Write-Error "Packaging failed: $($_.Exception.Message)"
        exit 1
    }
}

# Execute main function
Main 