# Build Saudi Patient Desk 7.1.0 on Windows 10/11 x64
# Requires: .NET 10 SDK  https://dotnet.microsoft.com/download
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet restore SaudiPatientDesk.sln
dotnet build SaudiPatientDesk.sln -c Release --warnaserror
New-Item -ItemType Directory -Force -Path out | Out-Null
dotnet publish src/SaudiPatientDesk/SaudiPatientDesk.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o out/SaudiPatientDesk-win-x64

Write-Host ""
Write-Host "EXE:" (Resolve-Path 'out/SaudiPatientDesk-win-x64/SaudiPatientDesk.exe')
Write-Host "Optional signing:  cd scripts; .\02-build-and-sign.ps1"
