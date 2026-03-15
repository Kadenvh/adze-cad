param(
    [string]$SamplePath = "",
    [int]$PostLaunchDelaySeconds = 8,
    [int]$PostOpenDelaySeconds = 10,
    [int]$BlockerTimeoutSeconds = 120,
    [switch]$SkipBlockerWait
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$hostLog = Join-Path $env:LOCALAPPDATA "Adze\logs\host.log"
$openScript = Join-Path $repoRoot 'scripts\setup\open-sample-document.ps1'
$launchScript = Join-Path $repoRoot 'scripts\setup\launch-and-check-host.ps1'
$sldworksFs = "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\sldworks_fs.exe"
$blockerTitlePatterns = @("Login", "Update", "Platform")

# --- Helper: parse launch-and-check-host output and write preflight JSON ---
function Invoke-LaunchAndCheck {
    $output = & powershell.exe -NoProfile -File $launchScript 2>&1 | Out-String
    Write-Output $output

    $blocked = $false
    $reason = ""
    $logFound = $false

    foreach ($line in ($output -split "`n")) {
        $trimmed = $line.Trim()
        if ($trimmed -eq "LaunchBlocked=True") {
            $blocked = $true
        }
        if ($trimmed -match "^LaunchBlockReason=(.+)$") {
            $reason = $Matches[1]
        }
        if ($trimmed -eq "LogFound=True") {
            $logFound = $true
        }
    }

    # The preflight JSON report is already written by launch-and-check-host.ps1

    return @{ Blocked = $blocked; Reason = $reason; LogFound = $logFound }
}

# --- Helper: check if a launcher blocker window is still present ---
function Test-LauncherBlockerPresent {
    $launcher = Get-Process -Name "SWXDesktopLauncher" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $launcher) { return $false }

    $title = $launcher.MainWindowTitle
    if ([string]::IsNullOrWhiteSpace($title)) { return $false }

    foreach ($pattern in $blockerTitlePatterns) {
        if ($title -match $pattern) { return $true }
    }
    return $false
}

# === Main flow ===

& powershell.exe -NoProfile -File (Join-Path $repoRoot 'scripts\setup\stop-solidworks-processes.ps1')

if ((Test-Path $sldworksFs) -and -not (Get-Process -Name "sldworks_fs" -ErrorAction SilentlyContinue)) {
    Start-Process $sldworksFs
    Start-Sleep -Seconds 4
}

& powershell.exe -NoProfile -File (Join-Path $repoRoot 'scripts\setup\build-host.ps1')
& powershell.exe -NoProfile -File (Join-Path $repoRoot 'scripts\setup\unregister-host-addin.ps1')
& powershell.exe -NoProfile -File (Join-Path $repoRoot 'scripts\setup\register-host-addin.ps1')

$launchResult = Invoke-LaunchAndCheck

if ($launchResult.Blocked -and -not $SkipBlockerWait) {
    $displayReason = if ($launchResult.Reason) { $launchResult.Reason } else { "unknown launcher blocker" }
    Write-Output "Launcher blocker detected: $displayReason. Waiting for blocker to clear..."

    $deadline = (Get-Date).AddSeconds($BlockerTimeoutSeconds)
    $cleared = $false

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 10

        if (-not (Test-LauncherBlockerPresent)) {
            $cleared = $true
            break
        }
    }

    if ($cleared) {
        Write-Output "Blocker cleared. Retrying launch..."
        $launchResult = Invoke-LaunchAndCheck
    }
    else {
        Write-Output "Launcher blocker did not clear within ${BlockerTimeoutSeconds}s. Manual intervention required."
        exit 2
    }
}

Start-Sleep -Seconds $PostLaunchDelaySeconds

if ($SamplePath -and (Test-Path $SamplePath)) {
    try {
        & powershell.exe -NoProfile -File $openScript -Path $SamplePath | Out-Null
    }
    catch {
        Start-Process $SamplePath
    }

    Start-Sleep -Seconds $PostOpenDelaySeconds
}

if (Test-Path $hostLog) {
    Get-Content $hostLog -Tail 200
}
