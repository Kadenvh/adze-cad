# Adze Task Index

**Last Updated:** 2026-03-15
**Source:** END-GOAL-FINAL.md + IMPLEMENTATION-BLUEPRINT.md + 7 research briefs + 4 discovery briefs

This is the comprehensive task breakdown for the full agentic implementation. Tasks are organized by phase, with dependencies, acceptance criteria, and references to supporting research.

---

## Phase 1A: Pre-Prompt Clarification UI

### T1A-01: Add clarification panel to TaskPaneControl
- [ ] Create collapsible panel between request box and Run button
- [ ] Add LinkLabel toggle ("Show options" / "Hide options")
- [ ] Adjust composerPanel height dynamically based on collapse state
- [ ] Default collapsed on first load
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`
- **Reference:** `discovery-clarification-ui.md` Section 2

### T1A-02: Add Intent ComboBox
- [ ] Add ComboBox with options: Inspect / Diagnose / Explain / Compare
- [ ] "Compare" visible only when `SessionContext.Configurations.Count > 1`
- [ ] Default selection: Inspect
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`
- **Reference:** `discovery-clarification-ui.md` Section 1.1

### T1A-03: Add Scope CheckedListBox
- [ ] Add CheckedListBox (max 6 visible rows, scrollable)
- [ ] Populate from live SessionContext: features, dimensions, configs, properties
- [ ] Document-type-sensitive: part shows features/dimensions, assembly adds mates/components
- [ ] "Current selection" auto-checked when `Selection.Count > 0`
- [ ] Cap at 20 items with truncation label
- [ ] Repopulate when document changes (via ActiveDocChangeNotify)
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`
- **Reference:** `discovery-clarification-ui.md` Section 1.2

### T1A-04: Add Output Mode ComboBox
- [ ] Add ComboBox with options: Brief / Detailed / Tabular
- [ ] "Tabular" visible only when scope has >2 items selected
- [ ] Default: Brief
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T1A-05: Add Diagnostics CheckBox
- [ ] Add CheckBox "Include rebuild diagnostics?"
- [ ] Auto-check when `Diagnostics.RebuildState != "clean"` or warnings/missing refs exist
- [ ] Uncheck by default when document is clean
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T1A-06: Format clarification prefix
- [ ] Build structured `[clarification]...[/clarification]` prefix from control state
- [ ] Prepend to user prompt before sending to broker
- [ ] No broker interface changes required
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`, `src/Adze.Host/Infrastructure/HostState.cs`

### T1A-07: Unit tests for clarification prefix formatting
- [ ] Test prefix generation for each axis (intent, scope, output mode, diagnostics)
- [ ] Test empty/default state produces no prefix
- [ ] Test assembly vs part scope population
- **Files:** `tests/Adze.Tests/`

---

## Phase 1B: Conversation State

### T1B-01: Define ConversationMessage and AgentConversationState
- [ ] Add `ConversationRole` enum (System, User, Assistant, Tool)
- [ ] Add `ConversationMessage` class with Role, Text, RawPayload, TimestampUtc
- [ ] Add `AgentConversationState` class with SessionId, Messages list, EstimatedTotalTokens
- **Files:** `src/Adze.Broker/Abstractions/` or `src/Adze.Broker/Models/`
- **Reference:** END-GOAL-INTERFACES Section 3

### T1B-02: Implement TruncationPolicy and IConversationTruncator
- [ ] Add `TruncationPolicy` (ProtectSystemMessage, ProtectInitialUserIntent, ProtectedRecentTurns=6)
- [ ] Implement sliding window truncator
- [ ] Protect system prompt + initial user message + most recent N turns
- [ ] Drop middle turns when token limit exceeded
- **Files:** `src/Adze.Broker/Orchestration/`
- **Reference:** `research-agent-loop-threading.md`, `discovery-agent-loop-architecture.md`

### T1B-03: Add conversation state to HostState
- [ ] Add session-scoped `AgentConversationState` field
- [ ] Populate on each Run assistant click
- [ ] Support follow-up turns (append to existing history)
- [ ] Clear on document change or explicit reset
- **Files:** `src/Adze.Host/Infrastructure/HostState.cs`

### T1B-04: Unit tests for conversation state and truncation
- [ ] Test message accumulation across turns
- [ ] Test truncation preserves system + initial + recent
- [ ] Test token estimation
- [ ] Test state reset on document change
- **Files:** `tests/Adze.Tests/`

---

## Phase 2: Agentic Tool Loop

### T2-01: Define IAgentModelClient interface
- [ ] `SendTurn(systemPrompt, conversationHistory, toolDefinitions, settings) → AgentTurnResponse`
- [ ] `BuildUserMessage(content) → object`
- [ ] `BuildToolResultMessages(results) → List<object>`
- **Files:** `src/Adze.Broker/Abstractions/`
- **Reference:** END-GOAL-INTERFACES Section 4, `research-tool-calling-abstraction.md`

### T2-02: Define AgentToolDefinition, AgentToolCall, AgentToolResult, AgentTurnResponse
- [ ] All DTOs as specified in END-GOAL-INTERFACES Section 4.1-4.2
- [ ] Include AgentStopReason and AgentRunOutcome enums
- [ ] Include AgentModelSettings (MaxTokens, TimeoutMs, MaxIterations, MaxConsecutiveErrors, etc.)
- **Files:** `src/Adze.Broker/Abstractions/`, `src/Adze.Broker/Configuration/`

### T2-03: Implement OpenAIFormatAgentClient
- [ ] Parse tool_calls from OpenAI-compatible responses
- [ ] Handle `arguments` as JSON string (requires separate deserialize)
- [ ] Build tool result messages with `role: "tool"` and `tool_call_id`
- [ ] Send tool definitions in `tools` array
- [ ] Handle `finish_reason: "tool_calls"` vs `"stop"`
- [ ] Support `parallel_tool_calls: false` to force sequential
- **Files:** `src/Adze.Broker/Clients/`
- **Reference:** `discovery-api-tool-use.md`, `research-tool-calling-abstraction.md`

### T2-04: Implement AnthropicAgentClient
- [ ] Parse `tool_use` content blocks from Anthropic responses
- [ ] Handle `input` as parsed object (not string like OpenAI)
- [ ] Build tool result messages with `role: "user"` containing `tool_result` blocks
- [ ] Handle `stop_reason: "tool_use"` vs `"end_turn"`
- **Files:** `src/Adze.Broker/Clients/`
- **Reference:** `discovery-api-tool-use.md`

### T2-05: Implement ToolDefinitionBuilder
- [ ] Build tool schema array from existing ToolCatalog
- [ ] Generate JSON Schema for each tool's parameter type
- [ ] Include tool descriptions and parameter descriptions
- [ ] Filter to enabled tools based on SessionContext policy
- **Files:** `src/Adze.Broker/Formatting/`

### T2-06: Implement IToolRegistry and IToolDescriptor
- [ ] Wrap existing GroundingToolCatalog in IToolRegistry interface
- [ ] Each tool gets IToolDescriptor with Name, Description, ParameterType, ResultType, CapabilityMetadata
- [ ] `BuildJsonSchema()` generates the parameter schema for API
- **Files:** `src/Adze.Tools/`
- **Reference:** END-GOAL-INTERFACES Section 5

### T2-07: Implement IToolExecutor (AgentToolDispatcher)
- [ ] Map tool_call name to existing grounding tool handler
- [ ] Deserialize arguments into typed parameter object
- [ ] Execute tool against SessionContext
- [ ] Return AgentToolResult with serialized output
- [ ] Handle tool errors gracefully (return error result, not throw)
- **Files:** `src/Adze.Broker/Orchestration/`

### T2-08: Implement AgentLoopRunner
- [ ] Iterative loop: send turn → if tool_calls: execute + send results → repeat
- [ ] Stop on: text response, max iterations (10), max consecutive errors (2), cancellation, max tokens
- [ ] Progress callbacks via `Action<AgentProgressUpdate>`
- [ ] Return AgentLoopResult with final answer, executed tools, usage, outcome
- [ ] Fallback to deterministic path on total failure
- **Files:** `src/Adze.Broker/Orchestration/`
- **Reference:** `discovery-agent-loop-architecture.md`, `research-agent-loop-threading.md`

### T2-09: Implement AgentModelClientFactory
- [ ] Create correct client based on provider setting
- [ ] Match existing ModelClientFactory pattern
- [ ] Support feature gate: `SOLIDWORKS_AI_AGENT_LOOP=true`
- **Files:** `src/Adze.Broker/Clients/`

### T2-10: Integrate agent loop into HostState
- [ ] When agent loop enabled: use AgentLoopRunner instead of existing two-pass flow
- [ ] Preserve existing path as fallback
- [ ] Thread loop on ThreadPool (matching existing TaskPaneControl pattern)
- [ ] No mid-loop COM refresh in Phase 1 (use initial SessionContext snapshot only)
- [ ] Progress updates via BeginInvoke to UI thread
- **Files:** `src/Adze.Host/Infrastructure/HostState.cs`
- **Reference:** `research-agent-loop-threading.md` (8 anti-patterns to avoid)

### T2-11: Add Cancel button behavior
- [ ] Toggle Run button to "Cancel" during agent loop
- [ ] CancellationTokenSource created per-run
- [ ] Check cancellation before/after API calls and before tool execution
- [ ] Cancelled runs produce partial result from completed work
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T2-12: Add PaneState state machine
- [ ] Enum: Idle / Running / WaitingForConfirmation / Completed / Failed / Cancelled
- [ ] State transitions control which UI elements are interactive
- [ ] Visible state indicator in run state label
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`
- **Reference:** END-GOAL-INTERFACES Section 11, `research-streaming-ux-patterns.md`

### T2-13: Progress display during agent loop
- [ ] Update run state label with current step: "Running tool 2/4: get_dimensions..."
- [ ] Live-append tool results to Tools tab as they complete
- [ ] Interim answer in answer panel during long loops
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`
- **Reference:** `research-streaming-ux-patterns.md`

### T2-14: Unit tests for agent loop
- [ ] Test tool dispatch with mock model client
- [ ] Test iteration limit enforcement
- [ ] Test consecutive error budget
- [ ] Test cancellation mid-loop
- [ ] Test fallback to deterministic path
- [ ] Test progress callbacks
- **Files:** `tests/Adze.Tests/`

### T2-15: Live smoke tests for agent loop
- [ ] Test real API call with tool definitions via OpenRouter
- [ ] Verify model returns tool_calls for dimension question
- [ ] Verify loop completes with final text answer
- [ ] Verify usage tracking through loop
- **Files:** `tests/Adze.Tests/Broker/`

---

## Phase 3: Snapshot/Diff and Verification Layer

### T3-01: Implement IStateSnapshotService
- [ ] `CaptureBefore(WriteTargetDescriptor)` → StateSnapshot
- [ ] `CaptureAfter(WriteTargetDescriptor)` → StateSnapshot
- [ ] Targeted snapshots (dimension value, property value, suppression state — not full model)
- **Files:** `src/Adze.Host/Runtime/` or `src/Adze.Host/Services/`
- **Reference:** END-GOAL-INTERFACES Section 9, END-GOAL-FINAL Section 9

### T3-02: Implement IStateDiffService
- [ ] `Compare(before, after)` → StateDiff with list of changed items
- [ ] Each StateDiffItem has Path, BeforeValue, AfterValue
- [ ] Minimal diff (only changed fields)
- **Files:** `src/Adze.Host/Runtime/`

### T3-03: Implement IVerificationPolicy
- [ ] `Evaluate(toolName, verification, refreshedContext)` → VerificationDecision
- [ ] Check: did the requested change actually happen?
- [ ] Detect: downstream rebuild errors
- [ ] Decide: accepted / suggest rollback
- **Files:** `src/Adze.Host/Policy/`

### T3-04: Enrich trace records with before/after state
- [ ] Add `WriteTraceRecord` to trace output (undo label, before/after snapshots, diff, verification)
- [ ] Extend `IAgentTraceWriter` for write-aware tracing
- **Files:** `src/Adze.Trace/`
- **Reference:** END-GOAL-INTERFACES Section 12

### T3-05: Unit tests for snapshot, diff, and verification
- [ ] Test snapshot capture for dimensions, properties, suppression
- [ ] Test diff computation
- [ ] Test verification success and failure cases
- **Files:** `tests/Adze.Tests/`

---

## Phase 4: First-Wave Write Tools + Confirmation UI

### T4-01: Define IWriteTool<TParams> interface
- [ ] `Preview(context, params)` → WritePreview
- [ ] `Apply(application, params)` → WriteApplyResult
- [ ] `Verify(refreshedContext, applyResult)` → WriteVerification
- [ ] `BuildUndoLabel(params)` → string
- **Files:** `src/Adze.Tools/Write/` (new namespace)
- **Reference:** END-GOAL-INTERFACES Section 8, END-GOAL-FINAL Section 8

### T4-02: Define ToolCapabilityMetadata
- [ ] ToolCapabilityClass enum (ReadSafe, SoftWrite, HardWriteFirstWave, HardWriteAdvanced, DeferredHighRisk)
- [ ] ApprovalRequirement enum (None, StandardConfirmation, ElevatedConfirmation, Disallowed)
- [ ] RequiresUiThread, RequiresRebuild, SupportsUndoGrouping, MustCaptureSnapshot flags
- **Files:** `src/Adze.Tools/Abstractions/` or `src/Adze.Contracts/`

### T4-03: Implement IWriteExecutionCoordinator
- [ ] Orchestrate: preview → approval → apply (in undo scope) → rebuild → verify → trace
- [ ] Start/finish undo recording with human-readable label
- [ ] Marshal Apply call to UI thread
- [ ] Capture before/after snapshots
- **Files:** `src/Adze.Host/Runtime/`
- **Reference:** END-GOAL-FINAL Section 8 (8-step lifecycle)

### T4-04: Implement IApprovalCoordinator
- [ ] `RequestApproval(preview, cancellationToken)` → ApprovalDecision
- [ ] Block background thread on `ManualResetEventSlim`
- [ ] UI thread signals on user decision (Apply/Cancel/Modify)
- [ ] Support timeout with auto-cancel
- **Files:** `src/Adze.Host/Runtime/`, `src/Adze.Host/UI/`
- **Reference:** `research-streaming-ux-patterns.md`

### T4-05: Implement IAgentPolicyEngine
- [ ] `EvaluateToolRequest(context, toolCall, descriptor)` → ToolExecutionPolicy
- [ ] Check capability class against current trust tier
- [ ] Determine approval requirement
- [ ] Fail closed on ambiguous targets
- **Files:** `src/Adze.Host/Policy/`
- **Reference:** END-GOAL-INTERFACES Section 10, END-GOAL-FINAL Section 7

### T4-06: Implement SetCustomPropertyTool
- [ ] COM API: `CustomPropertyManager.Add3()` / `.Set2()` / `.Delete2()`
- [ ] No rebuild required
- [ ] Preview: show property name, old value, new value
- [ ] Verify: re-read property and compare
- **Files:** `src/Adze.Tools/Write/`
- **Reference:** `research-write-safety-rollback.md`, `discovery-solidworks-write-api.md`

### T4-07: Implement SetDimensionValueTool
- [ ] COM API: `Dimension.SetSystemValue3()` or `.SetUserValueIn()`
- [ ] Direct lookup via `model.Parameter(fullName)`
- [ ] Rebuild required after change
- [ ] Preview: show dimension name, old value, new value with units
- [ ] Verify: re-read dimension and compare (account for unit conversion)
- [ ] Handle: driven dimensions (cannot be set), out-of-range values
- **Files:** `src/Adze.Tools/Write/`
- **Reference:** `research-write-safety-rollback.md`, `discovery-solidworks-write-api.md`

### T4-08: Implement SuppressFeatureTool / UnsuppressFeatureTool
- [ ] COM API: `Feature.SetSuppression2()`
- [ ] Rebuild required
- [ ] Preview: show feature name, current state, target state, CASCADE WARNING for dependents
- [ ] Verify: re-read suppression state
- **Files:** `src/Adze.Tools/Write/`
- **Reference:** `research-write-safety-rollback.md`

### T4-09: Add WritePreview panel to TaskPaneControl
- [ ] Inline panel (never modal) showing before/after values
- [ ] Apply / Cancel / Edit buttons
- [ ] Color-coded: red for old value, green for new value
- [ ] Warnings section for cascades or non-visibility
- [ ] Hidden by default, shown when WaitingForConfirmation state
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`
- **Reference:** `research-streaming-ux-patterns.md`

### T4-10: Add write history / undo surface
- [ ] Session history panel showing all writes with timestamps
- [ ] Individual Undo buttons per write
- [ ] Undo calls `model.EditUndo2(1)` on UI thread
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T4-11: Unit tests for write tools
- [ ] Test preview generation for each tool
- [ ] Test parameter validation (invalid dimension name, empty property name)
- [ ] Test undo label generation
- [ ] Mock COM interface tests for apply/verify patterns
- **Files:** `tests/Adze.Tests/`

### T4-12: Write tool eval suite
- [ ] Correct target identification
- [ ] Reject ambiguous write requests
- [ ] Preview text and value accuracy
- [ ] Verification after rebuild
- [ ] Cascade warning for suppression
- [ ] Wrong-target detection
- [ ] Cancellation during approval
- **Files:** `tests/Adze.Tests/`, `benchmarks/`

---

## Phase 5: Learning Activation and Trust Policy

### T5-01: Recipe candidate capture from agent traces
- [ ] Extract tool sequences from successful multi-step runs
- [ ] Create RecipeCandidate with title, tool sequence, verified run count
- [ ] Only capture from runs with verified write success
- **Files:** `src/Adze.Trace/Recipes/`

### T5-02: Recipe promotion workflow
- [ ] Require repeated verified success (configurable threshold)
- [ ] Track failure/cancellation rates
- [ ] User review step before promotion
- [ ] Promoted recipes stored under `%LOCALAPPDATA%\Adze\recipes\promoted\`
- **Files:** `src/Adze.Trace/Recipes/`, `src/Adze.Host/`

### T5-03: Trust tier progression
- [ ] Implement ITrustService with tier calculation from evidence
- [ ] Tiers: Baseline → Assisted → Reviewed → TrustedBounded
- [ ] Tier governs which capability classes are available
- [ ] No silent permission widening
- **Files:** `src/Adze.Host/Policy/`

### T5-04: Surface recipes in Task Pane
- [ ] "Suggested recipes" section when relevant recipes match current context
- [ ] One-click execution of promoted recipes
- [ ] Recipe execution still respects confirmation requirements
- **Files:** `src/Adze.Host/UI/TaskPaneControl.cs`

### T5-05: Achievement tracking from real usage
- [ ] Track mastery areas by tool usage patterns
- [ ] Achievement events recorded in traces
- [ ] Exploration percentage updated per session
- **Files:** `src/Adze.Trace/Progression/`

---

## Phase 6: Retrieval and Cross-Session Memory

### T6-01: Implement per-document memory
- [ ] Store learned patterns per document under `%LOCALAPPDATA%\Adze\memory\{hash}\`
- [ ] Key dimensions, common workflows, known issues
- [ ] Load on document open, save on session end
- **Files:** `src/Adze.Host/Runtime/` (new IMemoryStore)

### T6-02: Implement user preference memory
- [ ] Preferred answer mode, verbosity, focus areas
- [ ] Store under `%LOCALAPPDATA%\Adze\state\user-preferences.json`
- [ ] Inject into system prompt for personalization
- **Files:** `src/Adze.Host/Runtime/`

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

### T8-01: Cost controls and budget management
- [ ] Per-session token budget limit
- [ ] Per-day token budget limit
- [ ] Usage dashboard in Status tab with cost estimates by provider
- [ ] Warning when approaching budget limit

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

### TX-01: Feature gate infrastructure
- [ ] `SOLIDWORKS_AI_AGENT_LOOP=true|false`
- [ ] `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true|false`
- [ ] `SOLIDWORKS_AI_RETRIEVAL=true|false`
- [ ] `SOLIDWORKS_AI_LOCAL_MODELS=true|false`
- [ ] `SOLIDWORKS_AI_STREAM_FINAL_TEXT=true|false`
- [ ] Each phase independently disable-able
- [ ] Fallback path always reachable

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
