<#
.SYNOPSIS
    Builds, publishes (self-contained single-file), and Authenticode-signs SaudiPatientDesk.

.DESCRIPTION
    Equivalent to:
      dotnet publish ... -r win-x64 --self-contained -p:PublishSingleFile=true
    then signs the resulting EXE with the PFX created by 01-create-certificate.ps1.

.PARAMETER Configuration
    Build configuration (default: Release)

.PARAMETER PfxPath
    Path to the .pfx file (default: certs\SaudiPatientDesk.pfx relative to repo root)

.PARAMETER PfxPassword
    Password for the PFX. If omitted, you will be prompted.

.PARAMETER SkipBuild
    Only sign an already-published EXE (useful for re-signing).

.EXAMPLE
    .\02-build-and-sign.ps1
    .\02-build-and-sign.ps1 -PfxPassword "YourPassword"
#>

param(
    [string]$Configuration = "Release",
    [string]$PfxPath = "",
    [string]$PfxPassword = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Project = Join-Path $RepoRoot "src\SaudiPatientDesk\SaudiPatientDesk.csproj"
$OutDir = Join-Path $RepoRoot "out\SaudiPatientDesk-win-x64"
$ExeName = "SaudiPatientDesk.exe"

if ([string]::IsNullOrWhiteSpace($PfxPath)) {
    $PfxPath = Join-Path $RepoRoot "certs\SaudiPatientDesk.pfx"
}

Write-Host "=== SaudiPatientDesk — Build + Sign ===" -ForegroundColor Cyan
Write-Host "Repo root : $RepoRoot"
Write-Host ""

# ---------- Locate signtool ----------
function Find-SignTool {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
        "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\signtool.exe"
    )
    $found = Get-ChildItem -Path $candidates -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($found) { return $found.FullName }

    # Fallback: vswhere + Windows SDK
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $kitRoot = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.Windows10SDK.* -property installationPath 2>$null
    }
    throw "signtool.exe not found. Install 'Windows 10/11 SDK' (Signing Tools) via Visual Studio Installer or standalone SDK."
}

# ---------- Build / Publish ----------
if (-not $SkipBuild) {
    Write-Host "[1/3] Publishing self-contained single-file (win-x64)..." -ForegroundColor Yellow

    if (Test-Path $OutDir) {
        Remove-Item -Recurse -Force $OutDir
    }
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

    & dotnet publish $Project `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $OutDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
    Write-Host "Publish OK → $OutDir" -ForegroundColor Green
} else {
    Write-Host "[1/3] Skipping build (-SkipBuild)." -ForegroundColor DarkGray
}

$exePath = Join-Path $OutDir $ExeName
if (-not (Test-Path $exePath)) {
    throw "Expected EXE not found: $exePath"
}

# ---------- Password ----------
if ([string]::IsNullOrWhiteSpace($PfxPassword)) {
    if (-not (Test-Path $PfxPath)) {
        throw "PFX not found at '$PfxPath'. Run 01-create-certificate.ps1 first."
    }
    $secure = Read-Host "PFX password" -AsSecureString
    $BSTR = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    $PfxPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
}

if (-not (Test-Path $PfxPath)) {
    throw "PFX not found: $PfxPath"
}

# ---------- Sign ----------
Write-Host "[2/3] Signing $ExeName ..." -ForegroundColor Yellow

$signtool = Find-SignTool
Write-Host "Using signtool: $signtool"

# RFC 3161 timestamp (DigiCert public server — free, no account needed)
$timestampUrl = "http://timestamp.digicert.com"

& $signtool sign `
    /f $PfxPath `
    /p $PfxPassword `
    /fd SHA256 `
    /td SHA256 `
    /tr $timestampUrl `
    /v `
    $exePath

if ($LASTEXITCODE -ne 0) {
    throw "signtool failed with exit code $LASTEXITCODE"
}

# Verify
Write-Host "[3/3] Verifying signature..." -ForegroundColor Yellow
& $signtool verify /pa /v $exePath
if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: verification returned non-zero. Check output above." -ForegroundColor DarkYellow
} else {
    Write-Host "Signature verified." -ForegroundColor Green
}

# Optional: also show PowerShell view of the signature
$sig = Get-AuthenticodeSignature $exePath
Write-Host ""
Write-Host "Status     : $($sig.Status)" -ForegroundColor $(if ($sig.Status -eq 'Valid') { 'Green' } else { 'Yellow' })
Write-Host "Signer     : $($sig.SignerCertificate.Subject)"
Write-Host "Thumbprint : $($sig.SignerCertificate.Thumbprint)"
Write-Host ""

# Zip for distribution
$zipPath = Join-Path $RepoRoot "out\SaudiPatientDesk-v7.0.11-win-x64-signed.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $OutDir "*") -DestinationPath $zipPath -Force

Write-Host "=== DONE ===" -ForegroundColor Cyan
Write-Host "Signed EXE : $exePath"
Write-Host "ZIP        : $zipPath"
Write-Host ""
Write-Host "On each target PC install the certificate first:" -ForegroundColor Yellow
Write-Host "  scripts\03-install-certificate.ps1"
Write-Host "Then copy the EXE (or the whole folder) and run it."
Write-Host ""
