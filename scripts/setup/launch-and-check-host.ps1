$ErrorActionPreference = "Stop"

$guid = "{A2E09EE4-BB43-4A0C-945F-14711F792EFA}"
$logPath = Join-Path $env:LOCALAPPDATA "Adze\logs\host.log"
$swShortcut = "C:\Users\Public\Desktop\SOLIDWORKS Design.lnk"

if (Test-Path $logPath) {
    Remove-Item $logPath -Force
}

Write-Output "=== ADDIN REGISTRATION ==="
Get-ItemProperty "HKCU:\Software\SolidWorks\AddIns\$guid" | Select-Object PSPath, Title, Description
Get-ItemProperty "HKCU:\Software\SolidWorks\AddInsStartup\$guid" | Select-Object PSPath

Write-Output "=== COM REGISTRATION ==="
Get-ChildItem "Registry::HKEY_CLASSES_ROOT\CLSID\$guid" | Select-Object PSPath

Write-Output "=== LAUNCH SOLIDWORKS ==="
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

Write-Output ("LogFound=" + $found)
if ($found) {
    Write-Output "=== HOST LOG ==="
    Get-Content $logPath -Tail 80
}
else {
    $launcherWindow = Get-Process -Name "SWXDesktopLauncher" -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty MainWindowTitle
    if (-not [string]::IsNullOrWhiteSpace($launcherWindow)) {
        Write-Output ("LauncherWindowTitle=" + $launcherWindow)
        if ($launcherWindow -match "Login \| 3DEXPERIENCE ID") {
            Write-Output "LaunchBlocked=True"
            Write-Output "LaunchBlockReason=3DEXPERIENCE desktop login required before SOLIDWORKS can start."
        }
    }
}

Write-Output "=== SLDWORKS PROCESS ==="
Get-Process | Where-Object { $_.ProcessName -match "^(SLDWORKS|sldworks_fs|SWXDesktopLauncher|CATSTART)$" } | Select-Object ProcessName, Id, StartTime | Format-Table -AutoSize
