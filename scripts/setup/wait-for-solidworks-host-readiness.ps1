param(
    [int]$PollSeconds = 15,
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = "Stop"

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$checkScript = Join-Path $PSScriptRoot "check-solidworks-host-readiness.ps1"

if (-not (Test-Path $checkScript)) {
    throw "Missing readiness script: $checkScript"
}

do {
    $json = & powershell.exe -NoProfile -File $checkScript
    $status = $json | ConvertFrom-Json
    Write-Output ($status | ConvertTo-Json -Depth 4)

    if ($status.host_ready) {
        exit 0
    }

    Start-Sleep -Seconds $PollSeconds
} while ((Get-Date) -lt $deadline)

exit 1
