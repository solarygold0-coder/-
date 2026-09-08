[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Version = "4.2.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tests = Join-Path $root "PatientRecordsSaudi.Modern.Tests\PatientRecordsSaudi.Modern.Tests.csproj"
$app = Join-Path $root "PatientRecordsSaudi\PatientRecordsSaudi.csproj"
$package = Join-Path $root "InstallerV4\Package\Package.wixproj"
$bundle = Join-Path $root "InstallerV4\Bundle\Bundle.wixproj"
$publish = Join-Path $root "artifacts\installer-publish\$Runtime"
$release = Join-Path $root "release-installer"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publish, $release | Out-Null

dotnet restore $tests
if ($LASTEXITCODE -ne 0) { throw "Test package restore failed." }
dotnet run --project $tests -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Safety tests failed." }

dotnet restore $app -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "Application restore failed." }
dotnet publish $app -c Release -r $Runtime --self-contained true --no-restore -o $publish `
    -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Application publish failed." }

$appExe = Get-Item (Join-Path $publish "SaudiPatientRecords.exe")
$unexpected = @(Get-ChildItem $publish -File | Where-Object { $_.Name -ne "SaudiPatientRecords.exe" })
if ($unexpected.Count -ne 0) { throw "Publish did not produce exactly one executable." }

dotnet build $package -c Release -p:PublishDir="$publish" -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "MSI package build failed." }
$msi = Get-ChildItem (Join-Path $root "InstallerV4\Package\bin\Release") -Filter *.msi -Recurse | Select-Object -First 1
if (-not $msi) { throw "MSI output was not found." }

dotnet build $bundle -c Release -p:MsiPath="$($msi.FullName)" -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Setup bootstrapper build failed." }
$setup = Get-ChildItem (Join-Path $root "InstallerV4\Bundle\bin\Release") -Filter *.exe -Recurse | Where-Object { $_.Name -like "*Setup*" } | Select-Object -First 1
if (-not $setup) { throw "Setup.exe output was not found." }

$setupOut = Join-Path $release "Saudi-Patient-Records-Setup.exe"
$standaloneOut = Join-Path $release "Saudi-Patient-Records-Standalone.exe"
Copy-Item $setup.FullName $setupOut -Force
Copy-Item $appExe.FullName $standaloneOut -Force

foreach ($file in @($setupOut, $standaloneOut)) {
    $header = [System.IO.File]::ReadAllBytes($file)[0..1]
    if ($header[0] -ne 0x4D -or $header[1] -ne 0x5A) { throw "$file is not a valid Windows PE executable." }
}

$install = Start-Process $setupOut -ArgumentList "/quiet /norestart" -Wait -PassThru
if ($install.ExitCode -ne 0 -and $install.ExitCode -ne 3010) { throw "Automated installer verification failed with exit code $($install.ExitCode)." }
$installedExe = Join-Path $env:LOCALAPPDATA "Programs\Saudi Patient Records\SaudiPatientRecords.exe"
if (-not (Test-Path $installedExe)) { throw "Installed executable was not found in the expected per-user location." }
$selfTest = Start-Process $installedExe -ArgumentList "--self-test" -Wait -PassThru
if ($selfTest.ExitCode -ne 0) { throw "Installed application self-test failed with exit code $($selfTest.ExitCode)." }
$uninstall = Start-Process $setupOut -ArgumentList "/uninstall /quiet /norestart" -Wait -PassThru
if ($uninstall.ExitCode -ne 0 -and $uninstall.ExitCode -ne 3010) { throw "Automated uninstall verification failed with exit code $($uninstall.ExitCode)." }

$setupHash = (Get-FileHash $setupOut -Algorithm SHA256).Hash.ToLowerInvariant()
$standaloneHash = (Get-FileHash $standaloneOut -Algorithm SHA256).Hash.ToLowerInvariant()
@(
    "Product=Saudi Patient Records"
    "Version=$Version"
    "Installer=Interactive WiX Toolset 6 / Windows Installer Setup.exe"
    "InstallScope=Per-user"
    "DefaultUsername=admin"
    "DefaultPassword=admin (change immediately)"
    "StartupLogin=Disabled by default; configurable by administrator"
    "Database=Encrypted SQLite (SQLCipher)"
    "DigitalSignature=None (unsigned public-source build)"
    "SetupSHA256=$setupHash"
    "StandaloneSHA256=$standaloneHash"
) | Out-File (Join-Path $release "BUILD-INFO.txt") -Encoding utf8

Write-Host "Setup created: $setupOut"
Write-Host "Standalone created: $standaloneOut"
