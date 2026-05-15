# Release & Testing Procedure

The authoritative runbook for packaging, deploying, and verifying an Adze build. Follow this every time. It exists because session 8-10 wasted multiple rounds on "looks identical" reports caused by drift between source, build output, packaged zip, and installed DLL — every one of which was preventable by following these steps in order.

> **Audience:** dev + agent (this file is the source of truth either pair follows). If a step here disagrees with reality, fix the step.

---

## Lifecycle at a glance

```
  edit source                                                          ↓
       ↓                                                       launch SW
  ┌─────────────┐   ┌──────────────┐   ┌─────────────┐   ┌────────────────┐
  │  build      │ → │  validate    │ → │  package    │ → │  install +     │
  │  Release    │   │  (freshness, │   │  (zip +     │   │  diagnose      │
  │             │   │  DLL list,   │   │  manifest + │   │  hash + log    │
  │             │   │  schemas)    │   │  smoke)     │   │                │
  └─────────────┘   └──────────────┘   └─────────────┘   └────────────────┘
```

Each step has a gate that catches the failure mode the previous step's history actually produces. Skipping a step is how we got "looks identical" for two sessions.

---

## 1. Build Release

```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
    C:\adze-cad\Adze.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU" /nologo /v:minimal
```

Or, with the convenience wrapper:

```powershell
pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks
```

> **Gotcha:** `build-all.ps1` defaults to Debug unless invoked with the right config. The packager reads from `src\Adze.Host\bin\Release\`, so if you only built Debug your package will silently use stale Release output. Run msbuild directly with `/p:Configuration=Release` when in doubt.

### Success signal

- `Build succeeded.` printed at the end.
- `(Get-Item src\Adze.Host\bin\Release\Adze.Host.dll).LastWriteTime` is within seconds of the build command finishing.

---

## 2. Validate

```powershell
pwsh -NoProfile -File scripts\setup\validate-package.ps1
```

Four hermetic checks (see `scripts/setup/README.md` for full descriptions):

1. **Project-vs-list coverage** — every `src/Adze.*/*.csproj` runtime DLL is listed in both `install-adze.ps1` and `package-release.ps1` `$requiredDlls`.
2. **List symmetry** — the two arrays are byte-equal.
3. **Release build freshness** — every shippable project's source files are NO newer than the corresponding `bin/Release/.dll`. **This check is what would have caught sessions 8-10's stale-DLL bug.**
4. **Exclusion sanity** — test/harness/exe projects are NOT in the lists.

### Success signal

```
=== Summary ===

  All checks passed.
```

Exit code 0. Non-zero exit MUST block the rest of the pipeline.

### If staleness fails

```
STALE: Adze.Host.dll (2026-05-10 09:08:04). Source HostState.cs is 4.2h newer.
```

→ Go back to step 1 and rebuild Release. Do not skip this; it's the only thing preventing the same bug we just spent three sessions debugging.

### Also runs as part of packaging

`install/package-release.ps1` calls `validate-package.ps1` at Step 0 and refuses to package on failure. Decision #28 codifies this gate. **Do not delete the wire-in or pass `-SkipBuild` without checking timestamps yourself.**

---

## 3. Package

```powershell
pwsh -NoProfile -File install\package-release.ps1
```

> **`-SkipBuild` is dangerous.** It reuses existing `bin/Release` output without rebuilding. Only use it when you've just run step 1 in the same session AND the validator passes. Even then, prefer running the full pipeline — incremental rebuild of unchanged projects is fast.

### What package-release.ps1 does

| Step | Purpose | Failure behavior |
|---|---|---|
| 0    | Run `validate-package.ps1` | Hard-fails packaging |
| 0.5  | Warn on uncommitted working-tree changes in `install/` | Warning only |
| 1    | Build Release (unless `-SkipBuild`) | Hard-fails |
| 2    | Validate every required DLL + Manager EXE + scripts exist | Hard-fails |
| 3    | Stage clean `install/dist/` | Hard-fails |
| 4    | Write `MANIFEST.txt` (per-file SHA256 + git SHA + timestamp) | Hard-fails |
| 5    | Compress to `install/adze-v{version}.zip` | Hard-fails |
| 6    | Extract zip into `%TEMP%`, SHA256-compare against `dist/`, byte-compare `install-adze.ps1` in zip vs source | Deletes bad zip, hard-fails |

### Success signal

```
=== Package Summary ===
  Version   : 1.1.0.0
  Files     : 15 (incl. MANIFEST.txt)
  Zip       : C:\adze-cad\install\adze-v1.1.0.0.zip
  Size      : 0.32 MB (335728 bytes)
  Commit    : <short-sha>
```

### Verification (optional but recommended)

```powershell
$zip = 'C:\adze-cad\install\adze-v1.1.0.0.zip'
$tmp = Join-Path $env:TEMP "verify-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tmp | Out-Null
Expand-Archive $zip -DestinationPath $tmp -Force
(Get-FileHash (Join-Path $tmp 'Adze.Host.dll') -Algorithm SHA256).Hash
Remove-Item $tmp -Recurse -Force
```

Record the SHA256 — you'll compare it against the installed DLL in step 4.

---

## 4. Install + verify (local)

### 4a. Extract zip to a staging dir

The bundled mode of `install-adze.ps1` expects the DLLs and scripts to be sibling files. Always run from an extracted zip, never from the repo directly (those are different code paths and they have drifted in the past).

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

**This is the single most important check.** If the hashes don't match, the install copied the wrong DLL (probably because SW had it MMAP'd and the copy silently failed). Stop here, investigate, do not declare success.

### 4e. Launch SW

User launches SOLIDWORKS. Wait for it to fully load (Task Pane should appear).

### 4f. Diagnose post-launch

```powershell
pwsh -NoProfile -File scripts\setup\diagnose-install.ps1
```

Inspect the report. Success criteria:

- **B. Install state on disk:** all 9 expected DLLs present, `Adze.Mesh.dll` (canary) PRESENT.
- **C. Registry state:** AddIns key present with `(default)=1`, Title, Description; AddInsStartup key present with ONLY `(default)=1` (no extra REG_SZ entries — Decision #23).
- **D. host.log pattern hits:** one of these combinations:

| Probe outcome | Ribbon outcome | Meaning |
|---|---|---|
| `CompatibilityProbe: OK` | `RibbonTab: registered` | Best case — fully functional |
| `GracefulRefusal` + `attempting real registration anyway` | `RibbonTab: registered` | Probe was wrong; real registration succeeded. The whole point of Decision #27. |
| `GracefulRefusal` + `attempting real registration anyway` | `RibbonTab: CreateCommandGroup2 returned null` | SW genuinely rejects ribbon registration on this build. Different problem; surface flows via Manager buttons instead. |
| `Compatibility probe failed at step 'create-command-group'. Ribbon and context-menu registration will be skipped.` (legacy wording, no GracefulRefusal mention) | (any) | **STALE DLL** — installed `Adze.Host.dll` is from before commit `85a7573`. Repeat steps 1-4. |

---

## 5. Cross-device test (when applicable)

When local-only testing isn't enough — fresh-customer simulation, R2026x version differences, isolating local-machine contamination — deploy to a second Windows machine.

```powershell
pwsh -NoProfile -File scripts\setup\deploy-to-test.ps1 -TargetHost <test-box> -Clean -TailLog
```

This script automates steps 4a-4f against a remote machine via SSH/SCP. See `scripts/setup/README.md` "Cross-device deploy" section for one-time target setup (OpenSSH server + SSH key).

The script does NOT skip steps 1-3 by default — it builds, packages, and validates locally, then deploys the just-verified zip. Pass `-SkipBuild` only when you've already validated the zip via this procedure.

---

## 6. When something is wrong

If the diagnostic shows unexpected output, run the diagnostic first, paste the output. Don't iterate on "fixes" without evidence.

```powershell
pwsh -NoProfile -File scripts\setup\diagnose-install.ps1 -OutputPath C:\Temp\report.txt
```

The diagnostic captures: install state, registry state, host.log pattern hits, ExecutionPolicy, Defender exclusions, lingering SW processes. Everything safe to share — no API keys, no document contents.

### Common failure modes (in order of frequency)

| Symptom | Real cause | Fix |
|---|---|---|
| "Looks identical after install" | Installed DLL hash matches PRIOR install | Step 4d hash compare. If installed hash matches prior, the install couldn't replace the DLL (SW had it MMAP'd). Fully close SW, re-run uninstall, retry install. |
| "Probe failure even after my fix" | Stale Release build, source edits never compiled | Step 2 validator Check 3 catches this. If you got past it, your source edits aren't where you think they are. |
| "Adze.X.dll missing" | New project added without updating `$requiredDlls` | `pwsh validate-package.ps1 -Fix` auto-adds. Commit. |
| "AddInsStartup key has extra values" | Old install or hand-edit; Decision #23 violation | Uninstall + reinstall. |
| "Ribbon never appears" | Either probe outcome rejected ribbon OR DLL is stale | Step 4f host.log inspection. The probe-outcome table tells you which. |
| "SW didn't load the add-in at all" | AddInsStartup `(default) != 1`, OR AddIns key missing, OR DLL load exception | Check registry. Check `host.log` for any line at all. If host.log doesn't exist, SW never reached `ConnectToSW` — DLL load failed silently (usually missing dependency or .NET version mismatch). |

---

## 7. Release checklist (when shipping a public version)

Before tagging a version and pushing the zip to GitHub Releases:

- [ ] All session edits committed to git
- [ ] Step 1: full clean Release build (`MSBuild ... /p:Configuration=Release`)
- [ ] Step 2: `validate-package.ps1` passes all 4 checks
- [ ] Step 3: `package-release.ps1` succeeds, MANIFEST.txt has the correct git SHA
- [ ] Step 4 (local): install on dev box, hash matches, SW launches cleanly, ribbon visible
- [ ] Step 5 (remote): `deploy-to-test.ps1` against at least one fresh Windows VM or second machine
- [ ] Unit tests: 823+ pass, 0 fail (6 inconclusive smoke is expected without API key)
- [ ] CHANGELOG.md updated with the version's notable changes
- [ ] CLAUDE.md `**Version:**` banner and `**Updated:**` date current
- [ ] Tag the commit: `git tag -a v1.X.Y -m "..."`
- [ ] Push: `git push && git push --tags`
- [ ] Upload `install/adze-v1.X.Y.zip` to GitHub Releases

---

## Cross-references

- `scripts/setup/README.md` — full docs for `validate-package.ps1`, `deploy-to-test.ps1`, `diagnose-install.ps1`
- `install/package-release.ps1` — packaging pipeline (calls validator at Step 0)
- brain.db Decision #23 — `AddInsStartup\{GUID}` key shape invariant
- brain.db Decision #27 — `CompatibilityProbe` outcome split (Compatible / GracefulRefusal / HardIncompatible)
- brain.db Decision #28 — Build artifact freshness gate (this document's reason for existing)
- brain.db trace #18 — root cause of the session 8-10 stale-DLL incident this procedure was written to prevent
