# Adze Task Index

**Last Updated:** 2026-03-16
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

### T4-09: Add WritePreview panel to TaskPaneControl
- [ ] Inline panel showing before/after values
- [ ] Apply / Cancel / Edit buttons
- [ ] Hidden by default, shown when WaitingForConfirmation
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T4-10: Add write history / undo surface
- [ ] Session history panel showing all writes
- [ ] Individual Undo buttons per write
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

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

### T5-04: Surface recipes in Task Pane
- [ ] "Suggested recipes" section when relevant recipes match current context
- [ ] One-click execution of promoted recipes
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T5-05: Achievement tracking from real usage ✓
- Write tool achievements added to ProgressionEngine: first_property_write, first_dimension_write, first_feature_suppression, first_wave_writes_completed
- 3 unit tests

---

## Phase 6: Retrieval and Cross-Session Memory (core ✓)

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

### T6-03: Implement Adze.Index project (OLE reader)
- [ ] New project or namespace for closed-file indexing
- [ ] Use OpenMcdf (MIT, pure .NET) to read OLE Structured Storage
- [ ] Extract: custom properties, summary info, file metadata from .SLDPRT/.SLDASM/.SLDDRW
- [ ] Performance target: ~1-5ms per file, 500 files < 5 seconds
- [ ] Index stored under `%LOCALAPPDATA%\Adze\index\`
- **Files:** New `src/Adze.Index/` project or `src/Adze.Tools/Index/`
- **Reference:** `research-closed-file-retrieval.md`

### T6-04: Implement search_project_files grounding tool
- [ ] New read tool: search indexed files by property values
- [ ] Filter by file type, path pattern, property name/value
- [ ] Return matching file paths with relevant properties
- **Files:** `src/Adze.Tools/`

### T6-05: Optional Document Manager API enhancement
- [ ] When swdocumentmgr.dll and license key available
- [ ] Enrich index with configuration names, config-specific properties, reference paths
- [ ] Isolated cleanly from base OLE reader
- **Files:** `src/Adze.Index/`

---

## Phase 7: Advanced Writes and Multi-Step Plans

### T7-01: Multi-step plan review UI
- [ ] Show full plan with per-step checkboxes
- [ ] Apply All / Cancel Remaining buttons
- [ ] Step status: Pending / Approved / Applied / Failed
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`
- **Reference:** `research-streaming-ux-patterns.md`

### T7-02: Batch write execution
- [ ] Execute approved steps sequentially
- [ ] Each step follows full write lifecycle
- [ ] Stop on first failure, report partial progress
- **Files:** `src/Adze.Host/Runtime/`

### T7-03: Advanced write tools (gated by Gate G)
- [ ] Component insertion: `AssemblyDoc.AddComponent5()`
- [ ] Drawing view creation: standard views
- [ ] Configuration-scoped advanced edits
- [ ] Each requires elevated confirmation

### T7-04: Dependency preview for advanced writes
- [ ] Show affected dependents before suppression/modification
- [ ] Rebuild preview for cascade-sensitive operations

---

## Phase 8: Production Hardening

### T8-01: Cost controls and budget management ✓ (core)
- `CostBudgetSettings` + `BudgetStatus` in `Adze.Broker/Configuration/`
- Per-session and per-day token limits from env vars
- `IsOverBudget`, `IsNearLimit(percent)`, `FormatSummary()`
- [ ] Usage dashboard in Status tab with cost estimates by provider
- [ ] Warning UI when approaching budget limit
- 6 unit tests

### T8-02: Local model support (experimental)
- [ ] Add Ollama/LM Studio as provider options
- [ ] Route through existing OpenAI-compatible client
- [ ] 6 automated capability gate tests before enabling tool calling
- [ ] Minimum: Qwen 2.5 32B for tool selection
- [ ] Label as experimental in UI
- **Reference:** `research-local-model-feasibility.md`

### T8-03: Final-answer streaming
- [ ] SSE parsing for final text responses
- [ ] Stream tokens to answer panel as they arrive
- [ ] Tool call turns remain fully buffered
- **Reference:** `research-tool-calling-abstraction.md`

### T8-04: Large assembly performance
- [ ] Lazy tool execution (don't execute all tools upfront)
- [ ] Paginated results for large feature trees, dimension lists
- [ ] Tool result truncation limits
- [ ] Progressive context loading

### T8-05: Rate limiting and retry
- [ ] Exponential backoff for API failures
- [ ] Request queuing during rate limit windows
- [ ] Provider-specific rate limit detection

### T8-06: Advanced telemetry
- [ ] Track which tools are called most
- [ ] Track plan success/failure rates
- [ ] Track where users cancel
- [ ] Track recipe promotion rates
- [ ] Dashboard in Status tab or separate analytics

---

## Cross-Cutting Tasks

### TX-01: Feature gate infrastructure ✓
- `FeatureGateRegistry` in `Adze.Broker/Configuration/`
- Constants: AgentLoop, FirstWaveWrites, Retrieval, LocalModels, StreamFinalText
- `IsEnabled()`, `GetAllStates()`, `FormatSummary()`
- 4 unit tests

### TX-02: IUiThreadInvoker abstraction
- [ ] Clean interface for UI-thread marshaling
- [ ] Used by write execution, snapshot capture, COM refresh
- [ ] Testable via mock for unit tests
- **Reference:** END-GOAL-INTERFACES Section 7.2

### TX-03: Error presentation tiers
- [ ] Tool failures: non-prominent log lines (agent self-corrects)
- [ ] API errors: retry status shown
- [ ] COM/host errors: calm recovery guidance
- [ ] Never stack traces in the answer panel
- **Reference:** `research-streaming-ux-patterns.md`

### TX-04: Documentation updates per phase
- [ ] Update CLAUDE.md commands and baseline after each phase
- [ ] Update IMPLEMENTATION_PLAN.md checklist and handoff notes
- [ ] Update PROJECT_ROADMAP.md milestone table
- [ ] Update SETUP.md if new configuration is added

---

## Phase 9: Ecosystem-Informed Enhancements (from research-solidworks-ai-ecosystem.md)

### T9-01: HTML answer panel (replace raw TextBox)
- [ ] Replace answer TextBox with WebBrowser control in TaskPaneControl
- [ ] Render agent responses as formatted HTML (headers, bold, lists, tables)
- [ ] Message-style layout: user question → assistant response
- [ ] Subtle token/source/trace footer
- [ ] Preserve scroll position on refresh
- **Why:** Every competitor (AURA, Autodesk Assistant, Siemens Copilot) renders polished conversational UI. Raw text is Adze's most visible gap.
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`
- **Priority:** HIGH — highest UX impact for lowest effort

### T9-02: "What's Wrong" diagnostic intent
- [ ] Add dedicated diagnostic intent to clarification UI
- [ ] When triggered, agent prioritizes: get_rebuild_diagnostics, get_feature_tree_slice, get_dimensions
- [ ] Prompt tuning for root-cause analysis output style
- **Why:** SOLIDWORKS Labs "What's Wrong (Beta)" validates this as a high-value use case. Adze already has the tools — just needs intent routing.
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`, `src/Adze.Broker/Formatting/ContextPromptComposer.cs`
- **Priority:** MEDIUM — low effort, leverages existing infrastructure

### T9-03: Accelerate recipe suggestions in Task Pane (was T5-04)
- [ ] "Suggested recipes" section when relevant recipes match current context
- [ ] One-click execution of promoted recipes
- **Why:** SOLIDWORKS Labs "Command Predictor" validates predictive assistance. Adze's recipe system is architecturally richer. Surface it.
- **Reference:** T5-04, research-solidworks-ai-ecosystem.md (Command Predictor comparison)
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T9-04: MCP server exposure (future)
- [ ] Expose Adze's 14 tools as an MCP server
- [ ] External agents (Claude Code, Cursor, etc.) can inspect/modify SOLIDWORKS models
- [ ] Authentication and trust boundary for external tool callers
- **Why:** Autodesk is adopting MCP for Fusion AI extensibility. This is an industry-direction signal. Adze's typed tool surface is already MCP-shaped.
- **Reference:** research-solidworks-ai-ecosystem.md (Autodesk MCP adoption)
- **Priority:** LOW now, HIGH strategic — Phase 10+

### T9-05: Conversational chat history in Task Pane
- [ ] Show conversation thread (multi-turn) instead of single Q&A
- [ ] Agent conversation state already exists (AgentConversationState) — surface it in UI
- [ ] Follow-up questions without re-entering full context
- **Why:** Every competitor has chat-style interaction. Adze has the backend (conversation state + truncation) but renders single-shot.
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T9-06: .env file loader for scripts
- [x] `scripts/setup/load-env.ps1` — loads `.env` into process environment
- [x] Wired into `reload-host.ps1`
- [ ] Wire into `validate-host-spike.ps1`, `run-provider-smoke.ps1`, `run-broker-evals.ps1`
