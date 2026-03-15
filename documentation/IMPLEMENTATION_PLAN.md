# Adze - Implementation Plan

**Version:** 0.1.0
**Created:** 2026-03-11
**Last Updated:** 2026-03-15
**Current Phase:** Phase 2 (Agentic Tool Loop) complete. Next: Phase 3 (Snapshot/Diff/Verification) and Phase 4 (First Write Tools).
**Status:** Agentic tool loop is implemented and integrated. The assistant can now iteratively call tools, observe results, and generate grounded answers through a model-driven loop. 275 tests passing. Next: write tool safety infrastructure and first write tools.

## Current Working Baseline

- a buildable 6-project C# solution (5 production + 1 test)
- a native SOLIDWORKS add-in host with Task Pane UI (visual acceptance confirmed)
- 10 live read-only grounding tools
- hybrid broker with OpenAI/Anthropic/OpenRouter provider routing and deterministic fallback
- model-backed final answer synthesis with deterministic fallback
- per-run and session-level token usage monitoring (API response → answer footer → Status tab)
- 275 compiled NUnit unit tests + 6 live provider smoke tests (all passing)
- **agentic tool loop** (Phase 2): `OpenAIFormatAgentClient`, `AgentLoopRunner`, `AgentToolDispatcher`, `ToolDefinitionBuilder`, `AgentModelClientFactory`. Feature-gated behind `SOLIDWORKS_AI_AGENT_LOOP=true`. Existing single-turn path remains default fallback.
- pre-prompt clarification UI with Intent/Scope/Output/Diagnostics axes populated from live SessionContext
- conversation state with sliding window truncation (`AgentConversationState`, `ConversationTruncator`)
- cancel button support with `CancellationTokenSource` lifecycle
- launcher interruption detection with multi-pattern blocker scanning, JSON preflight report, retry with timeout, and validation preflight gate
- beta install/uninstall/packaging workflow (`install/install-adze.ps1`, `uninstall-adze.ps1`, `package-release.ps1`)
- Task Pane run state label shows meaningful status for blocked/disconnected states and token counts
- trace/progression/recipe/achievement persistence
- machine-readable broker and benchmark reports
- one-command support bundle workflow

## Validated Baseline - 2026-03-15

| Check | Result |
|------|--------|
| `build-all.ps1 -StopSolidWorks` | PASS |
| `run-tests.ps1` | PASS (`175/175`, 6 inconclusive smoke tests without env vars) |
| `run-broker-evals.ps1` | PASS (`12/12`) |
| `validate-host-spike.ps1` | PASS |
| `run-grounding-benchmarks.ps1` | PASS (`12/12`) |
| `run-provider-smoke.ps1` via OpenRouter | PASS (`6/6`) with usage tracking verified |
| Human visual acceptance of Task Pane | PASS (confirmed 2026-03-15) |
| Beta install/uninstall scripts | Created and reviewed |

## Active Workstreams

### 1. Agentic Implementation (PRIMARY)

**State:** Planning complete, implementation ready
**Why it matters:** The grounded alpha is proven. The next product leap is making the assistant feel agentic — clarification before acting, iterative tool use, and eventually governed writes.

**Authoritative plan:** `documentation/plans/END-GOAL-FINAL.md` (700 lines — vision, architecture layers, capability classes, phase order, success criteria)
**Task breakdown:** `documentation/tasks/TASK-INDEX.md` (60+ tasks across 8 phases)
**Tactical contracts:** `documentation/plans/IMPLEMENTATION-BLUEPRINT.md` (C# interface specs inline)

**Immediate next steps (Phase 1A + 1B, can run in parallel):**
- Add pre-prompt clarification controls to Task Pane (Intent/Scope/Output/Diagnostics)
- Add conversation state (AgentConversationState, truncation, follow-up turns)
- Then Phase 2: agentic tool loop with native API tool calling

### 2. Evidence and Research Base

**State:** Complete
**Artifacts in `documentation/plans/`:**
- 4 discovery briefs (API tool use, SOLIDWORKS write API, agent loop architecture, clarification UI)
- 7 research briefs (write safety/rollback, threading, tool-calling abstraction, local model feasibility, OpenClaw feasibility, closed-file retrieval, streaming UX)

**Key validated findings:**
- OpenRouter unifies both providers under one tool-calling format
- `set_custom_property` is the safest first write tool (no rebuild, clean API)
- Undo grouping via `StartRecordingUndoObject` / `FinishRecordingUndoObject`
- No mid-loop COM refresh needed for Phase 1 (eliminates deadlock risk)
- OLE Structured Storage enables zero-dependency closed-file indexing at ~1-5ms/file
- OpenClaw evaluated and declined for runtime integration (dev-workflow tool only)
- Local models work for synthesis but unreliable for tool calling below 32B params

## Current Risks And Constraints

- No local `dotnet` SDK. Visual Studio/MSBuild-first workflow.
- Launcher-managed prerequisite windows can block live validation (now detected and reported via JSON preflight).
- Runtime remains intentionally read-only until write contracts pass Gate D.
- No real provider API key in default shell environment (OpenRouter key used for smoke tests this session).

## Recently Completed Milestones

- Live provider smoke tests (6 tests via OpenRouter) with usage tracking verified
- Full token usage monitoring pipeline (ModelUsage contract → API parsing → session accumulation → Status tab → answer footer)
- Launcher interruption hardening (multi-pattern detection, CATSTART scanning, JSON preflight, retry loop, validation preflight gate with exit code 3)
- Beta install/uninstall/packaging (install-adze.ps1, uninstall-adze.ps1, package-release.ps1)
- Task Pane messaging improvement (meaningful run state for blocked/disconnected states, token counts)
- Visual acceptance pass confirmed (Task Pane praised as "really good" and "AMAZING")
- Full agentic vision validated through 4 discovery + 7 research briefs
- END-GOAL-FINAL.md compiled with external agent review
- IMPLEMENTATION-BLUEPRINT.md with C# interface contracts
- TASK-INDEX.md with 60+ tasks across 8 phases
- 41 DAL facts covering all architecture decisions, risks, phase gates, and invariants
- Documentation cleanup: 4 stale plans archived, plans README updated, BUILD_SPEC updated

## Immediate Task Checklist

- [x] Add synthesis answer-quality eval cases
- [x] Run a live provider-backed smoke test with a real API key
- [x] Add token usage monitoring across broker clients, host, and Status tab
- [x] Capture a human visual acceptance pass of the Task Pane
- [x] Harden launcher/update/login interruption handling
- [x] Add beta-friendly install/update assets under `install/`
- [x] Complete agentic vision discovery and research (11 briefs)
- [x] Compile END-GOAL-FINAL.md with external agent validation
- [x] Create comprehensive task breakdown (TASK-INDEX.md)
- [x] **Phase 1A:** Add pre-prompt clarification UI to Task Pane
- [x] **Phase 1B:** Add conversation state and follow-up turn support
- [x] **Phase 2:** Implement agentic tool loop with native API tool calling (OpenAIFormatAgentClient, AgentLoopRunner, AgentToolDispatcher, ToolDefinitionBuilder, HostState integration, cancel support)
- [ ] **Phase 2 live test:** Run agentic loop with real API key in SOLIDWORKS to verify end-to-end
- [ ] **Phase 3:** Implement snapshot/diff verification layer (IStateSnapshotService, IStateDiffService, IVerificationPolicy)
- [ ] **Phase 4:** Implement first-wave write tools with confirmation UI (set_custom_property, set_dimension_value, suppress_feature)
- [ ] Decide whether answer evidence snippets belong in the Task Pane
- [ ] Decide whether recipe suggestions should appear in the Task Pane

## Handoff Notes

If a new agent or session picks this up:

1. Read `CLAUDE.md` for critical rules and commands.
2. Read `documentation/plans/END-GOAL-FINAL.md` for the complete agentic vision (700 lines).
3. Read `documentation/tasks/TASK-INDEX.md` for the comprehensive task breakdown.
4. Read `documentation/plans/IMPLEMENTATION-BLUEPRINT.md` for C# interface contracts.
5. The 7 research briefs in `documentation/plans/research-*.md` are the validated evidence base.
6. Phases 1A, 1B, and 2 are implemented. The agentic tool loop is live behind `SOLIDWORKS_AI_AGENT_LOOP=true`.
7. Next: live test the agent loop in SOLIDWORKS, then build Phase 3 (snapshot/diff/verification) and Phase 4 (first write tools).
8. The 7 research briefs in `documentation/plans/research-*.md` validated all platform-specific execution concerns.
