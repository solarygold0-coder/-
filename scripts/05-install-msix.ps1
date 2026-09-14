<#
.SYNOPSIS
    Trusts the supplied public certificate and installs or updates SaudiPatientDesk.

.DESCRIPTION
    Run this file from the extracted release folder. It does not delete the
    patient database. It replaces the desktop shortcut with a package-identity
    shortcut so it always opens the active version selected by Windows.
#>

param(
    [string]$PackagePath = "",
    [string]$CertificatePath = ""
)

$ErrorActionPreference = "Stop"
$ReleaseDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    $candidate = Get-ChildItem $ReleaseDir -Filter "SaudiPatientDesk-*.msix" | Select-Object -First 1
    if ($null -eq $candidate) { throw "لم يتم العثور على ملف MSIX في مجلد الإصدار." }
    $PackagePath = $candidate.FullName
}
if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    $CertificatePath = Join-Path $ReleaseDir "SaudiPatientDesk.cer"
}
if (-not (Test-Path $PackagePath)) { throw "ملف التثبيت غير موجود: $PackagePath" }
if (-not (Test-Path $CertificatePath)) { throw "ملف الشهادة غير موجود: $CertificatePath" }

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
$trusted = Get-ChildItem "Cert:\CurrentUser\TrustedPeople\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
if ($null -eq $trusted) {
    Import-Certificate -FilePath $CertificatePath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
}

$installed = Get-AppxPackage -Name "SaudiPatientDesk" | Sort-Object Version -Descending | Select-Object -First 1
if ($null -ne $installed) {
    Write-Host "الإصدار المثبت حالياً: $($installed.Version)"
}

Add-AppxPackage -Path $PackagePath -ForceApplicationShutdown
$package = Get-AppxPackage -Name "SaudiPatientDesk" | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $package) { throw "لم يكتمل تسجيل التطبيق في ويندوز." }

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath = Join-Path $desktop "نظام سجلات المرضى السعودي.lnk"
$appUserModelId = "$($package.PackageFamilyName)!SaudiPatientDesk"
$shell = New-Object -ComObject WScript.Shell
$shortcutsToUpdate = @(Get-ChildItem $desktop -Filter '*.lnk' -File | Where-Object {
    $existingShortcut = $shell.CreateShortcut($_.FullName)
    [IO.Path]::GetFileName($existingShortcut.TargetPath) -ieq 'SaudiPatientDesk.exe'
})
if (-not ($shortcutsToUpdate.FullName -contains $shortcutPath)) {
    $shortcutsToUpdate += Get-Item $shortcutPath -ErrorAction SilentlyContinue
}
if ($shortcutsToUpdate.Count -eq 0) {
    $shortcutsToUpdate = @([pscustomobject]@{ FullName = $shortcutPath })
}
foreach ($shortcutFile in $shortcutsToUpdate) {
    if ($null -eq $shortcutFile) { continue }
    $shortcut = $shell.CreateShortcut($shortcutFile.FullName)
    $shortcut.TargetPath = "$env:WINDIR\explorer.exe"
    $shortcut.Arguments = "shell:AppsFolder\$appUserModelId"
    $shortcut.WorkingDirectory = $env:LOCALAPPDATA
    $shortcut.IconLocation = "$(Join-Path $package.InstallLocation 'SaudiPatientDesk.exe'),0"
    $shortcut.Description = "نظام سجلات المرضى السعودي"
    $shortcut.Save()
}

Write-Host "تم تثبيت الإصدار $($package.Version) وإنشاء اختصار ثابت على سطح المكتب." -ForegroundColor Green
Write-Host "لن تُحذف قاعدة المرضى الموجودة في %LocalAppData%\SaudiPatientDesk\Clinic2026."
