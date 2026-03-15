param(
    [string]$SamplePath = "C:\SOLIDWORKS\penjamin\Part1.SLDPRT"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$logPath = Join-Path $env:LOCALAPPDATA "Adze\logs\host.log"
$progressionPath = Join-Path $env:LOCALAPPDATA "Adze\state\progression-state.json"
$traceRoot = Join-Path $env:LOCALAPPDATA "Adze\traces"

& powershell.exe -NoProfile -File (Join-Path $repoRoot 'scripts\setup\reload-host.ps1') -SamplePath $SamplePath | Out-Null

if (-not (Test-Path $logPath)) {
    throw "Host log not found: $logPath"
}

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

$checks.GetEnumerator() | ForEach-Object {
    Write-Output ("{0}={1}" -f $_.Key, $_.Value)
}

if ($checks.Values -contains $false) {
    exit 1
}
