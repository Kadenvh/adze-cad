# Session 2 — R1 Resolution + R-phase Wrap-up + GitHub Polish

**Date:** 2026-04-19 (late afternoon through evening)
**Version at start:** 0.1.1 shipped · v1.0.0 code-complete but crash-blocked
**Version at close:** 0.1.1 shipped · v1.0.0 code-complete AND crash-resolved
**Branch:** `main`
**Duration:** ~3 hour push
**Outcome:** R1 closed via live probe, 5 R-follow-ups landed, 9 commits pushed to origin, GitHub repo polished, R5 live validation cycle the only remaining release gate

---

## One-line summary

Validated R2 against the live post-update SW (Outcome B: `CreateCommandGroup2` confirmed as the broken interop call; probe catches it gracefully), landed R2.5/R3.1/R3.4/R4.5/R4.6 + VHT-20 fix in 5 commits, filed and closed GitHub #7 as post-mortem for R1, created 21 repo labels, backfilled issue labels, refreshed repo description + 13 topics, and pushed all 9 session-1+session-2 commits to `origin/main`.

## What happened, in order

### 1. Task Pane docking — user-side resolution

Started session with Adze auto-loaded but user reporting that the SW Task Pane (all tabs, not just Adze) was "locked to the taskbar". Investigation showed the Task Pane was a floating MFC dockable pane whose saved coordinates placed it at screen `y=1075` — behind the Windows taskbar at `y=1040–1080`, making its title bar unreachable. Attempted registry fix (deleting `HKCU\SOFTWARE\SolidWorks\SOLIDWORKS 2026\DockingPaneLayouts`) was insufficient because the floating-position coordinates live in binary MFC bar state inside `General-Bar*` entries. User solved it in 10 seconds via Windows "auto-hide taskbar" setting — reveal the pane, drag-dock to right edge, pin. Recorded as a feedback memory: try OS-level UI workarounds before registry surgery for MFC pane placement bugs.

### 2. Linear seeding

Seeded Linear workspace `vhtech` project `ADZ` via MCP:
- 7 new labels (`blocker`, `research`, `docs`, `interop`, `installer`, `ui`, `v1.0-blocker`) on top of existing `Bug`/`Feature`/`Improvement`
- 6 R-phase epics (VHT-5 through VHT-10) with priority, state, description
- 9 child issues for R1.1–R1.4, R2.5, R3.1, R3.4, R4.5, R4.6
- Blocker chain: R1 ← R2 ← R3; R1/R2/R3/R4 ← R5 ← R6

### 3. R1/R2 live validation — Outcome B

User set `SOLIDWORKS_AI_CONTEXT_MENU=true` at user scope (env var didn't propagate because 3DX Launcher cached env, but probe fired anyway). Post-update SW 34.1.0.0140 `host.log`:

```
[2026-04-19 19:47:09Z] INFO ConnectToSW starting.
[2026-04-19 19:47:10Z] INFO Task Pane created. AddControl returned: ControlAxSourcingSite [Adze.Host.UI.TaskPaneControl].
[2026-04-19 19:47:10Z] INFO Compatibility probe failed at step 'create-command-group'. Ribbon and context-menu registration will be skipped. CreateCommandGroup2 returned null or error=1.
[2026-04-19 19:47:10Z] INFO Ribbon skipped: compatibility probe previously failed at step 'create-command-group'.
[2026-04-19 19:47:10Z] INFO Context-menu gate SOLIDWORKS_AI_CONTEXT_MENU is off; skipping.
[2026-04-19 19:47:10Z] INFO Snapshot captured. reason=Initial context snapshot tool_count=10 achievements=14 review_ready_recipes=194
[2026-04-19 19:47:10Z] INFO ConnectToSW completed.
```

No crash. Task Pane + 10 tools operational. **`CreateCommandGroup2` confirmed as the R2026x 34.1.0.0140 interop break** — shared dependency of both ribbon and context-menu registration paths, which is why toggling `SOLIDWORKS_AI_CONTEXT_MENU=false` in session 1 was only a partial mitigation.

### 4. Five R-follow-up commits

| Commit | Scope |
|---|---|
| `0620c59` | R2.5 probe banner in Task Pane (`BuildProbeBannerHtml` + `HostState.SetProbeResult/GetProbeFailure`), R3.1 pre-update eject (`HandlePreUpdateEjectIfNeeded` in `AdzeAddIn`), R4.6 Manager Eject emphasis (red bold + Install disabled when `swxdesktopupdate.exe` detected) |
| `5fea910` | R4.5 README.md + SETUP.md rewritten around Manager UI; R3.4 PRIVACY.md "Update lifecycle and compatibility state" section + refreshed API keys section |
| `e295ea0` | VHT-20 fix — moved `RunCompatibilityProbe()` before `CreateTaskPane()` so probe state is in `HostState` when the Task Pane's first `BuildFullPageHtml` runs. Banner now renders on initial load instead of only after first assistant run |
| `d3b3de2` | Extracted `PreUpdateEjectService` with injectable `ProcessesByNameProvider` + `ClearBuildStateOverride` test hooks. Added `[InternalsVisibleTo("Adze.Tests")]` to Adze.Host. Wrote 10 new tests (4 for HostState probe state + 6 for PreUpdateEjectService detection / clear-state / exception-tolerance). Test count 692 → 702 |
| `8d0421c` | CHANGELOG `[Unreleased]` section rewritten to reflect R-phase completion + 702 tests |

Plus docs/index.html KPI bump (670 → 702) and Install section mentioning Adze Manager.

### 5. GitHub #7 post-mortem

Filed GitHub issue #7 as a resolved-post-mortem documenting the R1 incident + R2/R2.5/R3.1/R4.6 resolution. Closed with `state_reason: completed` same session. Linked from Linear VHT-5 as the public mirror. Body documents symptoms, root cause (CreateCommandGroup2), resolution (probe + CSE attrs + banner + eject + Manager), validation log excerpt, and the specific commit shas.

### 6. GitHub repo polish

Installed gh CLI 2.90.0 via winget. User authenticated as `Kadenvh`. Used gh to:

- Create 21 custom labels per `plans/github-labels.md` (crash, installer, update-lifecycle, interop, ai-provider, ui, tool, docs, privacy-security, v1.0-blocker, v1.x-followup, future, needs-triage, needs-info, in-progress, sw-makers, sw-2025, sw-2026, sw-3dx-r2026x, partner-feedback, maker-community)
- Backfill issue #7 with 7 labels (`bug`, `crash`, `update-lifecycle`, `interop`, `sw-3dx-r2026x`, `sw-makers`, `v1.0-blocker`)
- Set repo description: *"Native AI assistant for SOLIDWORKS. In-process COM add-in, agentic loop, governed write tools. MIT licensed. v0.1.1 public beta; v1.0 pending live R5 validation."*
- Add repo topics totaling 13: solidworks, cad, ai-assistant, dotnet, csharp, com-addin, solidworks-addin, mcp, openai, anthropic, ollama, 3dexperience, agentic-ai

### 7. Push

`git push origin main` — all 9 commits live on origin. GitHub warning about 2 required status checks missing (no CI configured yet; parked for v1.1).

## Linear state at close

| ID | Title | Status |
|---|---|---|
| VHT-5 | R1 Crash root cause | Done · GitHub #7 linked |
| VHT-6 | R2 Interop resilience | Done |
| VHT-7 | R3 Update-lifecycle | In Review (R3.1 awaits R5.5 live test) |
| VHT-8 | R4 Adze.Manager UI | Done |
| VHT-9 | R5 Release-gate validation | Todo (user-driven) |
| VHT-10 | R6 Partner resubmission | Backlog (blocked by R5) |
| VHT-11 | R1.1 WinDbg | Canceled (probe identified faulting call directly) |
| VHT-12 | R1.2 Binary diff | Canceled (same) |
| VHT-13 | R1.3 Gate-toggle repro | Done |
| VHT-14 | R1.4 Forensic record | Done |
| VHT-15 | R2.5 Probe banner | Done |
| VHT-16 | R3.1 Pre-update eject | In Review (awaits R5.5 live test) |
| VHT-17 | R3.4 Update-flow docs | Done |
| VHT-18 | R4.5 README/SETUP rewrite | Done |
| VHT-19 | R4.6 Manager Eject emphasis | Done |
| VHT-20 | R2.5a Initial-render banner fix | Done |

## Test suite

| Session | Start | End |
|---|---|---|
| Session 2 | 692 (686 passed, 6 inconclusive) | 702 (696 passed, 6 inconclusive) |

Ten new tests: 4 for `HostState.SetProbeResult/GetProbeFailure` round-trip + transitions, 6 for `PreUpdateEjectService` detection / clear-state invocation / exception tolerance.

## brain.db updates

**Decisions added:**
- #16 — Linear seeded 2026-04-19 via MCP (R1–R6 epics + key follow-ups, labels, blocker chain)

**Notes added (handoff + improvement):**
- R1 CLOSED + R2 VALIDATED
- R-phase follow-ups all landed
- GitHub housekeeping (gh CLI + labels + #7 + repo meta)
- Next-session path to v1.0.0 tag
- Process improvement: closeout-before-push + use /ship skill by default

**Notes completed:**
- R1 BLOCKER (now resolved)
- R1 MITIGATION (ContextMenu default flip is now permanent for 34.1.0.0140, not temporary)
- Task Pane docking reset (partial mitigation; actual fix was user's auto-hide-taskbar workaround)

## Open blockers carried to next session

1. **R5 live validation cycle** — user-driven. R5.1 + R5.2 + R5.6 + R5.7 cheap on this machine. R5.3 + R5.5 require a real 3DX update OR soft-validation via a stub process named `swxdesktopupdate.exe`.
2. **GitHub CI workflow** — 2 required status checks expected by branch protection but not yet configured. Parked for v1.1.
3. **Linear UI tasks** — enable cycles in project settings, configure bidirectional GitHub sync, publish public roadmap + link from README. User-driven via Linear web UI.

## Cross-references

- `.ava/handoffs/handoff-2026-04-19T21-07-07.yaml` — session handoff
- `plans/polish-and-v1-path.md` — authoritative R-phase plan with 2026-04-19 late-session status update
- `plans/crash-investigation-20260419.md` — R1 forensic record
- `CHANGELOG.md` — `[Unreleased]` section reflects R-phase completion
- GitHub #7 — public R1 post-mortem, closed
- Linear project ADZ — private roadmap with R1–R6 epic structure

## Process improvement

Ran `git push` before `/session-closeout`. Correct order is closeout → push (ideally via `/ship` skill which adds secret scanning + attribution + security checks before publishing). Recorded as a process-improvement note for future sessions. Not fatal this session — closeout produced only `sessions/session-2.md` and a handoff YAML, which get pushed in a trailing commit.
