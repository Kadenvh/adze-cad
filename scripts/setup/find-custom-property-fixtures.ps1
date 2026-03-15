param(
    [string[]]$CandidateRoots = @(
        "C:\SOLIDWORKS",
        "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\sldBenchmarking\Macro",
        "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\data\GenerateAssembly"
    ),
    [int]$MaxCandidates = 10,
    [int]$PostLaunchDelaySeconds = 8,
    [int]$PostOpenDelaySeconds = 8
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
        [int]$Attempts = 25
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

function Get-PropertyKeys {
    param($Object)

    $keys = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Object) {
        return $keys
    }

    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($key in $Object.Keys) {
            $keys.Add([string]$key)
        }

        return $keys
    }

    foreach ($property in $Object.PSObject.Properties) {
        $keys.Add($property.Name)
    }

    return $keys
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

        Get-ChildItem -Path $root -Recurse -File -Include *.sldprt,*.sldasm -ErrorAction SilentlyContinue |
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
    throw "No SOLIDWORKS candidate files found."
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
            document_count = 0
            configuration_count = 0
            keys = ""
        })
        continue
    }

    $toolResult = $snapshot.tool_results | Where-Object { $_.tool_name -eq "get_custom_properties" } | Select-Object -First 1
    if ($null -eq $toolResult) {
        $results.Add([pscustomobject]@{
            path = $path
            status = "NO_TOOL_RESULT"
            document_count = 0
            configuration_count = 0
            keys = ""
        })
        continue
    }

    $documentCount = [int](Get-MemberValue -Object $toolResult.data -Name "document_count")
    $configurationCount = [int](Get-MemberValue -Object $toolResult.data -Name "configuration_count")
    $documentProperties = Get-MemberValue -Object $toolResult.data -Name "document_properties"
    $configurationProperties = Get-MemberValue -Object $toolResult.data -Name "configuration_properties"
    $keyNames = New-Object System.Collections.Generic.List[string]

    Get-PropertyKeys -Object $documentProperties | ForEach-Object { $keyNames.Add("doc:" + $_) }
    Get-PropertyKeys -Object $configurationProperties | ForEach-Object { $keyNames.Add("cfg:" + $_) }

    $results.Add([pscustomobject]@{
        path = $path
        status = $(if (($documentCount + $configurationCount) -gt 0) { "FOUND" } else { "EMPTY" })
        document_count = $documentCount
        configuration_count = $configurationCount
        keys = ($keyNames -join ", ")
    })
}

$results | Sort-Object status, path | ForEach-Object {
    Write-Output ("{0} doc={1} cfg={2} {3}" -f $_.status, $_.document_count, $_.configuration_count, $_.path)
    if (-not [string]::IsNullOrWhiteSpace($_.keys)) {
        Write-Output ("  keys: " + $_.keys)
    }
}
