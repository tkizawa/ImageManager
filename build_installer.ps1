$ErrorActionPreference = "Stop"

Write-Host "Building ImageManager..." -ForegroundColor Green
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build ImageManager."
    exit $LASTEXITCODE
}

Write-Host "Checking for Inno Setup Compiler..." -ForegroundColor Green
$innoSetupPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$innoSetupLocalPath = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"

if (Test-Path $innoSetupLocalPath) {
    $innoSetupPath = $innoSetupLocalPath
}

if (-not (Test-Path $innoSetupPath)) {
    Write-Host "Inno Setup Compiler not found. Attempting to install via winget..." -ForegroundColor Yellow
    winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
    
    # Wait a bit just in case
    Start-Sleep -Seconds 2

    if (-not (Test-Path $innoSetupPath)) {
        Write-Error "Failed to install Inno Setup. Please install it manually from https://jrsoftware.org/isinfo.php"
        exit 1
    }
}

Write-Host "Compiling Installer using Inno Setup..." -ForegroundColor Green
& $innoSetupPath "installer.iss"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile the installer."
    exit $LASTEXITCODE
}

Write-Host "Installer built successfully in the 'Output' directory!" -ForegroundColor Green
