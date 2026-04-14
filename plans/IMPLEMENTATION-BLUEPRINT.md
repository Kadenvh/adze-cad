# Adze Implementation Blueprint

**Date:** 2026-03-15
**Status:** Final — validated, de-risked, and ready for implementation
**Purpose:** Single authoritative source for the complete path from grounded alpha to full agentic SOLIDWORKS assistant

---

## Part I: Vision and Baseline

### The Vision

Adze becomes a fully agentic AI assistant that lives inside SOLIDWORKS as a native partner. It understands the live CAD session, asks intelligent questions before acting, inspects and modifies the model through governed tools, learns from interactions, and progressively earns trust to handle more complex operations. It feels like a knowledgeable colleague who happens to have perfect recall of every dimension, mate, and reference in your assembly.

### What Exists Today (v0.1.0)

- Native SOLIDWORKS add-in with Task Pane UI (answer-first layout, Plan/Status/Tools tabs)
- 10 live read-only grounding tools (inspection surface complete)
- Hybrid broker with OpenAI/Anthropic/OpenRouter provider routing
- Model-backed answer synthesis with deterministic fallback
- Token usage monitoring and session tracking (per-run and cumulative)
- Trace/progression/recipe/achievement persistence under `%LOCALAPPDATA%\Adze`
- 175 unit tests + 6 live provider smoke tests (all passing)
- Beta install/uninstall/packaging workflow (`install/`)
- Launcher interruption detection, retry logic, and recovery
- Visual acceptance confirmed

---

## Part II: Locked Decisions

### D1. Custom host-managed runtime
Use a custom C# host-managed agent loop rather than embedding a third-party runtime framework. The host must own SOLIDWORKS threading, COM safety, preview/approval/undo/verification behavior, and CAD-specific policy enforcement.

### D2. Two provider protocol families
Support two normalized provider families:
- **OpenAI-compatible:** OpenAI, OpenRouter, Ollama, LM Studio
- **Anthropic-native:** direct Anthropic Messages/tool-use path when needed

### D3. COM stays on the UI/STA thread
All SOLIDWORKS COM calls remain on the SOLIDWORKS/UI thread. Background threads handle HTTP, argument validation, loop logic, and trace assembly.

### D4. First-wave write tools are narrowly bounded
Limited to: `set_custom_property`, `set_dimension_value`, `suppress_feature`/`unsuppress_feature`, optional `rename_object`. No modal creation states, no large hidden blast radius.

### D5. OpenClaw is out of runtime scope
Evaluated and declined for runtime integration. Useful as development-time orchestration only.

### D6. Retrieval begins as property/index retrieval
Closed-file retrieval starts with metadata/property indexing via OLE Structured Storage, not deep feature/dimension/geometry retrieval.

### D7. Local models are optional, experimental, and late-bound
Cloud-first for the core agent loop. Local providers supported later as optional, behind capability gate tests.

### D8. Streaming final text is later; streaming tool calls is not required
Buffered tool turns are operationally fine. Final-answer streaming can be added later. Partial progress updates come from the host, not from forcing streamed tool turns.

---

## Part III: Invariants

These must never be broken:

1. **The host is the safety authority.**
2. **COM stays on the UI thread.**
3. **Hard writes require preview and verification.**
4. **The model may suggest; the host must authorize.**
5. **Undo is helpful but not magical.**
6. **Memory is evidence-based, not speculative.**
7. **Retrieval must not claim geometry knowledge it does not have.**
8. **Trust progression is earned, not toggled casually.**

---

## Part IV: Implementation Phases

### Phase 1A: Pre-Prompt Clarification UI

**Prerequisite:** Current baseline (no blockers)

**Four clarification axes:**

| Axis | Control | Data Source | Behavior |
|------|---------|-------------|----------|
| Intent | `ComboBox` | Static: Inspect / Diagnose / Explain / Compare | "Compare" only when `Configurations.Count > 1` |
| Scope | `CheckedListBox` (max 6 visible, scrollable, capped at 20) | Live: features, dimensions, configs, properties, mates, components | Document-type-sensitive. "Current selection" auto-checked when `Selection.Count > 0` |
| Output mode | `ComboBox` | Static: Brief / Detailed / Tabular | "Tabular" when scope has >2 items |
| Diagnostics | `CheckBox` | Auto-checked when warnings or rebuild issues exist | Unchecked by default when clean |

**Integration:** Structured `[clarification]...[/clarification]` prefix prepended to user prompt. Zero broker changes.

**UI:** Collapsible panel between request box and Run button. Controls populated from cached SessionContext.

**Key contracts:**
```csharp
public enum PaneState { Idle, Running, WaitingForConfirmation, Completed, Failed, Cancelled }
public interface ITaskPaneStateController { PaneState CurrentState { get; } void TransitionTo(PaneState state); }
```

### Phase 1B: Conversation State

**What:** Message history across turns within a session for follow-up questions.

**Key contracts:**
```csharp
public enum ConversationRole { System, User, Assistant, Tool }
public sealed class ConversationMessage { public ConversationRole Role { get; set; } public string Text { get; set; } = string.Empty; public object? RawPayload { get; set; } public DateTimeOffset TimestampUtc { get; set; } }
public sealed class AgentConversationState { public string SessionId { get; set; } = string.Empty; public List<ConversationMessage> Messages { get; } = new(); public int EstimatedTotalTokens { get; set; } }
public interface IConversationTruncator { AgentConversationState Truncate(AgentConversationState state, int maxTotalTokens, TruncationPolicy policy); }
public sealed class TruncationPolicy { public bool ProtectSystemMessage { get; set; } = true; public bool ProtectInitialUserIntent { get; set; } = true; public int ProtectedRecentTurns { get; set; } = 6; }
```

### Phase 2: Agentic Tool Loop

**Prerequisite:** Phase 1B

**Agent loop flow:**
```
User prompt → API call with tools array
  → if tool_calls: execute each → send results → API call again (up to 10 iterations)
  → if text: done — show final answer
```

**Threading model:**
- COM capture on UI thread before loop starts
- Agent loop on ThreadPool
- No mid-loop COM refresh in Phase 1 (eliminates deadlock risk)
- Progress updates via `Control.BeginInvoke()`

**Feature gate:** `SOLIDWORKS_AI_AGENT_LOOP=true`. Existing single-turn path remains default.

**Key contracts:**
```csharp
public enum AgentStopReason { EndTurn, ToolUse, WaitingForApproval, Cancelled, MaxTokens, MaxIterations, Error, Fallback }
public enum AgentRunOutcome { Success, Cancelled, BlockedByPolicy, Failed, FellBack }

public sealed class AgentToolDefinition { public string Name { get; set; } public string Description { get; set; } public Dictionary<string, object?> ParameterSchema { get; set; } public ToolCapabilityMetadata Capability { get; set; } }
public sealed class AgentToolCall { public string Id { get; set; } public string Name { get; set; } public Dictionary<string, object?> Arguments { get; set; } public string ArgumentsJson { get; set; } }
public sealed class AgentToolResult { public string ToolCallId { get; set; } public string ToolName { get; set; } public string OutputJson { get; set; } public bool IsError { get; set; } }
public sealed class AgentTurnResponse { public bool Success { get; set; } public AgentStopReason StopReason { get; set; } public string TextContent { get; set; } public List<AgentToolCall> ToolCalls { get; set; } public ModelUsage Usage { get; set; } public object? RawAssistantMessage { get; set; } }

public interface IAgentModelClient { AgentTurnResponse SendTurn(string systemPrompt, List<object> conversationHistory, List<AgentToolDefinition> toolDefinitions, AgentModelSettings settings); object BuildUserMessage(string content); List<object> BuildToolResultMessages(List<AgentToolResult> results); }
public interface IAgentLoopRunner { AgentLoopResult Run(IAgentModelClient modelClient, IToolExecutor toolExecutor, string systemPrompt, string userRequest, List<AgentToolDefinition> toolDefinitions, AgentModelSettings settings, CancellationToken cancellationToken, Action<AgentProgressUpdate>? onProgress); }
public interface IToolExecutor { AgentToolResult Execute(string toolName, Dictionary<string, object?> arguments, ToolExecutionContext context); }
public interface IToolRegistry { IReadOnlyList<IToolDescriptor> GetEnabledTools(SessionContext context); IToolDescriptor? GetByName(string toolName); }
```

**New classes (all additive):**

| Class | Layer | Purpose |
|-------|-------|---------|
| `OpenAIFormatAgentClient` | Broker clients | Base for OpenAI/OpenRouter/Ollama/LM Studio |
| `AnthropicAgentClient` | Broker clients | Anthropic-native tool use |
| `AgentLoopRunner` | Broker orchestration | Iterative tool call → execute → return loop |
| `AgentToolDispatcher` | Broker orchestration | Maps tool_call names to existing handlers |
| `ToolDefinitionBuilder` | Broker formatting | Builds tool schema array from catalog |

### Phase 3: First Write Tools

**Prerequisite:** Phase 2 (agent loop proven with read-only tools)

**Write tool order:**

| Order | Tool | Rebuild? | Risk |
|-------|------|----------|------|
| 1st | `set_custom_property` | No | Lowest |
| 2nd | `set_dimension_value` | Yes | Low |
| 3rd | `suppress_feature` / `unsuppress_feature` | Yes | Medium |

**Six-step pattern:** Preview → Confirm → Apply (inside undo recording) → Rebuild (if needed) → Verify → Trace

**Undo grouping:** `ext.StartRecordingUndoObject("Adze: ...")` / `ext.FinishRecordingUndoObject()`

**Key contracts:**
```csharp
public enum ToolCapabilityClass { ReadSafe, SoftWrite, HardWriteFirstWave, HardWriteAdvanced, DeferredHighRisk }
public enum ApprovalRequirement { None, StandardConfirmation, ElevatedConfirmation, Disallowed }
public sealed class ToolCapabilityMetadata { public ToolCapabilityClass CapabilityClass { get; set; } public ApprovalRequirement ApprovalRequirement { get; set; } public bool RequiresUiThread { get; set; } public bool RequiresRebuild { get; set; } public bool SupportsUndoGrouping { get; set; } public bool MustCaptureSnapshot { get; set; } }

public interface IWriteTool<TParams> { WritePreview Preview(SessionContext context, TParams parameters); WriteApplyResult Apply(ISldWorks application, TParams parameters); WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult); string BuildUndoLabel(TParams parameters); }
public sealed class WritePreview { public string ToolName { get; set; } public string Summary { get; set; } public List<WriteChangeItem> Changes { get; set; } public List<string> Warnings { get; set; } }
public sealed class WriteChangeItem { public string TargetLabel { get; set; } public string BeforeValue { get; set; } public string AfterValue { get; set; } }

public interface IApprovalCoordinator { ApprovalDecision RequestApproval(WritePreview preview, CancellationToken cancellationToken); }
public interface IWriteExecutionCoordinator { WriteExecutionOutcome Execute<TParams>(IWriteTool<TParams> tool, TParams parameters, ToolExecutionContext executionContext); }
public interface IStateSnapshotService { StateSnapshot CaptureBefore(WriteTargetDescriptor target); StateSnapshot CaptureAfter(WriteTargetDescriptor target); }
public interface IStateDiffService { StateDiff Compare(StateSnapshot before, StateSnapshot after); }
public interface IVerificationPolicy { VerificationDecision Evaluate(string toolName, WriteVerification verification, SessionContext refreshedContext); }
```

### Phase 4: Advanced Write Tools + Multi-Step Plans

**Prerequisite:** Phase 3 proven safe

- Sketch creation, feature creation (high-risk, modal — most dangerous failure mode is orphaned sketch edit mode)
- Assembly operations: mate creation, component insertion
- Multi-step plan display with per-step approval
- Undo history panel with individual rollback

### Phase 5: Learning System Activation

**Prerequisite:** Phase 3 (writes generate recipe data)

- Recipe capture, promotion, and one-click execution
- Achievement tracking by mastery area
- Trust tier progression: Baseline → Assisted → Reviewed → Trusted
- Exploration tracking across sessions

**Key contracts:**
```csharp
public enum TrustTier { Baseline, Assisted, Reviewed, TrustedBounded }
public interface ITrustService { TrustTier GetCurrentTier(UserContext userContext); bool CanPromoteRecipe(RecipeCandidate candidate); }
public interface IRecipePromotionService { RecipeCandidate? TryCreateCandidate(AgentRunTrace trace); bool Promote(RecipeCandidate candidate); }
```

### Phase 6: Multi-Session Memory and Context

**Prerequisite:** Phase 2 (conversation state)

- Per-document memory under `%LOCALAPPDATA%\Adze\memory\{document-hash}\`
- Per-user preferences under `%LOCALAPPDATA%\Adze\state\user-preferences.json`
- OLE Structured Storage indexing for closed-file property retrieval (~1-5ms/file, zero dependencies)
- Optional Document Manager API enhancement for configuration-aware search

**Key contracts:**
```csharp
public interface IMemoryStore { DocumentMemory? LoadDocumentMemory(string documentKey); void SaveDocumentMemory(DocumentMemory memory); UserPreferenceMemory? LoadUserPreferences(string userKey); void SaveUserPreferences(UserPreferenceMemory memory); }
public interface IClosedFileIndexer { IndexRunResult BuildIndex(string rootFolderPath); }
public interface IClosedFileSearchService { IReadOnlyList<ClosedFileSearchResult> Search(ClosedFileSearchQuery query); }
```

### Phase 7: Production Hardening

**Prerequisite:** All prior phases stable

- Cost controls and budget management
- Local model support (Ollama/LM Studio — experimental, behind capability gate tests)
- Final-answer streaming (tool turns remain buffered)
- Performance optimization for large assemblies
- Rate limiting with exponential backoff

**Feature gates:**
```
SOLIDWORKS_AI_AGENT_LOOP=true|false
SOLIDWORKS_AI_FIRST_WAVE_WRITES=true|false
SOLIDWORKS_AI_RETRIEVAL=true|false
SOLIDWORKS_AI_LOCAL_MODELS=true|false
SOLIDWORKS_AI_STREAM_FINAL_TEXT=true|false
```

---

## Part V: Risks

| # | Risk | Severity | Mitigation |
|---|------|----------|------------|
| R1 | UI-thread misuse / COM violation | Critical | Strict UI-thread boundary, no background COM, targeted marshaling |
| R2 | False-positive verification | High | Snapshot/diff layer, rebuild/error checks, targeted verification policies |
| R3 | Ambiguous target resolution | High | Fail closed on ambiguity, clarification UI, explicit target labels in preview |
| R4 | Undo mistaken for transactionality | High | Keep writes grouped per approved action, avoid nested undo groups |
| R5 | Over-trusting local models | Medium-High | Experimental flags, capability tests, labeled provider quality |
| R6 | Recipe promotion too early | Medium-High | Require repeated verified success, track failure/cancellation rates |
| R7 | Retrieval overclaim | Medium | Distinguish property/index retrieval from open-file CAD inspection |
| R8 | Large assembly performance | Medium | Lazy inspection, capped results, pagination, truncation limits |

---

## Part VI: Phase Gates

| Gate | Before... | Must have... |
|------|-----------|-------------|
| A | Phase 2 builds on conversation state | Clarification controls populate from live data, truncation works, no extra COM chatter |
| B | Write tools enabled | Agent loop reliable with read-only tools, cancellation works, error budgets work, traces capture each turn |
| C | First-wave writes enabled | Before/after snapshots, diffs recorded, verification policies implemented |
| D | Moving past first-wave writes | Each write has preview/apply/verify/rollback, undo labels correct, eval suite covers wrong-target and cancellation |
| E | Learning activation | Trace quality supports evidence-based promotion, unstable recipes filtered, trust logic auditable |
| F | Retrieval expansion | Index is fast/interactive, property queries accurate, unsupported queries rejected clearly |
| G | Advanced writes | First-wave writes proven safe in practice, multi-step plan review UI exists, dependency previews understood |

---

## Part VII: Implementation Dependencies

```
Phase 1A (Clarification UI) ──────────────────────────┐
Phase 1B (Conversation State) ─────────────────────────┤
                                                        │
Phase 2 (Agentic Tool Loop) ←── depends on 1B ────────┤
                                                        │
Phase 3 (First Write Tools) ←── depends on 2 ─────────┤
                                                        │
Phase 4 (Advanced Writes) ←──── depends on 3 ─────────┤
                                                        │
Phase 5 (Learning) ←──────────── depends on 3 ─────────┤
                                                        │
Phase 6 (Memory & Retrieval) ←── depends on 2 ─────────┤
                                                        │
Phase 7 (Hardening) ←─────────── depends on all ───────┘

1A and 1B run in parallel.
5 and 6 run in parallel.
Phase 7 items are independently deliverable.
```

---

## Part VIII: UX Patterns

### Three-tier progressive disclosure
- **Glance:** Status label + answer panel (most users stay here)
- **Scan:** Activity log in Plan tab
- **Dig:** Full tool output in Tools tab + log files

### Write confirmation
- Inline preview panel (never modal): before/after values, Apply/Cancel/Edit
- State machine: Idle → Running → WaitingForConfirmation → Completed
- Background thread waits on `ManualResetEventSlim`; UI signals on user decision

### Multi-step plan review (Phase 4)
- Step-at-a-time for single writes (Phase 3)
- `CheckedListBox` with per-step checkboxes for batch plans (Phase 4)

### Error presentation
- Tool failures: non-prominent log lines (agent self-corrects)
- API errors: retry status shown
- COM/host errors: calm recovery guidance
- Never stack traces in the answer panel

---

## Part IX: Testing Strategy

### Eval categories
- **Read-loop:** Correct tool selection, scope narrowing, configuration comparison
- **Write-loop:** Target identification, ambiguity rejection, preview correctness, post-rebuild verification, cascade warnings
- **Recovery:** Missing tool-call IDs, malformed arguments, timeouts, mid-loop cancellation, approval denial
- **Retrieval:** Property search on closed files, configuration-aware search, refuse unsupported geometry queries
- **Trust/recipe:** Only promote after repeated verified success, filter frequent cancellation/rollback

### Test infrastructure
- Unit tests for all new contracts, dispatchers, conversation state
- Live smoke tests extended for tool use format validation
- Write tool tests against mock COM interfaces
- Integration tests for agent loop (mock model responses, verify execution sequence)
- Benchmark suite extended for write tool coverage

---

## Part X: Success Criteria

The end-goal is reached when a SOLIDWORKS user can:

1. Open a part and say "Make the base 10mm wider"
2. See the agent ask "Do you mean D1@Sketch1 (currently 50mm → 60mm)?" — populated from real dimension data
3. Click "Yes" on the preview showing before/after values
4. Watch the dimension update in real-time with undo recording
5. See the agent verify the change and log it with a trace
6. Later say "Actually, undo that" and have it restored via the undo history
7. Have the agent remember this pattern and suggest it as a recipe next time
8. Trust the agent enough to batch-approve a multi-step modification plan
9. Work entirely offline with a local model when no API key is configured
10. Feel like the agent is a colleague who knows this model inside and out

---

## Research References

All supporting research is in `documentation/plans/`:

| Brief | Key Finding |
|-------|-------------|
| `research-write-safety-rollback.md` | Orphaned sketch edit mode is most dangerous failure; custom properties safest first tool |
| `research-agent-loop-threading.md` | No mid-loop COM refresh in Phase 1; 8 anti-patterns documented |
| `research-tool-calling-abstraction.md` | Two real format families (OpenAI-compat + Anthropic); normalized `IAgentModelClient` interface |
| `research-local-model-feasibility.md` | Local models work for synthesis; tool-calling unreliable below 32B params |
| `research-openclaw-feasibility.md` | Evaluated and declined for runtime — dev-workflow tool only |
| `research-closed-file-retrieval.md` | OLE Structured Storage gives zero-dependency property indexing at ~1-5ms/file |
| `research-streaming-ux-patterns.md` | Three-tier progressive disclosure; confirmation via ManualResetEventSlim |

---

## What to Build Next

Phase 1A (Clarification UI) and Phase 1B (Conversation State) start immediately, in parallel. Phase 2 (Agentic Tool Loop) follows as soon as conversation state is in place. The path is validated and unblocked.
