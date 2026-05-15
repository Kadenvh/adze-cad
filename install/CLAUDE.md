# Install — Source of Truth

**Role:** Beta-friendly install/update/uninstall + the authoritative release procedure for Adze.
**Status:** Single canonical doc. Do not split this into multiple .md files; that's how sessions 8-10 happened.
**Last updated:** 2026-05-15 (session 10 close) | **Procedure validated** end-to-end with `2705E9D5...` DLL hash.

This file replaces the old `README.md` + `RELEASE-AND-TESTING.md`. One file, auto-loaded by the agent, lives next to the scripts it documents.

---

## Why this doc reads "defensive"

Sessions 8-10 burned multiple rounds debugging a stale-DLL bug that the procedure below would have caught at step 2. Three machines, three "looks identical" reports, all because a `-SkipBuild` flag silently reused stale Release binaries while source edits got committed but never compiled. Decision #28 + validator Check 3 closed the gap. Session 10 verified end-to-end:

| Stage | Hash check |
|---|---|
| `src/Adze.Host/bin/Release/Adze.Host.dll` (after clean rebuild) | `2705E9D5F6670F21...` |
| `install/adze-v1.1.0.0.zip` contents (after `package-release.ps1`) | `2705E9D5F6670F21...` ✓ |
| `%LOCALAPPDATA%\Adze\bin\Adze.Host.dll` (after `install-adze.ps1`) | `2705E9D5F6670F21...` ✓ |

The procedure works **when followed**. The freshness gate enforces it.

---

## TL;DR for end users (beta testers)

You received a `adze-v*.zip`. Extract it anywhere, then:

1. **Double-click `install-adze.bat`** — installs the add-in to your user profile (no admin needed).
2. **Launch SOLIDWORKS** — the `Adze for SOLIDWORKS` tab appears in the right sidebar.
3. **Optional:** open `Adze.Manager.exe` for a control panel (logs, settings, install state, **Launch SOLIDWORKS** button).

**To update:** extract the newer zip over the same folder, double-click `install-adze.bat` again.

**To uninstall:** double-click `uninstall-adze.bat`.

That is the user-facing surface. Everything below is for the dev + agent who maintain it.

---

## File inventory

| File | Role | Audience |
|---|---|---|
| `install-adze.bat` | **Canonical install entry point** | end user |
| `uninstall-adze.bat` | **Canonical uninstall entry point** | end user |
| `adze-v*.zip` | **Shippable artifact** (15 files: 9 DLLs + Manager.exe + 4 scripts + MANIFEST) | end user / GitHub Releases |
| `CLAUDE.md` | This file | dev + agent |
| `install-adze.ps1` | Install implementation (the `.bat` invokes it) | dev (implementation detail) |
| `uninstall-adze.ps1` | Uninstall implementation | dev |
| `package-release.ps1` | Build + validate + package + smoke-test pipeline | dev only — never ships in the zip |
| `dist/` | Staging dir, recreated by `package-release.ps1`, gitignored | build output |

The `.ps1` files are *next to* the `.bat` files because the install must run from an extracted zip with everything in one folder; splitting them across directories is what dual-mode-bug territory looks like. Documenting them as "implementation detail" keeps user docs simple while preserving the better tooling for ourselves.

---

## Lifecycle at a glance

```
  edit source                                                          ↓
       ↓                                                       launch SW
  ┌─────────────┐   ┌──────────────┐   ┌─────────────┐   ┌────────────────┐
  │  1. build   │ → │  2. validate │ → │  3. package │ → │  4. install +  │
  │  Release    │   │  (freshness, │   │  (zip +     │   │  diagnose      │
  │             │   │  DLL list,   │   │  manifest + │   │  hash + log    │
  │             │   │  schemas)    │   │  smoke)     │   │                │
  └─────────────┘   └──────────────┘   └─────────────┘   └────────────────┘
```

Skipping a step is how we got sessions 8-10's "looks identical" theater. Follow every step every time.

---

## 1. Build Release

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
    C:\adze-cad\Adze.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU" /nologo /v:minimal
```

> **Gotcha:** `scripts\setup\build-all.ps1` defaults to Debug. The packager reads from `src\Adze.Host\bin\Release\`, so if you only built Debug your package will silently use stale Release output. Run msbuild directly with `/p:Configuration=Release` when in doubt.

**Success signal:** `Build succeeded.` printed at the end. `(Get-Item src\Adze.Host\bin\Release\Adze.Host.dll).LastWriteTime` is within seconds of the build command finishing.

---

## 2. Validate

```powershell
pwsh -NoProfile -File scripts\setup\validate-package.ps1
```

Four hermetic checks (see `scripts/setup/README.md` for full descriptions):

1. **Project-vs-list coverage** — every `src/Adze.*/*.csproj` runtime DLL is listed in both `install-adze.ps1` and `package-release.ps1` `$requiredDlls`.
2. **List symmetry** — the two arrays are byte-equal.
3. **Release build freshness** — every shippable project's source files are no newer than the corresponding `bin/Release/.dll`. **This is the check that would have caught sessions 8-10's stale-DLL bug.** Decision #28 codifies it.
4. **Exclusion sanity** — test/harness/exe projects are NOT in the lists.

**Success signal:** `All checks passed.` Exit code 0.

`package-release.ps1` invokes this at Step 0 and refuses to package on failure. Do not delete the wire-in. Do not pass `-SkipBuild` to `package-release.ps1` without first checking timestamps yourself.

---

## 3. Package

```powershell
pwsh -NoProfile -File install\package-release.ps1
```

| Step | Purpose | Failure |
|---|---|---|
| 0    | `validate-package.ps1` | hard-fail |
| 0.5  | Warn on uncommitted `install/` changes | warn |
| 1    | Build Release (unless `-SkipBuild`) | hard-fail |
| 2    | Validate every required DLL + Manager EXE + scripts exist | hard-fail |
| 3    | Stage clean `dist/` | hard-fail |
| 4    | Write `MANIFEST.txt` (per-file SHA256 + git SHA + timestamp) | hard-fail |
| 5    | Compress to `adze-v{version}.zip` | hard-fail |
| 6    | Extract zip into `%TEMP%`, SHA256-compare against `dist/`, byte-compare `install-adze.ps1` in zip vs source | delete bad zip, hard-fail |

**Optional verification before declaring success:**

```powershell
$tmp = Join-Path $env:TEMP "verify-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tmp | Out-Null
Expand-Archive 'C:\adze-cad\install\adze-v*.zip' -DestinationPath $tmp -Force
(Get-FileHash (Join-Path $tmp 'Adze.Host.dll') -Algorithm SHA256).Hash
Remove-Item $tmp -Recurse -Force
```

Record the hash — you'll compare it against the installed DLL in step 4.

---

## 4. Install + verify (local)

### 4a. Extract zip to a staging dir

The bundled mode of `install-adze.ps1` expects DLLs and scripts to be siblings. Always run from an extracted zip:

```powershell
$stage = Join-Path $env:TEMP "adze-deploy-$(Get-Date -Format yyyyMMdd-HHmmss)"
New-Item -ItemType Directory -Path $stage | Out-Null
Expand-Archive 'C:\adze-cad\install\adze-v1.1.0.0.zip' -DestinationPath $stage -Force
```

### 4b. Uninstall existing

```powershell
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $stage 'uninstall-adze.ps1')
```

Add `-RemoveUserData` for a true clean state (wipes `%LOCALAPPDATA%\Adze` + `SOLIDWORKS_AI_*` env vars). Use this when testing "fresh customer experience."

### 4c. Install new

```powershell
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $stage 'install-adze.ps1')
```

### 4d. Verify the installed DLL hash matches the zip's

```powershell
$installed = (Get-FileHash "$env:LOCALAPPDATA\Adze\bin\Adze.Host.dll" -Algorithm SHA256).Hash
$expected  = <hash from step 3 verification>
if ($installed -eq $expected) { 'MATCH' } else { 'MISMATCH — install copied wrong DLL' }
```

**This is the single most important check.** If hashes don't match, the install copied the wrong DLL (SW probably had it MMAP'd and the copy silently failed). Stop here, investigate.

### 4e. Launch SW

User launches SOLIDWORKS. Wait for it to fully load.

### 4f. Diagnose post-launch

```powershell
pwsh -NoProfile -File scripts\setup\diagnose-install.ps1
```

Inspect the report. Success criteria:

- **B. Install state on disk:** all 9 DLLs present, `Adze.Mesh.dll` (canary) PRESENT.
- **C. Registry state:** AddIns key present with `(default)=1`, Title, Description; AddInsStartup key with ONLY `(default)=1` (Decision #23).
- **D. host.log pattern hits:**

| Probe outcome | Ribbon outcome | Meaning |
|---|---|---|
| `CompatibilityProbe: OK` | `RibbonTab: registered` | Best case — fully functional |
| `GracefulRefusal` + `attempting real registration anyway` | `RibbonTab: registered` | Probe was wrong; real registration succeeded. The whole point of Decision #27. |
| `GracefulRefusal` + `attempting real registration anyway` | `RibbonTab: CreateCommandGroup2 returned null` | SW genuinely rejects ribbon on this build. Different problem; surface flows via Manager buttons. |
| `Compatibility probe failed at step 'create-command-group'. Ribbon and context-menu registration will be skipped.` (legacy wording, no GracefulRefusal mention) | (any) | **STALE DLL** — installed `Adze.Host.dll` is from before commit `85a7573`. Repeat steps 1-4. |

---

## 5. Cross-device test

For fresh-customer simulation, R2026x version isolation, or contamination-free testing:

```powershell
pwsh -NoProfile -File scripts\setup\deploy-to-test.ps1 -TargetHost <test-box> -Clean -TailLog
```

One command: builds, packages, validates, scp's to target, runs uninstall+install via SSH, optionally tails `host.log`. See `scripts/setup/README.md` "Cross-device deploy" for one-time target setup (OpenSSH server + SSH key).

---

## 6. Install logging (no Manager required)

Both `install-adze.ps1` and `uninstall-adze.ps1` tee their entire console output to timestamped log files:

- `%LOCALAPPDATA%\Adze\logs\install-yyyyMMdd-HHmmss.log`
- `%LOCALAPPDATA%\Adze\logs\uninstall-yyyyMMdd-HHmmss.log`

These are PowerShell transcripts — every line of console output, including errors that happen before the first `Write-Host`, is captured. Manager doesn't need to be running. `diagnose-install.ps1` reads these to surface install history.

To inspect the most recent install:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Adze\logs\install-*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content
```

---

## 7. When something is wrong

Run the diagnostic. Don't iterate on "fixes" without evidence.

```powershell
pwsh -NoProfile -File scripts\setup\diagnose-install.ps1
```

Captures: install state, registry state, host.log pattern hits, install + uninstall transcripts, ExecutionPolicy, Defender exclusions, lingering SW processes. No API keys, no document contents. Safe to share.

### Common failure modes

| Symptom | Real cause | Fix |
|---|---|---|
| "Looks identical after install" | Installed DLL hash matches PRIOR install | Step 4d hash compare. If installed hash matches prior, install couldn't replace the DLL (SW had it MMAP'd). Fully close SW, retry uninstall + install. |
| "Probe failure even after my fix" | Stale Release build; source edits never compiled | Step 2 validator Check 3 catches this. If you got past it, your source edits aren't where you think they are. |
| "Adze.X.dll missing" | New project added without updating `$requiredDlls` | `pwsh validate-package.ps1 -Fix` auto-adds. Commit. |
| "AddInsStartup key has extra values" | Old install or hand-edit; Decision #23 violation | Uninstall + reinstall. |
| "Ribbon never appears" | Probe outcome rejected ribbon OR DLL is stale | Step 4f host.log inspection. Probe-outcome table tells you which. |
| "SW didn't load the add-in at all" | AddInsStartup `(default) != 1`, OR AddIns key missing, OR DLL load exception | Check registry. Check `host.log` for any line. If host.log doesn't exist, SW never reached `ConnectToSW` — DLL load failed silently (missing dependency or .NET version mismatch). |

---

## 8. Release checklist (public version)

- [ ] All session edits committed to git
- [ ] Step 1: full clean Release build
- [ ] Step 2: `validate-package.ps1` passes all 4 checks
- [ ] Step 3: `package-release.ps1` succeeds, `MANIFEST.txt` has the correct git SHA
- [ ] Step 4 (local): install on dev box, hash matches, SW launches cleanly
- [ ] Step 5 (remote): `deploy-to-test.ps1` against at least one fresh Windows VM or second machine
- [ ] Unit tests: 823+ pass, 0 fail
- [ ] `CHANGELOG.md` updated
- [ ] `CLAUDE.md` (root) version banner + `**Updated:**` date current
- [ ] Tag commit: `git tag -a v1.X.Y -m "..."`
- [ ] Push: `git push && git push --tags`
- [ ] Upload `install/adze-v1.X.Y.zip` to GitHub Releases

---

## Cross-references

- `scripts/setup/README.md` — full docs for `validate-package.ps1`, `deploy-to-test.ps1`, `diagnose-install.ps1`
- brain.db Decision #23 — `AddInsStartup\{GUID}` key shape invariant
- brain.db Decision #27 — `CompatibilityProbe` outcome split
- brain.db Decision #28 — Build artifact freshness gate (this document's reason for existing)
- brain.db trace #18 — root cause of session 8-10 stale-DLL incident
- `plans/refactor-and-cleanup.md` — next-session project dejunk scope (includes moving `package-release.ps1` out of `install/`, remote opt-in log endpoint design, broader install surface consolidation)

---

## System requirements (end user)

- Windows 10 or Windows 11
- .NET Framework 4.8 or later
- SOLIDWORKS 2025 or 2026 desktop edition
- PowerShell 5.1 or later (ships with Windows)
- No admin rights required — registration is per-user

What the installer does:
1. Pre-flight checks (.NET 4.8+, SOLIDWORKS installed, required DLLs present)
2. Copies DLLs to `%LOCALAPPDATA%\Adze\bin\` (stable location outside the repo)
3. Registers COM classes under HKCU
4. Registers SOLIDWORKS add-in under HKCU with auto-start enabled
5. Writes install transcript to `%LOCALAPPDATA%\Adze\logs\install-<timestamp>.log`

What the uninstaller does:
1. Stops SOLIDWORKS + launcher child processes (`sldworks`, `sldworks_fs`, `SWXDesktopLauncher`, `CATSTART`, SW-bundled `msedgewebview2`)
2. Removes COM registration from HKCU
3. Removes SOLIDWORKS add-in registration from HKCU
4. Removes installed binaries from `%LOCALAPPDATA%\Adze\bin\`
5. Preserves user data by default (`%LOCALAPPDATA%\Adze\` logs, traces, state) — pass `-RemoveUserData` to wipe
6. Writes uninstall transcript to `%LOCALAPPDATA%\Adze\logs\uninstall-<timestamp>.log`
