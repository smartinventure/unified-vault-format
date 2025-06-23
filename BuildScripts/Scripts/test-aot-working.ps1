param(
    [string]$Configuration = "Release",
    [string]$Platform = "win-x64",
    [string]$OutputPath = ".\Dist\Native"
)

# Simple output functions
function Write-Success($Message) { 
    Write-Host "[SUCCESS] $Message" -ForegroundColor Green 
}
function Write-Error($Message) { 
    Write-Host "[ERROR] $Message" -ForegroundColor Red 
}
function Write-Info($Message) { 
    Write-Host "[INFO] $Message" -ForegroundColor Cyan 
}

Write-Info "=== UVF.NET AOT Build Test ==="
Write-Info "Platform: $Platform"
Write-Info "Configuration: $Configuration"

# Test prerequisites
Write-Info "Testing .NET SDK..."
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET SDK not found"
    exit 1
}
Write-Success ".NET SDK found: $dotnetVersion"

# Test project
$projectPath = "Uvf.Net\UvfLib.Core\UvfLib.Core.csproj"
if (-not (Test-Path $projectPath)) {
    Write-Error "Project not found: $projectPath"
    exit 1
}
Write-Success "Project found: $projectPath"

# Create output directory
$outputDir = Join-Path $OutputPath $Platform
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    Write-Info "Created output directory: $outputDir"
}

Write-Info "Building UvfLib.Core for $Platform with AOT..."

# Use explicit public NuGet source to avoid authentication issues
$buildArgs = @(
    "publish"
    $projectPath
    "--configuration", $Configuration
    "--runtime", $Platform
    "--output", $outputDir
    "/p:PublishAot=true"
    "/p:PublishTrimmed=true"
    "/p:PublishSingleFile=false"
    "/p:DebugType=embedded"
    "--source", "https://api.nuget.org/v3/index.json"
    "--verbosity", "normal"
)

try {
    Write-Info "Executing: dotnet $($buildArgs -join ' ')"
    & dotnet @buildArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "AOT build completed successfully!"
        
        # List output files
        if (Test-Path $outputDir) {
            $files = Get-ChildItem $outputDir -File
            Write-Info "Output files ($($files.Count) total):"
            foreach ($file in $files) {
                $sizeKB = [math]::Round($file.Length / 1KB, 1)
                Write-Host "  - $($file.Name) ($sizeKB KB)"
            }
            
            # Look for native library
            $nativeLib = $files | Where-Object { $_.Extension -eq ".dll" -and $_.Name -like "*UvfLib.Core*" }
            if ($nativeLib) {
                Write-Success "Native library found: $($nativeLib.Name)"
                $sizeMB = [math]::Round($nativeLib.Length / 1MB, 2)
                Write-Info "Native library size: $sizeMB MB"
            }
            
            # Check for AOT-specific files
            $aotFiles = $files | Where-Object { $_.Name -like "*.so" -or $_.Name -like "*.dylib" -or $_.Name -like "*native*" }
            if ($aotFiles) {
                Write-Success "AOT-specific files found:"
                foreach ($aotFile in $aotFiles) {
                    Write-Host "  - $($aotFile.Name)"
                }
            }
        }
    } else {
        Write-Error "Build failed with exit code: $LASTEXITCODE"
        exit 1
    }
} catch {
    Write-Error "Build exception: $($_.Exception.Message)"
    exit 1
}

Write-Success "AOT test completed successfully!"
Write-Info "Output location: $outputDir" 