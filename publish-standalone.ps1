[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Version = "6.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tests = Join-Path $root "PatientRecordsSaudi.Modern.Tests\PatientRecordsSaudi.Modern.Tests.csproj"
$app = Join-Path $root "PatientRecordsSaudi.Wpf\PatientRecordsSaudi.Wpf.csproj"
$intermediate = Join-Path $root "artifacts\publish\$Runtime"
$release = Join-Path $root "release-standalone"
$releaseExe = Join-Path $release "Saudi-Patient-Records.exe"

if (Test-Path (Join-Path $root "PatientRecordsSaudi")) { throw "Legacy WinForms source must not exist in the v6 tree." }
if (Get-ChildItem $root -Recurse -File -Include *.csproj,*.cs | Select-String -Pattern "LiteDB|System\.Windows\.Forms" -Quiet) { throw "Legacy LiteDB or WinForms code must not exist in the v6 tree." }

if (Test-Path $intermediate) { Remove-Item $intermediate -Recurse -Force }
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory -Force -Path $intermediate, $release | Out-Null

dotnet restore $tests
if ($LASTEXITCODE -ne 0) { throw "Test package restore failed." }
dotnet run --project $tests -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Safety tests failed." }

dotnet restore $app -r $Runtime
if ($LASTEXITCODE -ne 0) { throw "Application package restore failed." }
dotnet publish $app -c Release -r $Runtime --self-contained true --no-restore -o $intermediate `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Standalone publish failed." }

$publishedFiles = @(Get-ChildItem $intermediate -File)
$publishedExe = Get-Item (Join-Path $intermediate "SaudiPatientRecords.exe") -ErrorAction SilentlyContinue
if (-not $publishedExe) { throw "The expected Windows executable was not produced." }
$unexpected = @($publishedFiles | Where-Object { $_.Name -ne "SaudiPatientRecords.exe" })
if ($unexpected.Count -ne 0) { throw "Publish is not a single-file result: $($unexpected.Name -join ', ')" }

$header = [System.IO.File]::ReadAllBytes($publishedExe.FullName)[0..1]
if ($header[0] -ne 0x4D -or $header[1] -ne 0x5A) { throw "Output is not a valid Windows PE executable." }

Copy-Item $publishedExe.FullName $releaseExe -Force
$process = Start-Process $releaseExe -ArgumentList "--self-test" -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Standalone executable self-test failed with exit code $($process.ExitCode)." }

$signature = Get-AuthenticodeSignature $releaseExe
if ($signature.SignerCertificate) { throw "This requested build must remain unsigned." }
$hash = (Get-FileHash $releaseExe -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $releaseExe).Length

@(
    "Product=Saudi Patient Records"
    "Version=$Version"
    "Architecture=x64"
    "Packaging=Unpackaged Win32 single-file EXE"
    "Runtime=.NET 10 self-contained"
    "Frontend=WPF + WPF-UI 4.3 Fluent"
    "DefaultAccount=None"
    "StartupLogin=Disabled; a named manager account is required before enabling"
    "DataProfile=Independent SaudiPatientRecordsV6; no automatic legacy copy"
    "Database=Encrypted SQLite (SQLCipher)"
    "LegacyUI=None"
    "DigitalSignature=None (unsigned public-source build)"
    "SizeBytes=$size"
    "SHA256=$hash"
) | Out-File (Join-Path $release "BUILD-INFO.txt") -Encoding utf8

Write-Host "Standalone EXE created: $releaseExe"
Write-Host "SHA256: $hash"
