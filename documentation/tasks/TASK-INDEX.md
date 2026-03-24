# Adze Task Index

**Last Updated:** 2026-03-23
**Source:** END-GOAL-FINAL.md + IMPLEMENTATION-BLUEPRINT.md + 7 research briefs + 4 discovery briefs

This is the comprehensive task breakdown for the full agentic implementation. Tasks are organized by phase, with dependencies, acceptance criteria, and references to supporting research.

---

## Phase 1A: Pre-Prompt Clarification UI ✓

### T1A-01: Add clarification panel to TaskPaneControl ✓
### T1A-02: Add Intent ComboBox ✓
### T1A-03: Add Scope CheckedListBox ✓
### T1A-04: Add Output Mode ComboBox ✓
### T1A-05: Add Diagnostics CheckBox ✓
### T1A-06: Format clarification prefix ✓
### T1A-07: Unit tests for clarification prefix formatting ✓

---

## Phase 1B: Conversation State ✓

### T1B-01: Define ConversationMessage and AgentConversationState ✓
### T1B-02: Implement TruncationPolicy and IConversationTruncator ✓
### T1B-03: Add conversation state to HostState ✓
### T1B-04: Unit tests for conversation state and truncation ✓

---

## Phase 2: Agentic Tool Loop ✓

### T2-01: Define IAgentModelClient interface ✓
### T2-02: Define AgentToolDefinition, AgentToolCall, AgentToolResult, AgentTurnResponse ✓
### T2-03: Implement OpenAIFormatAgentClient ✓
### T2-04: Implement AnthropicAgentClient — DEFERRED (OpenRouter handles both providers via OpenAI-compatible format)
### T2-05: Implement ToolDefinitionBuilder ✓
### T2-06: Implement IToolRegistry and IToolDescriptor ✓
### T2-07: Implement IToolExecutor (AgentToolDispatcher) ✓
### T2-08: Implement AgentLoopRunner ✓
### T2-09: Implement AgentModelClientFactory ✓
### T2-10: Integrate agent loop into HostState ✓
### T2-11: Add Cancel button behavior ✓
### T2-12: Add PaneState state machine — partial (basic run/cancel state, full state machine deferred)
### T2-13: Progress display during agent loop — partial (run state label, full live-append deferred)
### T2-14: Unit tests for agent loop ✓ (19 tests)
### T2-15: Live smoke tests for agent loop ✓
- Verified 2026-03-16 via OpenRouter: model called get_dimensions autonomously, synthesized grounded answer (1866 tokens, outcome=Success)
- Bugs fixed during live test: SplitContainer init crash, AgentLoopRunner error counting, endpoint /chat/completions normalization

---

## Phase 3: Snapshot/Diff and Verification Layer ✓

### T3-01: Implement IStateSnapshotService ✓
- Contracts: `IStateSnapshotService` in `Adze.Contracts/Abstractions/IWriteServices.cs`
- DTOs: `WriteTargetDescriptor`, `StateSnapshot`, `SnapshotItem` in `Adze.Contracts/Models/WriteContracts.cs`
- COM implementation deferred to Host integration (Phase 4 host wiring)

### T3-02: Implement IStateDiffService ✓
- `StateDiffService` in `Adze.Broker/Orchestration/StateDiffService.cs`
- 7 unit tests covering identical, changed, added, removed, multi-item diffs

### T3-03: Implement IVerificationPolicy ✓
- `DefaultVerificationPolicy` in `Adze.Broker/Orchestration/DefaultVerificationPolicy.cs`
- 8 unit tests covering all verification scenarios

### T3-04: Enrich trace records with before/after state ✓
- `WriteTraceRecord` DTO in `Adze.Contracts/Models/WriteContracts.cs`
- `WriteTraceRecordBuilder` in `Adze.Trace/Tracing/WriteTraceRecordBuilder.cs`
- 4 unit tests

### T3-05: Unit tests for snapshot, diff, and verification ✓ (30 tests total)

---

## Phase 4: First-Wave Write Tools + Confirmation UI

### T4-01: Define IWriteTool<TParams> interface ✓
- `IWriteTool<TParams>` in `Adze.Contracts/Abstractions/IWriteServices.cs`
- Preview, Apply (object application), Verify, BuildUndoLabel

### T4-02: Define ToolCapabilityMetadata ✓ (completed in prior session)
- In `Adze.Tools/Abstractions/ToolCapabilityContracts.cs`

### T4-03: Implement WriteExecutionCoordinator ✓
- `WriteExecutionCoordinator` in `Adze.Broker/Orchestration/WriteExecutionCoordinator.cs`
- Full 8-step lifecycle with delegate-based COM marshaling
- 7 unit tests

### T4-04: Implement IApprovalCoordinator ✓
- `IApprovalCoordinator` interface in `Adze.Contracts/Abstractions/IWriteServices.cs`
- `ApprovalDecision` enum (Apply, Cancel, Modify)
- Host-side ManualResetEventSlim implementation pending (T4-09)

### T4-05: Implement IAgentPolicyEngine
- [ ] `EvaluateToolRequest(context, toolCall, descriptor)` → ToolExecutionPolicy
- [ ] Check capability class against current trust tier
- [ ] Fail closed on ambiguous targets
- **Files:** `src/Adze.Host/Policy/`

### T4-06: Implement SetCustomPropertyTool ✓
- In `Adze.Tools/Write/SetCustomPropertyTool.cs`
- Preview, Apply (COM via dynamic), Verify, BuildUndoLabel
- 7 unit tests

### T4-07: Implement SetDimensionValueTool ✓
- In `Adze.Tools/Write/SetDimensionValueTool.cs`
- Preview, Apply (COM via dynamic), Verify, BuildUndoLabel
- 7 unit tests

### T4-08: Implement SuppressFeatureTool / UnsuppressFeatureTool ✓
- In `Adze.Tools/Write/SuppressFeatureTool.cs`
- Preview with cascade warning, Apply, Verify
- 11 unit tests (7 suppress + 4 unsuppress)

### T4-09: Add WritePreview panel to TaskPaneControl ✓
- [x] Inline confirmation card showing before/after values in chat thread
- [x] Apply / Cancel buttons with JavaScript → C# bridge
- [x] PendingWriteAction tracking in HostState with write-tracking executor
- [x] Applied/Cancelled state rendering with result messages
- [x] Direct COM apply for all 4 first-wave write tools
- **Files:** `src/Adze.Host/Infrastructure/HostState.cs`, `src/Adze.Host/UI/TaskPaneControl.cs`

### T4-10: Add write history / undo surface ✓
- [x] Session history panel showing all writes (collapsible Write History section)
- [x] `CompletedWriteEntry` tracking, auto-record on apply, `GetWriteHistory()`/`ClearWriteHistory()`
- [ ] Individual Undo buttons per write (deferred — undo grouping via `StartRecordingUndoObject` future work)
- **Files:** `src/Adze.Host/Infrastructure/HostState.cs`, `src/Adze.Host/UI/TaskPaneControl.cs`

### T4-11: Unit tests for write tools ✓ (36 tests total)

### T4-12: Write tool eval suite
- [ ] Correct target identification
- [ ] Reject ambiguous write requests
- [ ] Cascade warning for suppression
- **Files:** `tests/Adze.Tests/`, `benchmarks/`

### T4-NEW: Write tool agent dispatch integration ✓
- Write tools added to `AgentToolDispatcher` (preview-only mode)
- Write tool definitions in `ToolDefinitionBuilder.BuildWriteToolDefinitions()`
- Feature gate: `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true`
- HostState branches: write defs included when gate enabled

### T4-NEW: Write tool parameter types ✓
- `SetCustomPropertyParameters`, `SetDimensionValueParameters`, `SuppressFeatureParameters`, `UnsuppressFeatureParameters`
- Added to `Adze.Contracts/Models/ToolContracts.cs`
- Write tool names added to `ToolNames.cs`

---

## Phase 5: Learning Activation and Trust Policy ✓ (core)

### T5-01: Recipe candidate capture from agent traces ✓
- `AgentRecipeCaptureService` in `Adze.Trace/Recipes/`
- Captures from successful runs, requires verified writes for write-containing recipes
- Promotes to review_ready after 3 successful traces
- 6 unit tests

### T5-02: Recipe promotion workflow ✓
- `AgentRecipeCaptureService.Promote()` — moves review_ready to promoted directory
- `ListPromoted()` — enumerates promoted recipes

### T5-03: Trust tier progression ✓
- `ITrustService` in `Adze.Contracts/Abstractions/`
- `TrustService` in `Adze.Trace/Progression/`
- Tiers: Baseline → Assisted → Reviewed → TrustedBounded
- TrustedBounded requires Reviewed + all first-wave writes completed
- 5 unit tests

### T5-04: Surface recipes in Task Pane ✓
- [x] "Suggested Recipes" collapsible section with recipe cards (title, state, reliability %, tool tags)
- [x] One-click execution via `RunRecipe()` JS bridge — populates request box and auto-runs
- [x] `PromoteRecipe()` JS bridge — promotes review-ready to promoted
- [x] `AgentRecipeCaptureService.ListReviewReady()` + `HostState.GetSuggestedRecipes()`
- **Files:** `src/Adze.Host/Infrastructure/HostState.cs`, `src/Adze.Host/UI/TaskPaneControl.cs`, `src/Adze.Trace/Recipes/AgentRecipeCaptureService.cs`

### T5-05: Achievement tracking from real usage ✓
- Write tool achievements added to ProgressionEngine: first_property_write, first_dimension_write, first_feature_suppression, first_wave_writes_completed
- 3 unit tests

---

## Phase 6: Retrieval and Cross-Session Memory ✓

### T6-01: Implement per-document memory ✓
- `DocumentMemory` + `MemoryStore` in `Adze.Trace/Memory/`
- SHA256 document key, stores key dimensions, properties, workflows, issues, recent intents
- `RecordIntent()` increments session count and tracks intents
- 8 unit tests

### T6-02: Implement user preference memory ✓
- `UserPreferenceMemory` in `Adze.Trace/Memory/`
- Stores answer mode, verbosity, focus areas, diagnostics preference
- Save/load under `%LOCALAPPDATA%\Adze\state\user-preferences.json`
- 4 unit tests

### T6-03: Implement Adze.Index project (OLE reader) ✓
- [x] New `src/Adze.Index/` project with OpenMcdf NuGet (MIT, pure .NET)
- [x] `OlePropertyReader` — reads SOLIDWORKS files without COM via OLE Structured Storage
- [x] `OlePropertySetParser` — parses property set streams
- [x] `ClosedFileIndexer` — scans folders and persists JSON index under `%LOCALAPPDATA%\Adze\index\`
- [x] `ClosedFileSearchService` — queries by property/keyword/type/path
- [x] Extract: custom properties, summary info, file metadata from .SLDPRT/.SLDASM/.SLDDRW
- [x] 13 unit tests
- **Files:** `src/Adze.Index/`
- **Reference:** `research-closed-file-retrieval.md`

### T6-04: Implement search_project_files grounding tool ✓
- [x] `SearchProjectFilesTool` in `src/Adze.Tools/Grounding/` implementing `IReadOnlyTool<SearchProjectFilesParameters>`
- [x] `SearchProjectFilesParameters` in `Adze.Contracts/Models/IndexContracts.cs`
- [x] Wired into `ToolCatalog`, `AgentToolDispatcher`, `ToolDefinitionBuilder.BuildRetrievalToolDefinitions()`
- [x] Feature-gated behind `SOLIDWORKS_AI_RETRIEVAL=true`
- [x] Filter by file type, path pattern, property name/value, keyword
- [x] 13 unit tests
- **Files:** `src/Adze.Tools/Grounding/SearchProjectFilesTool.cs`, `src/Adze.Broker/Orchestration/AgentToolDispatcher.cs`, `src/Adze.Broker/Formatting/ToolDefinitionBuilder.cs`

### T6-05: Optional Document Manager API enhancement
- [ ] When swdocumentmgr.dll and license key available
- [ ] Enrich index with configuration names, config-specific properties, reference paths
- [ ] Isolated cleanly from base OLE reader
- **Files:** `src/Adze.Index/`

---

## Phase 7: Advanced Writes and Multi-Step Plans

### T7-01: Multi-step plan review UI ✓
- [x] Show Write Plan header with step count when 2+ actionable pending writes
- [x] Apply All / Cancel All buttons with JS bridge methods
- [x] Step status: Applied / Cancelled tracked per PendingWriteAction
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`, `src/Adze.Host/Infrastructure/HostState.cs`

### T7-02: Batch write execution ✓
- [x] Execute approved steps sequentially via `ApplyAllPendingWrites()`
- [x] Each step follows full write lifecycle (via `ApplyPendingWrite` → `ApplyWriteToolDirect`)
- [x] Stop on first failure, report partial progress in write history
- **Files:** `src/Adze.Host/Infrastructure/HostState.cs`

### T7-03: Advanced write tools (partially ✓)
- [x] `rename_object` — `RenameObjectTool` with full IWriteTool lifecycle: preview (name collision detection, dimension reference warnings), apply (feature.Name via COM), verify (name change confirmed in refreshed tree). Wired into dispatcher, schema builder, HostState. 12 tests.
- [x] `insert_component` — `InsertComponentTool`, Class 3 (HardWriteAdvanced). Assembly-only. Preview validates doc type, file extension (.SLDPRT/.SLDASM), duplicate detection via reference graph, insertion coordinates. Apply uses `AddComponent5()` via COM. Elevated confirmation UI. 11 tests.
- [ ] Drawing view creation: standard views (gated by Gate G)
- [ ] Configuration-scoped advanced edits (gated by Gate G)
- [x] Elevated confirmation UI infra: `PendingWriteAction.IsElevated`, `ElevatedToolNames` set, orange-bordered cards with "Elevated Change" header, CSS for `.write-card-elevated` and `.write-header-elevated`. 6 tests.
- **Files:** `src/Adze.Tools/Write/RenameObjectTool.cs`, `src/Adze.Tools/Write/InsertComponentTool.cs`

### T7-04: Dependency preview for advanced writes ✓
- [x] `DependencyAnalyzer` — analyzes cascade risk for suppression and dimension changes
- [x] Feature tree ordering + type heuristics (sketch→extrusion, extrusion→fillet/shell/pattern/mirror)
- [x] Dimension reference detection (FullName search), mate reference detection (component search)
- [x] `CascadeRisk` enum (None/Low/Medium/High), `DependencyPreview` with affected features/dimensions/mates
- [x] Integrated into `SuppressFeatureTool.Preview` — replaces old basic dependency check
- 12 unit tests
- **Files:** `src/Adze.Tools/Write/DependencyAnalyzer.cs`

---

## Phase 8: Production Hardening

### T8-01: Cost controls and budget management ✓
- `CostBudgetSettings` + `BudgetStatus` in `Adze.Broker/Configuration/`
- Per-session and per-day token limits from env vars
- `IsOverBudget`, `IsNearLimit(percent)`, `FormatSummary()`
- [x] Usage dashboard in Status section with run count, token breakdown, session budget progress bar, estimated cost
- [x] Warning banner (health-warning) when approaching budget limit, error banner (health-error) when budget exhausted
- 9 unit tests

### T8-02: Local model support (experimental) ✓
- [x] Ollama (`SOLIDWORKS_AI_PROVIDER=ollama`) and LM Studio (`SOLIDWORKS_AI_PROVIDER=lmstudio`) as providers
- [x] Routes through existing `OpenAIModelClient` via `UsesOpenAIFormat` property
- [x] `BrokerModelSettings.IsLocalProvider`, `IsLocalProviderName()`, `NormalizeProvider()` extended
- [x] Default endpoints: `localhost:11434` (Ollama), `localhost:1234` (LM Studio)
- [x] Longer default timeouts: 60s broker, 90s synthesis
- [x] Custom model/endpoint via `SOLIDWORKS_AI_OLLAMA_MODEL`, `SOLIDWORKS_AI_LMSTUDIO_MODEL`, etc.
- [x] Capability gate probing before enabling tool calling (T8-02b) — `ToolCallCapabilityProbe`, 13 tests
- [x] Label as experimental in UI (T8-02c) — `[Experimental]` in answer footer + health banner guidance
- [x] 10 unit tests
- **Reference:** `research-local-model-feasibility.md`

### T8-03: Final-answer streaming ✓
- [x] `SseStreamReader` utility: parses SSE `data:` lines, extracts `choices[0].delta.content`, handles `[DONE]`, malformed JSON, usage in final chunk
- [x] `IStreamingModelClient` interface extending `IModelClient` with `SynthesizeStreaming(prompt, onTextChunk)`
- [x] `OpenAIModelClient.SynthesizeStreaming` — sends `stream:true` + `stream_options.include_usage`, reads SSE via `SseStreamReader`
- [x] `GroundingSynthesisService.Build` overload with `Action<string>? onTextChunk` — routes to streaming when client supports it
- [x] `HostState.CompleteAssistantRun` accepts `Action<string>? onStreamChunk`, gates on `SOLIDWORKS_AI_STREAM_FINAL_TEXT`
- [x] `TaskPaneControl`: `startStreaming(userHtml)` JS adds user bubble + `<pre id="stream-target">`, `appendStreamChunk(text)` appends via `createTextNode`, auto-scroll. `ApplySnapshot` re-renders final markdown.
- [x] Tool call turns remain fully buffered (streaming only on synthesis/final text pass)
- [x] Feature-gated behind `SOLIDWORKS_AI_STREAM_FINAL_TEXT=true`
- [x] 19 unit tests for SSE parsing + streaming infrastructure
- [x] Agentic loop final-turn streaming — `IStreamingAgentModelClient`, `OpenAIFormatAgentClient.SendTurnStreaming`, `AgentLoopRunner` streaming overload, HostState wiring. 12 tests.
- **Reference:** `research-streaming-ux-patterns.md`, `research-local-model-feasibility.md` section 3

### T8-NEW: Local endpoint health check and graceful degradation ✓
- [x] `LocalEndpointHealthCheck` — pings `GET /v1/models` for Ollama/LM Studio. Returns `LocalHealthStatus` (Ready, Reachable, NoModels, ModelNotFound, Unreachable, Error)
- [x] `BuildModelsUrl()` — derives `/v1/models` URL from chat completions endpoint
- [x] `LocalHealthResult.IsHealthy` — true only when `Ready`
- [x] `GroundingSynthesisService` — local provider failures produce clear "Local model (provider) response was unusable — used deterministic planner" message
- [x] Cloud provider failures remain unchanged
- [x] 13 health check tests + 5 synthesis degradation tests
- **Files:** `src/Adze.Broker/Clients/LocalEndpointHealthCheck.cs`, `src/Adze.Broker/Orchestration/GroundingSynthesisService.cs`
- [x] Wire health check into Task Pane Status section — `HostState.RunLocalHealthCheckAsync()`, styled health banners (ready/warning/error), actionable guidance messages
- [x] Capability gate probing (T8-02b) — `ToolCallCapabilityProbe` sends minimal tool-calling request, caches per provider+model, `AgentModelClientFactory` falls back to synthesis-only when unsupported. 13 tests.

### T8-04: Large assembly performance (partial ✓)
- [ ] Lazy tool execution (don't execute all tools upfront)
- [ ] Paginated results for large feature trees, dimension lists
- [x] Tool result truncation limits — `AgentLoopRunner` enforces `MaxToolResultChars` (default 8192). `GetReferenceGraphTool` `Limit` parameter (default 100). 4 tests.
- [ ] Progressive context loading

### T8-05: Rate limiting and retry ✓
- [x] Retry with backoff for 429 responses — `RateLimitHelper` with `Retry-After` header parsing (capped 15s), max 1 retry
- [x] Request queuing during rate limit windows — `RecordRateLimitWindow`, `IsInRateLimitWindow`, `WaitIfRateLimited`. All model clients call `WaitIfRateLimited()` before API requests. 5 additional tests.
- [x] Provider-specific rate limit detection — `RateLimitHelper.IsRateLimited()` checks HTTP 429 status. 12 tests total.

### T8-06: Advanced telemetry ✓
- [x] Track which tools are called most — `SessionTelemetry.GetToolCallRanking()`, case-insensitive, ranked by frequency
- [x] Track plan success/failure rates — `RecordRunOutcome()`, `SuccessRate`, `CancellationRate`, agentic vs classic path counts
- [x] Track where users cancel — `RecordCancellation(phase)` with API call / tool execution / user breakdown
- [x] Track recipe promotion rates — `RecordRecipeCaptured()`, `RecordRecipePromoted()`
- [x] Dashboard in Status section — top 5 tools, success/cancel/fail rates, write apply/cancel stats
- [x] Write tracking: proposed/applied/cancelled/failed/batch counts, `WriteApplyRate`
- 23 unit tests
- **Files:** `src/Adze.Broker/Models/SessionTelemetry.cs`, `src/Adze.Host/Infrastructure/HostState.cs`, `src/Adze.Host/UI/TaskPaneControl.cs`

---

## Cross-Cutting Tasks

### TX-01: Feature gate infrastructure ✓
- `FeatureGateRegistry` in `Adze.Broker/Configuration/`
- Constants: AgentLoop, FirstWaveWrites, Retrieval, LocalModels, StreamFinalText
- `IsEnabled()`, `GetAllStates()`, `FormatSummary()`
- 4 unit tests

### TX-02: IUiThreadInvoker abstraction ✓
- [x] `IUiThreadInvoker` interface in `Adze.Contracts/Abstractions/` — `Invoke(Action)` and `Invoke<T>(Func<T>)`
- [x] `WinFormsUiThreadInvoker` in `Adze.Host/Infrastructure/` — uses `Control.Invoke`/`InvokeRequired`
- [x] `SynchronousUiThreadInvoker` in `Adze.Broker/Orchestration/` — test-friendly inline execution
- [x] Wired into `HostState` — `SetUiThreadInvoker()`, `GetUiThreadInvoker()`, used in `ApplyPendingWrite` for automatic UI-thread marshaling
- [x] Registered in `TaskPaneControl` constructor
- 8 unit tests

### TX-03: Error presentation tiers ✓
- [x] `ErrorClassifier` in `Adze.Broker/Orchestration/` — 3-tier classification: ToolError, ApiError, HostError
- [x] Tool failures: non-prominent, logged in Tools tab only (agent self-corrects)
- [x] API errors (429/401/403/timeout/network/500): user-friendly messages with actionable guidance
- [x] COM/host errors: calm recovery guidance, never stack traces
- [x] `FormatForUser()` — produces clean user-facing messages with guidance
- [x] `FormatAgentOutcomeMessage()` — friendly text for agent loop failure outcomes (rate limit, timeout, max errors, cancellation)
- [x] Wired into `TaskPaneControl.ShowRunFailure` — classifies exceptions before rendering
- 17 unit tests
- **Files:** `src/Adze.Broker/Orchestration/ErrorClassifier.cs`, `src/Adze.Host/UI/TaskPaneControl.cs`, `src/Adze.Host/Infrastructure/HostState.cs`
- [ ] Never stack traces in the answer panel
- **Reference:** `research-streaming-ux-patterns.md`

### TX-04: Documentation updates per phase
- [ ] Update CLAUDE.md commands and baseline after each phase
- [ ] Update IMPLEMENTATION_PLAN.md checklist and handoff notes
- [ ] Update PROJECT_ROADMAP.md milestone table
- [ ] Update SETUP.md if new configuration is added

---

## Phase 9: Ecosystem-Informed Enhancements (from research-solidworks-ai-ecosystem.md)

### T9-01: HTML answer panel (replace raw TextBox) ✓
- [x] Replace answer TextBox with WebBrowser control in TaskPaneControl
- [x] Render agent responses as formatted HTML (headers, bold, lists, tables)
- [x] Message-style layout: user question → assistant response
- [x] Subtle token/source/trace footer per message
- [x] Auto-scroll to bottom on new messages
- [x] Tab state synced to C# via `window.external.SwitchTab()`
- [x] Status auto-refresh via `InvokeScript` (no full page re-render)
- **Why:** Every competitor (AURA, Autodesk Assistant, Siemens Copilot) renders polished conversational UI. Raw text is Adze's most visible gap.
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T9-02: "What's Wrong" diagnostic intent ✓
- [x] Clarification prefix parsing via `ExtractClarificationIntent()` — detects "diagnose"/"diagnostic"
- [x] Expanded keyword detection: "what's wrong", "broken", "failed", etc.
- [x] Diagnostic tool boosting: prioritizes `get_rebuild_diagnostics`, `get_feature_tree_slice`, `get_dimensions`
- [x] Intent-aware agent and synthesis prompts via `ContextPromptComposer.BuildAgentSystemPrompt(detectedIntent)`
- **Why:** SOLIDWORKS Labs "What's Wrong (Beta)" validates this as a high-value use case.
- **Files:** `src/Adze.Host/Infrastructure/HostState.cs`, `src/Adze.Broker/Formatting/ContextPromptComposer.cs`, `src/Adze.Broker/Orchestration/KeywordBrokerOrchestrator.cs`

### T9-03: Accelerate recipe suggestions in Task Pane (was T5-04) ✓
- [x] Collapsible "Suggested Recipes" section with recipe cards
- [x] Run/Promote buttons via JS bridge (`RunRecipe`, `PromoteRecipe`)
- [x] Merged with T5-04 implementation
- **Why:** SOLIDWORKS Labs "Command Predictor" validates predictive assistance. Adze's recipe system is architecturally richer.
- **Reference:** T5-04, research-solidworks-ai-ecosystem.md (Command Predictor comparison)
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T9-04: MCP server exposure (future)
- [ ] Expose Adze's 14 tools as an MCP server
- [ ] External agents (Claude Code, Cursor, etc.) can inspect/modify SOLIDWORKS models
- [ ] Authentication and trust boundary for external tool callers
- **Why:** Autodesk is adopting MCP for Fusion AI extensibility. This is an industry-direction signal. Adze's typed tool surface is already MCP-shaped.
- **Reference:** research-solidworks-ai-ecosystem.md (Autodesk MCP adoption)
- **Priority:** LOW now, HIGH strategic — Phase 10+

### T9-05: Conversational chat history in Task Pane ✓
- [x] Show conversation thread (multi-turn) instead of single Q&A
- [x] ChatEntry tracking in HostState with document-aware clearing
- [x] User/assistant bubble rendering with per-message footer
- [x] Request box clears after run for follow-up input
- [x] Pass prior conversation context to agent loop via `BuildPriorConversation()` → `ConversationTruncator` (max 20 messages, 6 protected recent) → OpenAI-format messages
- **Why:** Every competitor has chat-style interaction. Adze has the backend (conversation state + truncation) but renders single-shot.
- **Files:** `src/Adze.Host/Infrastructure/HostState.cs`, `src/Adze.Host/UI/TaskPaneControl.cs`

### T9-06: .env file loader for scripts
- [x] `scripts/setup/load-env.ps1` — loads `.env` into process environment
- [x] Wired into `reload-host.ps1`
- [ ] Wire into `validate-host-spike.ps1`, `run-provider-smoke.ps1`, `run-broker-evals.ps1`
