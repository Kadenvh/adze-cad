param(
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$nugetExe = Join-Path $repoRoot 'tools\nuget.exe'
$nugetUrl = 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe'
# MSBuild discovery: prefer local VS 2025 Community, fall back to vswhere, then PATH.
function Resolve-MSBuild {
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    $cmd = Get-Command -Name MSBuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw 'MSBuild.exe not found. Install Visual Studio or run microsoft/setup-msbuild action in CI.'
}
$msbuildExe = Resolve-MSBuild
$testProject = Join-Path $repoRoot 'tests\Adze.Tests\Adze.Tests.csproj'
$packagesConfig = Join-Path $repoRoot 'tests\Adze.Tests\packages.config'
$packagesDir = Join-Path $repoRoot 'packages'
$testDll = Join-Path $repoRoot 'tests\Adze.Tests\bin\Debug\Adze.Tests.dll'

Write-Host '=== Adze Unit Tests ===' -ForegroundColor Cyan
Write-Host ''

# 1. Ensure nuget.exe
if (-not (Test-Path $nugetExe)) {
    $toolsDir = Join-Path $repoRoot 'tools'
    if (-not (Test-Path $toolsDir)) { New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null }
    Write-Host 'Downloading nuget.exe ...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $nugetUrl -OutFile $nugetExe -UseBasicParsing
    Write-Host 'nuget.exe downloaded.'
}

# 2. Restore NuGet packages
if (-not $SkipRestore) {
    Write-Host 'Restoring NuGet packages ...'
    & $nugetExe restore $packagesConfig -PackagesDirectory $packagesDir -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }

    # Also install console runner if not present
    $consoleRunnerDir = Get-ChildItem -Path $packagesDir -Filter 'NUnit.ConsoleRunner*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $consoleRunnerDir) {
        Write-Host 'Installing NUnit.ConsoleRunner ...'
        & $nugetExe install NUnit.ConsoleRunner -Version 3.18.3 -OutputDirectory $packagesDir -NonInteractive
        if ($LASTEXITCODE -ne 0) { throw 'NUnit.ConsoleRunner install failed.' }
    }
    Write-Host 'Packages restored.'
}

# 3. Build the test project (and its dependencies)
Write-Host ''
Write-Host 'Building test project ...'
& $msbuildExe $testProject /p:Configuration=Debug /t:Build /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host 'Build succeeded.' -ForegroundColor Green

# 4. Locate the console runner
$consoleRunner = Get-ChildItem -Path $packagesDir -Recurse -Filter 'nunit3-console.exe' | Select-Object -First 1
if (-not $consoleRunner) { throw 'Could not find nunit3-console.exe in packages directory.' }

# 5. Run tests
Write-Host ''
Write-Host 'Running tests ...'
& $consoleRunner.FullName $testDll --noheader --labels=On
$testExit = $LASTEXITCODE

Write-Host ''
if ($testExit -eq 0) {
    Write-Host 'All tests passed.' -ForegroundColor Green
} else {
    Write-Host "Tests finished with exit code $testExit." -ForegroundColor Red
}

exit $testExit
