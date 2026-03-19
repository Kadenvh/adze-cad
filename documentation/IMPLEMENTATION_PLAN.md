# Adze - Implementation Plan

**Version:** 0.1.0
**Created:** 2026-03-11
**Last Updated:** 2026-03-18
**Current Phase:** Phase 9 ecosystem enhancements active. T9-01 (HTML panel), T9-05 (chat history), T4-09 (write confirmation) complete. Next: T9-02 (diagnostic intent), multi-turn agent context, Phase 6 retrieval (OLE indexer), live SOLIDWORKS testing.
**Status:** Full conversational UI implemented: HTML answer panel with WebBrowser, chat-style conversation thread, write confirmation cards with Apply/Cancel. 378 tests passing. Remaining: diagnostic intent routing, agent loop multi-turn context, OLE closed-file indexer, write history/undo.

## Current Working Baseline

- a buildable 6-project C# solution (5 production + 1 test)
- a native SOLIDWORKS add-in host with Task Pane UI (visual acceptance confirmed)
- 10 live read-only grounding tools + 4 first-wave write tools
- hybrid broker with OpenAI/Anthropic/OpenRouter provider routing and deterministic fallback
- model-backed final answer synthesis with deterministic fallback
- per-run and session-level token usage monitoring (API response → answer footer → Status tab)
- 378 compiled NUnit unit tests + 6 live provider smoke tests (all passing)
- **agentic tool loop** (Phase 2): `OpenAIFormatAgentClient`, `AgentLoopRunner`, `AgentToolDispatcher`, `ToolDefinitionBuilder`, `AgentModelClientFactory`. Feature-gated behind `SOLIDWORKS_AI_AGENT_LOOP=true`. Existing single-turn path remains default fallback.
- **write tool safety infrastructure** (Phase 3): `IStateSnapshotService`, `IStateDiffService`, `IVerificationPolicy`, `StateDiffService`, `DefaultVerificationPolicy`, `WriteTraceRecordBuilder`. All contracts and pure logic implementations with 30 tests.
- **first-wave write tools** (Phase 4 core): `SetCustomPropertyTool`, `SetDimensionValueTool`, `SuppressFeatureTool`, `UnsuppressFeatureTool`. Full `IWriteTool<TParams>` implementations with preview/apply/verify/undo lifecycle. `WriteExecutionCoordinator` orchestrates the 8-step write lifecycle. Agent dispatch integration with feature gate `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true`. 36 tests.
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
| `run-tests.ps1` | PASS (`378` total, 372 passed, 6 inconclusive smoke tests without env vars) |
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
- [x] **Phase 2 live test:** Agentic loop verified in SOLIDWORKS via OpenRouter — model called get_dimensions autonomously, synthesized grounded answer (1866 tokens, outcome=Success)
- [x] **Phase 3:** Implement snapshot/diff verification layer (IStateSnapshotService, IStateDiffService, IVerificationPolicy, StateDiffService, DefaultVerificationPolicy, WriteTraceRecordBuilder — 30 tests)
- [x] **Phase 4 core:** Implement first-wave write tools (set_custom_property, set_dimension_value, suppress_feature, unsuppress_feature) with IWriteTool, WriteExecutionCoordinator, agent dispatch, feature gate — 36 tests
- [x] **Phase 4 UI:** WritePreview confirmation card in chat thread with Apply/Cancel buttons, PendingWriteAction tracking, direct COM apply (T4-09)
- [x] **Phase 5:** Learning activation — ITrustService, TrustService, AgentRecipeCaptureService, write tool achievements, TrustedBounded tier progression — 14 tests
- [x] **Phase 6 core:** Per-document memory (DocumentMemory, MemoryStore) and user preference storage — 12 tests
- [ ] **Phase 6 retrieval:** OLE Structured Storage closed-file indexer (requires OpenMcdf NuGet)
- [x] **Phase 7/8 partial:** Cost budget controls (CostBudgetSettings, BudgetStatus), FeatureGateRegistry — 11 tests
- [x] Decide whether answer evidence snippets belong in the Task Pane → YES, via HTML answer panel (T9-01)
- [x] Decide whether recipe suggestions should appear in the Task Pane → YES, accelerate T5-04/T9-03
- [x] **Phase 2 live test (b):** Write tools verified — model called get_active_document → set_custom_property (preview), synthesized grounded answer (2881 tokens, 3 turns, outcome=Success)
- [x] **Phase 9 (ecosystem):** HTML answer panel with WebBrowser control, tab sync, InvokeScript status refresh (T9-01)
- [ ] **Phase 9 (ecosystem):** "What's Wrong" diagnostic intent (T9-02)
- [x] **Phase 9 (ecosystem):** Conversational chat history — ChatEntry tracking, user/assistant bubbles, document-aware clearing (T9-05)
- [x] **Ecosystem research:** `documentation/plans/research-solidworks-ai-ecosystem.md` — AURA/LEO/Labs/competitors mapped

## Handoff Notes

If a new agent or session picks this up:

1. Read `CLAUDE.md` for critical rules and commands.
2. Read `documentation/plans/END-GOAL-FINAL.md` for the complete agentic vision (700 lines).
3. Read `documentation/tasks/TASK-INDEX.md` for the comprehensive task breakdown (now with completion markers).
4. Read `documentation/plans/IMPLEMENTATION-BLUEPRINT.md` for C# interface contracts.
5. The 7 research briefs in `documentation/plans/research-*.md` are the validated evidence base.
6. Phases 1A through 7/8 (core infrastructure) are implemented. 378 tests passing.
7. Feature gates: `SOLIDWORKS_AI_AGENT_LOOP=true` (agentic loop), `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true` (write tools). See `FeatureGateRegistry` for all 5 gates.
8. Next priorities: (a) live test write confirmation flow in SOLIDWORKS, (b) "What's Wrong" diagnostic intent — T9-02, (c) multi-turn agent context — pass chat history to agent loop, (d) OLE closed-file indexer — T6-03, (e) write history/undo surface — T4-10.
9. Key new infrastructure this session (2026-03-18): HTML answer panel (WebBrowser + tab sync + InvokeScript), conversational chat history (ChatEntry + document-aware clearing), write confirmation cards (PendingWriteAction + Apply/Cancel + direct COM apply), write-tracking executor wrapper.
10. Prior session: write contracts + tools, snapshot/diff/verification, write execution coordinator, trust service, recipe capture, per-document memory, cost budgets, feature gate registry.
