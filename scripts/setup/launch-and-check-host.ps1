$ErrorActionPreference = "Stop"

$guid = "{A2E09EE4-BB43-4A0C-945F-14711F792EFA}"
$logPath = Join-Path $env:LOCALAPPDATA "Adze\logs\host.log"
$preflightPath = Join-Path $env:LOCALAPPDATA "Adze\logs\launcher-preflight.json"
$swShortcut = "C:\Users\Public\Desktop\SOLIDWORKS Design.lnk"

if (Test-Path $logPath) {
    Remove-Item $logPath -Force
}

# Ensure the logs directory exists for the preflight report
$logsDir = Join-Path $env:LOCALAPPDATA "Adze\logs"
if (-not (Test-Path $logsDir)) {
    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
}

Write-Output "=== ADDIN REGISTRATION ==="
Get-ItemProperty "HKCU:\Software\SolidWorks\AddIns\$guid" | Select-Object PSPath, Title, Description
Get-ItemProperty "HKCU:\Software\SolidWorks\AddInsStartup\$guid" | Select-Object PSPath

Write-Output "=== COM REGISTRATION ==="
Get-ChildItem "Registry::HKEY_CLASSES_ROOT\CLSID\$guid" | Select-Object PSPath

Write-Output "=== LAUNCH SOLIDWORKS ==="
$launchTime = Get-Date
Start-Process $swShortcut

$deadline = (Get-Date).AddSeconds(90)
$found = $false
while ((Get-Date) -lt $deadline) {
    if (Test-Path $logPath) {
        $found = $true
        break
    }

    Start-Sleep -Seconds 3
}

# --- Blocker detection ---

# Known blocker patterns and their recovery instructions
$blockerPatterns = @{
    "Login \| 3DEXPERIENCE ID" = @{
        Reason  = "3DEXPERIENCE desktop login required before SOLIDWORKS can start."
        Recovery = "Dismiss the 3DEXPERIENCE login window, then rerun this script."
    }
    "3DEXPERIENCE Update" = @{
        Reason  = "3DEXPERIENCE update window is blocking SOLIDWORKS from starting."
        Recovery = "Complete or dismiss the 3DEXPERIENCE update, then rerun this script."
    }
    "3DEXPERIENCE Platform" = @{
        Reason  = "3DEXPERIENCE platform window is blocking SOLIDWORKS from starting."
        Recovery = "Close the 3DEXPERIENCE Platform window, then rerun this script."
    }
}

# Gather window titles from both launcher-related processes
$launcherProcessNames = @("SWXDesktopLauncher", "CATSTART")
$detectedTitles = @()
foreach ($procName in $launcherProcessNames) {
    $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
    foreach ($proc in $procs) {
        $title = $proc.MainWindowTitle
        if (-not [string]::IsNullOrWhiteSpace($title)) {
            $detectedTitles += @{ ProcessName = $procName; Title = $title; StartTime = $proc.StartTime }
        }
    }
}

# Determine blocker state
$launchBlocked = $false
$blockReason = ""
$recoverySteps = @()
$blockerWindowTitle = ""

if (-not $found) {
    foreach ($entry in $detectedTitles) {
        $title = $entry.Title
        $matchedKnown = $false
        foreach ($pattern in $blockerPatterns.Keys) {
            if ($title -match $pattern) {
                $launchBlocked = $true
                $blockReason = $blockerPatterns[$pattern].Reason
                $recoverySteps += $blockerPatterns[$pattern].Recovery
                $blockerWindowTitle = $title
                $matchedKnown = $true
                break
            }
        }
        if ($matchedKnown) { break }
    }

    # If no known pattern matched but a launcher/CATSTART window has been visible >30s
    if (-not $launchBlocked -and $detectedTitles.Count -gt 0) {
        $now = Get-Date
        foreach ($entry in $detectedTitles) {
            $procStart = $entry.StartTime
            if ($null -ne $procStart -and ($now - $procStart).TotalSeconds -gt 30) {
                $launchBlocked = $true
                $blockReason = "An unrecognized launcher window ($($entry.ProcessName)) has been visible for over 30 seconds without SOLIDWORKS loading."
                $blockerWindowTitle = $entry.Title
                $recoverySteps += "An unrecognized launcher window is blocking SOLIDWORKS. Close it manually and rerun."
                break
            }
        }
    }
}

# --- Text output (backward-compatible) ---

Write-Output ("LogFound=" + $found)
if ($found) {
    Write-Output "=== HOST LOG ==="
    Get-Content $logPath -Tail 80
}
else {
    if (-not [string]::IsNullOrWhiteSpace($blockerWindowTitle)) {
        Write-Output ("LauncherWindowTitle=" + $blockerWindowTitle)
    }
    if ($launchBlocked) {
        Write-Output "LaunchBlocked=True"
        Write-Output ("LaunchBlockReason=" + $blockReason)
        Write-Output ""
        Write-Output "=== RECOVERY ==="
        foreach ($step in $recoverySteps) {
            Write-Output ("  - " + $step)
        }
    }
}

Write-Output "=== SLDWORKS PROCESS ==="
Get-Process | Where-Object { $_.ProcessName -match "^(SLDWORKS|sldworks_fs|SWXDesktopLauncher|CATSTART)$" } | Select-Object ProcessName, Id, StartTime | Format-Table -AutoSize

# --- Process presence snapshot for JSON report ---

$procSLDWORKS = $null -ne (Get-Process -Name "SLDWORKS" -ErrorAction SilentlyContinue)
$procFs = $null -ne (Get-Process -Name "sldworks_fs" -ErrorAction SilentlyContinue)
$procLauncher = $null -ne (Get-Process -Name "SWXDesktopLauncher" -ErrorAction SilentlyContinue)
$procCatstart = $null -ne (Get-Process -Name "CATSTART" -ErrorAction SilentlyContinue)

# --- Structured JSON preflight report ---

$preflight = [ordered]@{
    timestamp_utc        = (Get-Date).ToUniversalTime().ToString("o")
    log_found            = $found
    launch_blocked       = $launchBlocked
    block_reason         = $blockReason
    launcher_window_title = $blockerWindowTitle
    processes            = [ordered]@{
        SLDWORKS           = $procSLDWORKS
        sldworks_fs        = $procFs
        SWXDesktopLauncher = $procLauncher
        CATSTART           = $procCatstart
    }
    recovery_steps       = $recoverySteps
}

$preflightJson = $preflight | ConvertTo-Json -Depth 3
$preflightJson | Out-File -FilePath $preflightPath -Encoding utf8 -Force

Write-Output ""
Write-Output "=== PREFLIGHT REPORT ==="
Write-Output ("Written to: " + $preflightPath)
