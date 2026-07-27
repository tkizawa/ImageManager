$ErrorActionPreference = "Stop"

Write-Host "Building and Packaging ImageManager as MSIX..." -ForegroundColor Green

# To generate an MSIX, we publish the project. 
# We disable AppxPackageSigningEnabled because Microsoft Store handles the signing.
# If you want to test the MSIX locally, you will need a self-signed certificate.
dotnet publish -c Release -r win-x64 /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build ImageManager MSIX."
    exit $LASTEXITCODE
}

Write-Host "MSIX Package built successfully! Check the AppPackages folder in bin\Release\net10.0-windows10.0.19041.0\win-x64\AppPackages" -ForegroundColor Green
