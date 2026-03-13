$ErrorActionPreference = "Continue"

$logDir = "C:\SW_plugin\documentation\plans\diagnostics\logs"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logPath = Join-Path $logDir "cleanup-solidworks-residue-$timestamp.log"

New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Start-Transcript -Path $logPath -Force | Out-Null

Write-Output "=== SOLIDWORKS / 3DEXPERIENCE CLEANUP START ==="

$uninstallRoots = @(
  "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
  "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
  "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
)

foreach ($root in $uninstallRoots) {
  if (-not (Test-Path $root)) {
    continue
  }

  Get-ChildItem $root | ForEach-Object {
    $item = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
    if ($item.DisplayName -like "SOLIDWORKS HotFix HF-*") {
      Write-Output ("Removing uninstall key: " + $_.PSPath + " [" + $item.DisplayName + "]")
      Remove-Item $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
}

$targets = @(
  "C:\ProgramData\DassaultSystemes",
  "C:\ProgramData\SOLIDWORKS",
  "C:\ProgramData\SOLIDWORKS HotFix",
  (Join-Path $env:LOCALAPPDATA "SolidWorks"),
  (Join-Path $env:LOCALAPPDATA "DassaultSystemes"),
  (Join-Path $env:APPDATA "SolidWorks"),
  (Join-Path $env:APPDATA "DassaultSystemes")
)

foreach ($target in $targets) {
  if (-not (Test-Path $target)) {
    Write-Output ("Already missing " + $target)
    continue
  }

  Write-Output ("--- Reset ACL and remove " + $target + " ---")

  & takeown.exe /F $target /A /R /D Y
  & icacls.exe $target /grant "*S-1-5-32-544:(OI)(CI)F" /T /C

  try {
    Remove-Item $target -Recurse -Force -ErrorAction Stop
    Write-Output ("Removed " + $target)
  } catch {
    Write-Output ("Remove-Item failed for " + $target + ": " + $_.Exception.Message)
  }

  if (Test-Path $target) {
    Write-Output ("STILL EXISTS " + $target)
  } else {
    Write-Output ("REMOVED " + $target)
  }
}

Write-Output "=== SOLIDWORKS / 3DEXPERIENCE CLEANUP END ==="
Stop-Transcript | Out-Null
Write-Output ("Log written to " + $logPath)
