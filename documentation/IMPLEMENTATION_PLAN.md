# Adze - Implementation Plan

**Version:** 0.1.0  
**Created:** 2026-03-11  
**Last Updated:** 2026-03-15  
**Current Phase:** Phase 2A - Hardening To First Usable Build  
**Status:** Grounded assistant alpha is real and validated; the active work is now quality hardening, launcher/install reliability, UI acceptance, and safe-write contract design

## Current Working Baseline

The repo is no longer a speculative scaffold. It currently contains:

- a buildable 6-project C# solution (5 production + 1 test)
- a raw native SOLIDWORKS add-in host that registers, loads, and creates a Task Pane
- 10 live read-only grounding tools wired into the host, broker, and validation scripts
- a hybrid broker that can use OpenAI or Anthropic for structured turn planning with deterministic fallback
- model-backed final answer synthesis over executed tool results, also with deterministic fallback
- trace, snapshot, recipe-candidate, achievement, exploration, and unlock persistence
- an answer-first Task Pane layout with separate `Plan`, `Status`, and `Tools` surfaces
- background provider execution after host-thread context capture so slow network calls do not freeze the Task Pane
- COM traversal cleanup and logged graceful degradation in the session-context builder
- 166 compiled NUnit unit tests covering broker orchestration, model response parsing, configuration, prompt composition, all 10 grounding tools, trace serialization, deterministic answer building, tool results formatting, and synthesis service orchestration
- machine-readable broker and benchmark reports
- a one-command support bundle workflow for diagnostics

## Validated Baseline - 2026-03-15

| Check | Result |
|------|--------|
| `validate-json-schemas.ps1` | PASS |
| `build-all.ps1 -StopSolidWorks` | PASS |
| `run-tests.ps1` | PASS (`166/166`) |
| `run-broker-evals.ps1` | PASS (`12/12`) |
| `validate-host-spike.ps1` | PASS |
| `run-grounding-benchmarks.ps1` | PASS (`12/12`) |
| Provider selection matrix | PASS |
| `collect-support-bundle.ps1` | PASS |
| HKLM residue for active add-in GUID | Not present |
| Live external provider smoke with real API key | Pending local key availability |
| Human visual acceptance of latest Task Pane overhaul | Pending desktop confirmation |

## Active Workstreams

### 1. Grounded Answer Quality And Reliability

**State:** Active  
**Why it matters:** The system now produces model-backed grounded answers, but the eval surface still focuses more on tool selection than on answer quality.

**Next steps:**
- run a real OpenAI or Anthropic smoke test with a local API key and record the observed answer source in logs
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
- improve run-state presentation while preserving the current split between host-thread context capture and background model execution
- capture a direct visual acceptance pass for rendering, resize behavior, and status-tab scroll preservation inside SOLIDWORKS

### 4. Safe Write-Tool Boundary

**State:** Pending definition  
**Why it matters:** The product intent eventually includes action-taking behavior, but safe writes are still intentionally blocked until the contract is explicit and testable.

**Next steps:**
- choose the first reversible write-capable tool
- define preview/apply/verify/rollback contracts
- extend trace and benchmark expectations to support write paths

## Current Risks And Constraints

- No local `dotnet` SDK is installed. The repo still assumes a Visual Studio/MSBuild-first workflow. NUnit tests run via the NUnit3 console runner and NuGet packages restored with `nuget.exe` under `tools/`.
- Launcher-managed prerequisite windows can block live host validation even when the add-in code is healthy.
- The current eval surface is stronger for tool selection than for final answer quality.
- No real provider API key is available in the current shell environment, so external provider smoke validation is still pending.
- The latest Task Pane overhaul is build-validated and host-validated, but its current visual acceptance still needs a direct desktop check inside SOLIDWORKS.
- Packaging/install/update is still not implemented as a tester-friendly workflow.
- The runtime remains intentionally read-only. Any attempt to rush write tools before the safety contract exists would lower quality.

## Recently Completed Milestones

- Security cleanup and repo rename from `SolidWorksAi` to `Adze`
- User-scope development registration and removal of routine `RunAs` dependence
- Ten read-only grounding tools across part and assembly inspection
- Structured broker turn with blockers, recovery suggestions, and prioritized recommendations
- Provider-routed model-backed planning and final answer synthesis with deterministic fallback
- Answer-first Task Pane workspace with `Plan`, `Status`, and `Tools` tabs, active-tab refresh, and preserved status scrolling
- Background model execution after UI-thread context capture
- COM child-object release and diagnostic logging across session-context traversal
- Machine-readable benchmark/eval reports
- One-command support bundle generation under `%LOCALAPPDATA%\Adze\SupportBundles`
- Compiled NUnit 3 unit test suite (166 tests) covering broker, tools, trace serialization, configuration, prompt composition, deterministic answer building, tool results formatting, and synthesis service orchestration — all passing in under 1 second
- Moved pure-logic synthesis types (GroundingAnswerBuilder, GroundingToolResultsBuilder, GroundingSynthesisService) from Host to Broker to enable unit testing without SOLIDWORKS COM dependencies
- Portable setup scripts using `$PSScriptRoot`-relative paths instead of hardcoded repo locations

## Immediate Task Checklist

- [x] Add synthesis answer-quality eval cases (36 unit tests covering answer builder, tool results builder, and synthesis service)
- [x] Add explicit synthesis timeout/failure eval cases (synthesis service tests cover null client, model failure, empty/whitespace response, failure reason normalization)
- [ ] Run a live provider-backed smoke test with a real API key
- [ ] Capture a human visual acceptance pass of the latest Task Pane overhaul
- [ ] Harden launcher/update/login interruption handling
- [ ] Add beta-friendly install/update assets under `install/`
- [ ] Decide whether answer evidence snippets belong in the Task Pane
- [ ] Decide whether recipe suggestions should appear in the Task Pane
- [ ] Define the first safe write-tool contract
- [ ] Start the first retrieval/indexing slice without weakening the live grounding boundary

## Operational Commands

See root `CLAUDE.md` for all build, test, and validation commands.

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
