$ErrorActionPreference = "Stop"

$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$logDir = Join-Path $PSScriptRoot "logs"
$logPath = Join-Path $logDir "powershell-launch-smoke.log"

if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir | Out-Null
}

$hostName = $Host.Name
$pwshVersion = $PSVersionTable.PSVersion.ToString()
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)
$executionPolicy = Get-ExecutionPolicy

$lines = @(
    "timestamp=$timestamp"
    "host=$hostName"
    "version=$pwshVersion"
    "is_admin=$isAdmin"
    "execution_policy=$executionPolicy"
    "script_path=$PSCommandPath"
)

$lines | Add-Content -Path $logPath
$lines
