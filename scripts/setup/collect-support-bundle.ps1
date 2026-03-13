param(
    [string]$OutputDirectory = (Join-Path $env:LOCALAPPDATA "Adze\SupportBundles"),
    [switch]$IncludeLaunchCheck = $true,
    [switch]$KeepExpanded
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$reportsPath = Join-Path $repoRoot "benchmarks\reports"
$runtimeRoot = Join-Path $env:LOCALAPPDATA "Adze"
$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss")
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$bundleRoot = Join-Path $resolvedOutputDirectory ("adze-support-" + $timestamp)
$zipPath = $bundleRoot + ".zip"
$collectedItems = New-Object System.Collections.Generic.List[string]

[System.IO.Directory]::CreateDirectory($bundleRoot) | Out-Null

function Add-CopyItem {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$RelativeDestination
    )

    if (-not (Test-Path $SourcePath)) {
        return
    }

    $destinationPath = Join-Path $bundleRoot $RelativeDestination
    $destinationParent = Split-Path -Path $destinationPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($destinationParent)) {
        [System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null
    }

    Copy-Item -Path $SourcePath -Destination $destinationPath -Recurse -Force
    $collectedItems.Add($RelativeDestination) | Out-Null
}

function Add-TextFile {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $destinationPath = Join-Path $bundleRoot $RelativePath
    $destinationParent = Split-Path -Path $destinationPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($destinationParent)) {
        [System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null
    }

    [System.IO.File]::WriteAllText($destinationPath, $Content + [Environment]::NewLine)
    $collectedItems.Add($RelativePath) | Out-Null
}

Add-CopyItem -SourcePath (Join-Path $runtimeRoot "logs") -RelativeDestination "runtime\logs"
Add-CopyItem -SourcePath (Join-Path $runtimeRoot "snapshots") -RelativeDestination "runtime\snapshots"
Add-CopyItem -SourcePath (Join-Path $runtimeRoot "state") -RelativeDestination "runtime\state"
Add-CopyItem -SourcePath (Join-Path $runtimeRoot "traces") -RelativeDestination "runtime\traces"
Add-CopyItem -SourcePath (Join-Path $runtimeRoot "recipes") -RelativeDestination "runtime\recipes"

Get-ChildItem -Path $reportsPath -Filter "*-latest.json" -File -ErrorAction SilentlyContinue |
    ForEach-Object {
        Add-CopyItem -SourcePath $_.FullName -RelativeDestination (Join-Path "reports" $_.Name)
    }

$processSummary = Get-Process -Name "sldworks", "3DEXPERIENCELauncher" -ErrorAction SilentlyContinue |
    Sort-Object ProcessName |
    Select-Object ProcessName, Id, StartTime

$machineSummary = @(
    "timestamp_utc: $([DateTimeOffset]::UtcNow.ToString("o"))"
    "machine_name: $env:COMPUTERNAME"
    "user_name: $env:USERNAME"
    "repo_root: $repoRoot"
    "runtime_root: $runtimeRoot"
    "powershell_edition: $($PSVersionTable.PSEdition)"
    "powershell_version: $($PSVersionTable.PSVersion)"
    ""
    "execution_policy:"
    (Get-ExecutionPolicy -List | Out-String).TrimEnd()
    ""
    "running_processes:"
    (($processSummary | Out-String).TrimEnd())
)

Add-TextFile -RelativePath "machine-summary.txt" -Content ($machineSummary -join [Environment]::NewLine)

if ($IncludeLaunchCheck) {
    $launchCheckScript = Join-Path $PSScriptRoot "launch-and-check-host.ps1"
    $launchCheckOutput = & powershell.exe -NoProfile -File $launchCheckScript 2>&1 | Out-String
    Add-TextFile -RelativePath "launcher-preflight.txt" -Content $launchCheckOutput.TrimEnd()
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Compress-Archive -Path (Join-Path $bundleRoot "*") -DestinationPath $zipPath -Force

if (-not $KeepExpanded) {
    Remove-Item -Path $bundleRoot -Recurse -Force
}

[pscustomobject]@{
    BundleRoot = $bundleRoot
    ZipPath = $zipPath
    KeptExpanded = [bool]$KeepExpanded
    CollectedItemCount = $collectedItems.Count
    CollectedItems = $collectedItems
}
