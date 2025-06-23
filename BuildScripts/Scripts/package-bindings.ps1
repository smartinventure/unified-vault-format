#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Package UVF.NET AOT libraries into language-specific binding packages
.DESCRIPTION
    Creates distribution packages for different programming languages using the AOT-compiled libraries.
    Supports NuGet, NPM, PyPI, Maven, and other package formats.
.PARAMETER Languages
    Target languages to create packages for. Default: All supported languages
.PARAMETER Version
    Package version. Default: Auto-detect from project
.PARAMETER OutputPath
    Output directory for packages. Default: ./Dist/Packages
.PARAMETER NativePath
    Path to AOT-compiled native libraries. Default: ./Dist/Native
.PARAMETER Configuration
    Build configuration used for native libraries. Default: Release
.PARAMETER PublishToRegistry
    Publish packages to their respective registries
.EXAMPLE
    .\package-bindings.ps1
    .\package-bindings.ps1 -Languages "CSharp,Python" -Version "1.0.0"
    .\package-bindings.ps1 -PublishToRegistry
#>

param(
    [string[]]$Languages = @("CSharp", "Python", "NodeJs", "Java", "Cpp", "Go", "PHP"),
    
    [string]$Version = "",
    
    [string]$OutputPath = "./Dist/Packages",
    
    [string]$NativePath = "./Dist/Native",
    
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    
    [switch]$PublishToRegistry,
    
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
        Write-Warning "Could not auto-detect version: $_"
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
            
            # Create platform-specific NuGet package
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
    <file src="$platformDir\**\*" target="runtimes\$platform\native" />
  </files>
</package>
"@
            
            $nuspecPath = Join-Path $nugetDir "UvfLib.Native.$platform.nuspec"
            Set-Content $nuspecPath -Value $nuspecContent -Encoding UTF8
            
            # Create package
            try {
                & nuget pack $nuspecPath -OutputDirectory $nugetDir
                if ($LASTEXITCODE -eq 0) {
                    Write-Success "Created NuGet package: UvfLib.Native.$platform.$packageVersion.nupkg"
                }
            }
            catch {
                Write-Warning "Failed to create NuGet package for $platform"
            }
        }
    }
    
    # Create main package that depends on all platforms
    $mainNuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>UvfLib.Native</id>
    <version>$packageVersion</version>
    <authors>UVF.NET Team</authors>
    <description>Native AOT libraries for UVF.NET vault format - All platforms</description>
    <projectUrl>https://github.com/your-org/uvf.net</projectUrl>
    <license type="expression">MIT</license>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <tags>uvf cryptomator vault encryption native aot cross-platform</tags>
    <dependencies>
      <group targetFramework="net8.0">
        <dependency id="UvfLib.Native.win-x64" version="$packageVersion" />
        <dependency id="UvfLib.Native.win-arm64" version="$packageVersion" />
        <dependency id="UvfLib.Native.linux-x64" version="$packageVersion" />
        <dependency id="UvfLib.Native.linux-arm64" version="$packageVersion" />
        <dependency id="UvfLib.Native.osx-x64" version="$packageVersion" />
        <dependency id="UvfLib.Native.osx-arm64" version="$packageVersion" />
      </group>
    </dependencies>
  </metadata>
</package>
"@
    
    $mainNuspecPath = Join-Path $nugetDir "UvfLib.Native.nuspec"
    Set-Content $mainNuspecPath -Value $mainNuspecContent -Encoding UTF8
    
    try {
        & nuget pack $mainNuspecPath -OutputDirectory $nugetDir
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Created main NuGet package: UvfLib.Native.$packageVersion.nupkg"
        }
    }
    catch {
        Write-Warning "Failed to create main NuGet package"
    }
}

# Create Python packages
function New-PythonPackages {
    Write-Header "Creating Python Packages"
    
    $pythonDir = Join-Path $OutputPath "pip"
    if (-not (Test-Path $pythonDir)) {
        New-Item -ItemType Directory -Path $pythonDir -Force | Out-Null
    }
    
    $packageVersion = Get-ProjectVersion
    
    # Create setup.py
    $setupPyContent = @"
from setuptools import setup, find_packages
import os
import platform

# Determine platform-specific wheel
system = platform.system().lower()
machine = platform.machine().lower()

if system == "windows":
    if machine in ["amd64", "x86_64"]:
        platform_tag = "win-x64"
    elif machine in ["arm64", "aarch64"]:
        platform_tag = "win-arm64"
elif system == "linux":
    if machine in ["x86_64", "amd64"]:
        platform_tag = "linux-x64"
    elif machine in ["arm64", "aarch64"]:
        platform_tag = "linux-arm64"
elif system == "darwin":
    if machine in ["x86_64", "amd64"]:
        platform_tag = "osx-x64"
    elif machine in ["arm64", "aarch64"]:
        platform_tag = "osx-arm64"
else:
    raise RuntimeError(f"Unsupported platform: {system}-{machine}")

setup(
    name="uvf-vault",
    version="$packageVersion",
    author="UVF.NET Team",
    author_email="team@uvf.net",
    description="Python bindings for UVF.NET vault format with native performance",
    long_description=open("README.md").read(),
    long_description_content_type="text/markdown",
    url="https://github.com/your-org/uvf.net",
    packages=find_packages(),
    classifiers=[
        "Development Status :: 4 - Beta",
        "Intended Audience :: Developers",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent",
        "Programming Language :: Python :: 3",
        "Programming Language :: Python :: 3.8",
        "Programming Language :: Python :: 3.9",
        "Programming Language :: Python :: 3.10",
        "Programming Language :: Python :: 3.11",
        "Programming Language :: Python :: 3.12",
        "Topic :: Security :: Cryptography",
        "Topic :: System :: Archiving",
    ],
    python_requires=">=3.8",
    install_requires=[
        "cffi>=1.15.0",
    ],
    package_data={
        "uvf_vault": ["native/*"],
    },
    include_package_data=True,
)
"@
    
    $setupPyPath = Join-Path $pythonDir "setup.py"
    Set-Content $setupPyPath -Value $setupPyContent -Encoding UTF8
    
    # Create package structure
    $packageDir = Join-Path $pythonDir "uvf_vault"
    if (-not (Test-Path $packageDir)) {
        New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
    }
    
    # Copy native libraries
    $nativeDir = Join-Path $packageDir "native"
    if (-not (Test-Path $nativeDir)) {
        New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
    }
    
    $platforms = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
    foreach ($platform in $platforms) {
        $sourcePlatformDir = Join-Path $NativePath $platform
        if (Test-Path $sourcePlatformDir) {
            $targetPlatformDir = Join-Path $nativeDir $platform
            Copy-Item $sourcePlatformDir $targetPlatformDir -Recurse -Force
            Write-Info "Copied native libraries for $platform"
        }
    }
    
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
    $packageJsonContent = @{
        name = "uvf-vault"
        version = $packageVersion
        description = "Node.js bindings for UVF.NET vault format with native performance"
        main = "index.js"
        types = "index.d.ts"
        scripts = @{
            test = "node test/test.js"
            install = "node-pre-gyp install --fallback-to-build"
        }
        keywords = @("uvf", "cryptomator", "vault", "encryption", "native")
        author = "UVF.NET Team"
        license = "MIT"
        repository = @{
            type = "git"
            url = "https://github.com/your-org/uvf.net.git"
        }
        dependencies = @{
            "node-addon-api" = "^7.0.0"
            "node-pre-gyp" = "^0.17.0"
        }
        devDependencies = @{
            "@types/node" = "^20.0.0"
        }
        binary = @{
            module_name = "uvf_vault"
            module_path = "./lib/binding/{configuration}/{node_abi}-{platform}-{arch}/"
            remote_path = "./releases/download/{version}/"
            package_name = "{module_name}-v{version}-{node_abi}-{platform}-{arch}.tar.gz"
            host = "https://github.com/your-org/uvf.net"
        }
    }
    
    $packageJsonPath = Join-Path $npmDir "package.json"
    $packageJsonContent | ConvertTo-Json -Depth 10 | Set-Content $packageJsonPath -Encoding UTF8
    
    Write-Success "Created Node.js package.json"
}

# Create Java packages
function New-JavaPackages {
    Write-Header "Creating Java Packages"
    
    $mavenDir = Join-Path $OutputPath "maven"
    if (-not (Test-Path $mavenDir)) {
        New-Item -ItemType Directory -Path $mavenDir -Force | Out-Null
    }
    
    $packageVersion = Get-ProjectVersion
    
    # Create pom.xml
    $pomXmlContent = @"
<?xml version="1.0" encoding="UTF-8"?>
<project xmlns="http://maven.apache.org/POM/4.0.0"
         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 
         http://maven.apache.org/xsd/maven-4.0.0.xsd">
    <modelVersion>4.0.0</modelVersion>
    
    <groupId>net.uvf</groupId>
    <artifactId>uvf-vault-native</artifactId>
    <version>$packageVersion</version>
    <packaging>jar</packaging>
    
    <name>UVF Vault Native</name>
    <description>Java bindings for UVF.NET vault format with native performance</description>
    <url>https://github.com/your-org/uvf.net</url>
    
    <licenses>
        <license>
            <name>MIT License</name>
            <url>https://opensource.org/licenses/MIT</url>
        </license>
    </licenses>
    
    <developers>
        <developer>
            <name>UVF.NET Team</name>
            <email>team@uvf.net</email>
        </developer>
    </developers>
    
    <properties>
        <maven.compiler.source>11</maven.compiler.source>
        <maven.compiler.target>11</maven.compiler.target>
        <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
    </properties>
    
    <dependencies>
        <dependency>
            <groupId>net.java.dev.jna</groupId>
            <artifactId>jna</artifactId>
            <version>5.13.0</version>
        </dependency>
        <dependency>
            <groupId>net.java.dev.jna</groupId>
            <artifactId>jna-platform</artifactId>
            <version>5.13.0</version>
        </dependency>
    </dependencies>
    
    <build>
        <plugins>
            <plugin>
                <groupId>org.apache.maven.plugins</groupId>
                <artifactId>maven-compiler-plugin</artifactId>
                <version>3.11.0</version>
            </plugin>
            <plugin>
                <groupId>org.apache.maven.plugins</groupId>
                <artifactId>maven-resources-plugin</artifactId>
                <version>3.3.1</version>
                <configuration>
                    <includeEmptyDirs>true</includeEmptyDirs>
                </configuration>
            </plugin>
        </plugins>
    </build>
</project>
"@
    
    $pomXmlPath = Join-Path $mavenDir "pom.xml"
    Set-Content $pomXmlPath -Value $pomXmlContent -Encoding UTF8
    
    Write-Success "Created Java Maven pom.xml"
}

# Main execution
function Main {
    $startTime = Get-Date
    
    Write-Header "UVF.NET Binding Package Creator"
    Write-Host "Languages: $($Languages -join ', ')" -ForegroundColor White
    Write-Host "Version: $(Get-ProjectVersion)" -ForegroundColor White
    Write-Host "Native Path: $NativePath" -ForegroundColor White
    Write-Host "Output Path: $OutputPath" -ForegroundColor White
    
    # Verify native libraries exist
    if (-not (Test-Path $NativePath)) {
        Write-Error "Native libraries not found at: $NativePath"
        Write-Info "Run the AOT build scripts first: .\build-aot.ps1"
        exit 1
    }
    
    # Create output directories
    if (-not (Test-Path $OutputPath)) {
        New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    }
    
    try {
        # Create packages for each language
        foreach ($language in $Languages) {
            switch ($language) {
                "CSharp" {
                    New-CSharpPackages
                }
                "Python" {
                    New-PythonPackages
                }
                "NodeJs" {
                    New-NodeJsPackages
                }
                "Java" {
                    New-JavaPackages
                }
                default {
                    Write-Warning "Package creation for $language not yet implemented"
                }
            }
        }
        
        $endTime = Get-Date
        $duration = $endTime - $startTime
        
        Write-Header "Package Creation Summary"
        Write-Host "Completed in $($duration.TotalSeconds.ToString('F1')) seconds" -ForegroundColor Cyan
        Write-Success "Packages available in: $OutputPath"
        
        if ($PublishToRegistry) {
            Write-Warning "Registry publishing not yet implemented"
            Write-Info "Packages are ready for manual publishing"
        }
    }
    catch {
        Write-Error "Package creation failed: $_"
        exit 1
    }
}

# Execute main function
Main 