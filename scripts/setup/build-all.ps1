param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$StopSolidWorks
)

$ErrorActionPreference = "Stop"

$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$solution = "C:\SW_plugin\Adze.sln"

if (-not (Test-Path $msbuild)) {
    throw "MSBuild not found: $msbuild"
}

if (-not (Test-Path $solution)) {
    throw "Solution not found: $solution"
}

if ($StopSolidWorks) {
    & powershell.exe -NoProfile -File "C:\SW_plugin\scripts\setup\stop-solidworks-processes.ps1"
}

& $msbuild $solution /t:Build /p:Configuration=$Configuration /p:Platform="Any CPU" /nologo
