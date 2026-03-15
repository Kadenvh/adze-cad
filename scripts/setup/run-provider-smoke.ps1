param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$nugetExe = Join-Path $repoRoot 'tools\nuget.exe'
$nugetUrl = 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe'
$msbuildExe = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
$testProject = Join-Path $repoRoot 'tests\Adze.Tests\Adze.Tests.csproj'
$packagesConfig = Join-Path $repoRoot 'tests\Adze.Tests\packages.config'
$packagesDir = Join-Path $repoRoot 'packages'
$testDll = Join-Path $repoRoot 'tests\Adze.Tests\bin\Debug\Adze.Tests.dll'

Write-Host '=== Adze Live Provider Smoke Tests ===' -ForegroundColor Cyan
Write-Host ''

# Check for API key
$openAiKey = $env:SOLIDWORKS_AI_OPENAI_API_KEY
if (-not $openAiKey) { $openAiKey = $env:OPENAI_API_KEY }
$anthropicKey = $env:SOLIDWORKS_AI_ANTHROPIC_API_KEY
if (-not $anthropicKey) { $anthropicKey = $env:ANTHROPIC_API_KEY }

if (-not $openAiKey -and -not $anthropicKey) {
    Write-Host 'No API key found in environment.' -ForegroundColor Yellow
    Write-Host 'Set SOLIDWORKS_AI_OPENAI_API_KEY or SOLIDWORKS_AI_ANTHROPIC_API_KEY to run live provider smoke tests.'
    exit 1
}

$provider = $env:SOLIDWORKS_AI_PROVIDER
if (-not $provider) {
    if ($openAiKey -and -not $anthropicKey) { $provider = 'openai' }
    elseif ($anthropicKey) { $provider = 'anthropic' }
    else { $provider = 'openai' }
}
Write-Host "Provider: $provider" -ForegroundColor Gray
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

# 2. Restore and build
if (-not $SkipBuild) {
    Write-Host 'Restoring NuGet packages ...'
    & $nugetExe restore $packagesConfig -PackagesDirectory $packagesDir -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }

    $consoleRunnerDir = Get-ChildItem -Path $packagesDir -Filter 'NUnit.ConsoleRunner*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $consoleRunnerDir) {
        Write-Host 'Installing NUnit.ConsoleRunner ...'
        & $nugetExe install NUnit.ConsoleRunner -Version 3.18.3 -OutputDirectory $packagesDir -NonInteractive
        if ($LASTEXITCODE -ne 0) { throw 'NUnit.ConsoleRunner install failed.' }
    }
    Write-Host 'Packages restored.'

    Write-Host ''
    Write-Host 'Building test project ...'
    & $msbuildExe $testProject /p:Configuration=Debug /t:Build /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Write-Host 'Build succeeded.' -ForegroundColor Green
}

# 3. Locate the console runner
$consoleRunner = Get-ChildItem -Path $packagesDir -Recurse -Filter 'nunit3-console.exe' | Select-Object -First 1
if (-not $consoleRunner) { throw 'Could not find nunit3-console.exe in packages directory.' }

# 4. Run only LiveProvider category
Write-Host ''
Write-Host 'Running live provider smoke tests ...'
& $consoleRunner.FullName $testDll --noheader --labels=On --where "cat == LiveProvider"
$testExit = $LASTEXITCODE

Write-Host ''
if ($testExit -eq 0) {
    Write-Host 'All live provider smoke tests passed.' -ForegroundColor Green
} else {
    Write-Host "Live provider smoke tests finished with exit code $testExit." -ForegroundColor Red
}

exit $testExit
