# Adze - Implementation Plan

**Version:** 0.1.0
**Created:** 2026-03-11
**Last Updated:** 2026-03-23
**Current Phase:** IUiThreadInvoker abstraction, session telemetry, and cost budget UI complete. Next: advanced write tools (T7-03/T7-04), large assembly testing.
**Status:** Full tool surface (11 read + 4 write + 1 retrieval), SSE streaming (both paths), health check UI, capability probing, recipe suggestions UI, local model support, rate limiting, tool result truncation, write plan review UI, session telemetry dashboard, cost budget UI with warning banners, IUiThreadInvoker for COM threading. 537 tests passing. 7 projects (6 production + 1 test).

## Current Working Baseline

- a buildable 7-project C# solution (6 production + 1 test)
- a native SOLIDWORKS add-in host with Task Pane UI (visual acceptance confirmed)
- 11 read-only grounding tools + 4 first-wave write tools + 1 retrieval tool
- hybrid broker with OpenAI/Anthropic/OpenRouter/Ollama/LM Studio provider routing and deterministic fallback
- model-backed final answer synthesis with deterministic fallback
- per-run and session-level token usage monitoring (API response → answer footer → Status tab)
- 503 compiled NUnit unit tests + 6 live provider smoke tests (all passing)
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
- [x] **Phase 6 retrieval:** OLE Structured Storage closed-file indexer — Adze.Index project with OpenMcdf, OlePropertyReader, ClosedFileIndexer, ClosedFileSearchService, 13 tests
- [x] **Phase 7/8 partial:** Cost budget controls (CostBudgetSettings, BudgetStatus), FeatureGateRegistry — 11 tests
- [x] Decide whether answer evidence snippets belong in the Task Pane → YES, via HTML answer panel (T9-01)
- [x] Decide whether recipe suggestions should appear in the Task Pane → YES, accelerate T5-04/T9-03
- [x] **Phase 2 live test (b):** Write tools verified — model called get_active_document → set_custom_property (preview), synthesized grounded answer (2881 tokens, 3 turns, outcome=Success)
- [x] **Phase 9 (ecosystem):** HTML answer panel with WebBrowser control, tab sync, InvokeScript status refresh (T9-01)
- [x] **Phase 9 (ecosystem):** "What's Wrong" diagnostic intent — clarification prefix parsing, keyword expansion, diagnostic tool boosting, intent-aware prompts (T9-02)
- [x] **Phase 9 (ecosystem):** Conversational chat history — ChatEntry tracking, user/assistant bubbles, document-aware clearing (T9-05)
- [x] **Ecosystem research:** `documentation/plans/research-solidworks-ai-ecosystem.md` — AURA/LEO/Labs/competitors mapped
- [x] **Phase 6 dispatch (T6-04):** `SearchProjectFilesTool` wired into ToolCatalog, AgentToolDispatcher, ToolDefinitionBuilder. Feature-gated behind `SOLIDWORKS_AI_RETRIEVAL=true`. 13 tests.
- [x] **Phase 5/9 (T5-04/T9-03):** Recipe suggestions collapsible section in Task Pane — shows promoted + review-ready recipes with Run/Promote buttons. `AgentRecipeCaptureService.ListReviewReady()`. 3 tests.
- [x] **Phase 8 (T8-02):** Local model support — Ollama and LM Studio as experimental providers. Routes through OpenAI client. Longer default timeouts. `IsLocalProvider`/`UsesOpenAIFormat`. 10 tests.
- [x] **Phase 8 (T8-03):** Final-answer streaming — `SseStreamReader`, `IStreamingModelClient`, `OpenAIModelClient.SynthesizeStreaming`, TaskPaneControl streaming JS. Feature-gated behind `SOLIDWORKS_AI_STREAM_FINAL_TEXT=true`. 19 tests.
- [x] **Phase 8 (hardening):** Local endpoint health check (`LocalEndpointHealthCheck`, pings `/v1/models`), graceful degradation messaging for local model failures. 18 tests.
- [x] **TASK-INDEX.md:** Updated 8+ stale completion markers (T4-10, T5-04, T6-03, T6-04, T8-02, T9-02, T9-03, T9-05 multi-turn).
- [x] **Phase 8 (T8-03 agentic):** Agentic loop final-turn streaming — `IStreamingAgentModelClient`, `OpenAIFormatAgentClient.SendTurnStreaming`, `AgentLoopRunner` streaming overload, HostState wiring. 12 tests.
- [x] **Phase 8 (T8-NEW):** Health check wired into Task Pane — `HostState.RunLocalHealthCheckAsync()` on background thread, styled health banners in Status section with actionable guidance.
- [x] **Phase 8 (T8-02b):** Capability gate probing — `ToolCallCapabilityProbe` sends minimal tool-calling request to local models, caches result, `AgentModelClientFactory` falls back to synthesis-only when tool calling unsupported. 13 tests.
- [x] **Phase 8 (T8-02c):** Experimental label for local providers — `[Experimental]` in answer footer (both classic and agentic paths) + "Local model support is experimental." guidance in health check ready banner.
- [x] **Phase 8 (T8-05):** Rate-limit handling — `RateLimitHelper` (429 detection, Retry-After parsing capped at 15s, cancellation-aware wait). Retry-with-backoff (max 1 retry) in `OpenAIModelClient`, `AnthropicMessagesModelClient`, `OpenAIFormatAgentClient`. 7 tests.
- [x] **Phase 8 (T8-04 partial):** Large assembly performance — `AgentLoopRunner` enforces `MaxToolResultChars` (default 8192) truncation on tool results. `GetReferenceGraphTool` `Limit` parameter (default 100). 4 tests.
- [x] **Phase 7 (T7-01):** Multi-step plan review UI — "Write Plan (N steps)" header with Apply All / Cancel All buttons when 2+ pending writes. Plan CSS.
- [x] **Phase 7 (T7-02):** Batch write execution — `ApplyAllPendingWrites()` sequential apply with stop-on-first-failure, `CancelAllPendingWrites()`. JS bridge methods.
- [x] **Bug fix:** ApplyWrite COM threading — moved from ThreadPool to UI thread (STA). Was causing silent failures on write confirmation Apply button.
- [x] **Interactive testing:** All 8 Task Pane features validated in live SOLIDWORKS (streaming, write confirmations, recipe UI, diagnostic intent, multi-turn, collapsible sections, status, cancel).
- [x] **Cross-cutting (TX-02):** `IUiThreadInvoker` abstraction — `IUiThreadInvoker` interface in Contracts, `SynchronousUiThreadInvoker` for tests, `WinFormsUiThreadInvoker` for production. Wired into `HostState.ApplyPendingWrite` for automatic UI-thread marshaling. 8 tests.
- [x] **Phase 8 (T8-06):** Session telemetry — `SessionTelemetry` class tracking tool call frequency, run outcomes (success/cancelled/failed/fallback), write apply/cancel/fail rates, cancellation phases, recipe capture/promotion counts, agentic vs classic path tracking. Dashboard in Status section. 23 tests.
- [x] **Phase 8 (T8-01):** Cost budget UI — usage dashboard in Status section with run count, token breakdown, session budget progress bar, estimated cost. Warning banner when near limit (health-warning), error banner when over budget (health-error). 3 additional BudgetStatus tests.
- [x] **Cross-cutting (TX-03):** Error presentation tiers — `ErrorClassifier` with 3-tier classification (ToolError/ApiError/HostError). Rate limit, auth, timeout, network, COM error recognition. User-friendly messages with guidance, never stack traces. `FormatAgentOutcomeMessage` for agent loop failures. 17 tests.
- [x] **Phase 7 (T7-04):** Dependency preview — `DependencyAnalyzer` analyzes cascade risk for suppression and dimension changes. Feature tree ordering, type heuristics, dimension/mate references, `CascadeRisk` enum. Integrated into `SuppressFeatureTool.Preview`. 12 tests.
- [x] **Phase 7 (T7-03 partial):** `rename_object` write tool — 5th write tool. `RenameObjectTool` with full IWriteTool lifecycle. Preview validates existence, name collisions, dimension references. Wired into dispatcher, schema builder, HostState COM apply. 12 tests.
- [x] **Elevated confirmation infra:** `PendingWriteAction.IsElevated`, `WriteTrackingToolExecutor.ElevatedToolNames`, elevated card CSS (orange border, "Elevated Change" header). 6 tests.
- [x] **Phase 7 (T7-03b):** `insert_component` write tool — 6th write tool, Class 3 (HardWriteAdvanced). Assembly-only, `AddComponent5()` COM apply. Preview validates doc type, file extension, duplicates. Elevated confirmation. 11 tests.
- [x] **Phase 7 (T7-03c):** `create_drawing_view` write tool — 7th write tool, Class 3 (HardWriteAdvanced). Drawing-only, `CreateDrawViewFromModelView3()` COM apply. 9 standard view types. Elevated confirmation. 11 tests.
- [x] **Phase 8 (T8-04c):** Pagination for large tool outputs — `GetDimensionsTool` and `GetMatesTool` now support `offset`/`limit` with `total_count`/`has_more` response fields. Max limit 200. Tool definitions and dispatchers updated. 3 tests.
- [x] **Phase 7 (T7-03d):** Configuration-scoped write behavior — suppress/unsuppress use `swThisConfiguration` with named config when `configuration_name` specified. `SetDimensionValue` uses `SetSystemValue3` config-specific mode. HostState COM apply paths updated. Preview summaries include config name. 2 tests.
- [x] **Agent progress UI:** `AgentLoopRunner` progress callbacks wired through `HostState` → `TaskPaneControl.UpdateRunProgress`. Run state label shows tool name, iteration count, and status during agentic runs.
- [x] **Undo label tracking:** `CompletedWriteEntry.UndoLabel` populated from write preview `undo_label` field. Displayed in Write History section.
- [x] **Phase 8 (T8-05b):** Request queuing — `RateLimitHelper` tracks active rate limit windows. `WaitIfRateLimited()` called before all API requests in all 3 model clients. 5 tests.

## Handoff Notes

If a new agent or session picks this up:

1. Read `CLAUDE.md` for critical rules and commands.
2. Read `documentation/plans/END-GOAL-FINAL.md` for the complete agentic vision (700 lines).
3. Read `documentation/tasks/TASK-INDEX.md` for the comprehensive task breakdown.
4. Read `documentation/plans/IMPLEMENTATION-BLUEPRINT.md` for C# interface contracts.
5. The 8 research briefs in `documentation/plans/research-*.md` are the validated evidence base.
6. Phases 1A through 9 are implemented. 616 tests passing. 7 projects (6 production + 1 test). 19 tools (11 read + 7 write + 1 retrieval).
7. Feature gates: `SOLIDWORKS_AI_AGENT_LOOP=true` (agentic loop), `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true` (write tools), `SOLIDWORKS_AI_RETRIEVAL=true` (search tool), `SOLIDWORKS_AI_STREAM_FINAL_TEXT=true` (streaming synthesis). See `FeatureGateRegistry` for all 5 gates.
8. Local models: set `SOLIDWORKS_AI_PROVIDER=ollama` or `lmstudio` to use local inference. `ToolCallCapabilityProbe` automatically detects whether the model supports tool calling — if not, falls back to synthesis-only. `LocalEndpointHealthCheck.Check()` verifies server readiness and displays in Task Pane Status section. Local providers now show `[Experimental]` in answer footer and health banner. See `research-local-model-feasibility.md`.
9. Next priorities: (a) T7-03 remaining (component insertion, drawing view creation — Gate G), (b) large assembly testing with real .SLDASM files, (c) MCP server exposure (Phase 10+).
10. Key new infrastructure this session (2026-03-23): `IUiThreadInvoker`, `SessionTelemetry`, cost budget UI, `ErrorClassifier`, `DependencyAnalyzer`, 3 new write tools (`RenameObjectTool`, `InsertComponentTool`, `CreateDrawingViewTool`), elevated confirmation UI, config-scoped writes, tool pagination, agent progress UI, undo label tracking, request queuing. 113 new tests (503→616). 19 tools total (11 read + 7 write + 1 retrieval).
11. Prior session (2026-03-23 earlier): Agentic loop final-turn streaming, health check wired into Task Pane, `ToolCallCapabilityProbe`.
12. Prior session (2026-03-21): `RateLimitHelper` (429 detection + retry), `MaxToolResultChars` truncation in `AgentLoopRunner`, reference graph `Limit` parameter, write plan review UI (Apply All / Cancel All), batch write execution (`ApplyAllPendingWrites`), experimental label for local providers. ApplyWrite COM threading bug fixed (ThreadPool → UI thread). All 8 Task Pane features validated in live SOLIDWORKS.
12. Prior session (2026-03-22): SSE streaming (T8-03), `LocalEndpointHealthCheck`, graceful degradation messaging.
13. Prior session (2026-03-21 earlier): SearchProjectFilesTool (T6-04), recipe suggestions UI (T5-04/T9-03), local model support (T8-02).
14. Earlier sessions: diagnostic intent, multi-turn context, OLE indexer, write history, collapsible UI redesign, HTML answer panel, chat history, write confirmation cards, write tools + safety, trust/recipe/memory, cost budgets, feature gates.
