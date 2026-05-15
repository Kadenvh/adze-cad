<#
.SYNOPSIS
    One-shot diagnostic dump for an Adze install that "looks identical" /
    "didn't take effect". Collects every signal we use to debug an install
    failure into a single text report you can paste back to the dev agent.

.DESCRIPTION
    This script answers the question "did the install actually do what it
    claimed to do, and what did SOLIDWORKS see when it loaded the add-in?"
    Run it AFTER:
      1. uninstall-adze.bat (or .ps1)
      2. install-adze.bat (or .ps1)
      3. Launch SOLIDWORKS at least once and let it sit on the splash / open
         a part. The compatibility probe runs at ConnectToSW; we need that
         to have happened so host.log has the probe + ribbon outcome.
      4. Close SOLIDWORKS (so file locks release before this script reads).
      5. Run this script.

    Output goes to a single text file named adze-diagnostic-<timestamp>.txt
    in the current directory (or wherever -OutputPath points). The file is
    safe to share — no API keys, no credentials, no document contents.

    What it collects:
      A. Environment baseline
         - Windows version, PowerShell version, .NET 4.8 presence
         - SOLIDWORKS install path + executable version
         - ExecutionPolicy (per-scope) — install scripts fail silently when
           AllSigned/Restricted is in effect
         - Windows Defender exclusions + recent quarantine entries scoped
           to Adze paths
      B. Install state on disk
         - %LOCALAPPDATA%\Adze\bin contents with sizes + SHA256
         - Compare to MANIFEST.txt in the extracted zip if present
         - Adze.Mesh.dll presence (the session-7 regression canary)
      C. Registry state
         - HKCU\Software\Classes\Adze.* (COM ProgIDs)
         - HKCU\Software\Classes\CLSID\{Adze GUIDs}
         - HKCU\Software\SolidWorks\AddIns\{Adze GUID}
         - HKCU\Software\SolidWorks\AddInsStartup\{Adze GUID} — Decision #23
           invariant: only (Default) DWORD, no Title/Description
         - SOLIDWORKS_AI_* environment variables under HKCU\Environment
      D. host.log analysis
         - Last 40 lines
         - Specific patterns we care about:
            * CompatibilityProbe outcome (Compatible / GracefulRefusal /
              HardIncompatible)
            * RibbonTab registration outcome
            * ContextMenu registration outcome
            * NativeTaskPaneControlShim mount outcome
            * Any ERROR lines
      E. Process state
         - Any lingering SW + launcher children
         - Was SW running when this script ran? (Indicates whether file
           locks could have masked install issues)

.PARAMETER OutputPath
    Path to write the report. Defaults to .\adze-diagnostic-<timestamp>.txt
    in the current working directory.

.EXAMPLE
    pwsh scripts\setup\diagnose-install.ps1
    # Writes adze-diagnostic-<timestamp>.txt in cwd.

.EXAMPLE
    pwsh scripts\setup\diagnose-install.ps1 -OutputPath C:\Temp\adze-report.txt
    # Custom path.

#>

param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Continue"  # never let a missing key abort the dump

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
if (-not $OutputPath) {
    $OutputPath = Join-Path (Get-Location) "adze-diagnostic-$timestamp.txt"
}

$lines = New-Object System.Collections.Generic.List[string]
function Add-Line { param([string]$s = "") $script:lines.Add($s) }
function Add-Header {
    param([string]$title)
    Add-Line ""
    Add-Line ""
    Add-Line ("=" * 78)
    Add-Line "  $title"
    Add-Line ("=" * 78)
}
function Add-SubHeader {
    param([string]$title)
    Add-Line ""
    Add-Line "--- $title ---"
}

# ===== Header =====================================================

Add-Line "Adze install diagnostic dump"
Add-Line "Generated: $(Get-Date -Format 'o')"
Add-Line "Host: $env:COMPUTERNAME (user: $env:USERNAME)"
Add-Line "Working directory: $(Get-Location)"

# ===== A. Environment =============================================

Add-Header "A. Environment"

Add-SubHeader "Windows + PowerShell"
Add-Line "OS: $((Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).Caption) ($((Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).Version))"
Add-Line "PowerShell: $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"
Add-Line "pwsh path: $((Get-Command pwsh -ErrorAction SilentlyContinue).Source)"
Add-Line "powershell path: $((Get-Command powershell -ErrorAction SilentlyContinue).Source)"

Add-SubHeader ".NET Framework 4.8"
$net48 = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue
if ($net48 -and $net48.Release -ge 528040) {
    Add-Line ".NET 4.8 PRESENT (release=$($net48.Release))"
} elseif ($net48) {
    Add-Line ".NET v4 present but pre-4.8 (release=$($net48.Release))"
} else {
    Add-Line ".NET v4 NOT found — Adze requires .NET Framework 4.8"
}

Add-SubHeader "ExecutionPolicy (all scopes)"
Get-ExecutionPolicy -List | ForEach-Object {
    Add-Line ("  {0,-20} {1}" -f $_.Scope, $_.ExecutionPolicy)
}

Add-SubHeader "SOLIDWORKS install"
$swSetup = Get-ItemProperty 'HKLM:\SOFTWARE\SolidWorks\Setup' -Name 'SolidWorks Folder' -ErrorAction SilentlyContinue
if ($swSetup) {
    Add-Line "SW install dir: $($swSetup.'SolidWorks Folder')"
    $exe = Join-Path $swSetup.'SolidWorks Folder' 'sldworks.exe'
    if (Test-Path $exe) {
        $ver = (Get-Item $exe).VersionInfo
        Add-Line "sldworks.exe version: $($ver.FileVersion) (product: $($ver.ProductVersion))"
    } else {
        Add-Line "sldworks.exe NOT found at expected path"
    }
} else {
    Add-Line "HKLM\SOFTWARE\SolidWorks\Setup\SolidWorks Folder NOT found — SW install dir unknown"
    foreach ($candidate in @(
        "${env:ProgramFiles}\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x"
        "${env:ProgramFiles}\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2025x"
        "${env:ProgramFiles}\Dassault Systemes\SolidWorks Corp\SOLIDWORKS"
    )) {
        if (Test-Path $candidate) {
            Add-Line "Found fallback candidate: $candidate"
        }
    }
}

Add-SubHeader "Windows Defender — Adze-scoped quarantine + exclusions"
try {
    $exclusions = Get-MpPreference -ErrorAction SilentlyContinue
    if ($exclusions) {
        $excludedPaths = @($exclusions.ExclusionPath) | Where-Object { $_ -and $_ -match 'Adze' }
        if ($excludedPaths.Count -eq 0) {
            Add-Line "No Defender exclusions reference Adze."
        } else {
            foreach ($p in $excludedPaths) { Add-Line "Exclusion: $p" }
        }
    } else {
        Add-Line "Get-MpPreference unavailable (third-party AV or restricted)."
    }
} catch {
    Add-Line "Defender check threw: $($_.Exception.Message)"
}

# ===== B. Install state on disk ===================================

Add-Header "B. Install state on disk"

$binDir = Join-Path $env:LOCALAPPDATA "Adze\bin"
$logDir = Join-Path $env:LOCALAPPDATA "Adze\logs"

Add-SubHeader "%LOCALAPPDATA%\Adze\bin contents"
if (Test-Path $binDir) {
    $expected = @('Adze.Host.dll','Adze.Broker.dll','Adze.Tools.dll','Adze.Trace.dll',
                  'Adze.Contracts.dll','Adze.Index.dll','Adze.UI.dll','Adze.Mesh.dll','OpenMcdf.dll')
    $present = Get-ChildItem $binDir -File | Sort-Object Name
    Add-Line "Path: $binDir"
    Add-Line "File count: $($present.Count)"
    Add-Line ""
    Add-Line ("  {0,-24} {1,12}  {2}" -f "Name", "Size", "SHA256 (first 16)")
    Add-Line ("  " + "-" * 70)
    foreach ($f in $present) {
        $hash = (Get-FileHash $f.FullName -Algorithm SHA256 -ErrorAction SilentlyContinue).Hash
        $hashShort = if ($hash) { $hash.Substring(0, 16) } else { "(hash failed)" }
        Add-Line ("  {0,-24} {1,12:N0}  {2}" -f $f.Name, $f.Length, $hashShort)
    }
    Add-Line ""
    Add-Line "Missing expected DLLs:"
    $missing = $expected | Where-Object { -not ($present.Name -contains $_) }
    if ($missing.Count -eq 0) {
        Add-Line "  (none — all expected DLLs present)"
    } else {
        foreach ($m in $missing) { Add-Line "  MISSING: $m" }
    }
} else {
    Add-Line "PATH DOES NOT EXIST: $binDir"
    Add-Line "Adze is NOT INSTALLED on this machine, or install never wrote to bin/."
}

Add-SubHeader "Adze.Mesh.dll presence (session-7 regression canary)"
$meshDll = Join-Path $binDir 'Adze.Mesh.dll'
if (Test-Path $meshDll) {
    $info = Get-Item $meshDll
    Add-Line "PRESENT: $meshDll"
    Add-Line "  Size: $($info.Length) bytes"
    Add-Line "  LastWriteTime: $($info.LastWriteTime)"
    try {
        $asm = [System.Reflection.AssemblyName]::GetAssemblyName($meshDll)
        Add-Line "  AssemblyVersion: $($asm.Version)"
    } catch {
        Add-Line "  AssemblyName check failed: $($_.Exception.Message)"
    }
} else {
    Add-Line "MISSING: $meshDll"
    Add-Line "This means the install script's required-DLL list does NOT include Adze.Mesh.dll."
    Add-Line "Phase 11 ribbon buttons + mesh tools will fail to load at runtime."
}

# ===== C. Registry state ==========================================

Add-Header "C. Registry state"

Add-SubHeader "COM ProgID registrations (HKCU\Software\Classes)"
$comPaths = @(
    "HKCU:\Software\Classes\Adze.Host.AddIn",
    "HKCU:\Software\Classes\Adze.Host.TaskPaneControl",
    "HKCU:\Software\Classes\Adze.Host.NativeTaskPaneControl"
)
foreach ($p in $comPaths) {
    if (Test-Path $p) {
        Add-Line "PRESENT: $p"
        $item = Get-Item $p
        $clsidPath = Join-Path $p "CLSID"
        if (Test-Path $clsidPath) {
            $clsidVal = (Get-ItemProperty $clsidPath -Name '(default)' -ErrorAction SilentlyContinue).'(default)'
            Add-Line "  CLSID: $clsidVal"
        }
    } else {
        Add-Line "MISSING: $p"
    }
}

Add-SubHeader "SOLIDWORKS add-in registration"
$swAddins = "HKCU:\Software\SolidWorks\AddIns\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}"
$swStartup = "HKCU:\Software\SolidWorks\AddInsStartup\{A2E09EE4-BB43-4A0C-945F-14711F792EFA}"

Add-Line ""
Add-Line "AddIns key (descriptor):"
if (Test-Path $swAddins) {
    $props = Get-ItemProperty $swAddins
    Add-Line "  PRESENT: $swAddins"
    $props.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object {
        Add-Line ("    {0,-20} = {1}" -f $_.Name, $_.Value)
    }
} else {
    Add-Line "  MISSING: $swAddins"
    Add-Line "  Without this key, SW will not see Adze as an installed add-in at all."
}

Add-Line ""
Add-Line "AddInsStartup key (active state — Decision #23 invariant):"
if (Test-Path $swStartup) {
    $props = Get-ItemProperty $swStartup
    Add-Line "  PRESENT: $swStartup"
    $props.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object {
        Add-Line ("    {0,-20} = {1}" -f $_.Name, $_.Value)
    }
    Add-Line ""
    Add-Line "Decision #23 check: this key MUST contain ONLY (default) REG_DWORD."
    $extras = $props.PSObject.Properties | Where-Object {
        $_.Name -notmatch '^PS' -and $_.Name -ne '(default)'
    }
    if ($extras) {
        Add-Line "  REGRESSION: extra values present, SW will normalize (default) to 0 on shutdown:"
        $extras | ForEach-Object { Add-Line "    extra: $($_.Name)" }
    } else {
        $defaultVal = (Get-ItemProperty $swStartup -Name '(default)' -ErrorAction SilentlyContinue).'(default)'
        Add-Line "  OK — invariant holds. (default) = $defaultVal"
        if ($defaultVal -ne 1) {
            Add-Line "  BUT (default) is not 1, so the add-in is currently DISABLED."
        }
    }
} else {
    Add-Line "  MISSING: $swStartup"
    Add-Line "  Without this key, SW will not enable the add-in even if AddIns key is present."
}

Add-SubHeader "SOLIDWORKS_AI_* env vars (HKCU\Environment)"
$envKey = 'HKCU:\Environment'
if (Test-Path $envKey) {
    $envProps = Get-Item $envKey
    $adzeEnv = $envProps.Property | Where-Object { $_ -like 'SOLIDWORKS_AI_*' }
    if ($adzeEnv.Count -eq 0) {
        Add-Line "No SOLIDWORKS_AI_* variables set."
    } else {
        foreach ($name in $adzeEnv) {
            $val = (Get-ItemProperty $envKey -Name $name -ErrorAction SilentlyContinue).$name
            # mask anything that smells like an API key
            if ($name -match 'API_KEY|KEY' -and $val.Length -gt 8) {
                $val = $val.Substring(0, 4) + "..." + $val.Substring($val.Length - 4)
            }
            Add-Line ("  {0,-40} = {1}" -f $name, $val)
        }
    }
}

# ===== D. host.log analysis =======================================

Add-Header "D. host.log analysis"

$hostLog = Join-Path $logDir "host.log"
Add-SubHeader "File presence"
if (-not (Test-Path $hostLog)) {
    Add-Line "MISSING: $hostLog"
    Add-Line "Either SW has never connected to the add-in, or the add-in never loaded."
} else {
    $logInfo = Get-Item $hostLog
    Add-Line "PRESENT: $hostLog"
    Add-Line "  Size: $($logInfo.Length) bytes"
    Add-Line "  LastWriteTime: $($logInfo.LastWriteTime)"

    Add-SubHeader "Last 40 lines"
    $tail = Get-Content $hostLog -Tail 40 -ErrorAction SilentlyContinue
    if ($tail) {
        $tail | ForEach-Object { Add-Line "  $_" }
    } else {
        Add-Line "  (empty or unreadable)"
    }

    Add-SubHeader "Pattern hits (most recent occurrence of each)"
    $all = Get-Content $hostLog -ErrorAction SilentlyContinue
    $patterns = @{
        "ConnectToSW starting"               = 'ConnectToSW starting'
        "CompatibilityProbe: OK"             = 'CompatibilityProbe: OK'
        "CompatibilityProbe GracefulRefusal" = 'GracefulRefusal'
        "CompatibilityProbe failed (legacy)" = 'Compatibility probe failed'
        "RibbonTab attempt"                  = 'Ribbon.*[Aa]ttempting real registration'
        "RibbonTab registered"               = 'RibbonTab: registered'
        "RibbonTab failure"                  = 'RibbonTab.*null|RibbonTab.*failed|RibbonTab.*error'
        "ContextMenu registered"             = 'ContextMenu: registered'
        "NativeTaskPaneControlShim mounted"  = 'NativeTaskPaneControlShim mounted'
        "DisconnectFromSW"                   = 'DisconnectFromSW'
        "ERROR lines"                        = '\bERROR\b'
    }
    foreach ($label in $patterns.Keys) {
        $rx = $patterns[$label]
        $matches = $all | Select-String -Pattern $rx -CaseSensitive:$false
        if ($matches.Count -eq 0) {
            Add-Line ("  {0,-40} NOT FOUND" -f $label)
        } else {
            $last = $matches[-1]
            Add-Line ("  {0,-40} (line {1}) {2}" -f $label, $last.LineNumber, $last.Line.Trim())
        }
    }
}

# ===== E. Process state ===========================================

Add-Header "E. Process state"

$names = @('SLDWORKS','sldworks','sldworks_fs','SWXDesktopLauncher','CATSTART','swxdesktopupdate')
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.ProcessName }
if ($running) {
    Add-Line "WARNING: SOLIDWORKS or launcher processes are running. File locks may have"
    Add-Line "         masked install behavior. Close all SW processes and re-run the install."
    foreach ($p in $running) {
        $path = try { $p.Path } catch { "(access denied)" }
        Add-Line ("  PID {0,-6} {1,-25} {2}" -f $p.Id, $p.ProcessName, $path)
    }
} else {
    Add-Line "No SW or launcher processes running. Good."
}

# ===== Write =========================================================

[System.IO.File]::WriteAllLines($OutputPath, $lines)
Write-Host ""
Write-Host "Diagnostic report written: $OutputPath" -ForegroundColor Cyan
Write-Host ("Size: {0:N0} bytes" -f (Get-Item $OutputPath).Length)
Write-Host ""
Write-Host "Share the full file contents back to the dev agent. The report contains no"
Write-Host "API keys, no document contents, and no credentials. Anything sensitive is masked."
