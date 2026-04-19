# v1.0.0 Release Path — Postponed Pending Update-Lifecycle Fix

**Created:** 2026-04-18 | **Status:** Active — Monday submission cancelled | **Updated:** 2026-04-19
**Author/owner:** Kaden VanHoecke (VH Tech LLC)
**New target:** TBD (after R1–R5 below are complete)
**Supersedes:** `solidworks-partner/RELEASE-4-20.md` (archived)

---

## What changed

On 2026-04-19 a 3DEXPERIENCE / SOLIDWORKS update was applied on Zoe. After the update, enabling Adze crashes SOLIDWORKS. Evidence in the repo:

- Three SW crash sessions in `%APPDATA%\SolidWorks\20260419013311_34.1.0.0140`, `_013348_34.1.0.0140`, `_013814_34.1.0.0140` — all on SW build **34.1.0.0140** (the post-update version).
- Every v1.0 ConnectToSW in `%LOCALAPPDATA%\Adze\logs\host.log` logs `Task Pane created` then `RibbonTab: registered successfully` — and then **terminates silently** (no `Snapshot captured`, no `ConnectToSW completed`). The prior v0.1.1 sessions with context-menu gated off completed normally.
- Primary hypothesis: the feature-tree context-menu attachment (now default-on in v1.0) calls into an interop surface whose binding or semantics changed in SW 34.1.0.0140. Secondary hypotheses include the ribbon late callbacks and any post-ConnectToSW notification subscriptions.
- Compounding issue: the 3DX desktop updater refuses to run when SOLIDWORKS is held open by a broken add-in, so the update itself fails with "an issue with the SOLIDWORKS application". An update lifecycle that depends on our plugin staying out of the way cannot rely on the user knowing to uninstall us first.

## Release decision

**Monday 2026-04-20 partner resubmission is cancelled.** The release (any release — not just v1.0, but any forward motion that adds end-user install risk) is held until all of R1–R5 are complete and validated end-to-end across an update cycle.

The previously planned v1.0 feature work (Settings panel, DPAPI API key store, zero-config defaults, `.bat` wrappers, label polish — items P1–P7 in the earlier version of this plan) is **code-complete in the tree** and will ship as part of v1.0 when we do release. They are not the blocker; the lifecycle gap is.

---

## New phases (blocking any release)

### R1 — Crash root cause

| # | Done-when |
|---|-----------|
| R1.1 | Analyze the three CXPD crash sessions from 2026-04-19. Identify the faulting module, managed stack, and the final Adze call frame. |
| R1.2 | Compare SW 34.1.0.0140 interop surface against the prior SW build Adze targets. Identify the delta in `ContextMenu.Attach`, `ISwAddin.ConnectToSW` notification registration, and any ribbon late-init callbacks. |
| R1.3 | Reproduce the crash in a controlled way. Toggle context-menu gate off; confirm crash disappears. Toggle back on; confirm it returns. |
| R1.4 | Produce a minimal repro documented in `plans/crash-investigation-20260419.md` with dump filenames, exact stacks, environment details. |

### R2 — Interop resilience (the actual fix)

**Status:** code-complete 2026-04-19, **awaiting live validation** on post-update SW. See `plans/crash-investigation-20260419.md` for root-cause analysis.
**Approach chosen:** see brain.db Decision #15 — `[HandleProcessCorruptedStateExceptions]` + `[SecurityCritical]` on risky interop methods, plus a startup `CompatibilityProbe`. Full late-binding rewrite rejected as multi-day work with same failure mode.

| # | Done-when | Status |
|---|-----------|--------|
| R2.1 | Identify and quarantine the broken API call. Wrap in try/catch with diagnostic logging; disable the dependent feature and log a user-facing banner when the call fails. | ✅ ContextMenu.Register + TryRegisterMenu wrapped + CSE-protected; `LastProbeMessage` exposed on `AdzeAddIn` for future banner |
| R2.2 | Late-bind risky interop calls. | ⏭ **Rejected** — CSE attribute gives same outcome for 1% the effort. Can revisit if additional surfaces break in future SW builds. |
| R2.3 | Add a startup **compatibility probe** runs before `ConnectToSW` returns. Read-only smoke check against the live SW COM surface. If anything throws, refuse to load ribbon/context-menu and surface a banner. | ✅ `src/Adze.Host/AddIn/CompatibilityProbe.cs` — exercises `RevisionNumber` + `GetCommandManager` + `CreateCommandGroup2` + `RemoveCommandGroup2`. Typed `CompatibilityProbeResult`. Integrated in `AdzeAddIn.TryRegisterContextMenu`. Banner wiring deferred. |
| R2.4 | Unit / integration tests covering the fallback paths. | 🟡 Partial — existing 684 tests still green; new `CompatibilityProbe` is internal to Adze.Host and calls live interop, so direct unit testing is blocked on mocking `ISldWorks`. Live R5 validation covers the fallback end-to-end. |
| R2.5 | **(follow-up)** Task Pane banner wiring — surface `AdzeAddIn.LastProbeMessage` when the probe detected incompatibility so users understand why context-menu is absent. | 🔴 Not started. Tracked as item #11 in session "what we haven't done" sweep. |
| R2.6 | **(follow-up)** Live validation — user sets `SOLIDWORKS_AI_CONTEXT_MENU=true` at user scope, relaunches SW, observes expected log entries (probe OK + managed skip, or probe-failed-at-step). Until this lands, R1 is not closed. | 🔴 Blocked on SW UI cooperating post-update |

### R3 — Update-lifecycle cooperation

| # | Done-when | Status |
|---|-----------|--------|
| R3.1 | **Pre-update eject.** Detect the 3DX desktop updater process (`swxdesktopupdate.exe`) or its file locks; when present, Adze unhooks its SOLIDWORKS event subscriptions on disconnect and skips re-registering until the next clean launch. | 🔴 Not started |
| R3.2 | **Startup staleness check.** On `ConnectToSW`, compare the installed SW build version against the one Adze was last verified on (persisted in `%LOCALAPPDATA%\Adze\state\sw-build.txt`). If different, run the R2.3 compatibility probe before touching anything mutable in the SW UI. | ✅ `SwBuildStateService` + early probe in `AdzeAddIn.ConnectToSW`; ribbon + context-menu both gate on probe result; 8 new tests (692 total). |
| R3.3 | **Uninstaller hook.** The `uninstall-adze.ps1` / `.bat` / manager UI invariably works even if SW is currently running and Adze is loaded. It safely detaches before removing files. | 🟡 Partially — `install-adze.ps1 -Uninstall` already force-stops SW before COM removal. Post-R4 the Manager UI will wrap this more gracefully. |
| R3.4 | Document the update flow in `SETUP.md` and `PRIVACY.md`. | 🔴 Not started |

### R4 — End-user installer / manager UI (no PowerShell)

**Status:** MVP shipped 2026-04-19. Manager launches, status reflects install + SW process + verified-build state + config path + API-key presence, all four action buttons functional, release zip bundles it, `.bat` wrappers prefer it.

| # | Done-when | Status |
|---|-----------|--------|
| R4.1 | `Adze.Manager` — small Windows Forms or WPF app shipped alongside the DLLs. Double-clickable. Single window with Install / Update / Uninstall / Eject / Status buttons. Shows currently installed version, detected SW build, compatibility status, whether SW is currently running. | ✅ `src/Adze.Manager/` new project. WinForms .NET 4.8. Window shows install state (from DLL presence + FileVersionInfo), SW + 3DX-updater process state, last-verified SW build (via `SwBuildStateService`), API key presence (via `ApiKeyStore`), config path (via `FeatureGateConfigService`). |
| R4.2 | Manager wraps the existing PowerShell install/uninstall logic but presents it as a GUI. Shows progress, errors clearly, and asks "Would you like to stop SOLIDWORKS first?" when relevant. | ✅ Stream stdout/stderr of `install-adze.ps1` into an embedded RichTextBox log panel. Status line warns when SW or updater is running. |
| R4.3 | Release zip bundles `Adze.Manager.exe` + DLLs + PowerShell scripts (scripts remain for power users). The `.bat` wrappers launch `Adze.Manager.exe` when it exists, otherwise fall back to the PowerShell script. | ✅ `package-release.ps1` copies `Adze.Manager.exe`; zip now 12 files. `install-adze.bat` / `uninstall-adze.bat` check for `Adze.Manager.exe` adjacent and launch it; fall through to PowerShell when absent (source checkouts, older zips). |
| R4.4 | Manager has an "Eject before update" explicit button for the user who wants to be safe before applying a 3DX update. | ✅ "Eject for Update" button clears `SwBuildStateService` persisted build + runs uninstall script. Confirmation dialog spells out what will be removed (COM reg + DLLs) and what's preserved (config + stored API key). |
| R4.5 | `README.md` end-user section rewritten around the Manager UI, not the PowerShell flow. | 🟡 `README.md` + `SETUP.md` end-user sections currently describe the `.bat` double-click — which now launches Manager automatically, so the prose is correct but stops short of advertising the Manager explicitly. Follow-up: mention "Opens the Adze Manager" one sentence each, once the Manager has been live-tested on a clean install. |
| R4.6 | **(follow-up)** Auto-detect `swxdesktopupdate.exe` running — Manager should gray out Install and light up Eject as the prominent action. | 🔴 Partially — status line flags updater running, but button emphasis isn't yet swapped. |

### R5 — Release-gate validation

A release can be tagged only when all of these hold true end-to-end, verified on Zoe (or a fresh test VM) with a current 3DEXPERIENCE SW install:

| # | Gate |
|---|------|
| R5.1 | Clean rebuild (delete all `bin`/`obj`), `run-tests.ps1` green, `run-broker-evals.ps1` 12/12 green. |
| R5.2 | Fresh install via `Adze.Manager.exe` — SW launches cleanly, Task Pane appears, Settings panel visible. |
| R5.3 | Apply a real 3DX update with Adze installed. Updater completes without errors. |
| R5.4 | Post-update: relaunch SW. Adze either loads cleanly, or refuses to load with a friendly banner (no crash). |
| R5.5 | Pre-update eject flow: user clicks "Eject before update" in Manager, updater runs cleanly, Manager re-enables Adze after. |
| R5.6 | Uninstall via `Adze.Manager.exe` — clean COM unreg, clean DLL removal, SW launches again without Adze. |
| R5.7 | Live smoke test on SpdrBot v14 covering all items in `TESTING-MANUAL-USER.md` sections 3–8. |

### R6 — Partner resubmission (new date TBD)

Only after R5.1–R5.7 all pass. No new calendar date until then.

---

## What the previously-completed code stays

The v1.0 feature work from the earlier plan (P1–P7) is already in the tree and staying there. It was not the cause of the crash — every one of those changes passed 684 unit tests. When R1–R5 are done, v1.0 ships with them included.

- Settings panel (FeatureGateConfigService + ApiKeyStore + Task Pane UI + 14 tests)
- Default-on gates for ribbon, context-menu, model, agent-loop, writes, streaming, retrieval
- Tool outcome labels humanized (10 grounding tools)
- Answer-source mapping helper (internal → user-facing)
- `install-adze.bat` + `uninstall-adze.bat` wrappers
- `CODE_OF_CONDUCT.md`, `plans/README.md`, end-user README section, SETUP simplest path
- AssemblyVersion 1.0.0.0 across all projects

## What changes back in the documentation

Because v1.0.0 is not shipping on Monday (or at any currently-known date), every public-visible claim of "v1.0.0 released" is being reverted:

- `README.md` status line reverted to "v0.1.1 shipped, v1.0.0 in development"
- `docs/index.html` version badges reverted to v0.1.1
- `CHANGELOG.md` — `[1.0.0] — 2026-04-20` header reverted to `[Unreleased]` with blocker note
- `identity.project.version` reverted to `0.1.1` (last shipped build)
- `CLAUDE.md` header updated with new status

The `AssemblyVersion` in source stays at `1.0.0.0` — we're building toward 1.0, just not claiming it's released.

## Explicit non-goals

- No release date. No calendar pressure. This gets fixed properly.
- No cosmetic-only changes. Every edit from here on either moves R1–R5 forward, keeps the tree green, or corrects a misleading public claim.
- No rewrite. The sidecar architecture (`plans/design-mcp-server.md`) is still for Phase 10+; it's an isolation play, not the fix for R2026x compatibility.

## Cross-references

- `plans/crash-investigation-20260419.md` — will be created in R1.4
- `plans/design-mcp-server.md` — long-term isolation story, post-v1.0
- `solidworks-partner/RELEASE-4-20.md` — original Monday plan (archived, superseded)
- `plans/TESTING-MANUAL-USER.md` + `TESTING-PROCEDURE-AUTOMATED.md` — validation procedures (used in R5.7)
