<#
.SYNOPSIS
    Builds and signs the version-isolated MSIX package.

.DESCRIPTION
    The MSIX identity (Name + Publisher) remains fixed while PackageVersion
    increases. Windows therefore installs every update into its own protected
    package path and keeps one active Start-menu identity.

    The PFX must be the same long-lived signing certificate for every release.
    Never commit it to source control.
#>

param(
    [string]$PackageVersion = "",
    [string]$PfxPath = "",
    [string]$PfxPassword = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Project = Join-Path $RepoRoot "src\SaudiPatientDesk\SaudiPatientDesk.csproj"
$PublishDir = Join-Path $RepoRoot "out\SaudiPatientDesk-win-x64"
$LayoutDir = Join-Path $RepoRoot "out\msix-layout"
$PackageDir = Join-Path $RepoRoot "out\msix-release"
$Publisher = "CN=Saudi Patient Desk"

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    [xml]$projectXml = Get-Content $Project -Raw
    $applicationVersion = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($applicationVersion)) {
        throw "The application version was not found in the project file."
    }
    $versionParts = @($applicationVersion.Split('.'))
    while ($versionParts.Count -lt 4) { $versionParts += '0' }
    if ($versionParts.Count -gt 4) { throw "The application version has more than four parts." }
    $PackageVersion = $versionParts -join '.'
}
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "PackageVersion must contain four numeric parts, for example 7.1.0.0."
}
$PackagePath = Join-Path $PackageDir "SaudiPatientDesk-v$PackageVersion-x64.msix"
foreach ($part in $PackageVersion.Split('.')) {
    if ([int]$part -gt 65535) { throw "Each package version part must be 0-65535." }
}
if ([string]::IsNullOrWhiteSpace($PfxPath) -or -not (Test-Path $PfxPath)) {
    throw "A persistent PFX is required. Pass -PfxPath and -PfxPassword."
}

function Find-WindowsSdkTool([string]$Name) {
    $roots = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
        "${env:ProgramFiles}\Windows Kits\10\bin"
    )
    $tool = foreach ($root in $roots) {
        if (Test-Path $root) {
            Get-ChildItem $root -Filter $Name -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match '\\x64\\' }
        }
    }
    $selected = $tool | Sort-Object FullName -Descending | Select-Object -First 1
    if ($null -eq $selected) { throw "$Name was not found in the Windows SDK." }
    return $selected.FullName
}

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $PfxPath,
    $PfxPassword,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
if ($certificate.Subject -ne $Publisher) {
    throw "The MSIX Publisher must match the certificate subject exactly. Expected '$Publisher', got '$($certificate.Subject)'."
}
if (-not $certificate.HasPrivateKey) { throw "The PFX does not contain a private key." }
if ($certificate.NotAfter -le (Get-Date).AddDays(30)) { throw "The signing certificate expires too soon." }

if (-not $SkipBuild) {
    dotnet build (Join-Path $RepoRoot "SaudiPatientDesk.sln") --configuration Release --warnaserror
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    dotnet publish $Project `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}

$executable = Join-Path $PublishDir "SaudiPatientDesk.exe"
if (-not (Test-Path $executable)) { throw "Published executable was not found: $executable" }

$signTool = Find-WindowsSdkTool "signtool.exe"
$makeAppx = Find-WindowsSdkTool "makeappx.exe"
$timestampServers = @(
    "http://timestamp.acs.microsoft.com/",
    "http://timestamp.digicert.com"
)

function Sign-File([string]$FilePath) {
    foreach ($timestampServer in $timestampServers) {
        & $signTool sign /f $PfxPath /p $PfxPassword /fd SHA256 /td SHA256 /tr $timestampServer /v $FilePath
        if ($LASTEXITCODE -eq 0) { return }
        Write-Warning "Signing failed through timestamp server: $timestampServer"
    }
    throw "Signing failed through every timestamp server: $FilePath"
}

# Sign the desktop executable first so its signature is also protected by the package hash.
Sign-File $executable

if (Test-Path $LayoutDir) { Remove-Item $LayoutDir -Recurse -Force }
if (Test-Path $PackageDir) { Remove-Item $PackageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $LayoutDir, $PackageDir | Out-Null
Copy-Item (Join-Path $PublishDir "*") $LayoutDir -Recurse -Force
Copy-Item (Join-Path $RepoRoot "packaging\Assets") $LayoutDir -Recurse -Force

$manifestTemplate = Get-Content (Join-Path $RepoRoot "packaging\AppxManifest.xml") -Raw
$manifest = $manifestTemplate.Replace("__PACKAGE_VERSION__", $PackageVersion)
$manifestPath = Join-Path $LayoutDir "AppxManifest.xml"
$manifest | Set-Content $manifestPath -Encoding utf8

& $makeAppx pack /d $LayoutDir /p $PackagePath /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed." }
Sign-File $PackagePath

function Verify-SignedFile([string]$FilePath) {
    $verifyOutput = (& $signTool verify /pa /all /v $FilePath 2>&1 | Out-String)
    $verifyExitCode = $LASTEXITCODE
    Write-Host $verifyOutput

    $expectedSelfSignedTrustResult =
        $verifyExitCode -eq 1 -and
        $verifyOutput -match '(?s)root\s+certificate which is not trusted by the trust provider' -and
        $verifyOutput -match 'Hash of file \(sha256\):' -and
        $verifyOutput -match 'The signature is timestamped:'

    if ($verifyExitCode -ne 0 -and -not $expectedSelfSignedTrustResult) {
        throw "Signature verification failed unexpectedly: $FilePath"
    }

    $signature = Get-AuthenticodeSignature -FilePath $FilePath
    $expectedPowerShellTrustResult =
        $signature.Status.ToString() -in @('NotTrusted', 'UnknownError') -and
        $signature.StatusMessage -match '(?s)root certificate which is not\s+trusted by the trust provider'
    if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or
        $null -eq $signature.TimeStamperCertificate -or
        ($signature.Status.ToString() -ne 'Valid' -and -not $expectedPowerShellTrustResult)) {
        throw "The signer, timestamp, or Authenticode integrity is invalid: $FilePath"
    }
}

Verify-SignedFile $executable
Verify-SignedFile $PackagePath

$certificate.Dispose()
Write-Host "MSIX created and verified: $PackagePath" -ForegroundColor Green
