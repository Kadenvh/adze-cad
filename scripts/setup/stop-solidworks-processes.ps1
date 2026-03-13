$ErrorActionPreference = "Stop"

$patterns = @(
    "^sldworks$",
    "^SWXDesktopLauncher$",
    "^CATSTART$"
)

Get-Process | Where-Object {
    $processName = $_.ProcessName
    $patterns | Where-Object { $processName -match $_ }
} | Stop-Process -Force -ErrorAction SilentlyContinue
