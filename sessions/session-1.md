# Session 1 — v1.0 Prep + R2026x Crisis Response + Adze.Manager MVP

**Date:** 2026-04-19
**Version at start:** 0.1.1 (last shipped)
**Version at close:** 0.1.1 (last shipped; `AssemblyVersion` in source is `1.0.0.0` but v1.0 release is explicitly held)
**Branch:** `main`
**Duration:** extended single-session push
**Outcome:** massive forward progress + one new hard blocker discovered + fix landed awaiting live validation

---

## One-line summary

Started as a Monday-4/20 partner-application polish sprint (P1–P7). Became a v1.0 architectural push after user elected to reframe. Then a 3DEXPERIENCE R2026x desktop update landed mid-session and crashed the add-in on enable, prompting a crisis response: new R1–R6 phase plan, `CompatibilityProbe`, CSE-aware interop methods, `SwBuildStateService` for build-version staleness detection, and `Adze.Manager.exe` WinForms installer UI. Monday submission cancelled; v1.0 release held pending live validation.

## Phases worked

### Before the crisis — v1.0 polish (P-series)

- **P1 Repo hygiene** — removed `AGENTS.md` and `.stignore` from tracking, moved internal plans out of `plans/` into `solidworks-partner/`, added `CODE_OF_CONDUCT.md` (Contributor Covenant reference), wrote `plans/README.md`, created `install/install-adze.bat` + `uninstall-adze.bat` double-click wrappers, updated `package-release.ps1` to bundle the `.bat` files.
- **P2 End-user docs** — `README.md` gained a "Just trying it out" top-level section; `SETUP.md` gained a "Simplest path to first run" block above the developer content.
- **P3 UI quick fixes** — all 10 grounding tool `Summary` labels rewritten in user-facing language (e.g. "Active document resolved." → "Read the active document."); new `FormatAnswerSourceForUser` helper on `HostState` mapping internal source identifiers (`deterministic_fallback`, `model_openai`, `model_anthropic`, `agent_loop`, `agent_fallback`) to friendly labels (`Built-in broker`, `OpenAI`, `Anthropic`, `AI agent`, `AI agent (fallback)`); Task Pane sub-header changed from "Adze — SOLIDWORKS AI" (redundant with outer window title) to "Ask anything about your model"; "Suggested Recipes (N)" section dropped the noisy count; chat-label CSS softened (removed uppercase transform, weight 600, letter-spacing).
- **P4 Settings panel** — new `FeatureGateConfigService` persists feature-gate preferences to `%LOCALAPPDATA%\Adze\config.json`; `FeatureGateRegistry.IsEnabled` now resolves env var → config file → baked-in default; 10 known gates with v1.0 safe-default mix (ribbon/context/model/agent/writes/streaming/retrieval default-on, toast/PMP/local default-off); new Settings collapsible section in Task Pane with provider dropdown + masked API key input + gate state table; JS-to-C# bridge `SaveApiKey`/`ClearApiKey`.
- **P5 API key wizard** — new `ApiKeyStore` under `Adze.Broker/Configuration` persists the active provider + API key at `%LOCALAPPDATA%\Adze\keys.dat`, DPAPI-encrypted with user-scope entropy; `BrokerModelSettings.LoadFromEnvironment` now falls through to `ApiKeyStore` when env vars are absent.
- **P7 Version bump to 1.0.0** — all 12 source files updated (6 `AssemblyInfo.cs` + 6 `.csproj`) via PowerShell script; identity, CLAUDE.md, README, `docs/index.html`, CHANGELOG followed. Later reverted the public-visible claims (README/docs/CHANGELOG) back to "v0.1.1 shipped, v1.0 in development" when the crash surfaced.

### The crisis — 3DX R2026x update

On relaunch after a 3DEXPERIENCE desktop update, enabling Adze crashed SOLIDWORKS silently. Three CXPD dumps at `%APPDATA%\SolidWorks\20260419013*_34.1.0.0140\`. `host.log` pattern identical across attempts: `ConnectToSW starting` → `Task Pane created` → `RibbonTab: registered successfully` → silence. Diagnostic hypothesis: native access violation inside `ContextMenu.Register`'s first interop call sequence (`AddContextMenu` / `AddCommandItem2` / `Activate`) caused by SW build 34.1.0.0140 interop binary-signature drift. Managed `try/catch (Exception)` doesn't catch CSE by default on .NET 4.5+. The 3DX updater additionally failed with "Failed to parse user intentions file: ...\InstallData\incomplete\UserIntentions.tmp" — a Dassault installer-state issue resolved by user removing the `incomplete/` folder.

### After the crisis — R-phases

- **R1 crash investigation** — documented in `plans/crash-investigation-20260419.md`. Root cause narrowed to `ContextMenu.Register` first interop call on SW 34.1.0.0140. Live validation pending.
- **R2 interop resilience** — new `src/Adze.Host/AddIn/CompatibilityProbe.cs` runs a benign read-only smoke test (`RevisionNumber` → `GetCommandManager` → `CreateCommandGroup2` → `RemoveCommandGroup2`) at `ConnectToSW` time. Method decorated with `[HandleProcessCorruptedStateExceptions]` + `[SecurityCritical]` so native CSEs surface as typed returns. `ContextMenu.Register` and `ContextMenu.TryRegisterMenu` got the same attributes. `AdzeAddIn.RunCompatibilityProbe` called early; ribbon + context-menu gates on probe result.
- **R3.2 update-lifecycle cooperation (partial)** — new `src/Adze.Broker/Configuration/SwBuildStateService.cs` persists last-verified SW build at `%LOCALAPPDATA%\Adze\state\sw-build.txt`. Probe runs unconditionally early in `ConnectToSW`; on success the build version is persisted. 8 new tests. R3.1 (pre-update `swxdesktopupdate.exe` detection + clean eject) remains outstanding.
- **R4 Adze.Manager MVP** — new `src/Adze.Manager/` WinForms .NET 4.8 project. Single window shows install state, SW + 3DX-updater process state, last-verified SW build, API key presence, config path. Four buttons: Install/Reinstall, Uninstall, Eject for Update, Refresh. Wraps `install-adze.ps1` via `Process.Start` with live stdout/stderr streaming to embedded log. Release zip bundles `Adze.Manager.exe`; `.bat` wrappers prefer it when adjacent.

### Documentation system

- `plans/documentation-structure.md` — four-layer explanation (brain.db + GitNexus + GitHub Issues + Linear)
- `plans/release-process.md` — repeatable pre-release + publication + post-publish checklist
- `plans/github-labels.md` — recommended label set with colors, saved filters, anti-patterns
- `plans/linear-adoption-checklist.md` — 10-step walkthrough for Kaden (workspace `vhtech` + project `ADZ` now created)
- `plans/github-issue-v1-crash-draft.md` — ready-to-paste body for the R1 tracking issue
- `.github/ISSUE_TEMPLATE/crash_report.md` — structured crash report template
- `plans/polish-and-v1-path.md` — fully rewritten from "Monday 4/20 v0.1.2" to "R1–R6 v1.0 path, TBD"

## brain.db updates

**Decisions added:**
- #14 — Monday 2026-04-20 partner resubmission cancelled
- #15 — R2 interop resilience via `[HandleProcessCorruptedStateExceptions]` + compatibility probe, not full late-binding rewrite

**Notes added (multiple):**
- R1 BLOCKER (SW 34.1.0.0140 crash)
- R1 MITIGATION (ContextMenu default flipped to false)
- R2 CODE COMPLETE
- R3.2 CODE COMPLETE
- R4 MVP COMPLETE
- Monday cancellation handoff
- Linear workspace `vhtech` / project `ADZ` live

**Identity reverted:** `project.version` back to `0.1.1` from `1.0.0` (honesty — we haven't shipped 1.0 yet).

## Test suite

- Started: 670 / 664 passed / 6 inconclusive (live smoke)
- Ended: 692 / 686 passed / 6 inconclusive
- +22 new tests covering `FeatureGateConfigService`, `ApiKeyStore`, `SwBuildStateService`, expanded `FeatureGateRegistry` (10 known gates, defaults, default-on/off sanity)

## Open blockers carried to next session

1. **R2 live validation** — user must relaunch SW with `SOLIDWORKS_AI_CONTEXT_MENU=true` at user scope, observe `host.log`. Until then R1 isn't closed.
2. **R3.1 pre-update eject** — code not written yet
3. **R4.6 Manager auto-emphasize Eject when updater running** — code not written yet
4. **Task Pane banner for compat-probe failures** — backend exposes `LastProbeMessage`, UI wiring deferred
5. **GitHub R1 issue filing** — draft ready, requires user paste + label application
6. **Linear seeding** — user manual per checklist, or bulk via Linear MCP next session once `LINEAR_API_KEY` populated in `.mcp.json` and Claude Code restarted
7. **CWD cleanup** — parked through R-phase completion

## Proposed commit strategy (4 logical commits)

1. **`chore: v1.0 prep — repo hygiene, end-user installers, docs realignment`**
   AGENTS.md / .stignore removal · solidworks-partner/ moves · CODE_OF_CONDUCT + plans/README · .bat installers + package-release.ps1 update · README/SETUP end-user sections · docs/index.html + CHANGELOG reverted to honest v0.1.1 status
2. **`feat(ui + config): v1.0 config-aware feature gates, DPAPI API key store, Settings panel, user-facing labels`**
   FeatureGateConfigService + 10-gate FeatureGateRegistry · ApiKeyStore + BrokerModelSettings fallback · 10 grounding tool summary relabels · Task Pane Settings section + JS bridge · HostState FormatAnswerSourceForUser + bridge methods · AssemblyVersion 1.0.0.0 source bump · csproj updates · tests
3. **`fix(r2): R2026x interop resilience — compatibility probe + CSE-aware context menu + build-version staleness`**
   CompatibilityProbe · ContextMenu CSE attributes · AdzeAddIn probe integration · SwBuildStateService · context-menu default false · tests
4. **`feat(r4): Adze.Manager installer/manager UI + R-phase documentation + .mcp.json entries`**
   src/Adze.Manager WinForms app · Adze.sln updated · plans/polish-and-v1-path.md rewrite + R1–R6 + follow-ups · crash-investigation-20260419.md · documentation-structure · release-process · github-labels · linear-adoption-checklist · github-issue-v1-crash-draft · crash_report.md template · .mcp.json linear+slack+sentry

## Cross-references

- `.ava/handoffs/handoff-2026-04-19T11-34-40.yaml` — session handoff
- `~\.pe-health\adze-cad.json` — updated health beacon
- `plans/polish-and-v1-path.md` — authoritative next-steps plan
- `plans/crash-investigation-20260419.md` — R1.4 deliverable
- `plans/documentation-structure.md` — four-layer stack
- `plans/release-process.md` — pre-release + publication checklist
