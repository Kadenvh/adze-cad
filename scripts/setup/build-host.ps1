param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$project = "C:\SW_plugin\src\Adze.Host\Adze.Host.csproj"

if (-not (Test-Path $msbuild)) {
    throw "MSBuild not found: $msbuild"
}

if (-not (Test-Path $project)) {
    throw "Host project not found: $project"
}

& $msbuild $project /t:Build /p:Configuration=$Configuration /nologo
