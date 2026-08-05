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

Write-Host "MSIX Packages built successfully!" -ForegroundColor Green
Write-Host "x64 MSIX: bin\Release\net10.0-windows10.0.19041.0\win-x64\AppPackages"
Write-Host "ARM64 MSIX: bin\Release\net10.0-windows10.0.19041.0\win-arm64\AppPackages"
