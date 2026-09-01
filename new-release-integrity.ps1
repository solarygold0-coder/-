[CmdletBinding()]
param(
    [string]$ReleaseDirectory = ".\release",
    [string]$Publisher = "Mohammed Mousa Asiri"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $ReleaseDirectory).Path.TrimEnd("\")
$checksumPath = Join-Path $root "SHA256SUMS.txt"
$signatureReportPath = Join-Path $root "SIGNATURE_REPORT.txt"

$signableFiles = @(Get-ChildItem $root -Recurse -File | Where-Object {
    $_.Extension -in @(".exe", ".dll")
} | Sort-Object FullName)

if ($signableFiles.Count -eq 0) {
    throw "No signed Windows files were found in '$root'."
}

$report = @(
    "Publisher: $Publisher"
    "Release: 1.3.1"
    "GeneratedUtc: $([DateTime]::UtcNow.ToString('o'))"
    ""
    "File | Status | Publisher | Certificate SHA-1 thumbprint"
)

foreach ($file in $signableFiles) {
    $relative = $file.FullName.Substring($root.Length + 1).Replace("\", "/")
    $signature = Get-AuthenticodeSignature $file.FullName
    if (-not $signature.SignerCertificate) {
        throw "'$relative' has no Authenticode certificate."
    }

    $actualPublisher = $signature.SignerCertificate.GetNameInfo(
        [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false
    )
    if ($signature.Status -ne "Valid" -or $actualPublisher -ne $Publisher) {
        throw "'$relative' is not validly signed by '$Publisher'."
    }

    $report += "$relative | $($signature.Status) | $actualPublisher | $($signature.SignerCertificate.Thumbprint)"
}

$report | Out-File $signatureReportPath -Encoding utf8

$hashFiles = @(Get-ChildItem $root -Recurse -File | Where-Object {
    $_.FullName -ne $checksumPath
} | Sort-Object FullName)

$hashes = foreach ($file in $hashFiles) {
    $relative = $file.FullName.Substring($root.Length + 1).Replace("\", "/")
    $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}

$hashes | Out-File $checksumPath -Encoding ascii
Write-Host "Created signature report and SHA-256 manifest in '$root'."
