#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Resolves StorageLib source code for AOT compilation
.DESCRIPTION
    This script finds StorageLib source code and creates project references for AOT compilation.
.PARAMETER Force
    Force re-resolution even if StorageLib is already resolved
.PARAMETER Restore
    Restore original NuGet references
.PARAMETER Verbose
    Show detailed output
.EXAMPLE
    .\resolve-storagelib.ps1
    .\resolve-storagelib.ps1 -Restore
#>

param(
    [switch]$Force,
    [switch]$Restore,
    [switch]$Verbose
)

# Set error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Global variables
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"

# StorageLib paths
$LocalStorageLibPath = "D:\__PROGRAMMING\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj"

# Projects that need StorageLib
$ProjectsUsingStorageLib = @(
    "UvfLib.Storage\UvfLib.Storage.csproj",
    "UvfLib.FileSystem\UvfLib.FileSystem.csproj"
)

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

# Find StorageLib source code
function Find-StorageLibSource {
    Write-Header "Resolving StorageLib Source Code"
    
    # Check local development path
    if (Test-Path $LocalStorageLibPath) {
        Write-Success "Found local StorageLib: $LocalStorageLibPath"
        return $LocalStorageLibPath
    }
    
    # Check Azure DevOps checkout path
    $azureCheckoutPath = Join-Path $ProjectRoot "../FolderMagic/StorageLib/StorageLib.csproj"
    if (Test-Path $azureCheckoutPath) {
        Write-Success "Found Azure DevOps checkout: $azureCheckoutPath"
        return $azureCheckoutPath
    }
    
    # Not found
    Write-Warning "StorageLib source not found."
    Write-Host "`nPlease ensure one of these paths exists:" -ForegroundColor Yellow
    Write-Host "1. Local: $LocalStorageLibPath" -ForegroundColor White
    Write-Host "2. Azure DevOps: $azureCheckoutPath" -ForegroundColor White
    
    return $null
}

# Update project references
function Update-ProjectReferences {
    param([string]$StorageLibPath)
    
    Write-Header "Updating Project References for AOT"
    
    $relativeStorageLibPath = [System.IO.Path]::GetRelativePath($UvfNetRoot, $StorageLibPath)
    Write-Info "StorageLib relative path: $relativeStorageLibPath"
    
    foreach ($projectFile in $ProjectsUsingStorageLib) {
        $projectPath = Join-Path $UvfNetRoot $projectFile
        
        if (-not (Test-Path $projectPath)) {
            Write-Warning "Project not found: $projectPath"
            continue
        }
        
        Write-Info "Updating project: $projectFile"
        
        # Read project file
        [xml]$projectXml = Get-Content $projectPath
        
        # Remove NuGet PackageReference to StorageLib
        $packageReferences = $projectXml.Project.ItemGroup.PackageReference | Where-Object { 
            $_.Include -like "*StorageLib*"
        }
        
        foreach ($packageRef in $packageReferences) {
            Write-Info "Removing NuGet reference: $($packageRef.Include)"
            $packageRef.ParentNode.RemoveChild($packageRef) | Out-Null
        }
        
        # Add ProjectReference to StorageLib
        $projectRefExists = $projectXml.Project.ItemGroup.ProjectReference | Where-Object {
            $_.Include -like "*StorageLib*"
        }
        
        if (-not $projectRefExists) {
            # Find or create ItemGroup for ProjectReference
            $projectRefGroup = $projectXml.Project.ItemGroup | Where-Object { $_.ProjectReference }
            if (-not $projectRefGroup) {
                $projectRefGroup = $projectXml.CreateElement("ItemGroup")
                $projectXml.Project.AppendChild($projectRefGroup) | Out-Null
            }
            
            # Create ProjectReference element
            $projectRef = $projectXml.CreateElement("ProjectReference")
            $projectRef.SetAttribute("Include", $relativeStorageLibPath)
            $projectRefGroup.AppendChild($projectRef) | Out-Null
            
            Write-Success "Added project reference to StorageLib"
        }
        
        # Save the project file
        $projectXml.Save($projectPath)
        Write-Success "Updated $projectFile"
    }
}

# Restore original NuGet references
function Restore-NuGetReferences {
    Write-Header "Restoring NuGet References"
    
    foreach ($projectFile in $ProjectsUsingStorageLib) {
        $projectPath = Join-Path $UvfNetRoot $projectFile
        
        if (-not (Test-Path $projectPath)) {
            continue
        }
        
        Write-Info "Restoring NuGet references in: $projectFile"
        
        # Read project file
        [xml]$projectXml = Get-Content $projectPath
        
        # Remove ProjectReference to StorageLib
        $projectRefs = $projectXml.Project.ItemGroup.ProjectReference | Where-Object {
            $_.Include -like "*StorageLib*"
        }
        
        foreach ($projectRef in $projectRefs) {
            Write-Info "Removing project reference: $($projectRef.Include)"
            $projectRef.ParentNode.RemoveChild($projectRef) | Out-Null
        }
        
        # Add back NuGet PackageReference
        $packageRefExists = $projectXml.Project.ItemGroup.PackageReference | Where-Object {
            $_.Include -like "*StorageLib*"
        }
        
        if (-not $packageRefExists) {
            # Find or create ItemGroup for PackageReference
            $packageRefGroup = $projectXml.Project.ItemGroup | Where-Object { $_.PackageReference }
            if (-not $packageRefGroup) {
                $packageRefGroup = $projectXml.CreateElement("ItemGroup")
                $projectXml.Project.AppendChild($packageRefGroup) | Out-Null
            }
            
            # Create PackageReference element
            $packageRef = $projectXml.CreateElement("PackageReference")
            
            # Determine the correct package name and version based on project
            if ($projectFile -like "*UvfLib.Storage*") {
                $packageRef.SetAttribute("Include", "FolderMagic.StorageLib")
                $packageRef.SetAttribute("Version", "1.0.250613.1337-dev")
            } else {
                $packageRef.SetAttribute("Include", "StorageLib")
                $packageRef.SetAttribute("Version", "1.0.0")
            }
            
            $packageRefGroup.AppendChild($packageRef) | Out-Null
            Write-Success "Restored NuGet reference"
        }
        
        # Save the project file
        $projectXml.Save($projectPath)
        Write-Success "Restored $projectFile"
    }
}

# Check if StorageLib is already resolved
function Test-StorageLibResolved {
    foreach ($projectFile in $ProjectsUsingStorageLib) {
        $projectPath = Join-Path $UvfNetRoot $projectFile
        
        if (Test-Path $projectPath) {
            [xml]$projectXml = Get-Content $projectPath
            $projectRefs = $projectXml.Project.ItemGroup.ProjectReference | Where-Object {
                $_.Include -like "*StorageLib*"
            }
            
            if ($projectRefs) {
                return $true
            }
        }
    }
    return $false
}

# Main execution
function Main {
    Write-Header "StorageLib Source Resolution for AOT Compilation"
    
    # Handle restore first
    if ($Restore) {
        Restore-NuGetReferences
        Write-Success "NuGet references restored"
        return
    }
    
    # Check if already resolved
    if (-not $Force -and (Test-StorageLibResolved)) {
        Write-Success "StorageLib is already resolved with project references"
        Write-Host "Use -Force to re-resolve or -Restore to revert to NuGet" -ForegroundColor Yellow
        return
    }
    
    try {
        # Find StorageLib source
        $storageLibPath = Find-StorageLibSource
        
        if (-not $storageLibPath) {
            Write-Error "Could not resolve StorageLib source code"
            exit 1
        }
        
        # Update project references
        Update-ProjectReferences -StorageLibPath $storageLibPath
        
        Write-Success "StorageLib source resolved successfully!"
        Write-Host "StorageLib path: $storageLibPath" -ForegroundColor White
        Write-Host "Projects updated: $($ProjectsUsingStorageLib.Count)" -ForegroundColor White
        
        Write-Host "`nNext steps:" -ForegroundColor Yellow
        Write-Host "1. Build with AOT: .\Scripts\build-aot.ps1" -ForegroundColor White
        Write-Host "2. Restore NuGet refs: .\Scripts\resolve-storagelib.ps1 -Restore" -ForegroundColor White
        
    }
    catch {
        $errorMessage = $_.Exception.Message
        Write-Error "Failed to resolve StorageLib: $errorMessage"
        exit 1
    }
}

# Execute main function
Main 