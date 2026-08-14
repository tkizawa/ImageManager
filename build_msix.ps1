$ErrorActionPreference = "Stop"

Write-Host "Building and Packaging ImageManager as MSIX (x64 & ARM64)..." -ForegroundColor Green

# Build x64 MSIX
Write-Host "Building win-x64 MSIX..." -ForegroundColor Cyan
dotnet publish -c Release -r win-x64 /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build ImageManager win-x64 MSIX."
    exit $LASTEXITCODE
}

# Build ARM64 MSIX
Write-Host "Building win-arm64 MSIX..." -ForegroundColor Cyan
dotnet publish -c Release -r win-arm64 /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build ImageManager win-arm64 MSIX."
    exit $LASTEXITCODE
}

# Copy generated MSIX packages to shallow MSIX directory in project root
$targetDir = Join-Path $PSScriptRoot "MSIX"
if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir | Out-Null
}

$x64Msix = Get-ChildItem -Path "$PSScriptRoot\bin\Release\net10.0-windows10.0.19041.0\win-x64\AppPackages" -Recurse -Filter "*.msix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($x64Msix) {
    Copy-Item -Path $x64Msix.FullName -Destination $targetDir -Force
    Write-Host "Copied x64 MSIX -> $targetDir\$($x64Msix.Name)" -ForegroundColor Yellow
}

$arm64Msix = Get-ChildItem -Path "$PSScriptRoot\bin\Release\net10.0-windows10.0.19041.0\win-arm64\AppPackages" -Recurse -Filter "*.msix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($arm64Msix) {
    Copy-Item -Path $arm64Msix.FullName -Destination $targetDir -Force
    Write-Host "Copied ARM64 MSIX -> $targetDir\$($arm64Msix.Name)" -ForegroundColor Yellow
}

Write-Host "MSIX Packages built successfully and placed in: $targetDir" -ForegroundColor Green
