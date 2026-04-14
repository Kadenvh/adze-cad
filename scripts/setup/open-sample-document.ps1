param(
    [string]$Path = "C:\SOLIDWORKS\samples\Part1.SLDPRT"
)

$ErrorActionPreference = "Stop"

$interopPath = "C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x\SOLIDWORKS\api\redist\SolidWorks.Interop.sldworks.dll"

if (-not (Test-Path $Path)) {
    throw "Sample file not found: $Path"
}

if (-not (Test-Path $interopPath)) {
    throw "SOLIDWORKS interop assembly not found: $interopPath"
}

[void][System.Reflection.Assembly]::LoadFrom($interopPath)

if (-not ("SolidWorksScriptBridge" -as [type])) {
    Add-Type -ReferencedAssemblies $interopPath -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

public static class SolidWorksScriptBridge
{
    public static void Open(string path, int docType, ref int errors, ref int warnings)
    {
        ISldWorks application = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
        application.OpenDoc6(path, docType, 0, "", ref errors, ref warnings);
    }
}
"@
}

$extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
$docType = switch ($extension) {
    ".sldprt" { 1 }
    ".sldasm" { 2 }
    ".slddrw" { 3 }
    default { throw "Unsupported SOLIDWORKS extension: $extension" }
}

[int]$errors = 0
[int]$warnings = 0
[SolidWorksScriptBridge]::Open($Path, $docType, [ref]$errors, [ref]$warnings)

Write-Output ("Opened=" + $Path)
Write-Output ("Errors=" + $errors)
Write-Output ("Warnings=" + $warnings)
