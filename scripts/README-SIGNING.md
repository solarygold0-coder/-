# Code Signing (Self-Signed) — SaudiPatientDesk

Personal / clinic use. Free. No Microsoft Store.

## Overview

| Script | Purpose | When |
|--------|---------|------|
| `01-create-certificate.ps1` | Create self-signed cert + export `.pfx` / `.cer` | Once on your dev PC |
| `02-build-and-sign.ps1` | `dotnet publish` + Authenticode sign the EXE | Every release |
| `03-install-certificate.ps1` | Install `.cer` into Trusted Root | Once on every target PC |

## One-time setup (development machine)

1. Open **PowerShell** (normal user is enough).
2. Go to the repo folder.
3. Run:

```powershell
cd scripts
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\01-create-certificate.ps1
```

4. Enter a strong password when asked.  
   Files appear in `certs\`:
   - `SaudiPatientDesk.pfx` ← **secret** (used only for signing)
   - `SaudiPatientDesk.cer` ← public (install on clinic PCs)
   - `certificate-info.txt`

5. Add to `.gitignore` (already recommended):

```
certs/
*.pfx
```

## Build + sign every release

```powershell
cd scripts
.\02-build-and-sign.ps1
# or with password non-interactively:
.\02-build-and-sign.ps1 -PfxPassword "YourPassword"
```

Output:
- `out\SaudiPatientDesk-win-x64\SaudiPatientDesk.exe` (signed)
- `out\SaudiPatientDesk-v7.1.0-win-x64-signed.zip`

Requires **Windows SDK** (signtool). Install via Visual Studio Installer → Individual components → “Windows SDK” / “Signing Tools for Windows SDK”.

## Install certificate on each clinic PC

1. Copy `SaudiPatientDesk.cer` to the PC.
2. Right-click **PowerShell → Run as administrator**.
3. Run:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\03-install-certificate.ps1 -CerPath "C:\path\to\SaudiPatientDesk.cer"
```

Or manually:
- Double-click the `.cer` → Install Certificate → **Local Machine**
- Place all certificates in: **Trusted Root Certification Authorities** → Finish

## What this solves / what it does not

| Problem | Solved? |
|---------|---------|
| “Unknown publisher” in UAC / properties | Yes (after cert installed) |
| SmartScreen first-run “Windows protected your PC” | Reduced; one “Run anyway” usually enough per file hash |
| Works on machines where you never install the cert | No — they still see the normal SmartScreen flow |
| Global trust without installing anything | Impossible for free closed-source apps |

## Security notes

- Never commit `*.pfx` or share the PFX password.
- The private key stays on your build machine only.
- Timestamp is applied (`http://timestamp.digicert.com`) so the signature remains valid after the certificate expires.
- Certificate lifetime defaults to 5 years (change `-YearsValid` in script 01 if needed).

## Troubleshooting

**signtool not found**  
Install Windows 10/11 SDK (Signing tools component).

**“The signer's certificate is not valid for signing”**  
Recreate the certificate with script 01 (must include Code Signing EKU).

**Still “Unknown publisher” on target PC**  
Certificate was not installed into **Local Machine → Trusted Root**. Re-run script 03 as Administrator.

**Defender quarantines the file**  
Rare for a clean local app. Add an exclusion for the install folder, or submit a false-positive report to Microsoft.
