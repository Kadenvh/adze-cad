<#
.SYNOPSIS
    Static validation gate for the install + package scripts. Catches the
    "new src/Adze.* project shipped without being added to $requiredDlls"
    class of bug at packaging time rather than at customer install time.

.DESCRIPTION
    Runs three checks, all hermetic (no SOLIDWORKS required, no install):

      1. PROJECT-VS-LIST COVERAGE
         Every src/Adze.*/*.csproj that produces a redistributable DLL
         (i.e. excluding test/harness/exe projects) must have its DLL listed
         in BOTH install/install-adze.ps1 AND install/package-release.ps1
         $requiredDlls arrays. The exact regression that bit Session 7:
         Adze.Mesh.dll was packaged but not copied by the installer,
         because the installer's hardcoded list didn't know about it.

      2. LIST SYMMETRY
         The two $requiredDlls arrays must be identical. Drift between
         them means installer or packager disagrees about what ships,
         which manifests as silent failures.

      3. EXCLUSION SANITY
         Test projects (Adze.Tests), the dev harness (Adze.UiHarness),
         and the Manager exe (Adze.Manager — handled out-of-band) MUST
         NOT appear in either list.

    Exit codes:
      0  Validation passed.
      1  One or more checks failed (script prints which).
      2  Internal error (cannot locate repo, malformed scripts, etc.).

.PARAMETER Fix
    Where possible, auto-correct missing entries. Currently:
      - Adds missing Adze.*.dll entries to BOTH $requiredDlls arrays,
        preserving the surrounding formatting.
    Does NOT remove entries — removal might mask a real intent and is
    left to manual review.

.PARAMETER Quiet
    Suppress informational output. Errors still print. Exit code is the
    canonical signal for CI integration.

.EXAMPLE
    pwsh -NoProfile -File scripts\setup\validate-package.ps1
    # Standard dry-run validation. Non-zero exit on failure.

.EXAMPLE
    pwsh -NoProfile -File scripts\setup\validate-package.ps1 -Fix
    # Auto-add missing Adze.*.dll entries to both $requiredDlls arrays.

.NOTES
    Wired into install/package-release.ps1 Step 0.5 — packaging fails fast
    if this script reports drift. Run standalone any time you want to
    audit the deploy contract; it is safe to run repeatedly.
#>

param(
    [switch]$Fix,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$srcDir   = Join-Path $repoRoot 'src'
$installPs1 = Join-Path $repoRoot 'install\install-adze.ps1'
$packagePs1 = Join-Path $repoRoot 'install\package-release.ps1'

function Write-Info([string]$msg) { if (-not $Quiet) { Write-Host $msg } }
function Write-Step([string]$msg) { if (-not $Quiet) { Write-Host $msg -ForegroundColor Cyan } }
function Write-OK([string]$msg)   { if (-not $Quiet) { Write-Host "  $msg" -ForegroundColor Green } }
function Write-Fail([string]$msg) { Write-Host "  $msg" -ForegroundColor Red }

# Projects that ship as add-in runtime DLLs and MUST appear in $requiredDlls.
# Excluded by design (commented for posterity):
#   - Adze.Manager   — exe, handled by package-release.ps1 separately
#   - Adze.UiHarness — exe, dev-only, not shipped
#   - Adze.Tests     — test project, not shipped
function Get-ShippableProjects {
    if (-not (Test-Path $srcDir)) {
        Write-Fail "src/ directory not found at $srcDir"
        exit 2
    }
    $projects = Get-ChildItem $srcDir -Directory | Where-Object { $_.Name -like 'Adze.*' }
    $shippable = @()
    $excluded  = @('Adze.Manager', 'Adze.UiHarness', 'Adze.Tests')
    foreach ($p in $projects) {
        if ($excluded -contains $p.Name) { continue }
        $csproj = Join-Path $p.FullName ($p.Name + '.csproj')
        if (-not (Test-Path $csproj)) { continue }
        $content = Get-Content $csproj -Raw
        # Library projects produce a redistributable DLL. Treat absence of
        # OutputType (defaults to Library for csproj) the same as Library.
        $isLibrary = $content -match '<OutputType>Library</OutputType>' -or $content -notmatch '<OutputType>'
        if ($isLibrary) {
            $shippable += [PSCustomObject]@{
                Project = $p.Name
                Dll     = $p.Name + '.dll'
            }
        }
    }
    return $shippable
}

function Get-RequiredDllList([string]$scriptPath) {
    if (-not (Test-Path $scriptPath)) {
        Write-Fail "Script not found: $scriptPath"
        exit 2
    }
    $content = Get-Content $scriptPath -Raw
    # Match the $requiredDlls = @( ... ) block. Pulls every quoted DLL name.
    $match = [regex]::Match($content, '\$requiredDlls\s*=\s*@\(([^)]+)\)', 'Singleline')
    if (-not $match.Success) {
        Write-Fail "Could not locate `$requiredDlls in $scriptPath"
        exit 2
    }
    $block = $match.Groups[1].Value
    $names = [regex]::Matches($block, '["'']([^"'']+\.dll)["'']') | ForEach-Object { $_.Groups[1].Value }
    return ,@($names)
}

function Add-DllToList([string]$scriptPath, [string]$dllName) {
    # Insert the new DLL just before the closing paren of $requiredDlls,
    # preserving the indentation of the previous entry. We only support the
    # canonical formatting both scripts currently use; if someone reformats
    # the array we'll fail loudly here rather than silently corrupting.
    $content = Get-Content $scriptPath -Raw
    $regex   = '(\$requiredDlls\s*=\s*@\(\s*\r?\n)((?:.*\r?\n)+?)(\s*\))'
    $match   = [regex]::Match($content, $regex)
    if (-not $match.Success) {
        Write-Fail "Cannot auto-fix $scriptPath — `$requiredDlls block formatting not recognized."
        return $false
    }
    $existing = $match.Groups[2].Value
    # Derive the quote style and indentation from the last existing entry.
    $lastEntry = [regex]::Match($existing, '(?ms)^(\s*)(["''])([^"'']+\.dll)["''](,?)\s*$', 'RightToLeft')
    if (-not $lastEntry.Success) {
        Write-Fail "Cannot auto-fix $scriptPath — no existing entry to mimic."
        return $false
    }
    $indent = $lastEntry.Groups[1].Value
    $quote  = $lastEntry.Groups[2].Value
    # Ensure the previous-last entry has a trailing comma. PowerShell array
    # literals tolerate a hanging comma, so we always append one to be safe.
    $rewrittenExisting = $existing
    if ($rewrittenExisting -notmatch ',\s*$') {
        $rewrittenExisting = $rewrittenExisting.TrimEnd("`r","`n") + ",`r`n"
    }
    $newLine = "$indent$quote$dllName$quote`r`n"
    $rebuilt = $match.Groups[1].Value + $rewrittenExisting + $newLine + $match.Groups[3].Value
    $patched = $content.Substring(0, $match.Index) + $rebuilt + $content.Substring($match.Index + $match.Length)
    [System.IO.File]::WriteAllText($scriptPath, $patched)
    return $true
}

# --- Run validation ----------------------------------------------------------

Write-Step "=== validate-package.ps1 ==="
Write-Info "  Repo root: $repoRoot"

$failures = New-Object System.Collections.Generic.List[string]
$fixes    = New-Object System.Collections.Generic.List[string]

# Check 1: project-vs-list coverage
Write-Step ""
Write-Step "=== Check 1: project-vs-list coverage ==="
$shippable     = Get-ShippableProjects
$installList   = Get-RequiredDllList $installPs1
$packageList   = Get-RequiredDllList $packagePs1

if ($shippable.Count -eq 0) {
    $failures.Add("No shippable Adze.* projects discovered under src/. This is almost certainly wrong.")
} else {
    Write-Info "  Shippable projects under src/:"
    foreach ($p in $shippable) { Write-Info "    - $($p.Project) => $($p.Dll)" }
}

foreach ($p in $shippable) {
    $inInstall = $installList -contains $p.Dll
    $inPackage = $packageList -contains $p.Dll
    if ($inInstall -and $inPackage) {
        Write-OK "$($p.Dll) — present in both scripts"
        continue
    }
    if (-not $inInstall) {
        if ($Fix) {
            if (Add-DllToList $installPs1 $p.Dll) {
                $fixes.Add("Added $($p.Dll) to install-adze.ps1")
                Write-OK "fixed: added $($p.Dll) to install-adze.ps1"
            } else {
                $failures.Add("install-adze.ps1 missing $($p.Dll) and auto-fix failed")
            }
        } else {
            $failures.Add("install-adze.ps1 `$requiredDlls is missing: $($p.Dll)")
            Write-Fail "MISSING in install-adze.ps1: $($p.Dll)"
        }
    }
    if (-not $inPackage) {
        if ($Fix) {
            if (Add-DllToList $packagePs1 $p.Dll) {
                $fixes.Add("Added $($p.Dll) to package-release.ps1")
                Write-OK "fixed: added $($p.Dll) to package-release.ps1"
            } else {
                $failures.Add("package-release.ps1 missing $($p.Dll) and auto-fix failed")
            }
        } else {
            $failures.Add("package-release.ps1 `$requiredDlls is missing: $($p.Dll)")
            Write-Fail "MISSING in package-release.ps1: $($p.Dll)"
        }
    }
}

# Check 2: list symmetry (after any fixes)
Write-Step ""
Write-Step "=== Check 2: install vs package list symmetry ==="
$installList = Get-RequiredDllList $installPs1
$packageList = Get-RequiredDllList $packagePs1

$onlyInstall = $installList | Where-Object { $packageList -notcontains $_ }
$onlyPackage = $packageList | Where-Object { $installList -notcontains $_ }
if ($onlyInstall.Count -eq 0 -and $onlyPackage.Count -eq 0) {
    Write-OK "lists match ($($installList.Count) entries each)"
} else {
    foreach ($d in $onlyInstall) {
        $failures.Add("DLL only in install-adze.ps1 (not in package-release.ps1): $d")
        Write-Fail "ASYMMETRIC: only in install-adze.ps1: $d"
    }
    foreach ($d in $onlyPackage) {
        $failures.Add("DLL only in package-release.ps1 (not in install-adze.ps1): $d")
        Write-Fail "ASYMMETRIC: only in package-release.ps1: $d"
    }
}

# Check 3: exclusion sanity — test/harness/exe projects MUST NOT be listed
Write-Step ""
Write-Step "=== Check 3: exclusion sanity ==="
$forbidden = @('Adze.Tests.dll', 'Adze.UiHarness.dll', 'Adze.UiHarness.exe', 'Adze.Manager.dll')
$violations = $false
foreach ($d in $forbidden) {
    if ($installList -contains $d) {
        $failures.Add("install-adze.ps1 has forbidden entry: $d")
        Write-Fail "FORBIDDEN in install-adze.ps1: $d (test/harness/exe must not ship as a runtime DLL)"
        $violations = $true
    }
    if ($packageList -contains $d) {
        $failures.Add("package-release.ps1 has forbidden entry: $d")
        Write-Fail "FORBIDDEN in package-release.ps1: $d (test/harness/exe must not ship as a runtime DLL)"
        $violations = $true
    }
}
if (-not $violations) {
    Write-OK "no forbidden entries"
}

# --- Summary -----------------------------------------------------------------

Write-Step ""
Write-Step "=== Summary ==="

if ($fixes.Count -gt 0) {
    Write-Info "  Auto-fixes applied: $($fixes.Count)"
    foreach ($f in $fixes) { Write-Info "    - $f" }
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "  Validation FAILED — $($failures.Count) issue(s):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "    - $f" -ForegroundColor Red }
    Write-Host ""
    Write-Host "  Re-run with -Fix to auto-add missing entries, or edit the scripts manually." -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-OK "All checks passed."
exit 0
