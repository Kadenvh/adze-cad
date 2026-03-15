param(
    [string[]]$CandidateRoots = @(
        "C:\SOLIDWORKS",
        "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\sldBenchmarking\Macro",
        "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\data\GenerateAssembly"
    ),
    [int]$MaxCandidates = 10,
    [int]$PostLaunchDelaySeconds = 8,
    [int]$PostOpenDelaySeconds = 10
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$snapshotPath = Join-Path $env:LOCALAPPDATA "Adze\snapshots\latest-grounding-snapshot.json"
$reloadScript = Join-Path $repoRoot 'scripts\setup\reload-host.ps1'
$openScript = Join-Path $repoRoot 'scripts\setup\open-sample-document.ps1'

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

function Wait-ForSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedDocumentPath,
        [int]$Attempts = 30
    )

    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        if (Test-Path $snapshotPath) {
            $snapshot = Get-Content $snapshotPath -Raw | ConvertFrom-Json
            $actualPath = [string](Get-MemberValue -Object $snapshot.context.document -Name "path")
            if ([string]::Equals($actualPath, $ExpectedDocumentPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $snapshot
            }
        }

        Start-Sleep -Seconds 1
    }

    return $null
}

function Get-CandidateFiles {
    param(
        [string[]]$Roots,
        [int]$Limit
    )

    $results = New-Object System.Collections.Generic.List[string]
    foreach ($root in $Roots) {
        if (-not (Test-Path $root)) {
            continue
        }

        Get-ChildItem -Path $root -Recurse -File -Include *.sldasm -ErrorAction SilentlyContinue |
            Sort-Object FullName |
            ForEach-Object {
                if ($results.Count -ge $Limit) {
                    return
                }

                if (-not $results.Contains($_.FullName)) {
                    $results.Add($_.FullName)
                }
            }

        if ($results.Count -ge $Limit) {
            break
        }
    }

    return $results
}

$candidatePaths = Get-CandidateFiles -Roots $CandidateRoots -Limit $MaxCandidates
if ($candidatePaths.Count -eq 0) {
    throw "No assembly candidate files found."
}

$results = New-Object System.Collections.Generic.List[object]

for ($index = 0; $index -lt $candidatePaths.Count; $index++) {
    $path = $candidatePaths[$index]
    Remove-Item $snapshotPath -Force -ErrorAction SilentlyContinue

    if ($index -eq 0) {
        & powershell.exe -NoProfile -File $reloadScript -SamplePath $path -PostLaunchDelaySeconds $PostLaunchDelaySeconds -PostOpenDelaySeconds $PostOpenDelaySeconds | Out-Null
    }
    else {
        & powershell.exe -NoProfile -File $openScript -Path $path | Out-Null
        Start-Sleep -Seconds $PostOpenDelaySeconds
    }

    $snapshot = Wait-ForSnapshot -ExpectedDocumentPath $path
    if ($null -eq $snapshot) {
        $results.Add([pscustomobject]@{
            path = $path
            status = "NO_SNAPSHOT"
            direct_count = 0
            transitive_count = 0
            broken_count = 0
            sample_items = ""
        })
        continue
    }

    $toolResult = $snapshot.tool_results | Where-Object { $_.tool_name -eq "get_reference_graph" } | Select-Object -First 1
    if ($null -eq $toolResult) {
        $results.Add([pscustomobject]@{
            path = $path
            status = "NO_TOOL_RESULT"
            direct_count = 0
            transitive_count = 0
            broken_count = 0
            sample_items = ""
        })
        continue
    }

    $directCount = [int](Get-MemberValue -Object $toolResult.data -Name "direct_count")
    $transitiveCount = [int](Get-MemberValue -Object $toolResult.data -Name "transitive_count")
    $brokenCount = [int](Get-MemberValue -Object $toolResult.data -Name "broken_reference_count")
    $items = @(Get-MemberValue -Object $toolResult.data -Name "items")
    $sampleItems = @($items | Select-Object -First 4 | ForEach-Object {
        $itemPath = [string](Get-MemberValue -Object $_ -Name "path")
        $itemName = [string](Get-MemberValue -Object $_ -Name "name")
        if ([string]::IsNullOrWhiteSpace($itemPath)) {
            $itemName
        }
        else {
            $itemName + " -> " + $itemPath
        }
    })

    $results.Add([pscustomobject]@{
        path = $path
        status = $(if ($transitiveCount -gt 0 -or $directCount -gt 0) { "FOUND" } else { "EMPTY" })
        direct_count = $directCount
        transitive_count = $transitiveCount
        broken_count = $brokenCount
        sample_items = ($sampleItems -join "; ")
    })
}

$results | Sort-Object status, transitive_count, direct_count, path -Descending | ForEach-Object {
    Write-Output ("{0} direct={1} transitive={2} broken={3} {4}" -f $_.status, $_.direct_count, $_.transitive_count, $_.broken_count, $_.path)
    if (-not [string]::IsNullOrWhiteSpace($_.sample_items)) {
        Write-Output ("  sample: " + $_.sample_items)
    }
}
