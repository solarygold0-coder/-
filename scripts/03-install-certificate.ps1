<#
.SYNOPSIS
    Installs the SaudiPatientDesk public certificate into Trusted Root Certification Authorities.

.DESCRIPTION
    Must be run once on every Windows PC that will execute the signed application.
    Requires Administrator privileges (because Trusted Root is a machine-wide store).

.PARAMETER CerPath
    Path to SaudiPatientDesk.cer (default: certs\SaudiPatientDesk.cer relative to repo, or same folder as this script).

.EXAMPLE
    # Right-click PowerShell → Run as administrator, then:
    .\03-install-certificate.ps1
#>

param(
    [string]$CerPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "=== SaudiPatientDesk — Install Certificate (Trusted Root) ===" -ForegroundColor Cyan
Write-Host ""

# Must be elevated
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click PowerShell → Run as administrator, then run this script again."
    exit 1
}

# Locate .cer
if ([string]::IsNullOrWhiteSpace($CerPath)) {
    $candidates = @(
        (Join-Path $PSScriptRoot "..\certs\SaudiPatientDesk.cer"),
        (Join-Path $PSScriptRoot "SaudiPatientDesk.cer"),
        (Join-Path (Get-Location) "SaudiPatientDesk.cer"),
        (Join-Path (Get-Location) "certs\SaudiPatientDesk.cer")
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $CerPath = $c; break }
    }
}

if (-not (Test-Path $CerPath)) {
    throw "Certificate file not found. Place SaudiPatientDesk.cer next to this script or pass -CerPath."
}

$CerPath = (Resolve-Path $CerPath).Path
Write-Host "Certificate : $CerPath"

# Import into Local Machine \ Trusted Root Certification Authorities
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
    [System.Security.Cryptography.X509Certificates.StoreName]::Root,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)

$cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CerPath)

$store.Open("ReadWrite")
try {
    # Avoid duplicate
    $existing = $store.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if ($existing) {
        Write-Host "Certificate already installed (Thumbprint: $($cert.Thumbprint))." -ForegroundColor Green
    } else {
        $store.Add($cert)
        Write-Host "Certificate installed successfully into Trusted Root." -ForegroundColor Green
        Write-Host "  Subject    : $($cert.Subject)"
        Write-Host "  Thumbprint : $($cert.Thumbprint)"
        Write-Host "  Valid until: $($cert.NotAfter.ToString('yyyy-MM-dd'))"
    }
}
finally {
    $store.Close()
}

Write-Host ""
Write-Host "You can now run the signed SaudiPatientDesk.exe without the 'Unknown publisher' warning." -ForegroundColor Cyan
Write-Host "Note: SmartScreen may still show a first-run prompt for brand-new file hashes;" -ForegroundColor DarkGray
Write-Host "      after one 'Run anyway' the file is usually remembered on that PC." -ForegroundColor DarkGray
Write-Host ""
Write-Host "Done." -ForegroundColor Green
