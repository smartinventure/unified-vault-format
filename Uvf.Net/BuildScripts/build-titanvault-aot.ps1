# TitanVault AOT Build Script
# This script performs native AOT compilation of TitanVault.dll
# Use this instead of the automatic MSBuild targets to avoid infinite loops

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [switch]$PublishSingleFile = $false,
    [switch]$Clean = $false,
    [switch]$CopyToDemoApp = $true
)

Write-Host "🔧 TitanVault AOT Build Script" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Runtime: $Runtime" -ForegroundColor Yellow
Write-Host "Self-Contained: $SelfContained" -ForegroundColor Yellow
Write-Host "Copy to DemoApp: $CopyToDemoApp" -ForegroundColor Yellow
Write-Host ""

# Change to UvfLib.Master directory
$originalLocation = Get-Location
try {
    Set-Location "UvfLib.Master"
    
    if ($Clean) {
        Write-Host "🧹 Cleaning previous builds..." -ForegroundColor Yellow
        dotnet clean -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "Clean failed" }
    }
    
    Write-Host "📦 Building TitanVault with AOT..." -ForegroundColor Green
    
    $publishArgs = @(
        "publish"
        "-c", $Configuration
        "-r", $Runtime
        "--self-contained", $SelfContained.ToString().ToLower()
        "-p:PublishAot=true"
        "-p:OutputType=Library"
        "-p:EnableDynamicLoading=true"
        "-p:IsAotCompatible=true"
        "-p:NativeLib=Shared"
        "-p:CustomAfterMicrosoftCommonTargets="
        "-p:OptimizationPreference=Speed"
        "-p:IlcOptimizationPreference=Speed"
        "-p:IlcFoldIdenticalMethodBodies=true"
        "--verbosity", "minimal"
    )
    
    if ($PublishSingleFile) {
        $publishArgs += "-p:PublishSingleFile=true"
    }
    
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ AOT build completed successfully!" -ForegroundColor Green
        Write-Host ""
        
        $outputPath = "bin\$Configuration\net8.0\$Runtime\publish"
        $sourceDll = Join-Path $outputPath "TitanVault.dll"
        
        Write-Host "📁 Output location:" -ForegroundColor Cyan
        Write-Host "   $outputPath" -ForegroundColor White
        
        if (Test-Path $sourceDll) {
            Write-Host ""
            Write-Host "📋 Generated files:" -ForegroundColor Cyan
            Get-ChildItem $outputPath -Filter "TitanVault.*" | ForEach-Object {
                $size = [math]::Round($_.Length / 1MB, 2)
                Write-Host "   $($_.Name) ($size MB)" -ForegroundColor White
            }
            
            # Copy to distribution directory for DemoApp
            if ($CopyToDemoApp) {
                Write-Host ""
                Write-Host "📂 Setting up distribution directory..." -ForegroundColor Cyan
                
                $distPath = "..\Dist\Native\$Runtime"
                New-Item -ItemType Directory -Path $distPath -Force | Out-Null
                
                $targetDll = Join-Path $distPath "TitanVault.dll"
                Copy-Item $sourceDll $targetDll -Force
                
                Write-Host "✅ Copied TitanVault.dll to: $distPath" -ForegroundColor Green
                
                # Also copy PDB for debugging if it exists
                $sourcePdb = Join-Path $outputPath "TitanVault.pdb"
                if (Test-Path $sourcePdb) {
                    $targetPdb = Join-Path $distPath "TitanVault.pdb"
                    Copy-Item $sourcePdb $targetPdb -Force
                    Write-Host "✅ Copied TitanVault.pdb for debugging" -ForegroundColor Green
                }
                
                Write-Host ""
                Write-Host "🎯 DemoApp is now ready to use the native AOT library!" -ForegroundColor Green
                Write-Host "   Run: dotnet run --project ..\DemoApp" -ForegroundColor White
            }
        } else {
            Write-Host "❌ TitanVault.dll not found in output directory!" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "❌ AOT build failed!" -ForegroundColor Red
        exit 1
    }
    
} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    Set-Location $originalLocation
}

Write-Host ""
Write-Host "🎉 Build script completed!" -ForegroundColor Green
Write-Host ""
Write-Host "💡 Usage examples:" -ForegroundColor Cyan
Write-Host "   .\BuildScripts\build-titanvault-aot.ps1                    # Build and copy to DemoApp"
Write-Host "   .\BuildScripts\build-titanvault-aot.ps1 -Clean             # Clean build"
Write-Host "   .\BuildScripts\build-titanvault-aot.ps1 -Runtime linux-x64 # Linux build"
Write-Host "   .\BuildScripts\build-titanvault-aot.ps1 -CopyToDemoApp:`$false # Build only" 