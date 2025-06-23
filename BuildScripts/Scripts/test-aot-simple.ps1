#!/usr/bin/env pwsh
# Simple AOT test script without complex formatting

param(
    [string]$Configuration = "Release",
    [string[]]$Platforms = @("win-x64"),
    [string]$OutputPath = ".\Dist\Native"
)

# Color functions
function Write-Success($Message) { Write-Host "✅ $Message" -ForegroundColor Green }
function Write-Error($Message) { Write-Host "❌ $Message" -ForegroundColor Red }
function Write-Info($Message) { Write-Host "ℹ️ $Message" -ForegroundColor Cyan }

# Test prerequisites
Write-Info "Testing .NET SDK..."
$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    Write-Error ".NET SDK not found"
    exit 1
}
Write-Success ".NET SDK found: $dotnetVersion"

# Test project build
$projectPath = "Uvf.Net\UvfLib.Core\UvfLib.Core.csproj"
if (-not (Test-Path $projectPath)) {
    Write-Error "Project not found: $projectPath"
    exit 1
}
Write-Success "Project found: $projectPath"

# Create output directory
if (-not (Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Build for first platform
$platform = $Platforms[0]
$outputDir = Join-Path $OutputPath $platform
Write-Info "Building UvfLib.Core for $platform..."

$buildArgs = @(
    "publish"
    $projectPath
    "--configuration", $Configuration
    "--runtime", $platform
    "--output", $outputDir
    "/p:PublishAot=true"
    "/p:PublishTrimmed=true"
    "/p:PublishSingleFile=false"
    "--verbosity", "minimal"
)

try {
    & dotnet @buildArgs
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Build completed successfully"
        
        # List output files
        if (Test-Path $outputDir) {
            $files = Get-ChildItem $outputDir -File
            Write-Info "Output files:"
            foreach ($file in $files) {
                $fileName = $file.Name
                $fileSize = $file.Length
                Write-Host "  - $fileName ($fileSize bytes)"
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

Write-Success "AOT test completed successfully" 