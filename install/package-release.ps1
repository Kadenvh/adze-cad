param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$repoRoot      = Split-Path -Parent $PSScriptRoot
$msbuild       = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$solution      = Join-Path $repoRoot 'Adze.sln'
$releaseDir    = Join-Path $repoRoot 'src\Adze.Host\bin\Release'
$managerDir    = Join-Path $repoRoot 'src\Adze.Manager\bin\Release'
$distDir       = Join-Path $repoRoot 'install\dist'

# --- Build -------------------------------------------------------------------

if (-not $SkipBuild) {
    if (-not (Test-Path $msbuild)) {
        throw "MSBuild not found: $msbuild"
    }
    if (-not (Test-Path $solution)) {
        throw "Solution not found: $solution"
    }

    Write-Host "Building solution in Release configuration..."
    & $msbuild $solution /t:Build /p:Configuration=Release /p:Platform="Any CPU" /nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Write-Host "Build succeeded."
} else {
    Write-Host "Skipping build (-SkipBuild)."
}

# --- Validate Release output -------------------------------------------------

if (-not (Test-Path $releaseDir)) {
    throw "Release output directory not found: $releaseDir"
}

$requiredDlls = @(
    'Adze.Host.dll',
    'Adze.Broker.dll',
    'Adze.Tools.dll',
    'Adze.Trace.dll',
    'Adze.Contracts.dll',
    'Adze.Index.dll',
    'OpenMcdf.dll'
)

foreach ($dll in $requiredDlls) {
    $path = Join-Path $releaseDir $dll
    if (-not (Test-Path $path)) {
        throw "Required DLL not found: $path"
    }
}

# --- Stage artifacts ----------------------------------------------------------

if (Test-Path $distDir) {
    Remove-Item $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir | Out-Null

# Copy project DLLs (no interop, no PDBs, no XML docs)
foreach ($dll in $requiredDlls) {
    Copy-Item (Join-Path $releaseDir $dll) -Destination $distDir
}

# Copy Adze.Manager.exe — the end-user installer/manager UI
$managerExe = Join-Path $managerDir 'Adze.Manager.exe'
if (Test-Path $managerExe) {
    Copy-Item $managerExe -Destination $distDir
} else {
    Write-Warning "Adze.Manager.exe not found at $managerExe — zip will lack the manager UI"
}

# Copy install/uninstall scripts
$installScripts = @('install-adze.ps1', 'uninstall-adze.ps1', 'install-adze.bat', 'uninstall-adze.bat')
foreach ($script in $installScripts) {
    $scriptPath = Join-Path $PSScriptRoot $script
    if (Test-Path $scriptPath) {
        Copy-Item $scriptPath -Destination $distDir
    } else {
        Write-Warning "Install script not found, skipping: $scriptPath"
    }
}

# --- Read version via reflection ----------------------------------------------

$hostDllPath = Join-Path $distDir 'Adze.Host.dll'
$assembly    = [System.Reflection.Assembly]::LoadFrom($hostDllPath)
$version     = $assembly.GetName().Version.ToString()

# --- Create zip ---------------------------------------------------------------

$zipName = "adze-v$version.zip"
$zipPath = Join-Path $PSScriptRoot $zipName

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $distDir '*') -DestinationPath $zipPath -Force

# --- Summary ------------------------------------------------------------------

$fileCount = (Get-ChildItem $distDir -File).Count
$zipSize   = (Get-Item $zipPath).Length

$sizeMB = [math]::Round($zipSize / 1MB, 2)

Write-Host ""
Write-Host "=== Package Summary ==="
Write-Host "Version   : $version"
Write-Host "Files     : $fileCount"
Write-Host "Zip       : $zipPath"
Write-Host "Size      : $sizeMB MB ($zipSize bytes)"
