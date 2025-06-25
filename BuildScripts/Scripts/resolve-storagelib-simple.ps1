#!/usr/bin/env pwsh

param(
    [switch]$Restore
)

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$UvfNetRoot = Join-Path $ProjectRoot "Uvf.Net"
$LocalStorageLibPath = "D:\__PROGRAMMING\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj"

$ProjectsUsingStorageLib = @(
    "UvfLib.Storage\UvfLib.Storage.csproj",
    "UvfLib.FileSystem\UvfLib.FileSystem.csproj"
)

Write-Host "=== StorageLib Resolution ===" -ForegroundColor Cyan

if ($Restore) {
    Write-Host "Restoring NuGet references..." -ForegroundColor Yellow
    
    foreach ($projectFile in $ProjectsUsingStorageLib) {
        $projectPath = Join-Path $UvfNetRoot $projectFile
        
        if (Test-Path $projectPath) {
            Write-Host "Processing: $projectFile" -ForegroundColor White
            
            [xml]$projectXml = Get-Content $projectPath
            
            # Remove project references
            $projectRefs = $projectXml.Project.ItemGroup.ProjectReference | Where-Object {
                $_.Include -like "*StorageLib*"
            }
            
            foreach ($projectRef in $projectRefs) {
                $projectRef.ParentNode.RemoveChild($projectRef) | Out-Null
            }
            
            # Add NuGet reference back
            $packageRefGroup = $projectXml.Project.ItemGroup | Where-Object { $_.PackageReference } | Select-Object -First 1
            if (-not $packageRefGroup) {
                $packageRefGroup = $projectXml.CreateElement("ItemGroup")
                $projectXml.Project.AppendChild($packageRefGroup) | Out-Null
            }
            
            $packageRef = $projectXml.CreateElement("PackageReference")
            if ($projectFile -like "*UvfLib.Storage*") {
                $packageRef.SetAttribute("Include", "FolderMagic.StorageLib")
                $packageRef.SetAttribute("Version", "1.0.250613.1337-dev")
            } else {
                $packageRef.SetAttribute("Include", "StorageLib")
                $packageRef.SetAttribute("Version", "1.0.0")  
            }
            $packageRefGroup.AppendChild($packageRef) | Out-Null
            
            $projectXml.Save($projectPath)
            Write-Host "✅ Restored NuGet reference" -ForegroundColor Green
        }
    }
    
    Write-Host "✅ NuGet references restored" -ForegroundColor Green
    return
}

# Check if StorageLib exists
if (-not (Test-Path $LocalStorageLibPath)) {
    Write-Host "❌ StorageLib not found at: $LocalStorageLibPath" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Found StorageLib: $LocalStorageLibPath" -ForegroundColor Green

# Manual relative path calculation
# From Uvf.Net/UvfLib.Storage/ to D:\__PROGRAMMING\FolderMagic\FolderMagic\StorageLib\
$relativeStorageLibPath = "..\..\..\FolderMagic\FolderMagic\StorageLib\StorageLib.csproj"
Write-Host "Relative path: $relativeStorageLibPath" -ForegroundColor White

foreach ($projectFile in $ProjectsUsingStorageLib) {
    $projectPath = Join-Path $UvfNetRoot $projectFile
    
    if (Test-Path $projectPath) {
        Write-Host "Processing: $projectFile" -ForegroundColor White
        
        [xml]$projectXml = Get-Content $projectPath
        
        # Remove NuGet references
        $packageReferences = $projectXml.Project.ItemGroup.PackageReference | Where-Object { 
            $_.Include -like "*StorageLib*"
        }
        
        foreach ($packageRef in $packageReferences) {
            Write-Host "Removing NuGet: $($packageRef.Include)" -ForegroundColor Yellow
            $packageRef.ParentNode.RemoveChild($packageRef) | Out-Null
        }
        
        # Add project reference
        $projectRefGroup = $projectXml.Project.ItemGroup | Where-Object { $_.ProjectReference } | Select-Object -First 1
        if (-not $projectRefGroup) {
            $projectRefGroup = $projectXml.CreateElement("ItemGroup")
            $projectXml.Project.AppendChild($projectRefGroup) | Out-Null
        }
        
        $projectRef = $projectXml.CreateElement("ProjectReference")
        $projectRef.SetAttribute("Include", $relativeStorageLibPath)
        $projectRefGroup.AppendChild($projectRef) | Out-Null
        
        $projectXml.Save($projectPath)
        Write-Host "✅ Added project reference" -ForegroundColor Green
    }
}

Write-Host "✅ StorageLib resolved for AOT compilation!" -ForegroundColor Green
Write-Host "Use -Restore to revert to NuGet references" -ForegroundColor Yellow 