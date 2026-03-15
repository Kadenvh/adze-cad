# Install Assets

**Version:** 0.1.0
**Last Updated:** 2026-03-15
**Role:** Beta-friendly install, update, and uninstall workflow for Adze

## Quick Start (Beta Tester)

If you received a packaged release zip (`adze-v*.zip`):

1. Extract the zip to any folder
2. Open PowerShell and run:

```powershell
powershell.exe -NoProfile -File install-adze.ps1
```

3. Launch SOLIDWORKS — the `Adze for SOLIDWORKS` tab appears in the right sidebar

To update: extract the new zip over the old one and rerun `install-adze.ps1`.

To uninstall:

```powershell
powershell.exe -NoProfile -File uninstall-adze.ps1
```

Add `-RemoveUserData` to also remove traces, logs, and progression state.

## Quick Start (Developer)

To package a release zip from source:

```powershell
pwsh -NoProfile -File install\package-release.ps1
```

This builds the solution in Release, stages the DLLs and install scripts into `install/dist/`, and creates a versioned zip (`adze-v{version}.zip`).

## Contents

| File | Purpose |
|------|---------|
| `install-adze.ps1` | Standalone installer — pre-flight checks, DLL copy, COM + add-in registration |
| `uninstall-adze.ps1` | Standalone uninstaller — stops SOLIDWORKS, removes registration + binaries |
| `package-release.ps1` | Developer-side — builds Release, stages artifacts, creates distribution zip |
| `README.md` | This file |
| `dist/` | Staging directory created by `package-release.ps1` (not checked in) |

## What the Installer Does

1. **Pre-flight checks:** .NET Framework 4.8+, SOLIDWORKS installed, required DLLs present
2. **Copies DLLs** to `%LOCALAPPDATA%\Adze\bin\` (stable location outside the repo)
3. **Registers COM classes** under HKCU (no admin required)
4. **Registers SOLIDWORKS add-in** under HKCU with auto-start enabled

## What the Uninstaller Does

1. **Stops SOLIDWORKS processes** (sldworks, SWXDesktopLauncher, CATSTART)
2. **Removes COM registration** from HKCU
3. **Removes SOLIDWORKS add-in registration** from HKCU
4. **Removes installed binaries** from `%LOCALAPPDATA%\Adze\bin\`
5. **Preserves user data** by default (logs, traces, state, recipes under `%LOCALAPPDATA%\Adze\`)

## System Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8 or later
- SOLIDWORKS 2025 or 2026 desktop edition
- PowerShell 5.1 or later (included with Windows)

## Conventions

- Install scripts are standalone — they work from the extracted zip without the repo
- Development-time registration still lives in `scripts/setup/` for the build-register-launch dev loop
- The `dist/` directory and zip files are build outputs — do not check them in
