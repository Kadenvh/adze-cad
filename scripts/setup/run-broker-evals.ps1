param(
    [string]$TasksPath = "",
    [string]$ReportsPath = "",
    [switch]$IncludePending
)

$ErrorActionPreference = "Stop"

# Load .env if present
. (Join-Path $PSScriptRoot "load-env.ps1")

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $TasksPath) { $TasksPath = Join-Path $repoRoot 'benchmarks\grounding\starter-grounding-tasks.json' }
if (-not $ReportsPath) { $ReportsPath = Join-Path $repoRoot 'benchmarks\reports' }
$contractsAssemblyPath = Join-Path $repoRoot 'src\Adze.Contracts\bin\Debug\Adze.Contracts.dll'
$brokerAssemblyPath = Join-Path $repoRoot 'src\Adze.Broker\bin\Debug\Adze.Broker.dll'
$reportHelpersPath = Join-Path $PSScriptRoot "RegressionReportHelpers.ps1"

if (-not (Test-Path $reportHelpersPath)) {
    throw "Regression report helper not found: $reportHelpersPath"
}

. $reportHelpersPath

if (-not (Test-Path $TasksPath)) {
    throw "Task definition file not found: $TasksPath"
}

if (-not (Test-Path $contractsAssemblyPath)) {
    throw "Contracts assembly not found: $contractsAssemblyPath"
}

if (-not (Test-Path $brokerAssemblyPath)) {
    throw "Broker assembly not found: $brokerAssemblyPath"
}

[void][System.Reflection.Assembly]::LoadFrom($contractsAssemblyPath)
[void][System.Reflection.Assembly]::LoadFrom($brokerAssemblyPath)

function New-BrokerContext {
    param(
        [string]$DocumentType
    )

    $context = New-Object Adze.Contracts.Models.SessionContext
    $context.Environment.SolidWorksVersion = "34.1.0"
    $context.Environment.AddInVersion = "0.1.0.0"
    $context.Environment.MachineName = $env:COMPUTERNAME
    $context.Session.RequestId = [guid]::NewGuid().ToString("N")
    $context.Session.TimestampUtc = [DateTimeOffset]::UtcNow
    $context.Session.UserMode = "interactive"
    $context.Document = New-Object Adze.Contracts.Models.DocumentInfo
    $context.Document.Type = $DocumentType
    $context.Document.ActiveConfiguration = "Default"

    $toolNamesType = [Adze.Contracts.Tooling.ToolNames]
    foreach ($field in $toolNamesType.GetFields([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)) {
        $context.Policy.EnabledTools.Add([string]$field.GetValue($null))
    }

    return $context
}

$broker = New-Object Adze.Broker.Orchestration.KeywordBrokerOrchestrator
$taskDefinitions = Get-Content $TasksPath -Raw | ConvertFrom-Json
$selectedTasks = @($taskDefinitions | Where-Object {
    $IncludePending.IsPresent -or $_.status -eq "curated_verified"
})

if ($selectedTasks.Count -eq 0) {
    throw "No broker eval tasks selected from $TasksPath"
}

$results = New-Object System.Collections.Generic.List[object]

foreach ($task in $selectedTasks) {
    $context = New-BrokerContext -DocumentType $task.document_type
    $response = $broker.CreateGroundingPlan($context, [string]$task.question)
    $recommendedTools = @($response.RecommendedTools | ForEach-Object { $_.ToolName })
    $matched = $recommendedTools -contains [string]$task.expected_tool

    $results.Add([pscustomobject]@{
        task_id = $task.task_id
        document_type = $task.document_type
        question = $task.question
        expected_tool = $task.expected_tool
        status = $(if ($matched) { "PASS" } else { "FAIL" })
        recommended_tools = @($recommendedTools)
    })
}

$sortedResults = @($results | Sort-Object task_id)
$sortedResults | ForEach-Object {
    Write-Output ("{0} {1} expected={2} recommended={3}" -f $_.status, $_.task_id, $_.expected_tool, ($_.recommended_tools -join ", "))
}

$passed = @($sortedResults | Where-Object { $_.status -eq "PASS" }).Count
$failed = @($sortedResults | Where-Object { $_.status -eq "FAIL" }).Count
Write-Output ("summary: passed={0} failed={1}" -f $passed, $failed)

$report = [ordered]@{
    suite_name = "broker-evals"
    report_version = "0.1.0"
    generated_utc = [DateTimeOffset]::UtcNow.ToString("o")
    machine_name = $env:COMPUTERNAME
    tasks_path = [System.IO.Path]::GetFullPath($TasksPath)
    include_pending = [bool]$IncludePending.IsPresent
    totals = [ordered]@{
        selected = $selectedTasks.Count
        passed = $passed
        failed = $failed
    }
    results = $sortedResults
}

$reportPaths = Write-RegressionReportFiles -ReportsPath $ReportsPath -SuiteName "broker-evals" -Report $report
Write-Output ("report: latest={0}" -f $reportPaths.LatestPath)
Write-Output ("report: timestamped={0}" -f $reportPaths.TimestampedPath)

if ($failed -gt 0) {
    exit 1
}
