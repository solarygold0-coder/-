<#
.SYNOPSIS
    One-time creation of a self-signed code-signing certificate for SaudiPatientDesk.

.DESCRIPTION
    Creates a code-signing certificate valid for 5 years, exports:
      - SaudiPatientDesk.pfx  (private key + cert)  → keep secret, used for signing
      - SaudiPatientDesk.cer  (public cert only)    → install on every PC that will run the app

    Run this script ONCE on your development machine (as normal user is enough).

.NOTES
    After running, store the .pfx password in a safe place.
    Never commit the .pfx file to git.
#>

param(
    [string]$Subject = "CN=Saudi Patient Desk",
    [string]$FriendlyName = "SaudiPatientDesk Code Signing",
    [int]$YearsValid = 5,
    [string]$OutputDir = "$PSScriptRoot\..\certs",
    [string]$PfxPassword = ""
)

$ErrorActionPreference = "Stop"

Write-Host "=== SaudiPatientDesk — Create Code-Signing Certificate ===" -ForegroundColor Cyan
Write-Host ""

# Ensure output folder
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
$OutputDir = (Resolve-Path $OutputDir).Path

# Prompt for password if not supplied
if ([string]::IsNullOrWhiteSpace($PfxPassword)) {
    $secure = Read-Host "Enter a strong password for the .pfx file" -AsSecureString
    $PfxPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    if ([string]::IsNullOrWhiteSpace($PfxPassword) -or $PfxPassword.Length -lt 8) {
        throw "Password must be at least 8 characters."
    }
}

Write-Host "Creating self-signed certificate..." -ForegroundColor Yellow

$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -FriendlyName $FriendlyName `
    -TextExtension @(
        "2.5.29.37={text}1.3.6.1.5.5.7.3.3",   # Code Signing EKU
        "2.5.29.19={text}"                      # Basic Constraints = End Entity
    ) `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyExportPolicy Exportable `
    -KeySpec Signature `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($YearsValid)

Write-Host "Certificate created in CurrentUser\My" -ForegroundColor Green
Write-Host "  Thumbprint : $($cert.Thumbprint)"
Write-Host "  Subject    : $($cert.Subject)"
Write-Host "  Valid until: $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host ""

# Export public .cer (for installing on target machines)
$cerPath = Join-Path $OutputDir "SaudiPatientDesk.cer"
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Write-Host "Exported public certificate : $cerPath" -ForegroundColor Green

# Export .pfx (private key) for signing
$pfxPath = Join-Path $OutputDir "SaudiPatientDesk.pfx"
$securePass = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePass | Out-Null
Write-Host "Exported PFX (private key)  : $pfxPath" -ForegroundColor Green
Write-Host ""

# Save a small info file (no password)
$infoPath = Join-Path $OutputDir "certificate-info.txt"
@"
SaudiPatientDesk Code-Signing Certificate
Created : $(Get-Date -Format 'yyyy-MM-dd HH:mm')
Subject : $Subject
Thumbprint : $($cert.Thumbprint)
Valid until : $($cert.NotAfter.ToString('yyyy-MM-dd'))

Files:
  SaudiPatientDesk.cer  → Install on every target PC (Trusted Root)
  SaudiPatientDesk.pfx  → Keep secret. Used only for signing builds.

IMPORTANT:
  - Do NOT commit the .pfx file to git.
  - Add certs/ to .gitignore if not already present.
  - On each clinic PC run: scripts\03-install-certificate.ps1
"@ | Set-Content -Path $infoPath -Encoding UTF8

Write-Host "Info written to              : $infoPath" -ForegroundColor Green
Write-Host ""
Write-Host "=== NEXT STEPS ===" -ForegroundColor Cyan
Write-Host "1. Remember the PFX password you just entered."
Write-Host "2. Run scripts\02-build-and-sign.ps1 to publish + sign the app."
Write-Host "3. On every PC that will run the app, run scripts\03-install-certificate.ps1"
Write-Host "   (or double-click SaudiPatientDesk.cer and install to Trusted Root)."
Write-Host ""
Write-Host "Done." -ForegroundColor Green
