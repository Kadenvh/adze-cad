param(
    [string]$TasksPath = "",
    [string]$ReportsPath = "",
    [int]$PostLaunchDelaySeconds = 8,
    [int]$PostOpenDelaySeconds = 10,
    [switch]$IncludePending
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $TasksPath) { $TasksPath = Join-Path $repoRoot 'benchmarks\grounding\starter-grounding-tasks.json' }
if (-not $ReportsPath) { $ReportsPath = Join-Path $repoRoot 'benchmarks\reports' }
$snapshotPath = Join-Path $env:LOCALAPPDATA "Adze\snapshots\latest-grounding-snapshot.json"
$reloadScript = Join-Path $repoRoot 'scripts\setup\reload-host.ps1'
$reportHelpersPath = Join-Path $PSScriptRoot "RegressionReportHelpers.ps1"

if (-not (Test-Path $reportHelpersPath)) {
    throw "Regression report helper not found: $reportHelpersPath"
}

. $reportHelpersPath

function Get-MemberValue {
    param(
        $Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($key in $Object.Keys) {
            if ([string]::Equals([string]$key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-Output -NoEnumerate $Object[$key]
                return
            }
        }

        return $null
    }

    $property = $Object.PSObject.Properties | Where-Object { $_.Name -ieq $Name } | Select-Object -First 1
    if ($null -ne $property) {
        Write-Output -NoEnumerate $property.Value
        return
    }

    return $null
}

function Resolve-PathValue {
    param(
        $Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    try {
        $current = $Root
        foreach ($segment in ($Path -split '\.')) {
            $match = [System.Text.RegularExpressions.Regex]::Match($segment, '^(?<name>[^\[]+)(\[(?<index>\d+)\])?$')
            if (-not $match.Success) {
                return $null
            }

            $segmentName = $match.Groups["name"].Value
            $current = Get-MemberValue -Object $current -Name $segmentName
            if ($null -eq $current) {
                return $null
            }

            if ($match.Groups["index"].Success) {
                $index = [int]$match.Groups["index"].Value
                if ($current -is [System.Collections.IList]) {
                    if ($index -ge $current.Count) {
                        return $null
                    }

                    $current = $current[$index]
                }
                elseif ($current.GetType().IsArray) {
                    if ($index -ge $current.Length) {
                        return $null
                    }

                    $current = $current[$index]
                }
                else {
                    return $null
                }
            }
        }

        return $current
    }
    catch {
        return $null
    }
}

function Resolve-ToolValue {
    param(
        [Parameter(Mandatory = $true)]$ToolResult,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $value = Resolve-PathValue -Root $ToolResult.data -Path $Path
    if ($null -ne $value) {
        return $value
    }

    return Resolve-PathValue -Root $ToolResult -Path $Path
}

function Convert-ExpectedValue {
    param([Parameter(Mandatory = $true)][string]$Value)

    $trimmed = $Value.Trim()
    if ($trimmed -match '^(true|false)$') {
        return [System.Convert]::ToBoolean($trimmed)
    }

    $intValue = 0
    if ([int]::TryParse($trimmed, [ref]$intValue)) {
        return $intValue
    }

    $doubleValue = 0.0
    if ([double]::TryParse($trimmed, [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$doubleValue)) {
        return $doubleValue
    }

    return $trimmed
}

function Compare-Scalar {
    param(
        $Actual,
        $Expected
    )

    if ($null -eq $Actual -or $null -eq $Expected) {
        return $null -eq $Actual -and $null -eq $Expected
    }

    if ($Actual -is [bool] -or $Expected -is [bool]) {
        return [System.Convert]::ToBoolean($Actual) -eq [System.Convert]::ToBoolean($Expected)
    }

    if (($Actual -is [int] -or $Actual -is [long] -or $Actual -is [double] -or $Actual -is [decimal]) -and
        ($Expected -is [int] -or $Expected -is [long] -or $Expected -is [double] -or $Expected -is [decimal])) {
        return [double]$Actual -eq [double]$Expected
    }

    return [string]::Equals([string]$Actual, [string]$Expected, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-CollectionContains {
    param(
        [Parameter(Mandatory = $true)]$Collection,
        [Parameter(Mandatory = $true)]$Expected
    )

    foreach ($item in $Collection) {
        if ($null -eq $item) {
            continue
        }

        if (Compare-Scalar -Actual $item -Expected $Expected) {
            return $true
        }

        $nameValue = Get-MemberValue -Object $item -Name "name"
        if ($null -ne $nameValue -and (Compare-Scalar -Actual $nameValue -Expected $Expected)) {
            return $true
        }
    }

    return $false
}

function Test-Assertion {
    param(
        [Parameter(Mandatory = $true)]$ToolResult,
        [Parameter(Mandatory = $true)][string]$Assertion
    )

    if ($Assertion -match '^(?<left>.+?)\s+==\s+(?<right>.+)$') {
        $actual = Resolve-ToolValue -ToolResult $ToolResult -Path $matches.left.Trim()
        $expected = Convert-ExpectedValue -Value $matches.right
        return @{
            passed = Compare-Scalar -Actual $actual -Expected $expected
            detail = "actual=$actual expected=$expected"
        }
    }

    if ($Assertion -match '^(?<left>.+?)\s+>=\s+(?<right>.+)$') {
        $actual = Resolve-ToolValue -ToolResult $ToolResult -Path $matches.left.Trim()
        $expected = [double](Convert-ExpectedValue -Value $matches.right)
        $actualNumber = [double]$actual
        return @{
            passed = $actualNumber -ge $expected
            detail = "actual=$actualNumber expected>=$expected"
        }
    }

    if ($Assertion -match '^(?<left>.+?)\s+ends with\s+(?<right>.+)$') {
        $actual = [string](Resolve-ToolValue -ToolResult $ToolResult -Path $matches.left.Trim())
        $expected = [string](Convert-ExpectedValue -Value $matches.right)
        return @{
            passed = $actual.EndsWith($expected, [System.StringComparison]::OrdinalIgnoreCase)
            detail = "actual=$actual expected_suffix=$expected"
        }
    }

    if ($Assertion -match '^(?<left>.+?)\s+is empty$') {
        $actual = Resolve-ToolValue -ToolResult $ToolResult -Path $matches.left.Trim()
        $count = 0
        if ($null -eq $actual) {
            $count = 0
        }
        elseif ($actual -is [string]) {
            $count = $actual.Length
        }
        elseif ($actual -is [System.Collections.ICollection]) {
            $count = $actual.Count
        }
        elseif ($actual.GetType().IsArray) {
            $count = $actual.Length
        }
        else {
            $count = 1
        }

        return @{
            passed = $count -eq 0
            detail = "count=$count"
        }
    }

    if ($Assertion -match '^(?<left>.+?)\s+contains\s+(?<right>.+)$') {
        $actual = Resolve-ToolValue -ToolResult $ToolResult -Path $matches.left.Trim()
        $expected = Convert-ExpectedValue -Value $matches.right
        $items = @()

        if ($null -ne $actual) {
            if ($actual -is [System.Collections.IEnumerable] -and -not ($actual -is [string])) {
                $items = @($actual)
            }
            else {
                $items = @($actual)
            }
        }

        return @{
            passed = Test-CollectionContains -Collection $items -Expected $expected
            detail = "expected_item=$expected"
        }
    }

    throw "Unsupported assertion syntax: $Assertion"
}

function Wait-ForSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$ExpectedDocumentPath = ""
    )

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        if (Test-Path $Path) {
            $snapshot = Get-Content $Path -Raw | ConvertFrom-Json
            if ([string]::IsNullOrWhiteSpace($ExpectedDocumentPath)) {
                return $snapshot
            }

            $actualPath = [string](Get-MemberValue -Object $snapshot.context.document -Name "path")
            if ([string]::Equals($actualPath, $ExpectedDocumentPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $snapshot
            }
        }

        Start-Sleep -Seconds 1
    }

    throw "Grounding snapshot not found: $Path"
}

if (-not (Test-Path $TasksPath)) {
    throw "Task definition file not found: $TasksPath"
}

if (-not (Test-Path $reloadScript)) {
    throw "Reload script not found: $reloadScript"
}

$taskDefinitions = Get-Content $TasksPath -Raw | ConvertFrom-Json
$selectedTasks = @($taskDefinitions | Where-Object {
    $IncludePending.IsPresent -or $_.status -eq "curated_verified"
})

if ($selectedTasks.Count -eq 0) {
    throw "No benchmark tasks selected from $TasksPath"
}

$results = New-Object System.Collections.Generic.List[object]
$groupedTasks = $selectedTasks | Group-Object entry_path

foreach ($group in $groupedTasks) {
    $entryPath = [string]$group.Name
    if ([string]::IsNullOrWhiteSpace($entryPath) -or -not (Test-Path $entryPath)) {
        foreach ($task in $group.Group) {
            $results.Add([pscustomobject]@{
                task_id = $task.task_id
                tool = $task.expected_tool
                status = "FAIL"
                detail = "Entry path not found: $entryPath"
            })
        }

        continue
    }

    try {
        Remove-Item $snapshotPath -Force -ErrorAction SilentlyContinue
        & powershell.exe -NoProfile -File $reloadScript -SamplePath $entryPath -PostLaunchDelaySeconds $PostLaunchDelaySeconds -PostOpenDelaySeconds $PostOpenDelaySeconds | Out-Null
        $snapshot = Wait-ForSnapshot -Path $snapshotPath -ExpectedDocumentPath $entryPath
    }
    catch {
        foreach ($task in $group.Group) {
            $results.Add([pscustomobject]@{
                task_id = $task.task_id
                tool = $task.expected_tool
                status = "FAIL"
                detail = "Reload or snapshot failed: $($_.Exception.Message)"
            })
        }

        continue
    }

    foreach ($task in $group.Group) {
        $toolResult = @($snapshot.tool_results | Where-Object { $_.tool_name -eq $task.expected_tool } | Select-Object -First 1)
        if ($toolResult.Count -eq 0) {
            $results.Add([pscustomobject]@{
                task_id = $task.task_id
                tool = $task.expected_tool
                status = "FAIL"
                detail = "Tool result not found in snapshot."
            })
            continue
        }

        $toolResult = $toolResult[0]
        $failedAssertions = New-Object System.Collections.Generic.List[string]
        foreach ($assertion in $task.expected_assertions) {
            try {
                $assertionResult = Test-Assertion -ToolResult $toolResult -Assertion $assertion
                if (-not $assertionResult.passed) {
                    $failedAssertions.Add("$assertion ($($assertionResult.detail))")
                }
            }
            catch {
                $failedAssertions.Add("$assertion (error=$($_.Exception.Message))")
            }
        }

        $results.Add([pscustomobject]@{
            task_id = $task.task_id
            tool = $task.expected_tool
            status = $(if ($failedAssertions.Count -eq 0) { "PASS" } else { "FAIL" })
            detail = $(if ($failedAssertions.Count -eq 0) { "All assertions passed." } else { $failedAssertions -join "; " })
        })
    }
}

$sortedResults = @($results | Sort-Object task_id)
$sortedResults | ForEach-Object {
    Write-Output ("{0} {1} [{2}] {3}" -f $_.status, $_.task_id, $_.tool, $_.detail)
}

$passed = @($sortedResults | Where-Object { $_.status -eq "PASS" }).Count
$failed = @($sortedResults | Where-Object { $_.status -eq "FAIL" }).Count
Write-Output ("summary: passed={0} failed={1}" -f $passed, $failed)

$fixtureGroups = @($groupedTasks | ForEach-Object {
    [ordered]@{
        entry_path = [string]$_.Name
        task_count = $_.Count
    }
})

$report = [ordered]@{
    suite_name = "grounding-benchmarks"
    report_version = "0.1.0"
    generated_utc = [DateTimeOffset]::UtcNow.ToString("o")
    machine_name = $env:COMPUTERNAME
    tasks_path = [System.IO.Path]::GetFullPath($TasksPath)
    include_pending = [bool]$IncludePending.IsPresent
    post_launch_delay_seconds = $PostLaunchDelaySeconds
    post_open_delay_seconds = $PostOpenDelaySeconds
    fixture_groups = $fixtureGroups
    totals = [ordered]@{
        selected = $selectedTasks.Count
        passed = $passed
        failed = $failed
    }
    results = $sortedResults
}

$reportPaths = Write-RegressionReportFiles -ReportsPath $ReportsPath -SuiteName "grounding-benchmarks" -Report $report
Write-Output ("report: latest={0}" -f $reportPaths.LatestPath)
Write-Output ("report: timestamped={0}" -f $reportPaths.TimestampedPath)

if ($failed -gt 0) {
    exit 1
}
