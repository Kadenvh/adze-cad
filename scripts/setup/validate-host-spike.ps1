param(
    [string]$SamplePath = "C:\SOLIDWORKS\samples\Part1.SLDPRT"
)

$ErrorActionPreference = "Stop"

# Load .env if present
. (Join-Path $PSScriptRoot "load-env.ps1")

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logPath = Join-Path $env:LOCALAPPDATA "Adze\logs\host.log"
$progressionPath = Join-Path $env:LOCALAPPDATA "Adze\state\progression-state.json"
$traceRoot = Join-Path $env:LOCALAPPDATA "Adze\traces"
$preflightPath = Join-Path $env:LOCALAPPDATA "Adze\logs\launcher-preflight.json"
$reportDir = Join-Path $repoRoot "benchmarks\reports"
$reportPath = Join-Path $reportDir "host-validation-report-latest.json"

# ---------------------------------------------------------------------------
# Helper: write the validation report JSON
# ---------------------------------------------------------------------------
function Write-ValidationReport {
    param(
        [string]$Status,
        [string]$BlockReason = $null,
        [System.Collections.Specialized.OrderedDictionary]$Checks = $null,
        [int]$Passed = 0,
        [int]$Failed = 0
    )

    if (-not (Test-Path $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    }

    $checksObj = $null
    if ($Checks) {
        $checksObj = [ordered]@{}
        foreach ($kv in $Checks.GetEnumerator()) {
            $checksObj[$kv.Key] = [bool]$kv.Value
        }
    }

    $report = [ordered]@{
        timestamp_utc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        status        = $Status
        block_reason  = $BlockReason
        checks        = $checksObj
        passed        = $Passed
        failed        = $Failed
    }

    $report | ConvertTo-Json -Depth 4 | Set-Content -Path $reportPath -Encoding UTF8
}

# ---------------------------------------------------------------------------
# Run reload-host (rebuild, register, launch, open sample doc)
# ---------------------------------------------------------------------------
& powershell.exe -NoProfile -File (Join-Path $repoRoot 'scripts\setup\reload-host.ps1') -SamplePath $SamplePath | Out-Null

# ---------------------------------------------------------------------------
# Launcher preflight gate
# ---------------------------------------------------------------------------
if (Test-Path $preflightPath) {
    try {
        $preflight = Get-Content $preflightPath -Raw | ConvertFrom-Json
    } catch {
        $preflight = $null
    }

    if ($preflight -and $preflight.launch_blocked -eq $true) {
        $reason = if ($preflight.block_reason) { $preflight.block_reason } else { "unknown" }

        Write-Output ""
        Write-Output "SKIP: Launcher blocker detected - $reason"
        Write-Output "This is NOT a code regression. Clear the launcher window and rerun."
        Write-Output ""
        Write-Output "validation: SKIPPED (launcher blocked)"

        Write-ValidationReport -Status "blocked" -BlockReason $reason
        exit 3
    }
}

# ---------------------------------------------------------------------------
# Host log presence check
# ---------------------------------------------------------------------------
if (-not (Test-Path $logPath)) {
    Write-Output "Host log not found: $logPath"
    Write-ValidationReport -Status "fail" -BlockReason "host log not found"
    throw "Host log not found: $logPath"
}

# ---------------------------------------------------------------------------
# Run validation checks
# ---------------------------------------------------------------------------
$content = Get-Content $logPath -Raw
$checks = [ordered]@{
    connect_completed = $content -match "ConnectToSW completed"
    taskpane_created = $content -match "Task Pane created"
    document_logged = $content -match "title: Part1.SLDPRT"
    document_change_event = $content -match "ActiveDocChangeNotify"
    tool_report_logged = $content -match "get_document_summary"
    feature_tree_logged = $content -match "get_feature_tree_slice"
    dimensions_logged = $content -match "get_dimensions"
    configurations_logged = $content -match "get_configurations"
    mates_logged = $content -match "get_mates"
    reference_graph_logged = $content -match "get_reference_graph"
    diagnostics_logged = $content -match "get_rebuild_diagnostics"
    trace_recorded = $content -match "Trace recorded:"
    progression_state_written = Test-Path $progressionPath
    trace_files_written = (Test-Path $traceRoot) -and ((Get-ChildItem $traceRoot -Recurse -File | Measure-Object).Count -gt 0)
}

# ---------------------------------------------------------------------------
# Print individual check results
# ---------------------------------------------------------------------------
$checks.GetEnumerator() | ForEach-Object {
    Write-Output ("{0}={1}" -f $_.Key, $_.Value)
}

# ---------------------------------------------------------------------------
# Tally and summary
# ---------------------------------------------------------------------------
$passedCount = ($checks.Values | Where-Object { $_ -eq $true }).Count
$failedCount = ($checks.Values | Where-Object { $_ -eq $false }).Count

Write-Output ""
Write-Output ("validation: passed={0} failed={1} skipped=0" -f $passedCount, $failedCount)

# ---------------------------------------------------------------------------
# Write report and exit
# ---------------------------------------------------------------------------
if ($failedCount -gt 0) {
    Write-ValidationReport -Status "fail" -Checks $checks -Passed $passedCount -Failed $failedCount
    exit 1
}

Write-ValidationReport -Status "pass" -Checks $checks -Passed $passedCount -Failed $failedCount
