param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$classesRoot = "HKCU:\Software\Classes"
$solidWorksRoot = "HKCU:\Software\SolidWorks"

$paths = @(
    "$classesRoot\Adze.Host.AddIn",
    "$classesRoot\Adze.Host.TaskPaneControl",
    "$classesRoot\CLSID\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}",
    "$classesRoot\CLSID\{F4068202-600A-4D6F-973B-DA2048A949CF}",
    "$solidWorksRoot\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}",
    "$solidWorksRoot\AddInsStartup\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}"
)

foreach ($path in $paths) {
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force -ErrorAction Stop
    }
}
