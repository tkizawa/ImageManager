param(
    [ValidateSet("x64", "arm64", "all")]
    [string]$Architecture
)

$ErrorActionPreference = "Stop"

# プロジェクト情報およびバージョンの取得
$projPath = Join-Path $PSScriptRoot "ImageManager.csproj"
[xml]$projXml = Get-Content $projPath
$version = $projXml.Project.PropertyGroup.Version
if (-not $version) { $version = $projXml.Project.PropertyGroup.FileVersion }
if (-not $version) { $version = "1.1.4.0" }

# 対象アーキテクチャの決定 (指定がなければ実行環境に合わせる)
$targetArchs = if ($Architecture -eq "all") {
    @("x64", "arm64")
} elseif ($Architecture) {
    @($Architecture)
} else {
    $currentArch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) { "arm64" } else { "x64" }
    @($currentArch)
}

Write-Host "=== ImageManager スタンドアロンインストーラー作成 (v$version) ===" -ForegroundColor Cyan

# 出力先ディレクトリ (規約: .\Installer)
$installerDir = Join-Path $PSScriptRoot "Installer"
if (-not (Test-Path $installerDir)) {
    New-Item -ItemType Directory -Path $installerDir | Out-Null
}

# Inno Setup コンパイラの検出
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

$issFile = Join-Path $PSScriptRoot "installer.iss"
$builtFiles = @()

foreach ($arch in $targetArchs) {
    Write-Host ""
    Write-Host "--------------------------------------------------" -ForegroundColor Cyan
    Write-Host ">>> [win-$arch] ビルドおよびインストーラー作成開始" -ForegroundColor Cyan
    Write-Host "--------------------------------------------------" -ForegroundColor Cyan

    Write-Host "Publishing ImageManager for win-$arch..." -ForegroundColor Green
    dotnet publish "$projPath" -c Release -r "win-$arch" --self-contained true -p:WindowsPackageType=None

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build ImageManager for win-$arch."
        exit $LASTEXITCODE
    }

    $outputBaseFilename = "ImageManager_Setup_${version}_$arch"

    Write-Host "Compiling Installer using Inno Setup ($outputBaseFilename.exe)..." -ForegroundColor Green
    & $innoSetupPath "/DMyAppVersion=$version" "/DMyArch=$arch" "/DMyOutputDir=$installerDir" "/DMyOutputFilename=$outputBaseFilename" "$issFile"

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to compile the installer for win-$arch."
        exit $LASTEXITCODE
    }

    $targetExe = Join-Path $installerDir "$outputBaseFilename.exe"
    if (Test-Path $targetExe) {
        $fileInfo = Get-Item $targetExe
        $hashInfo = Get-FileHash -Path $targetExe -Algorithm SHA256
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        $builtFiles += [PSCustomObject]@{
            Arch     = $arch
            Path     = $fileInfo.FullName
            FileName = $fileInfo.Name
            SizeMB   = $sizeMB
            SizeByte = $fileInfo.Length
            SHA256   = $hashInfo.Hash
        }
    } else {
        Write-Warning "生成ファイルが見つかりません: $targetExe"
    }
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor Green
Write-Host "すべてのインストーラーの作成が完了しました" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Green
foreach ($item in $builtFiles) {
    Write-Host "[$($item.Arch)] $($item.FileName)" -ForegroundColor Yellow
    Write-Host "  パス:    $($item.Path)" -ForegroundColor White
    Write-Host "  サイズ:  $($item.SizeMB) MB ($($item.SizeByte) bytes)" -ForegroundColor White
    Write-Host "  SHA256:  $($item.SHA256)" -ForegroundColor White
}


