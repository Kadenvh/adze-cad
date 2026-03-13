function Write-RegressionReportFiles {
    param(
        [Parameter(Mandatory = $true)][string]$ReportsPath,
        [Parameter(Mandatory = $true)][string]$SuiteName,
        [Parameter(Mandatory = $true)]$Report
    )

    $resolvedReportsPath = [System.IO.Path]::GetFullPath($ReportsPath)
    [System.IO.Directory]::CreateDirectory($resolvedReportsPath) | Out-Null

    $timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss")
    $baseName = $SuiteName + "-report"
    $timestampedPath = Join-Path $resolvedReportsPath ($baseName + "-" + $timestamp + ".json")
    $latestPath = Join-Path $resolvedReportsPath ($baseName + "-latest.json")
    $json = $Report | ConvertTo-Json -Depth 10

    [System.IO.File]::WriteAllText($timestampedPath, $json + [Environment]::NewLine)
    [System.IO.File]::WriteAllText($latestPath, $json + [Environment]::NewLine)

    return [pscustomobject]@{
        ReportsPath = $resolvedReportsPath
        TimestampedPath = $timestampedPath
        LatestPath = $latestPath
    }
}
