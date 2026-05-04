<#
.SYNOPSIS
    Builds the Release configuration of Adze.sln and packages the runtime
    artifacts + install scripts into a versioned zip ready for distribution.

.DESCRIPTION
    Pipeline:
      1. Build solution (Release) unless -SkipBuild
      2. Validate every required artifact is present (fails fast — no warnings
         that can produce a broken zip silently)
      3. Stage clean dist/ directory
      4. Embed MANIFEST.txt with version, file list, and SHA256 hashes
      5. Compress to install/adze-v{version}.zip
      6. Smoke-test: extract the zip into %TEMP% and assert byte-for-byte
         match with the staged dist/ — catches packaging corruption and
         the "stale zip ships old script" class of regression that bit
         Session 5/6.

    Failure at any step exits non-zero and (for smoke-test failures) deletes
    the bad zip so it cannot be accidentally distributed.

.PARAMETER SkipBuild
    Reuse existing bin/Release output. Use only when iterating on packaging.

.PARAMETER AllowDirty
    Suppress the "uncommitted changes in install/" warning. Without this,
    the script still builds but prints a loud warning so you know the zip
    you are about to ship contains working-tree state.
#>

param(
    [switch]$SkipBuild,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"

$repoRoot      = Split-Path -Parent $PSScriptRoot
$msbuild       = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$solution      = Join-Path $repoRoot 'Adze.sln'
$releaseDir    = Join-Path $repoRoot 'src\Adze.Host\bin\Release'
$managerDir    = Join-Path $repoRoot 'src\Adze.Manager\bin\Release'
$distDir       = Join-Path $repoRoot 'install\dist'

# --- Step 0: Working-tree sanity check ---------------------------------------

Write-Host "=== Step 0: Working-tree check ===" -ForegroundColor Cyan
$gitDirty = & git -C $repoRoot status --porcelain -- install/ 2>$null
if ($LASTEXITCODE -eq 0 -and $gitDirty) {
    Write-Host "  Uncommitted changes in install/:" -ForegroundColor Yellow
    $gitDirty -split "`n" | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
    if (-not $AllowDirty) {
        Write-Host ""
        Write-Host "  The zip will contain the WORKING-TREE state of these files." -ForegroundColor Yellow
        Write-Host "  Pass -AllowDirty to suppress this warning, or commit first." -ForegroundColor Yellow
        Write-Host ""
    }
} else {
    Write-Host "  Clean (or git unavailable)." -ForegroundColor Green
}

# --- Step 1: Build -----------------------------------------------------------

Write-Host ""
Write-Host "=== Step 1: Build ===" -ForegroundColor Cyan
if (-not $SkipBuild) {
    if (-not (Test-Path $msbuild)) {
        throw "MSBuild not found: $msbuild"
    }
    if (-not (Test-Path $solution)) {
        throw "Solution not found: $solution"
    }

    Write-Host "  Building solution in Release configuration..."
    & $msbuild $solution /t:Build /p:Configuration=Release /p:Platform="Any CPU" /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Write-Host "  Build succeeded." -ForegroundColor Green
} else {
    Write-Host "  Skipping build (-SkipBuild)." -ForegroundColor Yellow
}

# --- Step 2: Validate every required artifact (fail fast) --------------------

Write-Host ""
Write-Host "=== Step 2: Validate artifacts ===" -ForegroundColor Cyan

if (-not (Test-Path $releaseDir)) {
    throw "Release output directory not found: $releaseDir"
}
if (-not (Test-Path $managerDir)) {
    throw "Manager Release output directory not found: $managerDir"
}

$requiredDlls = @(
    'Adze.Host.dll',
    'Adze.Broker.dll',
    'Adze.Tools.dll',
    'Adze.Trace.dll',
    'Adze.Contracts.dll',
    'Adze.Index.dll',
    'Adze.UI.dll',
    'OpenMcdf.dll'
)
$requiredScripts = @('install-adze.ps1', 'uninstall-adze.ps1', 'install-adze.bat', 'uninstall-adze.bat')

# DLLs from Release output
$missing = @()
foreach ($dll in $requiredDlls) {
    $path = Join-Path $releaseDir $dll
    if (-not (Test-Path $path)) { $missing += "src\Adze.Host\bin\Release\$dll" }
}

# Manager EXE — must exist (Session 4 regression: silently shipped without it)
$managerExe = Join-Path $managerDir 'Adze.Manager.exe'
if (-not (Test-Path $managerExe)) {
    $missing += "src\Adze.Manager\bin\Release\Adze.Manager.exe"
}

# Install scripts from install/
foreach ($script in $requiredScripts) {
    $path = Join-Path $PSScriptRoot $script
    if (-not (Test-Path $path)) { $missing += "install\$script" }
}

if ($missing.Count -gt 0) {
    Write-Host "  Missing required artifacts:" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "    - $m" -ForegroundColor Red }
    throw "Cannot package: $($missing.Count) artifact(s) missing. Run a full build."
}
Write-Host "  All $($requiredDlls.Count) DLLs + Manager EXE + $($requiredScripts.Count) scripts found." -ForegroundColor Green

# --- Step 3: Stage dist/ -----------------------------------------------------

Write-Host ""
Write-Host "=== Step 3: Stage dist ===" -ForegroundColor Cyan

if (Test-Path $distDir) {
    Remove-Item $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir | Out-Null
Write-Host "  Cleaned: $distDir"

foreach ($dll in $requiredDlls) {
    Copy-Item (Join-Path $releaseDir $dll) -Destination $distDir
}
Copy-Item $managerExe -Destination $distDir
foreach ($script in $requiredScripts) {
    Copy-Item (Join-Path $PSScriptRoot $script) -Destination $distDir
}
Write-Host "  Staged $($requiredDlls.Count + 1 + $requiredScripts.Count) files." -ForegroundColor Green

# --- Step 4: Read version + write MANIFEST.txt -------------------------------

Write-Host ""
Write-Host "=== Step 4: Manifest ===" -ForegroundColor Cyan

$hostDllPath = Join-Path $distDir 'Adze.Host.dll'
$assembly    = [System.Reflection.Assembly]::LoadFrom($hostDllPath)
$version     = $assembly.GetName().Version.ToString()
$packagedAt  = (Get-Date).ToString('o')

$manifestPath = Join-Path $distDir 'MANIFEST.txt'
$manifestLines = New-Object System.Collections.Generic.List[string]
$manifestLines.Add("Adze for SOLIDWORKS — Release Manifest")
$manifestLines.Add("Version    : $version")
$manifestLines.Add("Packaged   : $packagedAt")
$manifestLines.Add("Source     : $repoRoot")
$gitSha = & git -C $repoRoot rev-parse --short HEAD 2>$null
if ($LASTEXITCODE -eq 0) {
    $manifestLines.Add("Git commit : $gitSha")
}
$manifestLines.Add("")
$manifestLines.Add("Files (SHA256):")
Get-ChildItem $distDir -File | Where-Object { $_.Name -ne 'MANIFEST.txt' } | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    $manifestLines.Add(("  {0,-24} {1,10:N0} bytes  {2}" -f $_.Name, $_.Length, $hash))
}
[System.IO.File]::WriteAllLines($manifestPath, $manifestLines)
Write-Host "  Wrote: $manifestPath" -ForegroundColor Green

# --- Step 5: Compress --------------------------------------------------------

Write-Host ""
Write-Host "=== Step 5: Compress ===" -ForegroundColor Cyan

$zipName = "adze-v$version.zip"
$zipPath = Join-Path $PSScriptRoot $zipName

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $distDir '*') -DestinationPath $zipPath -Force
Write-Host "  Created: $zipPath" -ForegroundColor Green

# --- Step 6: Smoke test (extract + byte-compare) -----------------------------

Write-Host ""
Write-Host "=== Step 6: Smoke test ===" -ForegroundColor Cyan

$smokeDir = Join-Path $env:TEMP ("adze-smoke-" + [System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $smokeDir | Out-Null
try {
    Expand-Archive -Path $zipPath -DestinationPath $smokeDir -Force

    $expected = Get-ChildItem $distDir -File | Sort-Object Name
    $actual   = Get-ChildItem $smokeDir -File | Sort-Object Name

    if ($expected.Count -ne $actual.Count) {
        throw "Smoke test FAILED: dist has $($expected.Count) files, zip has $($actual.Count)."
    }

    $mismatches = @()
    for ($i = 0; $i -lt $expected.Count; $i++) {
        $exp = $expected[$i]
        $act = $actual[$i]
        if ($exp.Name -ne $act.Name) {
            $mismatches += "name: dist=$($exp.Name) zip=$($act.Name)"
            continue
        }
        $expHash = (Get-FileHash $exp.FullName -Algorithm SHA256).Hash
        $actHash = (Get-FileHash $act.FullName -Algorithm SHA256).Hash
        if ($expHash -ne $actHash) {
            $mismatches += "hash: $($exp.Name) (dist=$expHash zip=$actHash)"
        }
    }

    if ($mismatches.Count -gt 0) {
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        Write-Host "  Smoke test FAILED — bad zip deleted:" -ForegroundColor Red
        foreach ($m in $mismatches) { Write-Host "    - $m" -ForegroundColor Red }
        throw "Smoke test reported $($mismatches.Count) mismatch(es)."
    }

    # Also verify install-adze.ps1 in zip matches install/install-adze.ps1
    # (the actual regression that motivated this gate — Session 6).
    $sourcePs1 = Join-Path $PSScriptRoot 'install-adze.ps1'
    $zipPs1    = Join-Path $smokeDir   'install-adze.ps1'
    $sourceHash = (Get-FileHash $sourcePs1 -Algorithm SHA256).Hash
    $zipHash    = (Get-FileHash $zipPs1    -Algorithm SHA256).Hash
    if ($sourceHash -ne $zipHash) {
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
        throw "Smoke test FAILED: install/install-adze.ps1 != zipped install-adze.ps1 (the exact stale-script bug this gate exists to prevent). Bad zip deleted."
    }

    Write-Host "  All $($expected.Count) files match dist (SHA256). Source vs zip ps1 confirmed identical." -ForegroundColor Green
} finally {
    Remove-Item $smokeDir -Recurse -Force -ErrorAction SilentlyContinue
}

# --- Summary -----------------------------------------------------------------

$fileCount = (Get-ChildItem $distDir -File).Count
$zipSize   = (Get-Item $zipPath).Length
$sizeMB    = [math]::Round($zipSize / 1MB, 2)

Write-Host ""
Write-Host "=== Package Summary ===" -ForegroundColor Cyan
Write-Host "  Version   : $version"
Write-Host "  Files     : $fileCount (incl. MANIFEST.txt)"
Write-Host "  Zip       : $zipPath"
Write-Host "  Size      : $sizeMB MB ($zipSize bytes)"
if ($gitSha) { Write-Host "  Commit    : $gitSha" }
Write-Host ""
