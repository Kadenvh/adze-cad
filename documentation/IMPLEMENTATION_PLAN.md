# Adze - Implementation Plan

**Version:** 0.1.0  
**Created:** 2026-03-11  
**Last Updated:** 2026-03-13  
**Current Phase:** Phase 2A - Hardening To First Usable Build  
**Status:** Grounded assistant alpha is real and validated; the active work is now quality hardening, launcher/install reliability, and safe-write contract design

## Current Working Baseline

The repo is no longer a speculative scaffold. It currently contains:

- a buildable 5-project C# solution
- a raw native SOLIDWORKS add-in host that registers, loads, and creates a Task Pane
- 10 live read-only grounding tools wired into the host, broker, and validation scripts
- a hybrid broker that can use Anthropic for structured turn planning with deterministic fallback
- model-backed final answer synthesis over executed tool results, also with deterministic fallback
- trace, snapshot, recipe-candidate, achievement, exploration, and unlock persistence
- an answer-first Task Pane layout with separate plan/status surfaces
- machine-readable broker and benchmark reports
- a one-command support bundle workflow for diagnostics

## Validated Baseline - 2026-03-13

| Check | Result |
|------|--------|
| `validate-json-schemas.ps1` | PASS |
| `build-all.ps1 -StopSolidWorks` | PASS |
| `run-broker-evals.ps1` | PASS (`12/12`) |
| `validate-host-spike.ps1` | PASS |
| `run-grounding-benchmarks.ps1` | PASS (`12/12`) |
| Mocked hybrid broker turn | PASS |
| Mocked model-backed answer synthesis | PASS |
| `collect-support-bundle.ps1` | PASS |
| HKLM residue for active add-in GUID | Not present |

## Active Workstreams

### 1. Grounded Answer Quality And Reliability

**State:** Active  
**Why it matters:** The system now produces model-backed grounded answers, but the eval surface still focuses more on tool selection than on answer quality.

**Next steps:**
- add answer-quality eval cases for model synthesis
- add explicit timeout/failure coverage for the synthesis path
- decide whether the assistant should surface evidence snippets or citations from tool results
- expand from the current single-turn loop toward richer multi-step execution without breaking the COM boundary

### 2. Session And Install Hardening

**State:** Active  
**Why it matters:** The most common live failures are no longer code build failures; they are launcher/login/update interruptions and the lack of a tester-friendly install/update path.

**Next steps:**
- harden launcher preflight and blocked-state messaging
- add install/update assets under `install/`
- document the clean beta setup path
- keep support-bundle collection aligned with real support needs

### 3. Assistant Workspace Polish

**State:** Active  
**Why it matters:** The Task Pane now behaves like an assistant workspace, but it still needs more product polish before it feels like the intended end-user experience.

**Next steps:**
- improve empty-state copy and no-document recovery copy further
- decide whether to surface evidence snippets in the answer panel
- decide whether recipe suggestions should appear directly in the UI
- improve run-state presentation without moving COM work out of the host thread

### 4. Safe Write-Tool Boundary

**State:** Pending definition  
**Why it matters:** The product intent eventually includes action-taking behavior, but safe writes are still intentionally blocked until the contract is explicit and testable.

**Next steps:**
- choose the first reversible write-capable tool
- define preview/apply/verify/rollback contracts
- extend trace and benchmark expectations to support write paths

## Current Risks And Constraints

- No local `dotnet` SDK is installed. The repo still assumes a Visual Studio/MSBuild-first workflow.
- Launcher-managed prerequisite windows can block live host validation even when the add-in code is healthy.
- The current eval surface is stronger for tool selection than for final answer quality.
- Packaging/install/update is still not implemented as a tester-friendly workflow.
- The runtime remains intentionally read-only. Any attempt to rush write tools before the safety contract exists would lower quality.

## Recently Completed Milestones

- Security cleanup and repo rename from `SolidWorksAi` to `Adze`
- User-scope development registration and removal of routine `RunAs` dependence
- Ten read-only grounding tools across part and assembly inspection
- Structured broker turn with blockers, recovery suggestions, and prioritized recommendations
- Model-backed final answer synthesis with deterministic fallback
- Answer-first Task Pane layout with explicit run-state messaging
- Machine-readable benchmark/eval reports
- One-command support bundle generation under `%LOCALAPPDATA%\Adze\SupportBundles`

## Immediate Task Checklist

- [ ] Add synthesis answer-quality eval cases
- [ ] Add explicit synthesis timeout/failure eval cases
- [ ] Harden launcher/update/login interruption handling
- [ ] Add beta-friendly install/update assets under `install/`
- [ ] Decide whether answer evidence snippets belong in the Task Pane
- [ ] Decide whether recipe suggestions should appear in the Task Pane
- [ ] Define the first safe write-tool contract
- [ ] Start the first retrieval/indexing slice without weakening the live grounding boundary

## Operational Commands

```powershell
pwsh -NoProfile -File scripts\setup\validate-json-schemas.ps1
```

```powershell
pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks
```

```powershell
powershell.exe -NoProfile -File scripts\setup\launch-and-check-host.ps1
```

```powershell
powershell.exe -NoProfile -File scripts\setup\validate-host-spike.ps1
```

```powershell
powershell.exe -NoProfile -File scripts\setup\run-grounding-benchmarks.ps1
```

```powershell
powershell.exe -NoProfile -File scripts\setup\run-broker-evals.ps1
```

```powershell
pwsh -NoProfile -File scripts\setup\collect-support-bundle.ps1
```

## Debugging And Support Notes

### PowerShell Windows Closing Immediately

Current diagnosis:
- not reproduced through direct terminal invocation
- not a simple execution-policy block
- more likely related to detached launch context, launcher state, or task-specific elevation needs

If it reappears:
- reproduce the exact launch path
- capture stdout/stderr/exit code to a log
- compare behavior under `pwsh` and `powershell.exe`

### Launcher Gate

Known blocking windows:
1. `Login | 3DEXPERIENCE ID | Dassault Systèmes`
2. `3DEXPERIENCE Update`

Practical rule:
- if either window is present, clear it before assuming the add-in failed

### Registration Scope

Current state:
- development registration lives under `HKCU`
- the previously noted HKLM residue for the active add-in GUID is no longer present on this machine

### Support Bundle Workflow

Current script:
- `scripts/setup/collect-support-bundle.ps1`

Current bundle contents:
- host logs
- snapshots
- state
- traces
- recipe candidates
- latest benchmark/eval reports
- machine summary
- launcher preflight output

## Handoff Notes

If a new agent or session picks this up:

1. Start with `documentation/README.md`.
2. Read `PROJECT_ROADMAP.md` for the why.
3. Read `BUILD_SPEC.md` for the boundaries and commands.
4. Use this file for current state and next work.
5. Treat launcher state as a machine/runtime variable, not an automatic code regression.

The most important current fact is that the grounded assistant loop is real. The highest-value remaining work is no longer "make it exist"; it is "make it reliable, evaluable, and beta-usable."
