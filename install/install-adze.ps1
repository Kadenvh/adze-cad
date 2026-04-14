<#
.SYNOPSIS
    Installs the Adze SOLIDWORKS add-in for the current user.

.DESCRIPTION
    Works in two modes:
      Dev mode   -- Run from the repo root. Builds the solution and registers
                    the Debug DLLs directly from the build output. Requires
                    MSBuild (Visual Studio) to be installed.
      Release mode -- Run from the packaged zip. Registers pre-built DLLs
                    that are in the same folder as this script.

    Copies DLLs to %LOCALAPPDATA%\Adze\bin and registers COM classes and the
    SOLIDWORKS add-in under HKCU (no admin required). Idempotent.

.PARAMETER Uninstall
    Runs the uninstall script (uninstall-adze.ps1) from the same directory.

.PARAMETER SkipBuild
    Dev mode only: skip MSBuild and use whatever is already in the build output.

.EXAMPLE
    # From the repo root (dev mode):
    powershell.exe -NoProfile -File install\install-adze.ps1

    # From a packaged zip (release mode):
    powershell.exe -NoProfile -File install-adze.ps1

    # Uninstall:
    powershell.exe -NoProfile -File install\install-adze.ps1 -Uninstall
#>
param(
    [switch]$Uninstall,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Uninstall shortcut
# ---------------------------------------------------------------------------
if ($Uninstall) {
    $uninstallScript = Join-Path $PSScriptRoot "uninstall-adze.ps1"
    if (Test-Path $uninstallScript) {
        Write-Host "[Adze] Delegating to uninstall script..."
        & $uninstallScript
        exit $LASTEXITCODE
    }
    else {
        Write-Host "[Adze] ERROR: Uninstall script not found at: $uninstallScript" -ForegroundColor Red
        exit 1
    }
}

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
$requiredDlls = @(
    "Adze.Host.dll",
    "Adze.Broker.dll",
    "Adze.Tools.dll",
    "Adze.Trace.dll",
    "Adze.Contracts.dll",
    "Adze.Index.dll"
)

$installDir          = Join-Path $env:LOCALAPPDATA "Adze\bin"
$classesRoot         = "HKCU\Software\Classes"
$solidWorksRoot      = "HKCU\Software\SolidWorks"
$implementedCategory = "{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"
$runtimeVersion      = "v4.0.30319"
$addInGuid           = "{A2E09EE4-BB43-4A0C-945F-14711F792EFA}"
$taskPaneGuid        = "{F4068202-600A-4D6F-973B-DA2048A949CF}"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Adze for SOLIDWORKS - Installer"       -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# Detect mode: dev (DLLs in build output) vs release (DLLs beside script)
# ---------------------------------------------------------------------------
$devMode = $false
$dllSourceDir = $PSScriptRoot

# Check if running from install\ inside a repo (parent contains Adze.sln)
$repoRoot = Split-Path -Parent $PSScriptRoot
$slnPath  = Join-Path $repoRoot "Adze.sln"
if (Test-Path $slnPath) {
    $devMode = $true
    Write-Host "[Adze] Dev mode: repo root detected at $repoRoot" -ForegroundColor Yellow

    if (-not $SkipBuild) {
        Write-Host "[Adze] Building solution (Debug)..." -ForegroundColor Yellow
        $msBuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
        if (-not (Test-Path $msBuild)) {
            # Fallback: search PATH
            $msBuild = "MSBuild.exe"
        }
        & $msBuild $slnPath /t:Build /p:Configuration=Debug /v:minimal /nologo 2>&1 |
            Where-Object { $_ -match "error|warning|succeeded|failed" -or $_ -match "^Build" } |
            ForEach-Object { Write-Host "    $_" }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[Adze] ERROR: Build failed. Fix errors above and retry." -ForegroundColor Red
            exit 1
        }
        Write-Host "[Adze] Build succeeded." -ForegroundColor Green
        Write-Host ""
    }

    # Locate Debug output dir
    $debugDir = Join-Path $repoRoot "src\Adze.Host\bin\Debug"
    if (-not (Test-Path $debugDir)) {
        Write-Host "[Adze] ERROR: Debug output not found at $debugDir" -ForegroundColor Red
        Write-Host "       Run the build first: pwsh -NoProfile -File scripts\setup\build-all.ps1" -ForegroundColor Red
        exit 1
    }
    $dllSourceDir = $debugDir
}

# ---------------------------------------------------------------------------
# Pre-flight checks
# ---------------------------------------------------------------------------
Write-Host "[Adze] Running pre-flight checks..." -ForegroundColor Yellow

# .NET Framework 4.8+
Write-Host "  Checking .NET Framework 4.8+ ..."
$ndpKey     = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
$ndpKey32   = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\NET Framework Setup\NDP\v4\Full"
$ndpRelease = $null
foreach ($key in @($ndpKey, $ndpKey32)) {
    if (Test-Path $key) {
        $val = (Get-ItemProperty -Path $key -Name Release -ErrorAction SilentlyContinue).Release
        if ($null -ne $val) { $ndpRelease = $val; break }
    }
}
if ($null -eq $ndpRelease -or $ndpRelease -lt 528040) {
    $releaseStr = if ($null -eq $ndpRelease) { "not found" } else { $ndpRelease.ToString() }
    Write-Host "[Adze] ERROR: .NET Framework 4.8+ required. Release value: $releaseStr" -ForegroundColor Red
    Write-Host "       Install from: https://dotnet.microsoft.com/download/dotnet-framework/net48" -ForegroundColor Red
    exit 1
}
Write-Host "    .NET Framework OK (release $ndpRelease)" -ForegroundColor Green

# SOLIDWORKS installed
Write-Host "  Checking SOLIDWORKS installation..."
$swRegistryFound = (Test-Path "HKLM:\SOFTWARE\SolidWorks") -or (Test-Path "HKLM:\SOFTWARE\WOW6432Node\SolidWorks")
$swPathFound     = (Test-Path "${env:ProgramFiles}\Dassault Systemes") -or
                   (Test-Path "${env:ProgramFiles(x86)}\Dassault Systemes")
if (-not $swRegistryFound -and -not $swPathFound) {
    Write-Host "[Adze] ERROR: SOLIDWORKS does not appear to be installed." -ForegroundColor Red
    exit 1
}
Write-Host "    SOLIDWORKS detected" -ForegroundColor Green

# Required DLLs
Write-Host "  Checking required DLLs in: $dllSourceDir ..."
$missingDlls = @()
foreach ($dll in $requiredDlls) {
    if (-not (Test-Path (Join-Path $dllSourceDir $dll))) {
        $missingDlls += $dll
    }
}
if ($missingDlls.Count -gt 0) {
    Write-Host "[Adze] ERROR: Missing DLLs:" -ForegroundColor Red
    foreach ($m in $missingDlls) { Write-Host "         - $m" -ForegroundColor Red }
    if ($devMode) {
        Write-Host "       Run the build first: pwsh -NoProfile -File scripts\setup\build-all.ps1" -ForegroundColor Red
    } else {
        Write-Host "       Ensure all DLLs are in the same folder as this script." -ForegroundColor Red
    }
    exit 1
}
Write-Host "    All DLLs found" -ForegroundColor Green
Write-Host "[Adze] Pre-flight checks passed." -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Copy DLLs to install location
# ---------------------------------------------------------------------------
Write-Host "[Adze] Copying DLLs to $installDir ..." -ForegroundColor Yellow
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Write-Host "    Created: $installDir"
}
foreach ($dll in $requiredDlls) {
    $src  = Join-Path $dllSourceDir $dll
    $dest = Join-Path $installDir    $dll
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $dest -Force
        Write-Host "    Copied $dll"
    }
}
Write-Host "[Adze] DLLs installed." -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Read assembly metadata
# ---------------------------------------------------------------------------
$hostDllPath        = Join-Path $installDir "Adze.Host.dll"
$assemblyName       = [System.Reflection.AssemblyName]::GetAssemblyName($hostDllPath)
$assemblyDescriptor = "{0}, Version={1}, Culture=neutral, PublicKeyToken=null" -f $assemblyName.Name, $assemblyName.Version
$versionKey         = $assemblyName.Version.ToString()
$codeBase           = ([System.Uri]$hostDllPath).AbsoluteUri

Write-Host "[Adze] Assembly: $assemblyDescriptor"
Write-Host "[Adze] CodeBase: $codeBase"
Write-Host ""

# ---------------------------------------------------------------------------
# Registry helpers
# ---------------------------------------------------------------------------
function Set-RegistryValue {
    param(
        [Parameter(Mandatory)][string]$Key,
        [string]$Name = "",
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][string]$Value
    )
    $args = @("add", $Key, "/f", "/t", $Type, "/d", $Value)
    if ([string]::IsNullOrWhiteSpace($Name)) { $args += "/ve" }
    else { $args += @("/v", $Name) }
    & reg.exe @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "reg.exe failed: $Key\$Name" }
}

function Register-ComClass {
    param(
        [Parameter(Mandatory)][string]$ProgId,
        [Parameter(Mandatory)][string]$Guid,
        [Parameter(Mandatory)][string]$ClassName,
        [Parameter(Mandatory)][string]$DefaultName
    )
    Set-RegistryValue -Key "$classesRoot\$ProgId"                          -Type REG_SZ    -Value $DefaultName
    Set-RegistryValue -Key "$classesRoot\$ProgId\CLSID"                    -Type REG_SZ    -Value $Guid
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid"                      -Type REG_SZ    -Value $DefaultName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32"        -Type REG_SZ    -Value "mscoree.dll"
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "ThreadingModel" -Type REG_SZ -Value "Both"
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "Class"          -Type REG_SZ -Value $ClassName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "Assembly"       -Type REG_SZ -Value $assemblyDescriptor
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "RuntimeVersion" -Type REG_SZ -Value $runtimeVersion
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "CodeBase"       -Type REG_SZ -Value $codeBase
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "Class"          -Type REG_SZ -Value $ClassName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "Assembly"       -Type REG_SZ -Value $assemblyDescriptor
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "RuntimeVersion" -Type REG_SZ -Value $runtimeVersion
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "CodeBase"       -Type REG_SZ -Value $codeBase
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\ProgId"                -Type REG_SZ -Value $ProgId
    & reg.exe add "$classesRoot\CLSID\$Guid\Implemented Categories\$implementedCategory" /f | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "reg.exe failed: Implemented Categories for $Guid" }
}

# ---------------------------------------------------------------------------
# Register COM classes (HKCU -- no admin)
# ---------------------------------------------------------------------------
Write-Host "[Adze] Registering COM classes under HKCU..." -ForegroundColor Yellow

Write-Host "  Registering AdzeAddIn ($addInGuid)..."
Register-ComClass -ProgId "Adze.Host.AddIn" -Guid $addInGuid `
    -ClassName "Adze.Host.AddIn.AdzeAddIn" -DefaultName "Adze.Host.AddIn.AdzeAddIn"
Write-Host "    AdzeAddIn registered" -ForegroundColor Green

Write-Host "  Registering TaskPaneControl ($taskPaneGuid)..."
Register-ComClass -ProgId "Adze.Host.TaskPaneControl" -Guid $taskPaneGuid `
    -ClassName "Adze.Host.UI.TaskPaneControl" -DefaultName "Adze.Host.UI.TaskPaneControl"
Write-Host "    TaskPaneControl registered" -ForegroundColor Green

Write-Host "[Adze] COM registration complete." -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Register SOLIDWORKS add-in (HKCU)
# ---------------------------------------------------------------------------
Write-Host "[Adze] Registering SOLIDWORKS add-in..." -ForegroundColor Yellow

Set-RegistryValue -Key "$solidWorksRoot\AddIns\$addInGuid"          -Type REG_DWORD -Value "1"
Set-RegistryValue -Key "$solidWorksRoot\AddIns\$addInGuid" -Name "Title"       -Type REG_SZ    -Value "Adze for SOLIDWORKS"
Set-RegistryValue -Key "$solidWorksRoot\AddIns\$addInGuid" -Name "Description" -Type REG_SZ    -Value "Native AI assistant add-in for SOLIDWORKS."
Set-RegistryValue -Key "$solidWorksRoot\AddInsStartup\$addInGuid"   -Type REG_DWORD -Value "1"

Write-Host "    Add-in registered with auto-start enabled" -ForegroundColor Green
Write-Host "[Adze] SOLIDWORKS registration complete." -ForegroundColor Green
Write-Host ""

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Adze installation complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  DLLs installed to : $installDir"
Write-Host "  Assembly version  : $versionKey"
Write-Host "  COM scope         : HKCU (no admin required)"
if ($devMode) {
    Write-Host "  Mode              : Dev (Debug build)" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "  Next steps:"
Write-Host "    1. Close and relaunch SOLIDWORKS"
Write-Host "    2. The Adze Task Pane appears automatically"
Write-Host "    3. If not: Tools > Add-Ins > enable 'Adze for SOLIDWORKS'"
Write-Host ""
Write-Host "  To uninstall: powershell.exe -NoProfile -File install\install-adze.ps1 -Uninstall"
Write-Host ""
