# Setup Scripts

This directory contains the active developer scripts for build, registration, launch, validation, packaging, deployment, and fixture discovery.

## Contents

### Build + test
| File | Purpose |
|------|---------|
| `build-host.ps1` | Build the host project only |
| `build-all.ps1` | Build the full solution, optionally after stopping SOLIDWORKS |
| `run-tests.ps1` | Restore NuGet packages, build, and run the NUnit 3 compiled unit test suite |

### Package + deploy
| File | Purpose |
|------|---------|
| **`package-release.ps1`** | **6-step packaging pipeline (build → validate → stage → manifest → compress → smoke-test). Outputs `install/adze-v{version}.zip`. Moved here from `install/` in session 10 — it's a dev tool, not a user artifact.** |
| **`validate-package.ps1`** | **Pre-package contract validation. 4 checks: DLL list coverage in both `install-adze.ps1` and `package-release.ps1`; list symmetry; Release build freshness; exclusion sanity. Wired into `package-release.ps1` Step 0.** |
| **`deploy-to-test.ps1`** | **One-command build + scp + remote install onto a second Windows machine that runs SOLIDWORKS. Eliminates same-device dev/test contamination (DLL MMAP, launcher zombies, accumulated state).** |
| **`diagnose-install.ps1`** | **One-shot diagnostic dump for "install looks identical / didn't take effect" reports. Captures install state, registry, host.log pattern hits, install/uninstall transcripts, lingering SW processes.** |
| `validate-json-schemas.ps1` | Validate all JSON schemas under `schemas/` |

### Host registration + launch
| File | Purpose |
|------|---------|
| `check-solidworks-host-readiness.ps1` | One-shot preflight for interop and host prerequisites |
| `wait-for-solidworks-host-readiness.ps1` | Poll until the host prerequisites are visible |
| `stop-solidworks-processes.ps1` | Stop the known host-side processes used during reload and validation |
| `register-host-addin.ps1` | Write per-user COM and SOLIDWORKS add-in registration |
| `unregister-host-addin.ps1` | Remove the per-user registration written by the register script |
| `launch-and-check-host.ps1` | Launch the desktop path and report whether host load was blocked |
| `open-sample-document.ps1` | Open a target sample once a live `SldWorks.Application` server is available |
| `reload-host.ps1` | Stop, build, register, launch, and optionally open a sample document |
| `validate-host-spike.ps1` | End-to-end validation of the current host/tool baseline |

### Benchmarks + diagnostics
| File | Purpose |
|------|---------|
| `run-grounding-benchmarks.ps1` | Execute the curated grounding benchmark suite |
| `run-broker-evals.ps1` | Evaluate broker tool recommendation against the curated tasks |
| `run-provider-smoke.ps1` | Live provider smoke (requires API key in env) |
| `collect-support-bundle.ps1` | Collect logs, traces, snapshots, latest reports, and preflight output into a zipped support bundle |
| `RegressionReportHelpers.ps1` | Shared helper for writing JSON regression reports |

### Fixture discovery
| File | Purpose |
|------|---------|
| `find-custom-property-fixtures.ps1` | Discover parts with usable custom properties |
| `find-dimension-fixtures.ps1` | Discover parts with usable display dimensions |
| `find-reference-graph-fixtures.ps1` | Discover assemblies with reference-graph value |
| `inspect-annotation-types.ps1` | Inspect live annotation and dimension behavior during fixture debugging |

### GitHub / repo
| File | Purpose |
|------|---------|
| `enable-github-pages.ps1` | Idempotent GitHub Pages setup |
| `update-repo-meta.ps1` | Push repo metadata (description, topics) to GitHub |
| `load-env.ps1` | Source `.env`-style files into the current shell |

---

## Package validation — `validate-package.ps1`

**Why this exists.** The install script (`install/install-adze.ps1`) and the packager (`scripts/setup/package-release.ps1`) both hardcode a `$requiredDlls` array listing every DLL that ships with the add-in. When a new `src/Adze.*` project lands, that array must be updated in BOTH scripts or the install will silently skip the new DLL while the zip still claims to package the full set. This bit Session 4 (`Adze.UI.dll`) and Session 7 (`Adze.Mesh.dll`).

**What it checks:**

1. **Project-vs-list coverage** (hermetic). Every `src/Adze.*/*.csproj` that produces a runtime DLL (library projects only; excludes `Adze.Manager`/`Adze.UiHarness`/`Adze.Tests`) must appear in both `$requiredDlls` arrays.
2. **List symmetry** (hermetic). The two arrays must be identical — drift between them is by definition a bug.
3. **Release build freshness.** For each shippable project, compares `LastWriteTime` of the most recent `.cs`/`.csproj` in the source dir against `src/Adze.Host/bin/Release/Adze.Whatever.dll`. If source is newer than the DLL, fails. Catches the **stale-build-silently-shipped class of bug** that bit session 8: `-SkipBuild` reused stale `bin/Release` output, the probe-loosening fix never made it into the DLL, every "test" was theater on the old DLL. Skipped cleanly when no Release output exists yet.
4. **Exclusion sanity** (hermetic). Test / harness / exe projects must NOT appear in either list.

**Usage.**

```powershell
# Dry-run validation. Exits non-zero on failure.
pwsh -NoProfile -File scripts\setup\validate-package.ps1

# Auto-add any missing entries to both arrays.
pwsh -NoProfile -File scripts\setup\validate-package.ps1 -Fix

# Quiet output (CI-friendly). Failures still print.
pwsh -NoProfile -File scripts\setup\validate-package.ps1 -Quiet
```

**Wired into packaging.** `scripts/setup/package-release.ps1` invokes this as Step 0 and refuses to package if it fails. To bypass (don't), delete the Step 0 block — but the regression class it catches is silent at customer install time, so think twice.

**Adding a new project.** When you add `src/Adze.Whatever/Adze.Whatever.csproj`, just run `validate-package.ps1 -Fix` once. It will append `Adze.Whatever.dll` to both arrays in the canonical formatting. Then commit.

---

## Cross-device deploy — `deploy-to-test.ps1`

**Why this exists.** Same-device dev+test on Adze is hostile:
- SW MMAP's the add-in DLLs at `ConnectToSW`, so reinstalls during a SW session are no-ops until full SW restart.
- Launcher child processes (`sldworks_fs`, `SWXDesktopLauncher`, `CATSTART`, SW-bundled `msedgewebview2`) survive `File → Exit` and hold file locks.
- `%LOCALAPPDATA%\Adze` state, HKCU COM registrations, and env vars accumulate and mask issues that a fresh install would surface.

Moving the test loop to a second Windows machine eliminates classes (1)–(3) of those costs. This script automates the round-trip so it stays one command.

**What it does:**
1. Validate local prerequisites (zip exists or build it, OpenSSH client available)
2. Verify SSH reachability to the target
3. Build + package locally via `package-release.ps1` (skippable with `-SkipBuild`)
4. `scp` the zip to the target
5. SSH the target: extract, uninstall existing, install fresh
6. Verify installed binaries on the target
7. Optionally tail `host.log` on the target so `ConnectToSW` outcomes stream live

**Target setup (one-time, on the test machine):**

```powershell
# 1. Install OpenSSH Server (Settings → Optional features → "OpenSSH Server")
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service sshd -StartupType Automatic
Start-Service sshd

# 2. From the DEV box, copy your public key to the target.
#    (Or manually append id_ed25519.pub to %USERPROFILE%\.ssh\authorized_keys on target.)
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh user@target-box `
  "powershell -Command `"Add-Content -Path \$env:USERPROFILE\.ssh\authorized_keys`""

# 3. Test: should print hostname without password prompt.
ssh user@target-box hostname

# 4. (Optional) Make pwsh the default SSH shell so install scripts work without prefixes.
New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' `
  -Name DefaultShell -Value 'C:\Program Files\PowerShell\7\pwsh.exe' `
  -PropertyType String -Force

# 5. Install SOLIDWORKS on the target (Maker subscription works).
```

**Usage.**

```powershell
# Standard deploy: build, package, scp, install.
pwsh scripts\setup\deploy-to-test.ps1 -TargetHost test-box

# Full clean install + live host.log tail (Ctrl-C to stop).
pwsh scripts\setup\deploy-to-test.ps1 -TargetHost 10.0.0.5 -Clean -TailLog

# Reuse the latest zip without rebuilding.
pwsh scripts\setup\deploy-to-test.ps1 -TargetHost test-box -SkipBuild

# Dry-run: print what would happen, execute nothing.
pwsh scripts\setup\deploy-to-test.ps1 -TargetHost test-box -DryRun
```

**Parameters.**

| Flag | Purpose |
|---|---|
| `-TargetHost` | (Required.) Target DNS name or IP. |
| `-TargetUser` | SSH username. Defaults to `$env:USERNAME`. |
| `-RemoteRoot` | Working dir on target. Defaults to `C:\adze-test`. Cleaned + recreated per run. |
| `-SkipBuild` | Reuse the most-recent `install/adze-v*.zip` instead of rebuilding. |
| `-Clean` | Pass `-RemoveUserData` to uninstall on target — wipes `%LOCALAPPDATA%\Adze` and clears `SOLIDWORKS_AI_*` env vars. Use for "fresh customer" tests. |
| `-TailLog` | After install, tail `host.log` on the target via SSH. Useful for watching `ConnectToSW` outcomes when SW launches. |
| `-DryRun` | Print the commands without executing them. |

**Typical workflow.**
1. Edit code on dev box.
2. `pwsh scripts\setup\deploy-to-test.ps1 -TargetHost test-box -TailLog` (one command — build, deploy, install, watch logs).
3. Launch SW on the target. `host.log` streams locally. Watch for `RibbonTab: registered successfully` (or whatever you're validating).
4. Iterate.

The 30s SW launch cost on the target isn't eliminated by this workflow — but every other contamination class is. No file locks on the dev box, no MMAP'd DLLs blocking the install, no stale state masking the real issue.

---

## Conventions

- Run these scripts with `pwsh -NoProfile -File` unless the script explicitly depends on Windows PowerShell.
- Development registration is per-user by default.
- Treat launcher state as part of validation. If `launch-and-check-host.ps1` reports a login or update window, clear it before assuming the host failed.
- Benchmark and broker-eval runs write JSON reports under `benchmarks/reports`.
- Support bundles default to `%LOCALAPPDATA%\Adze\SupportBundles` and are intended for beta/support triage, not for source control.

## Adding New Items

- Put reusable developer workflows here, not one-off notes.
- Keep scripts safe to rerun and log any unattended or installer-related work.
- Update this README when a new workflow script becomes part of the normal path.
- For new `src/Adze.*` projects: run `validate-package.ps1 -Fix` after creating the csproj to register its DLL with the installer. Otherwise the install will silently skip your DLL on customer machines.
