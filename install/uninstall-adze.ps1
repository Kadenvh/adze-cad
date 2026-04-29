<#
.SYNOPSIS
    Uninstalls the Adze SOLIDWORKS add-in.

.DESCRIPTION
    Stops SOLIDWORKS processes, removes COM and add-in registry entries under
    HKCU, and deletes installed binaries from %LOCALAPPDATA%\Adze\bin.

    By default user data (logs, traces, state, recipes) is preserved.
    Pass -RemoveUserData to delete the entire %LOCALAPPDATA%\Adze directory.

    The script is idempotent: running it when Adze is already uninstalled
    produces no errors.

.PARAMETER RemoveUserData
    When set, removes the entire %LOCALAPPDATA%\Adze directory including
    logs, traces, state, recipes, and support bundles.

.EXAMPLE
    pwsh -NoProfile -File install\uninstall-adze.ps1

.EXAMPLE
    pwsh -NoProfile -File install\uninstall-adze.ps1 -RemoveUserData
#>

param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# 1. Stop SOLIDWORKS processes (process cleanup checklist)
# ---------------------------------------------------------------------------
# When 3DX wants to apply a SOLIDWORKS update it refuses to launch if any
# files in the install dir are locked. SLDWORKS.exe holds the obvious lock,
# but child processes (sldworks_fs.exe and the SW-bundled msedgewebview2.exe
# fleet for 3DX UI panels) frequently survive File -> Exit and continue
# locking files. This step kills the full child tree so the 3DX updater
# (DSYProcessMgt) can replace win_b64\SWXWebView2\msedgewebview2.exe and the
# rest of the install tree without "Terminate these applications" errors.
Write-Host ""
Write-Host "=== Step 1: Process cleanup ===" -ForegroundColor Cyan

# Detect SW install dir so the WebView2 kill is scoped to SW-bundled
# instances only -- Edge / Teams / Outlook / VS Code WebView2 must not be
# touched. Without a detected dir we conservatively skip WebView2 cleanup.
$swInstallDir = $null
$swSetup = Get-ItemProperty 'HKLM:\SOFTWARE\SolidWorks\Setup' -Name 'SolidWorks Folder' -ErrorAction SilentlyContinue
if ($swSetup -and $swSetup.'SolidWorks Folder') { $swInstallDir = $swSetup.'SolidWorks Folder' }
if (-not $swInstallDir) {
    foreach ($candidate in @(
        "${env:ProgramFiles}\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x"
        "${env:ProgramFiles}\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2025x"
        "${env:ProgramFiles}\Dassault Systemes\SolidWorks Corp\SOLIDWORKS"
    )) {
        if (Test-Path $candidate) { $swInstallDir = $candidate; break }
    }
}
if ($swInstallDir) {
    Write-Host "  SW install dir: $swInstallDir"
} else {
    Write-Host "  SW install dir: (not detected -- WebView2 cleanup will skip)" -ForegroundColor Yellow
}

# 1a. Main SW + launcher processes
$mainProcs = @("SLDWORKS", "sldworks", "SWXDesktopLauncher", "CATSTART")
$mainStopped = 0
foreach ($name in $mainProcs) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        try {
            Stop-Process -InputObject $p -Force -ErrorAction Stop
            Write-Host ("  Stopped: {0} (PID {1})" -f $p.Name, $p.Id)
            $mainStopped++
        } catch {
            Write-Host ("  Could not stop {0} (PID {1}): {2}" -f $p.Name, $p.Id, $_.Exception.Message) -ForegroundColor Yellow
        }
    }
}
if ($mainStopped -eq 0) { Write-Host "  No main SW processes running" }

# 1b. SLDWORKS file-server sub-process (lingers after main exit)
$fsStopped = 0
foreach ($p in (Get-Process -Name "sldworks_fs" -ErrorAction SilentlyContinue)) {
    try {
        Stop-Process -InputObject $p -Force -ErrorAction Stop
        Write-Host ("  Stopped: sldworks_fs.exe (PID {0}) -- file-server sub-process" -f $p.Id)
        $fsStopped++
    } catch {
        Write-Host ("  Could not stop sldworks_fs.exe (PID {0}): {1}" -f $p.Id, $_.Exception.Message) -ForegroundColor Yellow
    }
}
if ($fsStopped -eq 0) { Write-Host "  No sldworks_fs.exe processes" }

# 1c. SW-bundled WebView2 zombies -- the actual blocker for 3DX update.
#     Scope strictly by .Path so user's Edge / Teams / Outlook / VS Code
#     WebView2 stay untouched.
if ($swInstallDir) {
    $wv2Stopped = 0
    foreach ($p in (Get-Process -Name "msedgewebview2" -ErrorAction SilentlyContinue)) {
        $isSwBundled = $false
        try {
            if ($p.Path) {
                $isSwBundled = $p.Path.StartsWith($swInstallDir, [System.StringComparison]::OrdinalIgnoreCase)
            }
        } catch {
            # Access denied on Path is normal for processes from other users -- skip silently.
        }
        if ($isSwBundled) {
            try {
                Stop-Process -InputObject $p -Force -ErrorAction Stop
                $wv2Stopped++
            } catch {
                Write-Host ("  Could not stop msedgewebview2.exe (PID {0}): {1}" -f $p.Id, $_.Exception.Message) -ForegroundColor Yellow
            }
        }
    }
    if ($wv2Stopped -gt 0) {
        Write-Host ("  Stopped: {0}x msedgewebview2.exe under SW install dir (3DX update unblocker)" -f $wv2Stopped)
    } else {
        Write-Host "  No SW-bundled msedgewebview2.exe zombies"
    }
}

# 1d. Sanity: warn if 3DX updater is mid-flight -- ejecting now is benign for
#     us but the user may have launched the wrong action.
$updater = Get-Process -Name "swxdesktopupdate" -ErrorAction SilentlyContinue
if ($updater) {
    Write-Host ("  NOTE: swxdesktopupdate.exe running (PID {0}). If a 3DX update is in progress, let it finish before reinstalling Adze." -f $updater[0].Id) -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# 2. Remove COM registration (HKCU)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 2: Removing COM registration ===" -ForegroundColor Cyan

$classesRoot = "HKCU:\Software\Classes"
$comPaths = @(
    "$classesRoot\Adze.Host.AddIn",
    "$classesRoot\Adze.Host.TaskPaneControl",
    "$classesRoot\Adze.Host.NativeTaskPaneControl",
    "$classesRoot\CLSID\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}",
    "$classesRoot\CLSID\{F4068202-600A-4D6F-973B-DA2048A949CF}",
    "$classesRoot\CLSID\{C8B41F45-D2A6-4B5E-9F7C-3E0A1D8B2F61}"
)

foreach ($path in $comPaths) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "  Removed: $path"
    } else {
        Write-Host "  Already absent: $path"
    }
}

# ---------------------------------------------------------------------------
# 3. Remove SOLIDWORKS add-in registration (HKCU)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 3: Removing SOLIDWORKS add-in registration ===" -ForegroundColor Cyan

$solidWorksRoot = "HKCU:\Software\SolidWorks"
$addinPaths = @(
    "$solidWorksRoot\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}",
    "$solidWorksRoot\AddInsStartup\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}"
)

foreach ($path in $addinPaths) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "  Removed: $path"
    } else {
        Write-Host "  Already absent: $path"
    }
}

# ---------------------------------------------------------------------------
# 4. Remove installed binaries
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 4: Removing installed binaries ===" -ForegroundColor Cyan

$adzeLocalRoot = Join-Path $env:LOCALAPPDATA "Adze"
$binDir = Join-Path $adzeLocalRoot "bin"

if (Test-Path $binDir) {
    Remove-Item $binDir -Recurse -Force
    Write-Host "  Removed: $binDir"
} else {
    Write-Host "  Already absent: $binDir"
}

# ---------------------------------------------------------------------------
# 5. Optionally remove user data
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 5: User data ===" -ForegroundColor Cyan

if ($RemoveUserData) {
    if (Test-Path $adzeLocalRoot) {
        # Defensive cleanup. A single Remove-Item -Recurse can leave empty
        # subdirs behind if a child file is momentarily locked when the
        # walker visits it (state\sw-build.txt held by SwBuildStateService
        # is a known offender even after Step 1 kills SOLIDWORKS -- Windows
        # release-handle is async). First pass; then sweep any leftover
        # children deepest-first; then remove the root.
        Remove-Item $adzeLocalRoot -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $adzeLocalRoot) {
            Start-Sleep -Milliseconds 200
            Get-ChildItem $adzeLocalRoot -Recurse -Force -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending |
                Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
            Remove-Item $adzeLocalRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path $adzeLocalRoot) {
            Write-Host "  Partial: $adzeLocalRoot still has leftover content (likely a file lock). Close SOLIDWORKS and retry." -ForegroundColor Yellow
        } else {
            Write-Host "  Removed: $adzeLocalRoot (all user data deleted)"
        }
    } else {
        Write-Host "  Already absent: $adzeLocalRoot"
    }
} else {
    if (Test-Path $adzeLocalRoot) {
        Write-Host "  Preserved: $adzeLocalRoot (pass -RemoveUserData to delete)"
    } else {
        Write-Host "  No user data directory found."
    }
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Adze uninstall complete ===" -ForegroundColor Green
Write-Host ""
