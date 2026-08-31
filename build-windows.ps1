$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root "PatientRecordsSaudi\PatientRecordsSaudi.sln"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "Visual Studio 2022 Build Tools غير مثبتة." }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
if (-not $msbuild) { throw "لم يتم العثور على MSBuild." }
& $msbuild $solution /t:Restore /p:Configuration=Release /m
if ($LASTEXITCODE -ne 0) { throw "فشل استرجاع الحزم." }
& $msbuild $solution /t:Build /p:Configuration=Release /m
if ($LASTEXITCODE -ne 0) { throw "فشل البناء." }
& (Join-Path $root "PatientRecordsSaudi.Tests\bin\Release\PatientRecordsSaudi.Tests.exe")
if ($LASTEXITCODE -ne 0) { throw "فشلت اختبارات السلامة." }
$out = Join-Path $root "release\سجلات_المرضى"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item (Join-Path $root "PatientRecordsSaudi\bin\Release\*") $out -Recurse -Force
Write-Host "تم البناء بنجاح: $out\سجلات_المرضى.exe"
