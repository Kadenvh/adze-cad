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
# 1. Stop SOLIDWORKS processes
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Step 1: Stopping SOLIDWORKS processes ===" -ForegroundColor Cyan

$processNames = @("sldworks", "SWXDesktopLauncher", "CATSTART")
$anyFound = $false

foreach ($name in $processNames) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($procs) {
        $anyFound = $true
        $procs | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "  Stopped: $name"
    }
}

if (-not $anyFound) {
    Write-Host "  No SOLIDWORKS processes were running."
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
    "$classesRoot\CLSID\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}",
    "$classesRoot\CLSID\{F4068202-600A-4D6F-973B-DA2048A949CF}"
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
        Remove-Item $adzeLocalRoot -Recurse -Force
        Write-Host "  Removed: $adzeLocalRoot (all user data deleted)"
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
