<#
.SYNOPSIS
    Installs the Adze SOLIDWORKS add-in for the current user.

.DESCRIPTION
    Standalone installer for beta testers. Distributed as a zip containing this
    script alongside the five Adze DLLs. Copies DLLs to a stable location under
    %LOCALAPPDATA%\Adze\bin and registers COM classes and the SOLIDWORKS add-in
    under HKCU (no admin required). Idempotent — safe to run more than once.

.PARAMETER Uninstall
    Runs the uninstall script (uninstall-adze.ps1) from the same directory, if
    it exists, and exits.

.EXAMPLE
    pwsh -NoProfile -File install-adze.ps1
    pwsh -NoProfile -File install-adze.ps1 -Uninstall
#>
param(
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

# ── Uninstall shortcut ──────────────────────────────────────────────────────
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

# ── Constants ───────────────────────────────────────────────────────────────
$requiredDlls = @(
    "Adze.Host.dll",
    "Adze.Broker.dll",
    "Adze.Tools.dll",
    "Adze.Trace.dll",
    "Adze.Contracts.dll"
)

$installDir        = Join-Path $env:LOCALAPPDATA "Adze\bin"
$classesRoot       = "HKCU\Software\Classes"
$solidWorksRoot    = "HKCU\Software\SolidWorks"
$implementedCategory = "{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"
$runtimeVersion    = "v4.0.30319"

$addInGuid         = "{A2E09EE4-BB43-4A0C-945F-14711F792EFA}"
$taskPaneGuid      = "{F4068202-600A-4D6F-973B-DA2048A949CF}"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Adze for SOLIDWORKS — Installer"       -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ═══════════════════════════════════════════════════════════════════════════
# 1. Pre-flight checks
# ═══════════════════════════════════════════════════════════════════════════
Write-Host "[Adze] Running pre-flight checks..." -ForegroundColor Yellow

# ── .NET Framework 4.8+ ────────────────────────────────────────────────────
Write-Host "  Checking .NET Framework 4.8+ ..."
$ndpKey = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
if (-not (Test-Path $ndpKey)) {
    Write-Host "[Adze] ERROR: .NET Framework 4.x is not installed." -ForegroundColor Red
    Write-Host "       Install .NET Framework 4.8 or later from:" -ForegroundColor Red
    Write-Host "       https://dotnet.microsoft.com/download/dotnet-framework/net48" -ForegroundColor Red
    exit 1
}
$ndpRelease = (Get-ItemProperty -Path $ndpKey -Name Release -ErrorAction SilentlyContinue).Release
if ($null -eq $ndpRelease -or $ndpRelease -lt 528040) {
    Write-Host "[Adze] ERROR: .NET Framework 4.8 or later is required (found release $ndpRelease)." -ForegroundColor Red
    Write-Host "       Install .NET Framework 4.8 or later from:" -ForegroundColor Red
    Write-Host "       https://dotnet.microsoft.com/download/dotnet-framework/net48" -ForegroundColor Red
    exit 1
}
Write-Host "    .NET Framework detected (release $ndpRelease)" -ForegroundColor Green

# ── SOLIDWORKS installed ───────────────────────────────────────────────────
Write-Host "  Checking SOLIDWORKS installation..."
$swRegistryFound = Test-Path "HKLM:\SOFTWARE\SolidWorks"
$swInstallPaths = @(
    "${env:ProgramFiles}\Dassault Systemes",
    "${env:ProgramFiles(x86)}\Dassault Systemes"
)
$swPathFound = $false
foreach ($p in $swInstallPaths) {
    if (Test-Path $p) { $swPathFound = $true; break }
}
if (-not $swRegistryFound -and -not $swPathFound) {
    Write-Host "[Adze] ERROR: SOLIDWORKS does not appear to be installed." -ForegroundColor Red
    Write-Host "       Expected registry key HKLM\SOFTWARE\SolidWorks or" -ForegroundColor Red
    Write-Host "       an installation directory under Program Files\Dassault Systemes." -ForegroundColor Red
    exit 1
}
Write-Host "    SOLIDWORKS installation detected" -ForegroundColor Green

# ── Required DLLs present alongside script ─────────────────────────────────
Write-Host "  Checking required DLLs in script directory..."
$missingDlls = @()
foreach ($dll in $requiredDlls) {
    $dllPath = Join-Path $PSScriptRoot $dll
    if (-not (Test-Path $dllPath)) {
        $missingDlls += $dll
    }
}
if ($missingDlls.Count -gt 0) {
    Write-Host "[Adze] ERROR: Missing required DLLs in the installer directory:" -ForegroundColor Red
    foreach ($m in $missingDlls) {
        Write-Host "         - $m" -ForegroundColor Red
    }
    Write-Host "       Ensure all DLLs are in the same folder as this script." -ForegroundColor Red
    exit 1
}
Write-Host "    All 5 required DLLs found" -ForegroundColor Green

Write-Host "[Adze] Pre-flight checks passed." -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════════════
# 2. Copy DLLs to install location
# ═══════════════════════════════════════════════════════════════════════════
Write-Host "[Adze] Copying DLLs to $installDir ..." -ForegroundColor Yellow

if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Write-Host "    Created directory: $installDir"
}

foreach ($dll in $requiredDlls) {
    $source = Join-Path $PSScriptRoot $dll
    $dest   = Join-Path $installDir $dll
    Copy-Item -Path $source -Destination $dest -Force
    Write-Host "    Copied $dll"
}

Write-Host "[Adze] DLLs installed." -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════════════
# 3. Read assembly metadata
# ═══════════════════════════════════════════════════════════════════════════
$hostDllPath   = Join-Path $installDir "Adze.Host.dll"
$assemblyName  = [System.Reflection.AssemblyName]::GetAssemblyName($hostDllPath)
$assemblyDescriptor = "{0}, Version={1}, Culture=neutral, PublicKeyToken=null" -f $assemblyName.Name, $assemblyName.Version
$versionKey    = $assemblyName.Version.ToString()
$codeBase      = ([System.Uri]$hostDllPath).AbsoluteUri

Write-Host "[Adze] Assembly: $assemblyDescriptor"
Write-Host "[Adze] CodeBase: $codeBase"
Write-Host ""

# ═══════════════════════════════════════════════════════════════════════════
# 4. Registry helpers
# ═══════════════════════════════════════════════════════════════════════════
function Set-RegistryValue {
    param(
        [Parameter(Mandatory = $true)][string]$Key,
        [string]$Name = "",
        [Parameter(Mandatory = $true)][string]$Type,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $arguments = @("add", $Key, "/f", "/t", $Type, "/d", $Value)
    if ([string]::IsNullOrWhiteSpace($Name)) {
        $arguments += "/ve"
    }
    else {
        $arguments += @("/v", $Name)
    }

    & reg.exe @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "reg.exe failed setting $Key\$Name"
    }
}

function Register-ComClass {
    param(
        [Parameter(Mandatory = $true)][string]$ProgId,
        [Parameter(Mandatory = $true)][string]$Guid,
        [Parameter(Mandatory = $true)][string]$ClassName,
        [Parameter(Mandatory = $true)][string]$DefaultName
    )

    # ProgId -> CLSID mapping
    Set-RegistryValue -Key "$classesRoot\$ProgId" -Type REG_SZ -Value $DefaultName
    Set-RegistryValue -Key "$classesRoot\$ProgId\CLSID" -Type REG_SZ -Value $Guid

    # CLSID registration
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid" -Type REG_SZ -Value $DefaultName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Type REG_SZ -Value "mscoree.dll"
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "ThreadingModel" -Type REG_SZ -Value "Both"
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "Class" -Type REG_SZ -Value $ClassName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "Assembly" -Type REG_SZ -Value $assemblyDescriptor
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "RuntimeVersion" -Type REG_SZ -Value $runtimeVersion
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32" -Name "CodeBase" -Type REG_SZ -Value $codeBase

    # Versioned InprocServer32 subkey
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "Class" -Type REG_SZ -Value $ClassName
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "Assembly" -Type REG_SZ -Value $assemblyDescriptor
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "RuntimeVersion" -Type REG_SZ -Value $runtimeVersion
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\InprocServer32\$versionKey" -Name "CodeBase" -Type REG_SZ -Value $codeBase

    # ProgId back-reference
    Set-RegistryValue -Key "$classesRoot\CLSID\$Guid\ProgId" -Type REG_SZ -Value $ProgId

    # Implemented Categories
    & reg.exe add "$classesRoot\CLSID\$Guid\Implemented Categories\$implementedCategory" /f | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "reg.exe failed adding implemented category for $Guid"
    }
}

# ═══════════════════════════════════════════════════════════════════════════
# 5. Register COM classes (HKCU — no admin)
# ═══════════════════════════════════════════════════════════════════════════
Write-Host "[Adze] Registering COM classes under HKCU..." -ForegroundColor Yellow

Write-Host "  Registering AdzeAddIn ($addInGuid)..."
Register-ComClass `
    -ProgId "Adze.Host.AddIn" `
    -Guid $addInGuid `
    -ClassName "Adze.Host.AddIn.AdzeAddIn" `
    -DefaultName "Adze.Host.AddIn.AdzeAddIn"
Write-Host "    AdzeAddIn registered" -ForegroundColor Green

Write-Host "  Registering TaskPaneControl ($taskPaneGuid)..."
Register-ComClass `
    -ProgId "Adze.Host.TaskPaneControl" `
    -Guid $taskPaneGuid `
    -ClassName "Adze.Host.UI.TaskPaneControl" `
    -DefaultName "Adze.Host.UI.TaskPaneControl"
Write-Host "    TaskPaneControl registered" -ForegroundColor Green

Write-Host "[Adze] COM registration complete." -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════════════
# 6. Register SOLIDWORKS add-in (HKCU)
# ═══════════════════════════════════════════════════════════════════════════
Write-Host "[Adze] Registering SOLIDWORKS add-in..." -ForegroundColor Yellow

Set-RegistryValue `
    -Key "$solidWorksRoot\AddIns\$addInGuid" `
    -Type REG_DWORD -Value "1"
Set-RegistryValue `
    -Key "$solidWorksRoot\AddIns\$addInGuid" `
    -Name "Title" -Type REG_SZ -Value "Adze for SOLIDWORKS"
Set-RegistryValue `
    -Key "$solidWorksRoot\AddIns\$addInGuid" `
    -Name "Description" -Type REG_SZ -Value "Native AI assistant add-in for SOLIDWORKS."

Set-RegistryValue `
    -Key "$solidWorksRoot\AddInsStartup\$addInGuid" `
    -Type REG_DWORD -Value "1"

Write-Host "    Add-in registered with auto-start enabled" -ForegroundColor Green
Write-Host "[Adze] SOLIDWORKS registration complete." -ForegroundColor Green
Write-Host ""

# ═══════════════════════════════════════════════════════════════════════════
# 7. Done
# ═══════════════════════════════════════════════════════════════════════════
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Adze installation completed!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  DLLs installed to:  $installDir"
Write-Host "  Assembly version:   $versionKey"
Write-Host "  COM scope:          HKCU (no admin required)"
Write-Host ""
Write-Host "  Next steps:"
Write-Host "    1. Launch SOLIDWORKS"
Write-Host "    2. The Adze task pane should appear automatically"
Write-Host "    3. If not, check Tools > Add-Ins and enable 'Adze for SOLIDWORKS'"
Write-Host ""
Write-Host "  To uninstall, run:  pwsh -NoProfile -File uninstall-adze.ps1"
Write-Host ""
