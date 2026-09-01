[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [string]$Publisher = "Mohammed Mousa Asiri"
)

$ErrorActionPreference = "Stop"

function Get-SignTool {
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kits)) {
        throw "Windows SDK SignTool was not found."
    }

    $tool = Get-ChildItem $kits -Filter signtool.exe -Recurse -File |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $tool) {
        throw "The x64 SignTool executable was not found."
    }

    return $tool.FullName
}

function Get-PublisherCertificate([string]$ExpectedPublisher) {
    $matches = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
        $_.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -eq $ExpectedPublisher
    })

    if ($matches.Count -ne 1) {
        throw "Expected exactly one code-signing certificate for '$ExpectedPublisher', found $($matches.Count)."
    }

    return $matches[0]
}

$resolved = Resolve-Path $Path
$target = Get-Item $resolved
$files = if ($target.PSIsContainer) {
    @(Get-ChildItem $target.FullName -Recurse -File | Where-Object {
        $_.Extension -in @(".exe", ".dll")
    } | Sort-Object FullName)
} else {
    @($target)
}

if ($files.Count -eq 0) {
    throw "No EXE or DLL files were found to sign in '$Path'."
}

$signTool = Get-SignTool
$certificate = Get-PublisherCertificate $Publisher

foreach ($file in $files) {
    Write-Host "Signing $($file.FullName)"
    & $signTool sign /fd SHA256 /tr http://ts.ssl.com /td SHA256 /sha1 $certificate.Thumbprint /d "نظام إدارة سجلات المراجعين" $file.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed for '$($file.FullName)'."
    }

    & $signTool verify /pa /all /v $file.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode verification failed for '$($file.FullName)'."
    }

    $signature = Get-AuthenticodeSignature $file.FullName
    $actualPublisher = $signature.SignerCertificate.GetNameInfo(
        [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false
    )
    if ($signature.Status -ne "Valid" -or $actualPublisher -ne $Publisher) {
        throw "The resulting signature for '$($file.FullName)' is not valid for '$Publisher'."
    }
}

Write-Host "Signed and verified $($files.Count) file(s) for publisher '$Publisher'."
