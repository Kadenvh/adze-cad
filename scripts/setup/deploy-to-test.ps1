<#
.SYNOPSIS
    One-command deploy from the dev box to a second Windows machine that
    runs SOLIDWORKS, for live add-in testing without contaminating the dev
    environment.

.DESCRIPTION
    The dev/test loop on a single machine is hostile to Adze: SW MMAP's
    the add-in DLLs at ConnectToSW so reinstalls are no-ops until full
    SW restart; launcher child processes survive File→Exit and hold file
    locks; HKCU registry + %LOCALAPPDATA% state accumulates and masks
    issues that a fresh install would surface. Moving the test loop to
    a second machine eliminates classes (1)-(4) of those costs entirely.

    Pipeline:
      1. Validate prerequisites locally (zip exists, ssh present)
      2. Optionally rebuild + repackage via package-release.ps1
      3. scp the zip + install/uninstall scripts to the target
      4. ssh into the target: extract zip, run uninstall-adze.ps1,
         then run install-adze.ps1
      5. Optionally tail host.log on the target after install

    Authentication is via OpenSSH key-based auth. Password prompts will
    work but break the "one command" promise of this script. Set up your
    SSH key on the target first (see TARGET SETUP below).

.PARAMETER TargetHost
    DNS name or IP of the test machine. REQUIRED.

.PARAMETER TargetUser
    SSH username on the target. Defaults to the local user.

.PARAMETER RemoteRoot
    Working directory on the target where the zip is extracted.
    Defaults to C:\adze-test. Cleaned and re-created each run.

.PARAMETER SkipBuild
    Reuse the existing install/adze-v*.zip rather than rebuilding.
    The most recent zip in install/ is selected automatically.

.PARAMETER Clean
    Pass -RemoveUserData to uninstall-adze.ps1 on the target. Wipes
    %LOCALAPPDATA%\Adze and clears all SOLIDWORKS_AI_* env vars.
    Use this when you want a true "fresh customer" test.

.PARAMETER TailLog
    After install, tail %LOCALAPPDATA%\Adze\logs\host.log on the target
    via SSH and stream output to the local console. Useful for
    watching ConnectToSW outcomes when the founder launches SW.
    Ctrl-C to stop.

.PARAMETER DryRun
    Print the commands that would run without executing them. Useful
    for verifying argument substitution / paths.

.EXAMPLE
    pwsh scripts\setup\deploy-to-test.ps1 -TargetHost test-box
    # Build, package, scp, install on test-box (no-clean, no-tail).

.EXAMPLE
    pwsh scripts\setup\deploy-to-test.ps1 -TargetHost 10.0.0.5 -Clean -TailLog
    # Full clean install + tail host.log so you can watch the ribbon
    # registration outcome live.

.EXAMPLE
    pwsh scripts\setup\deploy-to-test.ps1 -TargetHost test-box -SkipBuild
    # Reuse the latest zip without rebuilding. Useful for re-installing
    # the same artifact on a different target.

TARGET SETUP (one-time, on the test machine):

    1. Install OpenSSH Server:
       Settings → Apps → Optional features → Add a feature → "OpenSSH Server"
       Then: Set-Service sshd -StartupType Automatic; Start-Service sshd

    2. Allow inbound TCP/22 in Windows Firewall (the installer does this
       automatically on most builds).

    3. From the DEV box, copy your public key to the target:
       ssh-copy-id user@target-box      # if ssh-copy-id is available
       # or manually append id_ed25519.pub to %USERPROFILE%\.ssh\authorized_keys
       # on the target.

    4. Test: ssh user@target-box "hostname"  — should print without prompt.

    5. Set PowerShell as the default SSH shell on the target (optional
       but makes the install scripts work without explicit pwsh prefixes):
       New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' `
         -Name DefaultShell -Value 'C:\Program Files\PowerShell\7\pwsh.exe' `
         -PropertyType String -Force
       # Falls back to powershell.exe if pwsh isn't installed.

    6. Install SOLIDWORKS on the target (Maker subscription is fine).

#>

param(
    [Parameter(Mandatory = $true)]
    [string]$TargetHost,

    [string]$TargetUser = $env:USERNAME,

    [string]$RemoteRoot = 'C:\adze-test',

    [switch]$SkipBuild,

    [switch]$Clean,

    [switch]$TailLog,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$repoRoot   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$installDir = Join-Path $repoRoot 'install'
$target     = "$TargetUser@$TargetHost"

function Write-Step([string]$msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  $msg" -ForegroundColor Green }
function Write-Warn2([string]$msg) { Write-Host "  $msg" -ForegroundColor Yellow }
function Write-Fail([string]$msg) { Write-Host "  $msg" -ForegroundColor Red }

function Invoke-Step {
    param([string]$Description, [scriptblock]$Action)
    Write-Step ""
    Write-Step "=== $Description ==="
    if ($DryRun) {
        Write-Warn2 "(dry-run — would execute the step above)"
        return
    }
    & $Action
}

function Invoke-Ssh {
    param([string]$Command, [switch]$IgnoreErrors)
    $args = @($target, $Command)
    if ($DryRun) {
        Write-Warn2 "ssh $($args -join ' ')"
        return ""
    }
    $output = & ssh @args 2>&1
    if ($LASTEXITCODE -ne 0 -and -not $IgnoreErrors) {
        throw "ssh command failed (exit $LASTEXITCODE): $Command"
    }
    return $output
}

function Invoke-Scp {
    param([string]$LocalPath, [string]$RemotePath)
    if ($DryRun) {
        Write-Warn2 "scp `"$LocalPath`" `"${target}:$RemotePath`""
        return
    }
    & scp $LocalPath "${target}:$RemotePath"
    if ($LASTEXITCODE -ne 0) {
        throw "scp failed (exit $LASTEXITCODE): $LocalPath → ${target}:$RemotePath"
    }
}

# --- Step 0: Local prerequisites --------------------------------------------

Invoke-Step "Step 0: Local prerequisites" {
    $sshExists = Get-Command ssh -ErrorAction SilentlyContinue
    $scpExists = Get-Command scp -ErrorAction SilentlyContinue
    if (-not $sshExists -or -not $scpExists) {
        throw "OpenSSH client not found. Install via: Settings → Apps → Optional features → 'OpenSSH Client'."
    }
    Write-OK "ssh + scp available locally"
    Write-OK "Target: $target → $RemoteRoot"
    if ($Clean) { Write-OK "Clean install: -RemoveUserData will be passed to uninstall on the target" }
    if ($TailLog) { Write-OK "host.log tail will start after install (Ctrl-C to stop)" }
}

# --- Step 1: Verify SSH reachability ----------------------------------------

Invoke-Step "Step 1: Verify SSH reachability" {
    $output = Invoke-Ssh "hostname"
    if (-not $DryRun) {
        Write-OK "ssh reachable. Target hostname: $($output | Out-String).Trim()"
    }
}

# --- Step 2: Build + package locally (unless -SkipBuild) --------------------

$zipPath = $null
Invoke-Step "Step 2: Build + package" {
    $packageScript = Join-Path $installDir 'package-release.ps1'
    if ($SkipBuild) {
        # Pick the most-recently-modified adze-v*.zip in install/
        $candidates = Get-ChildItem (Join-Path $installDir 'adze-v*.zip') -ErrorAction SilentlyContinue |
                      Sort-Object LastWriteTime -Descending
        if ($candidates.Count -eq 0) {
            throw "No adze-v*.zip in $installDir and -SkipBuild was set. Drop -SkipBuild to build fresh."
        }
        $script:zipPath = $candidates[0].FullName
        Write-OK "Reusing existing zip: $($candidates[0].Name) ($([math]::Round($candidates[0].Length / 1KB, 1)) KB)"
    } else {
        if ($DryRun) {
            Write-Warn2 "pwsh $packageScript"
        } else {
            & pwsh -NoProfile -File $packageScript
            if ($LASTEXITCODE -ne 0) { throw "package-release.ps1 failed (exit $LASTEXITCODE)" }
        }
        # After packaging, pick the latest zip again.
        $latest = Get-ChildItem (Join-Path $installDir 'adze-v*.zip') -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $latest) { throw "Packaging completed but no zip found." }
        $script:zipPath = $latest.FullName
        Write-OK "Packaged: $($latest.Name)"
    }
}

# --- Step 3: Prepare remote dir ---------------------------------------------

Invoke-Step "Step 3: Prepare remote directory" {
    # Clean and recreate the remote staging dir. Using cmd.exe via SSH because
    # it's the lowest common denominator (sshd's default shell can be cmd or
    # PowerShell; both understand mkdir/rmdir via cmd /c).
    $remoteRootEscaped = $RemoteRoot.Replace('\','\\')
    Invoke-Ssh "powershell -NoProfile -Command `"if (Test-Path '$RemoteRoot') { Remove-Item -Recurse -Force '$RemoteRoot' }; New-Item -ItemType Directory -Force -Path '$RemoteRoot' | Out-Null; Write-Host 'remote dir ready'`"" | Out-Null
    Write-OK "Remote dir cleaned and recreated: $RemoteRoot"
}

# --- Step 4: Copy zip to target ---------------------------------------------

Invoke-Step "Step 4: scp zip → target" {
    Invoke-Scp $zipPath "$RemoteRoot/"
    Write-OK "Uploaded: $(Split-Path $zipPath -Leaf)"
}

# --- Step 5: Extract on target ----------------------------------------------

Invoke-Step "Step 5: Extract zip on target" {
    $zipLeaf = Split-Path $zipPath -Leaf
    $cmd = "powershell -NoProfile -Command `"Expand-Archive -Path '$RemoteRoot\$zipLeaf' -DestinationPath '$RemoteRoot' -Force; Get-ChildItem '$RemoteRoot' -File | Select-Object -ExpandProperty Name`""
    $output = Invoke-Ssh $cmd
    if (-not $DryRun) {
        Write-OK "Extracted to ${RemoteRoot}:"
        ($output | Out-String) -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { Write-Host "    $_" }
    }
}

# --- Step 6: Uninstall existing Adze (idempotent) ---------------------------

Invoke-Step "Step 6: Uninstall existing Adze on target" {
    $removeUserData = if ($Clean) { '-RemoveUserData' } else { '' }
    $cmd = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$RemoteRoot\uninstall-adze.ps1`" $removeUserData"
    # Uninstall is idempotent — never fail the deploy on a no-op uninstall.
    $output = Invoke-Ssh $cmd -IgnoreErrors
    if (-not $DryRun) {
        $output | ForEach-Object { Write-Host "    $_" }
        Write-OK "Uninstall step complete"
    }
}

# --- Step 7: Install fresh on target ----------------------------------------

Invoke-Step "Step 7: Install Adze on target" {
    $cmd = "powershell -NoProfile -ExecutionPolicy Bypass -File `"$RemoteRoot\install-adze.ps1`""
    $output = Invoke-Ssh $cmd
    if (-not $DryRun) {
        $output | ForEach-Object { Write-Host "    $_" }
        Write-OK "Install complete"
    }
}

# --- Step 8: Sanity-check installed bin -------------------------------------

Invoke-Step "Step 8: Verify installed binaries on target" {
    $cmd = "powershell -NoProfile -Command `"Get-ChildItem `$env:LOCALAPPDATA\Adze\bin -File | Sort-Object Name | Select-Object Name, Length | Format-Table -AutoSize | Out-String`""
    $output = Invoke-Ssh $cmd
    if (-not $DryRun) {
        ($output | Out-String) -split "`n" | Where-Object { $_.Trim() } | ForEach-Object { Write-Host "    $_" }
    }
}

# --- Step 9: Optional log tail ----------------------------------------------

if ($TailLog -and -not $DryRun) {
    Invoke-Step "Step 9: Tail host.log (Ctrl-C to stop)" {
        Write-Warn2 "Launch SOLIDWORKS on the target now. ConnectToSW output will stream here."
        Write-Warn2 ""
        # Wait-for-host.log loop + tail. host.log doesn't exist until the
        # add-in writes to it (first ConnectToSW), so we wait up to 60s.
        $tailCmd = @'
powershell -NoProfile -Command "
  $log = Join-Path $env:LOCALAPPDATA 'Adze\logs\host.log';
  $deadline = (Get-Date).AddSeconds(60);
  while (-not (Test-Path $log) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 };
  if (-not (Test-Path $log)) { Write-Host '(no host.log yet — SW not launched)'; exit 0 };
  Get-Content $log -Wait -Tail 0
"
'@
        & ssh $target $tailCmd
    }
}

# --- Summary ----------------------------------------------------------------

Write-Step ""
Write-Step "=== Deploy complete ==="
Write-OK "Target: $target"
if ($zipPath) {
    Write-OK "Installed zip: $(Split-Path $zipPath -Leaf)"
}
if ($DryRun) {
    Write-Warn2 "Dry-run only — nothing was executed on the target."
}
if (-not $TailLog -and -not $DryRun) {
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  - On the target, launch SOLIDWORKS." -ForegroundColor Gray
    Write-Host "  - To watch the host.log live, re-run with -TailLog." -ForegroundColor Gray
    Write-Host "  - Or ssh and tail manually:" -ForegroundColor Gray
    Write-Host "      ssh $target 'powershell -Command `"Get-Content `$env:LOCALAPPDATA\Adze\logs\host.log -Wait -Tail 0`"'" -ForegroundColor Gray
}
