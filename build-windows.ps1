$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "PatientRecordsSaudi\PatientRecordsSaudi.sln"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "Visual Studio Build Tools are not installed." }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild was not found." }
& $msbuild $solution /t:Restore /p:Configuration=Release /m
if ($LASTEXITCODE -ne 0) { throw "Package restore failed." }
& $msbuild $solution /t:Build /p:Configuration=Release /m
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
& (Join-Path $root "PatientRecordsSaudi.Tests\bin\Release\PatientRecordsSaudi.Tests.exe")
if ($LASTEXITCODE -ne 0) { throw "Safety tests failed." }
$out = Join-Path $root "release\App"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item (Join-Path $root "PatientRecordsSaudi\bin\Release\*") $out -Recurse -Force
Write-Host "Build succeeded: $out"
