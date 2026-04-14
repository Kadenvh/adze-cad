# END-GOAL: Full Agentic SOLIDWORKS Assistant

**Date:** 2026-03-15
**Status:** Discovery complete — validated and ready for implementation
**Purpose:** The complete, compiled path from current grounded alpha to the fully realized product

---

## The Vision

Adze becomes a fully agentic AI assistant that lives inside SOLIDWORKS as a native partner. It understands the live CAD session, asks intelligent questions before acting, inspects and modifies the model through governed tools, learns from interactions, and progressively earns trust to handle more complex operations. It feels like a knowledgeable colleague who happens to have perfect recall of every dimension, mate, and reference in your assembly.

---

## What Exists Today (v0.1.0)

- Native SOLIDWORKS add-in with Task Pane UI (answer-first layout, Plan/Status/Tools tabs)
- 10 live read-only grounding tools (inspection surface complete)
- Hybrid broker with OpenAI/Anthropic/OpenRouter provider routing
- Model-backed answer synthesis with deterministic fallback
- Token usage monitoring and session tracking (per-run and cumulative)
- Trace/progression/recipe/achievement persistence under `%LOCALAPPDATA%\Adze`
- 175 unit tests + 6 live provider smoke tests (all passing)
- Beta install/uninstall/packaging workflow (`install/`)
- Launcher interruption detection, retry logic, and recovery
- Visual acceptance confirmed — Task Pane looks and works well in SOLIDWORKS

---

## Phase 1: Interactive Clarification + Conversation State

**Prerequisite:** Current baseline (no blockers)
**Discovery source:** `discovery-clarification-ui.md`

### 1A: Pre-Prompt Clarification UI

**What:** Add contextual clarifying controls to the Task Pane composer, populated from live SessionContext data. Controls appear between the request text box and the Run button in a collapsible panel.

**Validated design — four axes:**

| Axis | Control | Data Source | Behavior |
|------|---------|-------------|----------|
| Intent | `ComboBox` | Static: Inspect / Diagnose / Explain / Compare | "Compare" only shown when `Configurations.Count > 1` |
| Scope | `CheckedListBox` (max 6 visible, scrollable, capped at 20 items) | Live: features from `FeatureTree.Features`, dimensions from `Dimensions.Items`, configs, properties, mates, components | Document-type-sensitive: part shows features/dimensions, assembly adds mates/components. "Current selection" auto-checked when `Selection.Count > 0` |
| Output mode | `ComboBox` | Static: Brief / Detailed / Tabular | "Tabular" shown when scope has >2 items selected |
| Diagnostics | `CheckBox` | Auto-checked when warnings or rebuild issues exist | Unchecked by default when document is clean |

**Integration approach (zero broker changes):**
- Clarification choices are formatted as a structured `[clarification]...[/clarification]` prefix prepended to the user prompt
- The existing keyword scanner already picks up terms like "dimensions", "mates", etc.
- The prefix naturally steers the model's tool selection and answer scope
- Controls populated from cached `SessionContext` (no additional COM calls)

**UI layout:**
- Collapsible via a `LinkLabel` toggle ("Show options" / "Hide options")
- Controls stack vertically (labels above dropdowns, not beside) for the narrow 300-400px sidebar
- Panel inserts into existing `composerPanel` between request box and run row
- `composerPanel` height adjusts dynamically based on collapse state

**Implementation:** ~2-3 sessions. Pure UI work in `TaskPaneControl.cs` + prompt prefix formatting.

### 1B: Conversation State

**What:** Maintain message history across turns within a session so the user can ask follow-up questions.

**Validated design:**
- `AgentConversationState` class holds a provider-neutral `List<ConversationMessage>` with roles (system/user/assistant/tool)
- State is per-click initially (Phase 1), extended to cross-click in Phase 3
- Token management via sliding window truncator that protects: system prompt, initial user message, and most recent turns
- Stored in `HostState` as session-scoped state

**Implementation:** ~1-2 sessions. Broker-layer work in `HostState` + `HybridBrokerOrchestrator`.

---

## Phase 2: Agentic Tool Loop

**Prerequisite:** Phase 1B (conversation state infrastructure)
**Discovery sources:** `discovery-api-tool-use.md`, `discovery-agent-loop-architecture.md`

### What Changes

The current two-pass architecture (broker plans → host executes all tools → synthesis answers) becomes a model-driven iterative loop where the model calls tools, observes results, and decides what to do next.

### Validated API Format

**Key finding: OpenRouter with OpenAI-format tool calling can access both providers through a single client implementation.** This simplifies our architecture — we only need one tool-calling format.

**Tool definition format (OpenAI-compatible, works via OpenRouter for all providers):**
```json
{
  "type": "function",
  "function": {
    "name": "get_dimensions",
    "description": "Returns all dimensions in the active document.",
    "parameters": {
      "type": "object",
      "properties": {
        "feature_name": { "type": "string", "description": "Optional feature to scope query." }
      },
      "required": []
    }
  }
}
```

**Agent loop flow:**
```
User prompt → API call with tools array
  → if response has tool_calls: execute each tool → send tool results → API call again
  → if response has tool_calls: repeat (up to iteration cap, default 10)
  → if response is text: done — show final answer
```

**Response parsing:** Model returns `tool_calls` array with `id`, `function.name`, `function.arguments` (JSON string requiring separate parse). Results sent back as `role: "tool"` messages with `tool_call_id`.

### Validated Threading Model

**Discovery source:** `discovery-agent-loop-architecture.md`

```
[UI thread] Capture SessionContext from COM
  → ThreadPool.QueueUserWorkItem()
    → [Background thread] Agent loop runs
      → API call (background — no COM needed)
      → Tool execution against cached SessionContext (background — pure functions)
      → If tool needs fresh COM data: Control.Invoke() → UI thread → re-capture → return
      → Progress updates: Control.BeginInvoke() → UI thread (fire-and-forget)
    → Final answer returned
  → [UI thread] PostToUi() → update Task Pane
```

### New Classes (all additive — existing code untouched)

| Class | Layer | Purpose |
|-------|-------|---------|
| `IAgentModelClient` | Broker abstractions | Interface for tool-use-aware model calls |
| `AgentLoopRunner` | Broker orchestration | Iterative tool call → execute → return loop |
| `OpenAIAgentClient` | Broker clients | Tool use with OpenAI/OpenRouter format |
| `AnthropicAgentClient` | Broker clients | Tool use with Anthropic format (if needed beyond OpenRouter) |
| `AgentToolDispatcher` | Broker orchestration | Maps tool_call names to existing grounding tool handlers |
| `AgentConversationState` | Broker models | Message history with token-aware truncation |
| `ToolDefinitionBuilder` | Broker formatting | Builds tool schema array from existing tool catalog |

### Feature Gate

- `SOLIDWORKS_AI_AGENT_LOOP=true` enables the new path
- Existing single-turn path remains the default — zero-risk rollback
- Deterministic fallback preserved: if agent loop fails entirely, falls back to existing path

### Cancellation

- `CancellationTokenSource` with checks before/after each API call and before each tool execution
- "Run assistant" button toggles to "Cancel" during a run
- Cancelled runs produce a partial result from whatever was completed

### Error Handling

- Tool failures sent back to the model as error results (model can self-correct)
- API failures use consecutive-error budget (default 2 — two failures in a row triggers fallback)
- Full fallback to deterministic single-turn path when agent loop fails

**Implementation:** ~3-4 sessions. Broker + host work. Phase 2A: Anthropic-native. Phase 2B: OpenAI + hardening.

---

## Phase 3: First Write Tools

**Prerequisite:** Phase 2 (agent loop for iterative execution and verification)
**Discovery source:** `discovery-solidworks-write-api.md`

### Write Tool Order (validated by COM API research)

| Order | Tool | COM API | Rebuild? | Risk | Reason for ordering |
|-------|------|---------|----------|------|-------------------|
| **1st** | `set_custom_property` | `CustomPropertyManager.Add3()` / `.Set2()` / `.Delete2()` | No | Lowest | Clean API, no rebuild, already read-implemented, no risk of geometric corruption |
| **2nd** | `set_dimension_value` | `Dimension.SetSystemValue3()` or `.SetUserValueIn()` | Yes | Low | Direct lookup via `model.Parameter(fullName)`, clear return codes, requires rebuild |
| **3rd** | `suppress_feature` / `unsuppress_feature` | `Feature.SetSuppression2()` | Yes | Medium | Suppression cascades to dependent features, requires rebuild |
| **Later** | `create_sketch` | `SketchManager.InsertSketch(true)` + geometry methods | Yes | High | Modal state, corruption risk if interrupted |
| **Later** | `add_extrusion` | `FeatureManager.FeatureExtrusion3()` (25+ params) | Yes | High | Requires valid closed sketch, fails silently |
| **Later** | `add_mate` | `AssemblyDoc.AddMate5()` | Yes | High | Requires pre-selected entities with marks, constraint solving |

### Safety Contract

Every write tool implements `IWriteTool<TParams>`:

```csharp
public interface IWriteTool<TParams>
{
    WritePreview Preview(SessionContext context, TParams parameters);
    WriteResult Apply(ISldWorks application, TParams parameters);
    WriteVerification Verify(SessionContext refreshedContext, WriteResult result);
    string RollbackGuidance { get; }
}
```

**Undo grouping (validated):**
```csharp
ModelDocExtension ext = model.Extension;
ext.StartRecordingUndoObject("Adze: Set dimension D1@Sketch1 to 60mm");
// ... execute write operation ...
ext.FinishRecordingUndoObject();
// User sees "Undo Adze: Set dimension D1@Sketch1 to 60mm" in Edit menu
```

**Six-step pattern for every write:**
1. **Preview:** Build `WritePreview` showing what will change (before/after values)
2. **Confirm:** Show preview in Task Pane, wait for user approval
3. **Apply:** Execute COM operation inside undo recording scope
4. **Rebuild:** Call `model.ForceRebuild3(false)` if the tool requires it
5. **Verify:** Re-read the affected data to confirm the change took effect
6. **Trace:** Record the write operation with before/after state, undo label, and verification status

### Confirmation UI

When the agent loop decides to call a write tool:
1. Agent loop pauses
2. Task Pane shows a preview panel: "Change D1@Sketch1 from 50.0mm to 60.0mm"
3. User clicks Apply / Cancel / Modify
4. If Apply: execute the write, verify, resume agent loop
5. If Cancel: send cancellation back to model, model adjusts plan
6. If Modify: user edits the value, then Apply

**Implementation:** ~3-4 sessions. `Adze.Tools` + `Adze.Host` + confirmation UI.

---

## Phase 4: Advanced Write Tools + Multi-Step Plans

**Prerequisite:** Phase 3 (basic writes proven safe)

### What Changes

- Agent can chain multiple write operations in a single plan
- Multi-step plan display: show all planned steps, let user approve individually or batch
- Sketch creation and feature creation tools (high-risk, modal — require careful state management)
- Assembly operations: mate creation, component insertion

### Multi-Step Plan UI

```
Agent Plan (3 steps):
  1. [x] Set D1@Sketch1 to 60mm                    [Applied]
  2. [ ] Set Material property to "Aluminum 6061"   [Pending - Apply?]
  3. [ ] Suppress Fillet1                           [Pending]

  [Apply All] [Cancel Remaining]
```

### Undo History Panel

Timeline of all modifications made this session, with individual undo buttons:
```
Session History:
  14:32 — Set D1@Sketch1: 50mm → 60mm              [Undo]
  14:33 — Set Material: "Steel" → "Aluminum 6061"   [Undo]
  14:35 — Suppressed Fillet1                         [Undo]
```

**Implementation:** ~4-6 sessions.

---

## Phase 5: Learning System Activation

**Prerequisite:** Phase 3 (write operations generate meaningful recipe data)

### What Changes

The existing trace/recipe/achievement/progression infrastructure becomes active:

- **Recipe capture:** Successful multi-step workflows (read + write sequences) are automatically captured as recipe candidates
- **Recipe promotion:** User reviews candidates and approves them as repeatable workflows
- **One-click recipes:** Approved recipes appear as suggested actions in the Task Pane ("Apply material standardization recipe?")
- **Achievement tracking:** Mastery areas tracked by actual usage (dimensioning expert, assembly structure expert, diagnostics expert)
- **Trust tier progression:**
  - Tier 0 (Baseline): Read-only inspection — where we are now
  - Tier 1 (Assisted): Simple reversible writes with per-operation confirmation
  - Tier 2 (Reviewed): Complex writes, batch approval of reviewed recipe plans
  - Tier 3 (Trusted): Autonomous execution within reviewed recipe bounds
- **Exploration tracking:** How much of the tool surface has been exercised across sessions

**Implementation:** ~2-3 sessions. Mostly `Adze.Trace` + `Adze.Host` UI work — the data structures already exist.

---

## Phase 6: Multi-Session Memory and Context

**Prerequisite:** Phase 2 (conversation state infrastructure)

### What Changes

The agent remembers across sessions:

- **Per-document memory:** What the agent learned about this specific file (key dimensions, common workflows, known issues). Stored as JSON alongside traces under `%LOCALAPPDATA%\Adze\memory\{document-hash}\`
- **Per-user preferences:** Verbosity level, focus areas, common question patterns. Stored under `%LOCALAPPDATA%\Adze\state\user-preferences.json`
- **Cross-session recipe promotion:** Patterns that succeed repeatedly across sessions get automatically promoted for review
- **Project context:** Understanding of multi-file assemblies and their inter-document relationships (which parts go in which assemblies, shared dimensions)
- **Retrieval without COM:** Index closed SOLIDWORKS file metadata (custom properties, feature names, dimension names) by reading OLE structured storage from the file format directly — no COM or SOLIDWORKS process needed. Enables search across a project folder.

**Implementation:** ~3-4 sessions. New persistence layer + retrieval infrastructure.

---

## Phase 7: Production Hardening and Infrastructure

**Prerequisite:** All prior phases stable

### What Changes

- **Cost controls:** Per-session and per-day token budget limits. Usage dashboard in Status tab shows cumulative cost estimates by provider.
- **Local model support:** Add Ollama and LM Studio as provider options alongside cloud APIs. Same tool use format (OpenAI-compatible). Enable fully offline operation.
- **OpenClaw integration:** Explore whether OpenClaw instances can provide orchestration, routing, or agent infrastructure. Discovery needed.
- **Advanced telemetry:** Agent behavior analytics — which tools are called most, which plans succeed/fail, where users cancel, what recipes get promoted.
- **Performance optimization:** Handle large assemblies (1000+ components) without timeout. Lazy tool execution, paginated results, progressive context loading.
- **Rate limiting:** Proper retry with exponential backoff for API calls. Request queuing during rate limit windows.
- **Streaming:** Stream final-answer text to the Task Pane as it generates (tool call turns still require full buffering).

**Implementation:** Ongoing. Each item is independently deliverable.

---

## Cross-Cutting Concerns

### Security
- Write tools never execute without explicit user confirmation (Tiers 0-2)
- Undo recording wraps every write operation
- No COM access outside the in-process add-in boundary
- API keys stored in environment variables, never in files or registry
- Traces record all operations for auditability

### Testing Strategy
- Unit tests for all new contracts, tool dispatchers, conversation state management
- Live smoke tests extended for tool use format validation
- Write tool tests against mock COM interfaces (verify correct API calls and parameter patterns)
- Integration tests for agent loop (mock model responses, verify tool execution sequence)
- Benchmark suite extended for write tool coverage

### Backward Compatibility
- Every phase is feature-gated behind environment variables
- The existing single-turn deterministic path remains functional at all times
- New code is additive — existing classes and interfaces are not modified
- Install/uninstall scripts work identically regardless of which features are enabled

---

## Implementation Dependencies

```
Phase 1A (Clarification UI) ─────────────────────────────────────┐
Phase 1B (Conversation State) ───────────────────────────────────┤
                                                                  │
Phase 2 (Agentic Tool Loop) ←────── depends on 1B ──────────────┤
                                                                  │
Phase 3 (First Write Tools) ←────── depends on 2 ───────────────┤
                                                                  │
Phase 4 (Advanced Writes) ←──────── depends on 3 ───────────────┤
                                                                  │
Phase 5 (Learning Activation) ←──── depends on 3 ───────────────┤
                                                                  │
Phase 6 (Memory & Retrieval) ←───── depends on 2 ───────────────┤
                                                                  │
Phase 7 (Production Hardening) ←─── depends on all ─────────────┘

Phases 1A and 1B can run in parallel.
Phases 5 and 6 can run in parallel (both depend on earlier phases but not each other).
Phase 7 items are independently deliverable at any point after their prerequisites.
```

---

## Discovery Brief References

All open questions from the original vision have been researched and validated:

| Question | Discovery Brief | Status | Key Finding |
|----------|----------------|--------|-------------|
| API tool use format | `discovery-api-tool-use.md` | Validated | OpenRouter unifies both providers under OpenAI-compatible format |
| COM write safety | `discovery-solidworks-write-api.md` | Validated | `StartRecordingUndoObject` enables grouped undo; `set_custom_property` is safest first tool |
| Agent loop threading | `discovery-agent-loop-architecture.md` | Validated | ThreadPool + Control.Invoke for COM; feature-gated; all additive classes |
| Clarification UI | `discovery-clarification-ui.md` | Validated | Zero broker changes for Phase 1; prefix-based integration; dynamic SessionContext population |
| Streaming | `discovery-api-tool-use.md` | Validated | Defer to Phase 7; tool call turns need full buffering anyway |
| Token management | `discovery-agent-loop-architecture.md` | Validated | Sliding window truncator protecting system prompt + initial user message + recent turns |
| OpenClaw feasibility | Not yet researched | Pending | Deferred to Phase 7 exploration |
| Retrieval architecture | Not yet researched | Pending | Deferred to Phase 6; likely OLE structured storage parsing |

---

## Success Criteria

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

## What to Build Next

Phase 1A (Clarification UI) and Phase 1B (Conversation State) can start immediately and run in parallel. Phase 2 (Agentic Tool Loop) follows as soon as conversation state is in place. The path is validated and unblocked.


---
---
---

# RESEARCH FINDINGS

The following 7 research briefs validate and de-risk the platform-specific execution truth underneath the END-GOAL vision above. Each brief was produced by an independent research agent with full access to the codebase and existing discovery documents.


---

# Research Brief: Write Safety and Rollback Guarantees in the SOLIDWORKS COM API

**Date:** 2026-03-15
**Mode:** De-risking research
**Status:** Complete
**Companion document:** `discovery-solidworks-write-api.md` (method signatures and parameter enums)
**Grounding sources:** Existing read tool implementations in `src/Adze.Tools/Grounding/`, COM access patterns in `src/Adze.Host/Services/SessionContextBuilder.cs`, interop assemblies from `SOLIDWORKS 3DEXPERIENCE R2026x`

---

## Purpose

This document answers a single question: **for each write operation Adze might perform, what are the exact rollback and undo guarantees available through the SOLIDWORKS COM API, and what are the failure modes that could leave a document in a state the user did not request?**

This is not a design proposal. It is a risk inventory. The recommendations at the end are risk classifications, not implementation plans.

---

## 1. The Undo Infrastructure Available to Add-ins

SOLIDWORKS exposes three undo-related mechanisms through the COM API. Understanding their exact guarantees and limitations is the foundation for everything that follows.

### 1.1 Single-Step Undo: `IModelDoc2.EditUndo2(int count)`

| Property | Detail |
|----------|--------|
| **What it does** | Undoes the most recent `count` operations from the undo stack |
| **Return value** | `bool` -- `true` if at least one undo step succeeded |
| **Atomicity** | Each call is atomic; partial undo within a grouped operation is not possible |
| **Availability** | Available on all document types (part, assembly, drawing) |
| **Limitations** | Cannot target a specific operation in the middle of the stack. Cannot undo past a save boundary. The undo stack has a finite depth controlled by `Tools > Options > System Options > General > Undo` (default varies by version, typically 20-50 steps). Once the stack is full, the oldest entries are silently dropped. |
| **Thread safety** | Must be called on the SOLIDWORKS UI/host thread |

### 1.2 Grouped Undo: `IModelDocExtension.StartRecordingUndoObject` / `FinishRecordingUndoObject`

| Property | Detail |
|----------|--------|
| **What it does** | Groups all operations between `Start` and `Finish` into a single undo step with a user-visible label |
| **API** | `void StartRecordingUndoObject(string label)` / `void FinishRecordingUndoObject()` |
| **Label visibility** | The label appears in the Edit menu as "Undo [label]" and in the undo dropdown |
| **Nesting** | **Not supported.** Calling `StartRecordingUndoObject` while a recording is already active produces undefined behavior. The inner group may silently merge into the outer group, or both may be lost. Adze must guarantee single-level grouping only. |
| **Exception during recording** | If an exception occurs between `Start` and `Finish`, calling `FinishRecordingUndoObject` in a `finally` block closes the group. Any operations that executed before the exception are included in the group and are undoable as one step. Operations that did not execute are simply absent from the group. |
| **Empty group** | If no operations execute between `Start` and `Finish`, an empty undo step may appear in the stack. This is harmless but unclean. The implementation should detect this case and avoid emitting a rollback reference for it. |
| **Availability in R2026x** | This method exists on `IModelDocExtension` in the R2026x interop. It has been present since SOLIDWORKS 2010 (API version 18). |
| **Rebuild within a group** | Calling `EditRebuild3()` inside a group is valid. The rebuild itself does not create a separate undo step -- it is absorbed into the group. |

### 1.3 No Transactions, No Savepoints

SOLIDWORKS has **no** equivalent to a database transaction, savepoint, or rollback-to-mark mechanism. Specifically:

- There is no `BeginTransaction()` / `CommitTransaction()` / `RollbackTransaction()` API.
- There is no way to create a named savepoint and later restore to it.
- There is no way to atomically test-and-commit: an operation either succeeds and is recorded, or fails and is not recorded, but there is no way to execute speculatively and then decide whether to keep the result.
- `IModelDoc2.ClearUndoList()` destroys all undo history and must never be called by Adze.

### 1.4 Undo Stack Boundary Conditions

| Condition | Effect on undo |
|-----------|---------------|
| **Document save** (`IModelDoc2.Save3`)| Commits all pending state. Undo stack is preserved across save in SOLIDWORKS 2016+, but saving is irreversible itself -- you cannot undo a save. |
| **Document close/reopen** | Undo stack is destroyed on close. |
| **Configuration switch** | Undo stack is preserved, but undoing a pre-switch operation after switching configurations can produce unexpected geometry if the operation was configuration-scoped. |
| **External file modification** | If a referenced part file is modified externally while the assembly is open, undo behavior for operations that depend on that reference becomes unreliable. |
| **Add-in crash** | Undo stack survives add-in unload/crash because SOLIDWORKS manages it. The user can still undo from the Edit menu after an add-in failure. |
| **SOLIDWORKS crash** | Undo stack is lost. Recovery file may exist. |

---

## 2. Operation-by-Operation Rollback Matrix

### Legend

| Column | Meaning |
|--------|---------|
| **Operation** | The write action under evaluation |
| **API entry points** | Primary COM methods used |
| **Grouped undo supported** | Whether `StartRecordingUndoObject` / `FinishRecordingUndoObject` wrapping works correctly |
| **Reversal deterministic** | Whether `EditUndo2(1)` after a grouped operation restores the exact prior state with no side effects |
| **Before/after snapshot needed** | Whether Adze must capture state before applying the change, beyond what the undo stack provides |
| **Rebuild required** | Whether `EditRebuild3()` must be called for the change to take geometric effect |
| **Failure modes** | Ways the operation can fail or leave the document in an unexpected state |
| **Verification method** | How to confirm the write took effect as intended |
| **Recommendation** | Wave assignment and risk classification |

---

### 2.1 Dimension Value Change

| Attribute | Detail |
|-----------|--------|
| **Operation** | Set a dimension to a new numeric value |
| **API entry points** | `Dimension.SetUserValueIn(ModelDoc2, double)` (document units) or `Dimension.SetSystemValue3(double, int, string[])` (SI units). Dimension lookup via `ModelDoc2.Parameter(fullName)` or feature traversal. |
| **Grouped undo supported** | **Yes.** Dimension value changes are recorded in the SOLIDWORKS undo stack. Wrapping in `StartRecordingUndoObject` / `FinishRecordingUndoObject` works correctly and absorbs the subsequent rebuild into the same group. |
| **Reversal deterministic** | **Yes, with caveats.** `EditUndo2(1)` restores the prior dimension value and triggers an implicit rebuild. The geometry returns to its prior state. **Caveat:** if the dimension change caused a downstream rebuild error (e.g., a fillet that can no longer resolve), undoing the dimension change also undoes the error, restoring the prior clean state. This is correct behavior. **Caveat:** if equations reference this dimension, the equation values update on undo, restoring the prior equation state. This is also correct. |
| **Before/after snapshot needed** | **Recommended but not strictly required for rollback.** The undo stack handles restoration. However, a before-snapshot enables Adze to report what changed in the trace log and to detect verification mismatches. Read the dimension value via `Dimension.GetUserValueIn(model)` before and after. |
| **Rebuild required** | **Yes.** The model does not update geometrically until `EditRebuild3()` is called. |
| **Failure modes** | 1. `swSetValue_InvalidValue` -- value out of valid range (e.g., negative length for a boss extrude depth). No state change occurs. 2. `swSetValue_InternalError` -- COM-level failure. No state change occurs. 3. Attempting to set a **driven dimension** (controlled by a relation, equation, or constraint). `SetSystemValue3` returns an error code. Check `Dimension.DrivenState` beforehand (`swDimensionDrivenState_e`). 4. Attempting to set a dimension on a **suppressed feature**. The dimension object may be obtainable but the value change has no geometric effect until the feature is unsuppressed. 5. Wrong configuration scope -- setting a value in the wrong configuration silently succeeds for that configuration but does not affect the active view. 6. Post-rebuild failure -- the new value is geometrically valid in isolation but causes a downstream feature to fail (e.g., a cut that now exceeds the body bounds). The rebuild completes but the model has errors visible in diagnostics. |
| **Verification method** | Re-read the dimension value via `Dimension.GetUserValueIn(model)` and compare to the intended value. Check `IModelDocExtension.NeedsRebuild2` to confirm rebuild completed. Check `IModelDoc2.GetFirstModelDoc2Error()` or feature error states for downstream failures. |
| **Recommendation** | **Wave 2 -- Tier 1 (safe for first-wave automation).** ActionClass: Yellow. Requires preview, confirmation, undo grouping, rebuild, and verification. Pre-check driven state. Pre-check suppression state. |

---

### 2.2 Custom Property Set/Add/Delete

| Attribute | Detail |
|-----------|--------|
| **Operation** | Add, modify, or delete a custom property at the document or configuration level |
| **API entry points** | `CustomPropertyManager.Add3(name, type, value, overwriteOption)`, `CustomPropertyManager.Set2(name, value)`, `CustomPropertyManager.Delete2(name)`. Manager obtained via `IModelDocExtension.get_CustomPropertyManager("")` (document-level) or `get_CustomPropertyManager("ConfigName")` (configuration-level). |
| **Grouped undo supported** | **Yes.** Custom property operations are recorded in the undo stack. Multiple property changes within a single undo group appear as one undo step. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` after a grouped property change restores the prior property state exactly. For `Add3`, undo removes the property. For `Set2`, undo restores the prior value. For `Delete2`, undo restores the deleted property with its prior value and type. |
| **Before/after snapshot needed** | **Recommended.** Read the property value via `CustomPropertyManager.Get6()` before and after. Especially important for `Delete2`, where the trace log should record the deleted value for audit purposes. |
| **Rebuild required** | **No.** Custom properties are metadata. No geometric rebuild is needed. |
| **Failure modes** | 1. `Add3` with `swCustomPropertyOnlyIfNew` returns `swCustomInfoAddResult_AlreadyExists` if the property exists. No state change. 2. `Set2` on a non-existent property may silently fail (returns error code, no exception). 3. `Delete2` on a non-existent property returns a failure code but does not throw. 4. Overwriting a **linked property** (e.g., one whose value expression is `"$PRP:SW-File Name"`) replaces the expression with a literal string, breaking the link. The prior expression is not recoverable without a snapshot or undo. 5. Setting a property on a configuration that does not exist silently creates a configuration-level property manager but may not behave as expected. Verify configuration existence first. |
| **Verification method** | Re-read the property via `CustomPropertyManager.Get6()` and compare to the intended value. For deletions, confirm the property is absent. |
| **Recommendation** | **Wave 2 -- Tier 1 (safest first-wave automation).** ActionClass: Yellow. Lowest risk of all write operations. No rebuild, no geometry impact, fully undoable, clean return codes. Must warn before overwriting linked properties. |

---

### 2.3 Feature Suppression State Change

| Attribute | Detail |
|-----------|--------|
| **Operation** | Suppress or unsuppress a feature |
| **API entry points** | `Feature.SetSuppression2(int state, int inConfig, string[] configNames)`. Feature lookup via `IModelDoc2.FeatureByName(name)` or traversal. State enum: `swFeatureSuppressionAction_e`. |
| **Grouped undo supported** | **Yes.** Suppression changes, including cascading child suppression, are captured in a single undo group when wrapped in `StartRecordingUndoObject` / `FinishRecordingUndoObject`. |
| **Reversal deterministic** | **Mostly yes, with an important asymmetry.** Suppressing a parent automatically suppresses its children. Undoing the suppression restores the parent to its unsuppressed state, **and also restores the children to their prior state.** The undo stack correctly tracks the cascade. However: if you suppress a parent (which cascades to children), and then **separately** unsuppress one child, and then undo the unsuppression, you are back to parent-suppressed + all-children-suppressed. The undo stack is linear, not tree-structured, so interleaved operations on parents and children undo in stack order. |
| **Before/after snapshot needed** | **Strongly recommended.** Suppression cascades make the effective change larger than the explicit request. Adze should snapshot the suppression state of the target feature **and all its children and dependents** before applying, and compare after. This enables accurate trace logging and cascade-aware verification. |
| **Rebuild required** | **Yes.** `EditRebuild3()` must be called after suppression state changes for geometry to update. |
| **Failure modes** | 1. `SetSuppression2` returns `false` if the feature is in an edit state (e.g., sketch edit mode is active). No state change. 2. Suppressing a feature that other features reference externally (e.g., in-context references from another part in an assembly) does not fail, but breaks those external references. The referencing documents will show rebuild errors. 3. Unsuppressing a feature whose parent is suppressed fails silently -- the feature cannot resolve without its parent. 4. Suppressing/unsuppressing in a read-only configuration fails silently. 5. Suppressing a sketch that is the basis for a boss extrude cascades to the extrude. This is correct behavior but can surprise users. |
| **Verification method** | Re-read `Feature.IsSuppressed2()` for the target feature. Also check suppression state of known dependents. Check `IModelDocExtension.NeedsRebuild2` after rebuild. |
| **Recommendation** | **Wave 2 -- Tier 2 (safe with dependency awareness).** ActionClass: Yellow. Requires pre-checking the feature dependency graph (`Feature.GetParents()`, `Feature.GetChildren()`) and presenting the cascade in the preview. |

---

### 2.4 Configuration-Specific Edits (Dimension Values and Suppression)

| Attribute | Detail |
|-----------|--------|
| **Operation** | Set a dimension value or suppression state in a specific named configuration (not the active one) |
| **API entry points** | `Dimension.SetSystemValue3(value, swSetValue_InSpecifiedConfigurations, configNames)` for dimensions. `Feature.SetSuppression2(state, swSpecifyConfiguration, configNames)` for suppression. |
| **Grouped undo supported** | **Yes.** Configuration-scoped changes are recorded in the undo stack and can be grouped. |
| **Reversal deterministic** | **Yes, but invisible until the configuration is activated.** Undoing a change made to a non-active configuration restores that configuration's stored values, but the user sees no visual change in the viewport because they are viewing a different configuration. This is correct but disorienting. |
| **Before/after snapshot needed** | **Required.** Because the change is invisible in the current viewport, verification must explicitly activate the target configuration or read the configuration-specific value without switching. For dimensions: `Dimension.GetSystemValue3(swInConfigurationOpts_e.swSpecifyConfiguration, configNames)`. For suppression: `Feature.IsSuppressed2(swSpecifyConfiguration, configNames)`. |
| **Rebuild required** | **Only if the target configuration is active.** Changes to non-active configurations take effect when those configurations are activated. |
| **Failure modes** | 1. Specifying a configuration name that does not exist causes the operation to fail silently (no exception, but the value is not set). 2. Setting a dimension in all configurations (`swSetValue_InAllConfigurations`) overwrites configuration-specific overrides, which may not be the user's intent. 3. The user may not realize a change was made to a non-active configuration, leading to confusion later. |
| **Verification method** | Read the value back using the configuration-specific accessor. Do not rely on visual inspection of the viewport. |
| **Recommendation** | **Wave 2 -- Tier 2, but only for the active configuration initially.** Writing to non-active configurations should require explicit user confirmation with a warning that the change is not visible in the current viewport. ActionClass: Yellow for active-config writes, elevated confirmation for cross-config writes. |

---

### 2.5 Sketch Creation

| Attribute | Detail |
|-----------|--------|
| **Operation** | Create a new 2D sketch on a plane or face, add geometry (lines, arcs, circles, rectangles), optionally add constraints, and exit sketch edit mode |
| **API entry points** | `IModelDoc2.Extension.SelectByID2()` (select plane/face), `SketchManager.InsertSketch(true)` (enter sketch mode), `SketchManager.CreateLine()`, `CreateCircle()`, `CreateArc()`, `CreateCornerRectangle()`, `IModelDoc2.SketchAddConstraints()`, `SketchManager.InsertSketch(true)` again (exit sketch mode). 3D sketches: `SketchManager.Insert3DSketch(true)`. |
| **Grouped undo supported** | **Partially.** The entire sketch creation (enter, add geometry, exit) can be wrapped in `StartRecordingUndoObject` / `FinishRecordingUndoObject`, and it will appear as a single undo step. However, if an exception or crash occurs **while in sketch edit mode**, `FinishRecordingUndoObject` in the `finally` block will close the undo group, but the model may still be in sketch edit mode. The undo group will contain a partial sketch. |
| **Reversal deterministic** | **Conditionally.** If the sketch is created, populated, and exited cleanly, `EditUndo2(1)` removes the entire sketch and restores the prior feature tree state. This is deterministic. If the sketch was only partially created (entered but not exited), the undo behavior is unreliable -- the model may remain in sketch edit mode after undo, or the sketch may be partially removed. |
| **Before/after snapshot needed** | **Required.** Capture the feature tree before and after to confirm exactly one new sketch feature was added. Also capture the active edit state (`IModelDoc2.GetActiveSketch2()`) to confirm the model is not still in sketch edit mode. |
| **Rebuild required** | **Implicitly.** Exiting sketch edit mode via `InsertSketch(true)` triggers a partial rebuild. No explicit `EditRebuild3()` is needed for the sketch itself, but one may be needed if the sketch will be consumed by a feature. |
| **Failure modes** | 1. **Orphaned sketch edit mode** -- the most dangerous failure. If the add-in crashes, throws an exception, or loses context while in sketch edit mode, the model is left in sketch edit mode. The user cannot perform normal model operations until they manually exit the sketch. The add-in must detect this state on re-entry via `IModelDoc2.GetActiveSketch2()` and exit it if found. 2. No plane/face selected before `InsertSketch(true)` -- the method may open a sketch on an arbitrary default plane or fail silently. 3. Selecting a suppressed plane -- `SelectByID2` returns `false`, but `InsertSketch` may still execute on whatever was previously selected. 4. Creating geometry with invalid coordinates (e.g., zero-length line) -- the segment is not created, but no exception is thrown. 5. Creating geometry in the wrong coordinate system -- all sketch coordinates are in meters regardless of document units. Unit mismatches produce incorrectly scaled geometry. 6. Sketch constraints on incompatible entities fail silently. |
| **Verification method** | Check `IModelDoc2.GetActiveSketch2() == null` to confirm sketch edit mode was exited. Compare the feature tree before and after to find the new sketch feature. Read the sketch's segment count via `Sketch.GetSketchSegments()`. |
| **Recommendation** | **Wave 3+ (exclude from first-wave automation).** ActionClass: Red. The modal state risk (orphaned sketch edit mode) makes this operation fundamentally more dangerous than value-setting operations. Requires robust state detection and recovery. Should not be attempted until value-setting tools are proven and traced. |

---

### 2.6 Feature Creation (Extrusion)

| Attribute | Detail |
|-----------|--------|
| **Operation** | Create a boss-extrude or cut-extrude feature from an existing sketch profile |
| **API entry points** | `FeatureManager.FeatureExtrusion3(...)` (24 parameters) for boss extrude. `FeatureManager.FeatureCut4(...)` (28 parameters) for cut extrude. Requires a valid closed sketch profile to be pre-selected or active. |
| **Grouped undo supported** | **Yes.** Feature creation is recorded in the undo stack. It can be included in an undo group. The implicit rebuild triggered by feature creation is absorbed into the group. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` removes the created feature and restores the prior feature tree and geometry. The underlying sketch is preserved (features consume sketches, not destroy them). |
| **Before/after snapshot needed** | **Required.** Feature creation has so many parameters that verification must confirm the correct feature type was created with the correct depth, direction, and end condition. Capture the feature tree before and after. Read the new feature's parameters via `Feature.GetDefinition()` to confirm they match intent. |
| **Rebuild required** | **Implicitly.** Feature creation methods trigger a rebuild internally. |
| **Failure modes** | 1. `FeatureExtrusion3` returns `null` if the operation fails. No exception is thrown. Common causes: no valid sketch profile selected, open (non-closed) contour for boss extrude, sketch on a suppressed plane. 2. Incorrect parameter combinations produce unexpected geometry silently. The 24-parameter signature of `FeatureExtrusion3` is error-prone. A wrong boolean for draft direction or a swapped depth value creates valid but incorrect geometry. 3. A sketch profile with multiple closed contours may extrude all of them, which can be surprising. 4. Thin-feature parameters (parameters 20-24) create thin-walled extrusions when non-zero; accidentally setting them produces unexpected geometry. 5. Merge-result flag (parameter 18) controls whether the extrusion merges with existing bodies or creates a new body. Wrong setting causes multi-body parts or unexpected Boolean operations. |
| **Verification method** | Confirm `FeatureExtrusion3` returned a non-null `Feature`. Read the feature's type name. Read the depth dimension value. Check rebuild state for errors. |
| **Recommendation** | **Wave 3+ (exclude from first-wave automation).** ActionClass: Red. The parameter complexity, prerequisite state (valid sketch), and risk of silently incorrect geometry make this unsuitable for early automation. Even with correct undo support, the cost of debugging incorrect feature creation is high. |

---

### 2.7 Mate Creation (Assembly)

| Attribute | Detail |
|-----------|--------|
| **Operation** | Add a mate between two entities in an assembly |
| **API entry points** | `IAssemblyDoc.AddMate5(int mateType, int align, bool flip, double distance, double upperLimit, double lowerLimit, double gearRatio1, double gearRatio2, double angle, double angleUpper, double angleLower, bool forPositioningOnly, bool lockRotation, int widthOption, out int errorStatus)`. Requires exactly two entities pre-selected with correct selection marks. |
| **Grouped undo supported** | **Yes.** Mate creation is recorded in the undo stack and can be wrapped in an undo group. |
| **Reversal deterministic** | **Mostly yes.** `EditUndo2(1)` removes the mate and restores the prior assembly position. However, if the mate caused SOLIDWORKS to move components to satisfy the constraint, undoing the mate moves components back to their pre-mate positions. If the user manually repositioned components after the mate was added, and then undo is called, the manual repositioning is also undone (standard stack-order behavior). |
| **Before/after snapshot needed** | **Required.** Capture the mate list and component positions before and after. Mate creation can move components in non-obvious ways, and the user needs to understand what changed. |
| **Rebuild required** | **Yes.** `IAssemblyDoc.EditRebuild3()` is needed to see positional effects. |
| **Failure modes** | 1. **Over-constraint.** Adding a mate that conflicts with existing mates produces an error in `errorStatus` (`swAddMateError_e`) but may partially apply. The mate may appear in the feature tree with an error icon. Undoing it is safe. 2. **Wrong selection.** `AddMate5` requires exactly two entities pre-selected. If the selection is wrong (wrong entity count, wrong entity types), the method fails and sets `errorStatus`. 3. **Selection mark issues.** Some mate types require entities to be selected with specific selection marks (`SelectByID2` mark parameter). Incorrect marks cause mate creation to fail. 4. **Component moved unexpectedly.** A valid mate may move components to positions the user did not anticipate. The mate itself is correct, but the geometric result is surprising. 5. **Redundant mate.** Adding a mate that is already implied by existing mates produces a redundant mate warning. The mate is created but flagged. |
| **Verification method** | Check `errorStatus` output parameter. Read the mate list to confirm the new mate exists. Check for over-defined/redundant warnings via feature state. |
| **Recommendation** | **Wave 3+ (exclude from first-wave automation).** ActionClass: Red. The selection-dependent, two-entity prerequisite makes this harder to automate reliably than single-entity operations. Over-constraint risk requires deep mate graph understanding. |

---

### 2.8 Component Insertion (Assembly)

| Attribute | Detail |
|-----------|--------|
| **Operation** | Insert a part or sub-assembly into an assembly at a specified position |
| **API entry points** | `IAssemblyDoc.AddComponent5(string path, int resolveState, string configName, bool newInstance, string configOption, double x, double y, double z)`. |
| **Grouped undo supported** | **Yes.** Component insertion is recorded in the undo stack. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` removes the inserted component and restores the prior assembly state. No side effects on other components. |
| **Before/after snapshot needed** | **Recommended.** Capture the component list before and after. The insertion itself is straightforward, but the component path must be validated beforehand. |
| **Rebuild required** | **Implicitly.** Component insertion triggers an automatic lightweight rebuild. |
| **Failure modes** | 1. Invalid `compPath` (file does not exist) -- `AddComponent5` returns `null`. No state change. 2. Component file is read-only or locked by another process -- may load as read-only or fail to load. 3. Configuration name does not exist in the referenced file -- component loads in default configuration. 4. Insertion coordinates are far from the assembly origin -- component is placed at an unexpected location. 5. Circular reference -- inserting an assembly into itself or a descendant of itself. SOLIDWORKS blocks this. |
| **Verification method** | Confirm `AddComponent5` returned a non-null `Component2`. Read the component list to verify the new component exists. |
| **Recommendation** | **Wave 3 (after Tier 1/2 tools are proven).** ActionClass: Yellow if the component path is user-confirmed, Red if auto-selected. The operation itself is clean and undoable, but file system dependencies add failure surface. |

---

### 2.9 Drawing View Creation

| Attribute | Detail |
|-----------|--------|
| **Operation** | Create a standard, projected, section, or detail view in a drawing document |
| **API entry points** | `IDrawingDoc.CreateDrawViewFromModelView3(string modelPath, string viewName, double x, double y, double z)` for named model views. `IDrawingDoc.InsertModelView3(string modelPath, int viewOrientation, double x, double y, double z, int displayMode, string configName)` for standard orientation views. Projected views: `IDrawingDoc.CreateProjectedView()` (requires a base view selected). Section views: `IDrawingDoc.CreateSectionViewAt5(...)`. Detail views: `IDrawingDoc.CreateDetailViewAt4(...)`. |
| **Grouped undo supported** | **Yes.** Drawing view creation is recorded in the undo stack and can be grouped. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` removes the created view and restores the prior drawing sheet state. |
| **Before/after snapshot needed** | **Required.** Capture the drawing sheet's view list before and after. Drawing views reference external model files, so their creation depends on file availability and model state. |
| **Rebuild required** | **Implicitly.** View creation triggers an automatic rebuild/view generation. |
| **Failure modes** | 1. `modelPath` does not exist or is not loadable -- returns `null`. 2. The referenced model has rebuild errors -- the view may show with error indicators. 3. Incorrect position coordinates place the view off-sheet or overlapping other views. SOLIDWORKS does not prevent overlapping views. 4. Section views and detail views have additional prerequisites (a base view must exist and a section line or detail circle must be defined). 5. Creating a view on the wrong sheet (if the drawing has multiple sheets). |
| **Verification method** | Confirm the method returned a non-null `View`. Read the sheet's view list. Check view position and scale. |
| **Recommendation** | **Wave 3 (planned in BUILD_SPEC.md as Wave 3 tool).** ActionClass: Yellow for basic standard views from a confirmed model path. Red for section/detail views due to prerequisite complexity. |

---

### 2.10 Object Rename

| Attribute | Detail |
|-----------|--------|
| **Operation** | Rename a feature, component, configuration, or other named entity |
| **API entry points** | `Feature.Name = "NewName"` (direct property set for features). `Component2.Name2 = "NewName"` for components. `Configuration.Name = "NewName"` for configurations. |
| **Grouped undo supported** | **Yes.** Rename operations are recorded in the undo stack. |
| **Reversal deterministic** | **Yes.** `EditUndo2(1)` restores the prior name. |
| **Before/after snapshot needed** | **Recommended.** Record old name and new name for trace logging. |
| **Rebuild required** | **No.** Renames are metadata operations (but note: renaming a feature that is referenced by name in equations or macros may break those references, and this breakage is not detected until the next rebuild or equation evaluation). |
| **Failure modes** | 1. Duplicate name -- SOLIDWORKS may auto-append a suffix or reject the rename. Behavior varies by entity type. 2. Empty or whitespace name -- behavior varies. Some entities accept empty names, others reject them. 3. Renaming a feature that is referenced in equations by name (e.g., `"D1@OldName"`) breaks the equation reference. The equation continues to reference the old name string, which no longer resolves. This is not detected until the equation is evaluated. 4. Renaming a component in an assembly that is referenced by in-context features in other components may break those references. |
| **Verification method** | Re-read the entity's name property and compare. |
| **Recommendation** | **Wave 2 -- Tier 1 (safe for first-wave automation).** ActionClass: Yellow. Simple, fully undoable, no rebuild. Must warn about equation/reference impacts. |

---

### 2.11 Entity Selection / Highlighting

| Attribute | Detail |
|-----------|--------|
| **Operation** | Select or highlight entities in the model (features, faces, edges, dimensions) for user attention |
| **API entry points** | `IModelDocExtension.SelectByID2(name, type, x, y, z, append, mark, callout, options)`. `IModelDoc2.ClearSelection2(true)`. |
| **Grouped undo supported** | **Not applicable.** Selection changes are **not** recorded in the undo stack. They are transient UI state. |
| **Reversal deterministic** | **Not applicable.** Selection is not an undoable operation. Clearing the selection via `ClearSelection2(true)` is the reversal mechanism. |
| **Before/after snapshot needed** | **Optional.** Selection is transient state. No document modification occurs. |
| **Rebuild required** | **No.** Selection is purely visual/UI state. |
| **Failure modes** | 1. `SelectByID2` returns `false` if the entity is not found. 2. Selecting a suppressed entity returns `false`. 3. Entity names can be ambiguous in assemblies (same feature name in multiple components). The `type` parameter and coordinates help disambiguate. |
| **Verification method** | Read `ISelectionManager.GetSelectedObjectCount2(-1)` and compare to expected count. |
| **Recommendation** | **Wave 2 -- Tier 1 (safest possible operation).** ActionClass: Green. No document modification, no undo needed, no rebuild. This is essentially a read operation with visual side effects. |

---

## 3. Consolidated Safety Matrix

| Operation | Grouped Undo | Reversal Deterministic | Snapshot Needed | Rebuild | Wave | ActionClass | Safe for First-Wave |
|-----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Select/highlight entities | N/A | N/A (transient) | Optional | No | 2 | Green | **Yes** |
| Set custom property | Yes | Yes | Recommended | No | 2 | Yellow | **Yes** |
| Rename object | Yes | Yes | Recommended | No | 2 | Yellow | **Yes** |
| Set dimension value | Yes | Yes (with caveats) | Recommended | Yes | 2 | Yellow | **Yes** |
| Suppress feature | Yes | Yes (cascade-aware) | Required | Yes | 2 | Yellow | **Yes** (with dependency preview) |
| Unsuppress feature | Yes | Yes (cascade-aware) | Required | Yes | 2 | Yellow | **Yes** (with dependency preview) |
| Config-specific dimension | Yes | Yes (invisible) | Required | Conditional | 2 | Yellow | **Conditional** (active config only initially) |
| Component insertion | Yes | Yes | Recommended | Implicit | 3 | Yellow/Red | No |
| Drawing view (standard) | Yes | Yes | Required | Implicit | 3 | Yellow | No |
| Sketch creation | Partial | Conditional | Required | Implicit | 3+ | Red | **No** |
| Feature creation | Yes | Yes | Required | Implicit | 3+ | Red | **No** |
| Mate creation | Yes | Mostly | Required | Yes | 3+ | Red | **No** |
| Drawing view (section/detail) | Yes | Yes | Required | Implicit | 3+ | Red | **No** |

---

## 4. Undo Group Contract for Adze

Based on the analysis above, every write tool in Adze should follow this exact undo pattern:

```
1. Pre-check guards
   - Document is not read-only
   - Document is not null
   - Target entity exists and is in a valid state
   - For dimensions: check DrivenState is not driven
   - For suppression: check parent/child cascade

2. Capture before-state
   - Read the value(s) that will change
   - Store in a WriteTrace record

3. Start undo group
   - ext.StartRecordingUndoObject("Adze: " + description)

4. Execute write
   - Call the COM write method
   - Check return code immediately
   - If failed: jump to step 6

5. Rebuild (if required)
   - model.EditRebuild3()
   - Check rebuild state for errors

6. Finish undo group
   - ext.FinishRecordingUndoObject()
   - (MUST be in a finally block)

7. Capture after-state
   - Re-read the value(s) that changed
   - Compare to expected result

8. Emit trace
   - Record before, after, success/failure, undo label
```

### Critical implementation constraints

1. **Never nest undo groups.** `StartRecordingUndoObject` does not support nesting. If a write tool calls another write tool internally, the inner tool must not start its own undo group.

2. **Always close the undo group in a `finally` block.** If `FinishRecordingUndoObject` is not called, the undo stack may be corrupted for the remainder of the session.

3. **Never call `ClearUndoList()`.** This is destructive and irreversible.

4. **Keep undo labels prefixed with `"Adze: "`.** This makes Adze-originated changes identifiable in the Edit > Undo menu.

5. **One undo group per user-confirmed action.** Do not silently chain multiple undo groups. If the user confirms one operation, that is one undo group. If they need to undo, one Ctrl+Z undoes the entire Adze action.

---

## 5. Operations Requiring Elevated Confirmation

The following operations should require elevated confirmation beyond the standard preview-confirm flow, due to higher risk of unintended consequences:

| Operation | Reason for Elevation |
|-----------|---------------------|
| Dimension change that causes downstream rebuild error | The value itself is valid but the geometric consequence is a broken model. Preview should attempt to predict this (hard). |
| Suppression of a feature with external references | Other documents may break. The user may not have those documents open. |
| Custom property overwrite of a linked property | The link expression is destroyed and not easily recoverable. |
| Writing to a non-active configuration | The change is invisible in the current viewport, creating confusion risk. |
| Any write to an assembly document | Assembly writes have broader blast radius than part writes due to inter-component dependencies. |
| Any operation when the document has existing rebuild errors | The model is already in a degraded state; adding writes increases unpredictability. |

---

## 6. Operations That Should Be Excluded from Automation Entirely (Current Phase)

| Operation | Exclusion Reason |
|-----------|-----------------|
| Sketch creation | Modal state risk (orphaned sketch edit mode) is fundamentally different from value-setting risk. Requires dedicated state detection and recovery infrastructure. |
| Feature creation (extrude, cut, revolve, etc.) | Requires sketch creation as a prerequisite. 24-28 parameter signatures make silent mis-parameterization likely. |
| Mate creation | Two-entity selection prerequisite and over-constraint risk require deep assembly graph understanding. |
| Section/detail drawing views | Multi-step prerequisite (base view, section line definition) with modal interaction requirements. |
| Equation modification | Equations form a dependency graph. Modifying one equation can cascade to many dimensions. No clean COM API for equation impact preview. |
| Material assignment | While the API exists (`IPartDoc.SetMaterialPropertyName2`), material changes affect mass properties, simulation results, and BOM entries in ways that are hard to preview. |
| File save (`IModelDoc2.Save3`) | Save is irreversible (cannot undo a save). Must never be triggered automatically without explicit user confirmation. Should be a separate, elevated action, not part of a write tool. |

---

## 7. Snapshot Strategy Recommendations

### What to snapshot before writes

| Write Operation | Snapshot Content |
|----------------|-----------------|
| Dimension change | `{ dimensionFullName, oldValue, units, drivenState, featureName, configurationName }` |
| Custom property | `{ propertyName, scope, configurationName, oldValue, oldType, isLinked, linkExpression }` |
| Suppression change | `{ featureName, oldState, childFeatures[].{name, oldState}, dependentFeatures[].{name, oldState} }` |
| Rename | `{ entityType, oldName, newName, equationReferences[], externalReferences[] }` |

### What to snapshot after writes

| Write Operation | Verification Content |
|----------------|---------------------|
| Dimension change | `{ dimensionFullName, newValue, rebuildState, downstreamErrors[] }` |
| Custom property | `{ propertyName, newValue, newType, isLinked }` |
| Suppression change | `{ featureName, newState, childFeatures[].{name, newState}, rebuildState }` |
| Rename | `{ entityType, newName, resolvedSuccessfully }` |

### Snapshot storage

Snapshots should be stored in the trace event alongside the undo label, enabling post-hoc audit of what Adze changed and whether the change was verified. The trace schema at `schemas/traces/trace-event.schema.json` should be extended with a `write_snapshot` field for this purpose.

---

## 8. Risk Summary and Recommendation

### Safe for Wave 2 (first-wave agent automation)

These operations have clean COM APIs, deterministic undo via grouped undo recording, no modal state risk, and verifiable outcomes:

1. **Select/highlight entities** -- zero risk, no document modification
2. **Set custom property** -- lowest risk write, no rebuild, fully undoable
3. **Rename object** -- simple, fully undoable, no rebuild
4. **Set dimension value** -- moderate risk due to rebuild dependency, but clean API and deterministic undo
5. **Suppress/unsuppress feature** -- moderate risk due to cascade, but clean API, deterministic undo, and verifiable with dependency preview

### Defer to Wave 3+

These operations have modal state risk, complex prerequisites, large parameter surfaces, or inter-document blast radius:

6. **Sketch creation** -- modal state risk
7. **Feature creation** -- prerequisite complexity, parameter surface size
8. **Mate creation** -- two-entity prerequisite, over-constraint risk
9. **Component insertion** -- file system dependency, but relatively clean otherwise
10. **Drawing view creation (standard)** -- external model dependency
11. **Drawing view creation (section/detail)** -- multi-step modal prerequisite

### Exclude entirely until dedicated infrastructure exists

12. **Equation modification** -- dependency graph cascade
13. **Material assignment** -- cross-concern blast radius
14. **File save** -- irreversible, must never be automated without extreme elevation

---

## 9. Open Questions for Implementation

1. **Undo stack depth detection.** Can Adze detect the configured undo stack depth to warn when approaching the limit? The system option `swUserPreferenceIntegerValue_e.swUndoLimit` may provide this. If the stack is nearly full, older Adze undo groups may be silently dropped.

2. **Undo group state detection.** Can Adze detect whether an undo group recording is already active (e.g., started by the user via macro or by another add-in)? There is no known API for this. The safest approach is to always assume no recording is active and to never nest.

3. **Post-rebuild error enumeration.** The existing `NeedsRebuild2` check detects whether a rebuild is needed, but does not enumerate specific errors. For write verification, Adze may need to traverse the feature tree and check each feature's `GetErrorCode2()` to detect downstream failures caused by the write.

4. **Drawing document undo behavior.** Drawing undo behavior for view creation is less well-documented than part/assembly undo. Empirical testing with `StartRecordingUndoObject` on drawing view operations should be performed before implementing Wave 3 drawing tools.

5. **Concurrent add-in writes.** If another add-in modifies the model between Adze's preview and apply steps, Adze's before-snapshot may be stale. There is no COM-level locking mechanism to prevent this. The mitigation is to re-read the value immediately before writing and abort if it differs from the preview.

---

## References

- `discovery-solidworks-write-api.md` -- companion document with full method signatures and parameter enumerations
- `src/Adze.Host/Services/SessionContextBuilder.cs` -- established COM access patterns for read operations
- `src/Adze.Contracts/Enums/ActionClass.cs` -- Green/Yellow/Red action classification
- `src/Adze.Contracts/Enums/ApprovalState.cs` -- Draft through Completed/RolledBack/Failed state machine
- `documentation/BUILD_SPEC.md` -- Wave 2/3 tool lists and safety rules
- `SolidWorks.Interop.sldworks.dll` (R2026x interop)
- `SolidWorks.Interop.swconst.dll` (enumeration definitions)

---

# Research: Agent Loop Threading Architecture for SOLIDWORKS Add-In

**Date:** 2026-03-15
**Status:** De-risking research, implementation-ready
**Scope:** Thread safety, COM apartment rules, UI marshaling, cancellation, and streaming patterns for running a multi-step AI agent loop inside the Adze SOLIDWORKS add-in
**Grounded in:** `src/Adze.Host/UI/TaskPaneControl.cs`, `src/Adze.Host/Infrastructure/HostState.cs`, `src/Adze.Host/Services/SessionContextBuilder.cs`, `src/Adze.Host/AddIn/AdzeAddIn.cs`, `documentation/plans/discovery-agent-loop-architecture.md`

---

## 1. Platform Constraints: The Non-Negotiable Facts

### 1.1 SOLIDWORKS is an STA COM Server

SOLIDWORKS runs on the main UI thread inside a Single-Threaded Apartment (STA). Every COM interface pointer obtained from SOLIDWORKS (`ISldWorks`, `ModelDoc2`, `Feature`, `SelectionMgr`, `Dimension`, `Mate2`, `Component2`, etc.) is bound to this STA thread. These are the platform-level facts:

- **All SOLIDWORKS COM calls must execute on the thread that created the COM object**, which is the SOLIDWORKS main UI thread (the same thread that hosts Windows Forms controls in the Task Pane).
- The .NET Runtime Callable Wrapper (RCW) that wraps each COM interface does not automatically marshal cross-apartment calls for in-process add-ins the way it would for out-of-process COM. SOLIDWORKS add-ins are loaded in-process via `ISwAddin.ConnectToSW()`, meaning the add-in DLL shares the SOLIDWORKS process and its main STA thread.
- Calling a SOLIDWORKS COM method from a background thread (which lives in the MTA by default) will either:
  1. Silently produce wrong results or corrupt internal state.
  2. Throw a `System.Runtime.InteropServices.COMException` with `RPC_E_WRONG_THREAD` (0x8001010E).
  3. Deadlock if the STA thread is blocked waiting for the background thread while the background thread tries to marshal a COM call back to the STA.
  4. Crash SOLIDWORKS outright.
- **There is no safe way to call SOLIDWORKS COM from a non-STA thread**, period. Not with locks, not with `lock (application)`, not with `Monitor`, not with `Mutex`. The apartment model is enforced at the COM infrastructure level, below the .NET runtime.

### 1.2 The Task Pane Control Lives on the STA Thread

The `TaskPaneControl` is a `System.Windows.Forms.UserControl` created by SOLIDWORKS via COM activation (`TaskpaneView.AddControl`). It runs on the SOLIDWORKS main UI thread. This thread pumps both Win32 messages and COM calls. This is the same thread that:

- Receives SOLIDWORKS event callbacks (`ActiveModelDocChangeNotify`, `NewSelectionNotify`, etc.).
- Processes Windows Forms message loop events (button clicks, timer ticks, paint).
- Executes COM API calls to SOLIDWORKS.

Blocking this thread for any significant duration (more than ~50ms) causes:

- The SOLIDWORKS UI to freeze (no model rotation, no menu interaction, no button response).
- COM callback delivery to stall (event notifications queue up).
- Windows to report the application as "Not Responding" after ~5 seconds of blocked message pump.

### 1.3 .NET Framework 4.8 Constraints

The project targets .NET Framework 4.8 (confirmed in `Adze.Host.csproj`: `<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>`). This means:

- `System.Threading.CancellationTokenSource` and `CancellationToken` are available (since .NET 4.0).
- `System.Threading.Tasks.Task` and `Task<T>` are available (since .NET 4.0).
- `async`/`await` are available at the language level (C# latest is enabled via `<LangVersion>latest</LangVersion>`).
- **However**, `System.Net.Http.HttpClient` is not referenced. The existing HTTP calls use `System.Net.HttpWebRequest` with synchronous `.GetResponse()`. Adding `HttpClient` would require a NuGet package or framework reference change.
- `Task.Run()` is available and is the modern replacement for `ThreadPool.QueueUserWorkItem`.
- `SynchronizationContext` is available. Windows Forms installs a `WindowsFormsSynchronizationContext` on the UI thread, which `Control.BeginInvoke` / `Control.Invoke` rely on internally.

### 1.4 Current Threading Pattern (Observed in Code)

From `TaskPaneControl.RunAssistant()` (lines 475-518):

```
[UI thread]  Click handler fires
[UI thread]  HostState.PrepareAssistantRun()     -- calls COM via SessionContextBuilder.Build()
[UI thread]  ThreadPool.QueueUserWorkItem()       -- hands off to background
[BG thread]  HostState.CompleteAssistantRun()     -- API calls, tool execution, synthesis
[BG thread]  PostToUi(() => ApplySnapshot())      -- marshals result back
[UI thread]  ApplyAssistantRunSnapshot()          -- updates TextBox controls
```

`PostToUi()` (lines 709-727) uses `BeginInvoke` (asynchronous, fire-and-forget). This is correct for the final result delivery and progress updates. The existing pattern is sound but needs extension for the multi-turn loop.

---

## 2. Recommended Runtime Design

### 2.1 Thread Responsibility Matrix

| Responsibility | Thread | Why |
|---|---|---|
| All SOLIDWORKS COM calls (`ISldWorks`, `ModelDoc2`, `Feature`, etc.) | UI thread (STA) | COM apartment rules. No exceptions. |
| `SessionContextBuilder.Build()` | UI thread | Traverses COM objects (features, dimensions, mates, references). |
| Windows Forms control reads/writes (`TextBox.Text`, `Label.Text`, `Button.Enabled`) | UI thread | Windows Forms affinity. |
| HTTP API calls to OpenAI/Anthropic | Background thread | Blocking I/O, 2-30 seconds. Must not block UI. |
| JSON serialization/deserialization of API payloads | Background thread | CPU work, can be heavy for large payloads. |
| Tool execution against `SessionContext` | Background thread | Tools are pure functions over serialized data. No COM. |
| Conversation state management | Background thread | In-memory list manipulation, no thread safety concerns within the single agent loop thread. |
| Trace recording, file I/O | Background thread | Disk I/O should not block UI. |
| Progress updates to UI | Marshaled from BG to UI via `BeginInvoke` | Fire-and-forget is correct for status text. |
| Mid-loop COM recapture (if needed) | Marshaled from BG to UI via `Invoke` | Must block BG thread until fresh data is captured. |
| Cancellation signal | UI thread sets the token; BG thread checks it | `CancellationTokenSource` is thread-safe by design. |

### 2.2 Core Execution Flow

```
[UI Thread - STA]                              [Background Thread - MTA/ThreadPool]
=================                              ====================================

User clicks "Run assistant"
  |
  |-- _isRunning = true
  |-- Disable run button, enable cancel button
  |-- _agentCancellation = new CancellationTokenSource()
  |-- context = HostState.PrepareAssistantRun()    [COM capture happens here]
  |-- ThreadPool.QueueUserWorkItem() --->          |
  |                                                |
  |   (UI thread returns to message pump)          |-- Build initial conversation messages
  |   (SOLIDWORKS remains responsive)              |
  |                                                |-- LOOP:
  |                                                |   |
  |                                                |   |-- token.ThrowIfCancellationRequested()
  |                                                |   |
  |                                                |   |-- API call (HttpWebRequest, blocking)
  |                                                |   |   (2-30 seconds)
  |                                                |   |
  |                                                |   |-- Parse response
  |                                                |   |
  |   <-- BeginInvoke(progress update) ------------|   |-- PostToUi("Thinking... turn 2")
  |   _runStateLabel.Text = "Thinking..."          |   |
  |                                                |   |-- if tool_use:
  |                                                |   |     |
  |                                                |   |     |-- [Optional: needs fresh COM data?]
  |   <-- Invoke(COM recapture) -------------------|   |     |   Control.Invoke(() => {
  |   context = SessionContextBuilder.Build(app)   |   |     |     return BuildContextUnsafe(app);
  |   return context;  --------------------------->|   |     |   })  // BG thread blocks here
  |                                                |   |     |
  |   <-- BeginInvoke(progress) -------------------|   |     |-- PostToUi("Running get_dims...")
  |   _runStateLabel.Text = "Running..."           |   |     |
  |                                                |   |     |-- result = tool.Execute(context)
  |                                                |   |     |     [pure function, no COM]
  |                                                |   |     |
  |                                                |   |     |-- Append tool_result to messages
  |                                                |   |     |-- continue LOOP
  |                                                |   |
  |                                                |   |-- if text response:
  |                                                |   |     break LOOP
  |                                                |   |
  |                                                |   |-- if max iterations:
  |                                                |   |     break LOOP
  |                                                |
  |   <-- BeginInvoke(final result) ---------------|-- PostToUi(() => ApplyResult(result))
  |   Apply answer, plan, tools to UI              |
  |   Re-enable run button                         |
  |   _isRunning = false                           |
  |                                                |
  |   (message pump continues)                     (thread returns to pool)
```

### 2.3 Why This Shape Is Correct

1. **COM calls never leave the STA thread.** `PrepareAssistantRun()` runs on the UI thread before the background work starts. Mid-loop COM refresh uses `Control.Invoke`, which marshals execution back to the UI thread and blocks the background thread until it completes.

2. **The UI thread never blocks for more than milliseconds.** COM context capture (`SessionContextBuilder.Build`) takes single-digit milliseconds because it reads in-memory COM properties. The `Invoke` call from the background thread briefly borrows the UI thread for this capture, then returns.

3. **API calls never touch the UI thread.** All HTTP I/O runs on the background thread. The UI message pump continues to process SOLIDWORKS interactions during the 2-30 second API calls.

4. **Progress updates are non-blocking.** `BeginInvoke` (asynchronous) is used for progress text because the background thread does not need to wait for the UI to repaint.

5. **A single background thread runs the loop.** No concurrent tool execution, no parallel API calls. This eliminates an entire class of thread-safety bugs with zero measurable performance cost (tools execute in microseconds against in-memory data).

---

## 3. Detailed Pattern Specifications

### 3.1 Control.Invoke vs. Control.BeginInvoke

These are the two mechanisms for marshaling work from a background thread to the UI thread. They have different semantics and using the wrong one causes deadlocks or race conditions.

**`Control.Invoke(Delegate)`** -- Synchronous marshal.

- Posts a message to the UI thread's message queue and **blocks the calling thread** until the delegate executes on the UI thread and returns.
- The calling thread does not proceed until the UI thread has completed the work.
- Returns the delegate's return value (if any).
- **Use for:** Operations where the background thread needs the result before continuing. The only case in the agent loop is mid-loop COM recapture.
- **Deadlock risk:** If the UI thread is blocked waiting for the background thread (e.g., `Thread.Join()`, `Task.Wait()`, `ManualResetEvent.WaitOne()`), and the background thread calls `Invoke`, both threads are stuck. **This is the #1 threading bug in SOLIDWORKS add-ins.** The prevention is simple: the UI thread must never synchronously wait on the background thread.

```csharp
// CORRECT: Background thread calls Invoke to get fresh COM data.
// UI thread is free (pumping messages), so Invoke completes promptly.
SessionContext freshContext = (SessionContext)_control.Invoke(
    new Func<SessionContext>(() => HostState.CaptureContext()));
```

```csharp
// DEADLOCK: UI thread waits for BG thread, BG thread calls Invoke.
// UI thread:
_backgroundTask.Wait();  // BLOCKED -- waiting for BG thread
// BG thread:
_control.Invoke(...);    // BLOCKED -- waiting for UI thread to pump
// Both threads are stuck forever.
```

**`Control.BeginInvoke(Delegate)`** -- Asynchronous marshal.

- Posts a message to the UI thread's message queue and **returns immediately** to the calling thread.
- The delegate will execute on the UI thread at some future point when the message pump processes it.
- Returns an `IAsyncResult` that can be used with `EndInvoke`, but this is rarely needed.
- **Use for:** Fire-and-forget operations. Progress updates, final result delivery, UI state changes.
- **No deadlock risk** because the calling thread never waits.

```csharp
// CORRECT: Background thread fires progress update and continues immediately.
_control.BeginInvoke(new Action(() =>
{
    _runStateLabel.Text = "Running get_dimensions... (turn 2 of 10)";
}));
```

**Decision matrix for the agent loop:**

| Operation | Method | Why |
|---|---|---|
| Progress text update | `BeginInvoke` | BG thread does not need to wait for repaint. |
| Intermediate tool result append | `BeginInvoke` | Fire-and-forget UI update. |
| Final result delivery | `BeginInvoke` | Fire-and-forget; `FinishAssistantRunUi` runs after. |
| Mid-loop COM context recapture | `Invoke` | BG thread needs the `SessionContext` before executing the tool. |
| Cancel button state change | Direct (UI thread) | Cancel click handler runs on UI thread already. |

### 3.2 PostToUi Pattern (Existing, Extend)

The existing `PostToUi` method in `TaskPaneControl` (lines 709-727) is well-written:

```csharp
private void PostToUi(Action action)
{
    if (action == null || IsDisposed) return;
    try
    {
        if (!IsHandleCreated) return;
        BeginInvoke(action);
    }
    catch (InvalidOperationException)
    {
        // Control was disposed between the check and the call.
        // Swallow -- the UI is gone, nothing to update.
    }
}
```

This pattern is correct and should be reused for all fire-and-forget UI updates from the agent loop. The `InvalidOperationException` catch handles the race condition where the control is disposed between the `IsHandleCreated` check and the `BeginInvoke` call.

For the synchronous COM recapture case, add a parallel method:

```csharp
/// <summary>
/// Marshals a function to the UI thread and blocks until it returns.
/// Used exclusively for mid-loop COM recapture where the background
/// thread needs the result before proceeding.
/// </summary>
private T InvokeOnUi<T>(Func<T> func)
{
    if (func == null) throw new ArgumentNullException(nameof(func));
    if (IsDisposed || !IsHandleCreated)
        throw new ObjectDisposedException(nameof(TaskPaneControl));

    try
    {
        return (T)Invoke(func);
    }
    catch (InvalidOperationException)
    {
        // Control disposed during Invoke -- treat as cancellation.
        throw new OperationCanceledException(
            "Task Pane was disposed during agent loop execution.");
    }
}
```

### 3.3 ThreadPool.QueueUserWorkItem vs. Task.Run

Both are available on .NET 4.8. The existing code uses `ThreadPool.QueueUserWorkItem`. For the agent loop, either works, but `Task.Run` has a minor advantage:

| Feature | `ThreadPool.QueueUserWorkItem` | `Task.Run` |
|---|---|---|
| Returns a handle to the work | No | Yes (`Task`) |
| Exception propagation | Must catch in callback | Can observe via `Task.Exception` (but we catch internally anyway) |
| `CancellationToken` support | Not built in (pass manually) | `Task.Run(action, token)` checks token before scheduling |
| Available on .NET 4.8 | Yes | Yes |

**Recommendation:** Use `Task.Run` for the agent loop. It integrates cleanly with `CancellationToken` and the returned `Task` handle can be stored for diagnostic purposes (though we should never `.Wait()` on it from the UI thread).

```csharp
private CancellationTokenSource? _agentCancellation;
private Task? _agentTask;  // For diagnostics only. NEVER .Wait() on UI thread.

private void RunAssistant()
{
    if (_isRunning) return;

    _isRunning = true;
    _agentCancellation = new CancellationTokenSource();
    CancellationToken token = _agentCancellation.Token;

    // --- UI thread: capture COM state ---
    AssistantRunPreparation preparation;
    try
    {
        preparation = HostState.PrepareAssistantRun(GetRequestText());
    }
    catch (Exception ex)
    {
        ShowRunFailure(ex);
        FinishAssistantRunUi();
        return;
    }

    // --- Transition to cancel-capable UI state ---
    SwitchToCancelMode();

    // --- Launch background agent loop ---
    _agentTask = Task.Run(() =>
    {
        try
        {
            AgentLoopResult result = RunAgentLoopOnBackground(preparation, token);
            PostToUi(() => ApplyAgentResult(result));
        }
        catch (OperationCanceledException)
        {
            PostToUi(() => ShowCancelled());
        }
        catch (Exception ex)
        {
            PostToUi(() => ShowRunFailure(ex));
        }
        finally
        {
            PostToUi(FinishAssistantRunUi);
        }
    }, token);
}
```

### 3.4 CancellationTokenSource Lifecycle

`CancellationTokenSource` is thread-safe: `.Cancel()` can be called from the UI thread while the background thread checks `.Token.IsCancellationRequested` or calls `.Token.ThrowIfCancellationRequested()`.

**Lifecycle:**

```
UI Thread                              Background Thread
========                              =================
new CancellationTokenSource()
  |
  |-- Pass token to Task.Run() ------> token received
  |                                     |
  |                                     |-- token.ThrowIfCancellationRequested()
  |                                     |   (passes, not cancelled yet)
  |                                     |
  |                                     |-- API call (blocking, 10 seconds)
  |                                     |
User clicks "Cancel"                    |
  |                                     |
  |-- _agentCancellation.Cancel()       |   (background thread is blocked in HTTP)
  |   (sets token.IsCancelled = true)   |
  |                                     |
  |                                     |-- API call returns (or times out)
  |                                     |-- token.ThrowIfCancellationRequested()
  |                                     |   THROWS OperationCanceledException
  |                                     |
  |   <-- PostToUi(ShowCancelled) ------|-- caught in Task.Run catch block
```

**Cancellation points in the agent loop** (ordered by where they appear in the loop):

1. **Top of each loop iteration** -- before the API call starts.
2. **After each API call returns** -- the HTTP call itself is not cancellable with `HttpWebRequest` (see section 3.5), but the token is checked immediately after.
3. **Before each tool execution** -- between processing individual `tool_use` blocks.
4. **Before mid-loop COM recapture** -- no point marshaling to the UI thread if cancelled.

```csharp
while (state.IterationCount < state.MaxIterations)
{
    // Cancellation point 1: before API call
    cancellationToken.ThrowIfCancellationRequested();

    state.IterationCount++;
    PostProgress("Thinking...", state.IterationCount);

    AgentModelResponse response = modelClient.CompleteWithTools(/*...*/);

    // Cancellation point 2: after API call
    cancellationToken.ThrowIfCancellationRequested();

    if (response.StopReason == "tool_use")
    {
        foreach (ToolUseRequest toolUse in response.ToolUses)
        {
            // Cancellation point 3: before each tool
            cancellationToken.ThrowIfCancellationRequested();

            PostProgress($"Running {toolUse.Name}...", state.IterationCount);
            ToolResult result = ExecuteTool(context, toolUse);
            state.AddToolResult(toolUse.Id, result);
        }
        continue;
    }

    // Text response -- final answer
    break;
}
```

**Disposal:** Dispose the `CancellationTokenSource` in `FinishAssistantRunUi()`:

```csharp
private void FinishAssistantRunUi()
{
    _agentCancellation?.Dispose();
    _agentCancellation = null;
    _agentTask = null;
    _isRunning = false;
    SwitchToRunMode();
    // ... existing refresh logic ...
}
```

### 3.5 HTTP Request Cancellation with HttpWebRequest

`HttpWebRequest.GetResponse()` is a blocking call with no native `CancellationToken` support. There are two approaches:

**Approach A: Timeout-based (recommended for initial implementation).**

The existing code already sets `request.Timeout` and `request.ReadWriteTimeout`. When the user cancels, the background thread is blocked in `GetResponse()` until either:
- The response arrives (then the cancellation token is checked after).
- The timeout expires (throws `WebException` with `Status == WebExceptionStatus.Timeout`).

This means cancellation has latency up to the timeout value. With a 30-second timeout, the worst case is the user waits 30 seconds after clicking Cancel. This is acceptable for Phase 1.

**Approach B: Request abort (optional improvement).**

Store a reference to the active `HttpWebRequest` and call `.Abort()` from the cancel handler:

```csharp
// In the model client:
private volatile HttpWebRequest? _activeRequest;

// In the API call method:
_activeRequest = (HttpWebRequest)WebRequest.Create(endpoint);
try
{
    // ... configure and send request ...
    using var response = (HttpWebResponse)_activeRequest.GetResponse();
    // ... read response ...
}
finally
{
    _activeRequest = null;
}

// Called from cancel handler (UI thread):
public void AbortActiveRequest()
{
    _activeRequest?.Abort();
    // .Abort() causes GetResponse() to throw WebException
    // with Status == WebExceptionStatus.RequestCanceled
}
```

The `volatile` keyword ensures the UI thread sees the most recent assignment. `HttpWebRequest.Abort()` is documented as thread-safe.

**Recommendation:** Start with Approach A. Add Approach B only if user-perceived cancellation latency is a problem in practice. The complexity cost of Approach B is low but the code path is tricky to test.

### 3.6 The Deadlock Trap: Never Wait on Background Thread from UI Thread

This is the single most dangerous pattern in the codebase. It must be documented as a hard rule.

**The rule: The UI thread must NEVER call `Task.Wait()`, `Task.Result`, `Thread.Join()`, `ManualResetEvent.WaitOne()`, or any other blocking wait that depends on the background agent loop thread completing.**

Why: The background thread may call `Control.Invoke()` for COM recapture. If the UI thread is blocked waiting on the background thread, and the background thread is blocked waiting on `Invoke` to complete on the UI thread, both threads deadlock permanently. SOLIDWORKS freezes. The user must kill the process.

```csharp
// FORBIDDEN -- will deadlock if BG thread ever calls Invoke:
private void OnFormClosing(object sender, FormClosingEventArgs e)
{
    _agentTask?.Wait();  // UI thread blocked here
    // Meanwhile, BG thread tries Invoke() for COM recapture
    // DEADLOCK: UI waits for BG, BG waits for UI
}

// CORRECT -- cancel and let the background thread finish asynchronously:
private void OnFormClosing(object sender, FormClosingEventArgs e)
{
    _agentCancellation?.Cancel();
    // Do NOT wait. The background thread will finish on its own.
    // PostToUi calls will be swallowed because the control is disposing.
}
```

---

## 4. COM Interaction Patterns

### 4.1 The SessionContext Snapshot Model

The existing architecture captures a complete `SessionContext` on the UI thread via `SessionContextBuilder.Build()` **before** any background work starts. This is the correct pattern. The `SessionContext` is a plain C# object graph with no COM references -- it is safe to read from any thread.

The snapshot model means:
- **Tools never hold COM interface pointers.** They operate on `SessionContext` properties (`context.FeatureTree.Features`, `context.Dimensions.Items`, etc.).
- **The background thread never needs to touch COM** for normal tool execution.
- **COM data can go stale** during a long agent loop (the user might change selection, open a different document, etc.), but this is acceptable for most queries.

### 4.2 Mid-Loop COM Refresh (When Needed)

Some agent loop iterations may benefit from fresh COM data:

| Scenario | Trigger | How |
|---|---|---|
| User changed selection during a long loop | Model calls `get_selection_context` | Marshal `SessionContextBuilder.Build()` to UI thread via `Invoke` |
| Model asks about a feature that was created since the loop started | Model calls `get_feature_tree_slice` | Same: full context refresh via `Invoke` |
| Document was switched | Model calls `get_active_document` | Same: full context refresh via `Invoke` |

**Implementation:**

The `AgentLoopRunner` receives a `Func<SessionContext>` delegate that, when called from the background thread, marshals to the UI thread, captures fresh COM data, and returns the new `SessionContext`:

```csharp
// Constructed in TaskPaneControl, passed to the agent loop:
Func<SessionContext> refreshContext = () =>
{
    return InvokeOnUi(() => HostState.CaptureContext());
};
```

**When to refresh:** Refreshing on every iteration is wasteful. Refresh only when:
1. The model explicitly requests a tool that would benefit from fresh data (e.g., `get_selection_context` after the initial context was captured many seconds ago).
2. A configurable "stale threshold" has elapsed (e.g., 30 seconds since last capture).

For Phase 1, **do not refresh mid-loop**. Use the initially captured context for all tool executions. This avoids the complexity of determining when a refresh is needed and eliminates the `Invoke` deadlock risk during the loop. Add mid-loop refresh in Phase 2 when there is evidence that stale data is causing bad agent behavior.

### 4.3 COM Object Lifetime: ReleaseComObject Discipline

`SessionContextBuilder.Build()` already follows correct COM lifetime practice (visible in lines 95-140, 199-233, etc.):

```csharp
Feature? feature = null;
try
{
    feature = model.IFirstFeature();
    while (feature != null) {
        Feature currentFeature = feature;
        feature = null;
        try {
            // ... use currentFeature ...
            feature = currentFeature.IGetNextFeature();
        }
        finally {
            ReleaseComObject(currentFeature);
        }
    }
}
finally
{
    ReleaseComObject(feature);
}
```

This pattern is critical: COM objects obtained from SOLIDWORKS have reference counts. Failing to release them causes memory leaks and can prevent SOLIDWORKS from shutting down cleanly. The `ReleaseComObject` helper (lines 931-946) is correctly implemented:

```csharp
private static void ReleaseComObject(object? value)
{
    if (value == null || !Marshal.IsComObject(value)) return;
    try { Marshal.ReleaseComObject(value); }
    catch (Exception ex) { FileLogger.Error("COM object release failed.", ex); }
}
```

**Rule for the agent loop:** No COM object references should ever leak into `SessionContext`, `ToolResult`, or any object that crosses from the UI thread to the background thread. The existing code already follows this rule. The agent loop does not introduce new COM interaction patterns -- it only adds more iterations of the same tool execution against the same serialized `SessionContext`.

### 4.4 RCW and Prevent Prevent Prevent: Never Store COM Pointers on Background Thread

A Runtime Callable Wrapper (RCW) is the .NET proxy for a COM interface pointer. RCWs are bound to the thread's apartment. If a background thread (MTA) accidentally obtains a reference to a SOLIDWORKS COM object (e.g., by capturing `ISldWorks` in a closure), calling methods on it will either fail or produce undefined behavior.

The existing `HostState._application` field stores the `ISldWorks` reference, but it is only accessed under `lock (Sync)` and the returned reference is only used on the UI thread (in `PrepareAssistantRun` and `BuildStatusText`, both called from UI thread context).

**Rule:** The `ISldWorks` reference (and any COM interface pointer) must never be passed to the background thread, stored in a closure that runs on the background thread, or accessed from the `AgentLoopRunner`. All COM access flows through the `SessionContext` snapshot or the `Invoke`-based refresh delegate.

---

## 5. Streaming Partial Responses into the Task Pane

### 5.1 Safe UI Update Pattern

The Task Pane uses Windows Forms `TextBox` controls for answer, plan, and tools display. Updating these from the background thread requires marshaling. The existing `ReplaceTextPreserveView` method (lines 675-706) is designed for this:

```csharp
private void ReplaceTextPreserveView(TextBox target, string text, bool preserveView)
{
    if (string.Equals(target.Text, text, StringComparison.Ordinal)) return;
    // ... save scroll position ...
    target.Text = text;
    // ... restore scroll position ...
}
```

For streaming partial responses during the agent loop, the pattern is:

```csharp
// On background thread, after each iteration or tool completion:
PostToUi(() =>
{
    // Append to tools display
    _toolsBox.Text += Environment.NewLine + "--- " + toolName + " ---" +
                      Environment.NewLine + resultSummary;
    _toolsBox.SelectionStart = _toolsBox.TextLength;
    _toolsBox.ScrollToCaret();
});
```

### 5.2 Throttling UI Updates

If the agent loop runs fast (e.g., multiple tool executions in quick succession), posting a `BeginInvoke` for each one can flood the message queue and cause the UI to flicker. Throttle updates:

```csharp
private DateTime _lastProgressUpdate = DateTime.MinValue;
private const int MinProgressIntervalMs = 100;  // 10 updates/sec max

private void PostThrottledProgress(string message)
{
    DateTime now = DateTime.UtcNow;
    if ((now - _lastProgressUpdate).TotalMilliseconds < MinProgressIntervalMs)
        return;  // Skip this update

    _lastProgressUpdate = now;
    PostToUi(() => _runStateLabel.Text = message);
}
```

For the tools panel, accumulate results and post in batches:

```csharp
// In AgentLoopRunner: accumulate a StringBuilder of tool results
// Post the full accumulated text after each tool_use iteration completes,
// not after each individual tool within the iteration.
```

### 5.3 TextBox.AppendText vs. TextBox.Text Assignment

`TextBox.AppendText(string)` is more efficient than `TextBox.Text += string` because it does not allocate a new string for the entire content. However, it does not allow scroll position preservation. For the agent loop:

- Use `TextBox.Text = fullText` (full replacement) for the answer panel, since the answer is replaced on each final response.
- Use accumulation + `TextBox.Text = accumulated` for the tools panel, since we want to show the full tool execution log.
- Use `Label.Text = message` for the status label (single-line, always replaced).

### 5.4 Streaming API Responses (Future Phase)

Both OpenAI and Anthropic support Server-Sent Events (SSE) streaming. On .NET 4.8 with `HttpWebRequest`, implementing SSE streaming requires:

```csharp
request.Headers["Accept"] = "text/event-stream";
using var response = (HttpWebResponse)request.GetResponse();
using var stream = response.GetResponseStream();
using var reader = new StreamReader(stream, Encoding.UTF8);

string? line;
while ((line = reader.ReadLine()) != null)
{
    if (cancellationToken.IsCancellationRequested) break;
    if (!line.StartsWith("data: ")) continue;
    string data = line.Substring(6);
    if (data == "[DONE]") break;

    // Parse incremental content delta
    // Post partial text to UI via BeginInvoke
    PostToUi(() => _answerBox.Text = accumulatedText);
}
```

**Risk:** `StreamReader.ReadLine()` is blocking. If the server stops sending events (network issue), the thread blocks until the read timeout. The `HttpWebRequest.ReadWriteTimeout` applies to individual read operations, so this is bounded. Cancellation between chunks is possible because `ReadLine()` returns after each line.

**Recommendation:** Defer streaming to Phase 3. The initial agent loop uses non-streaming requests. The model's tool-use responses are typically short (JSON with tool names and parameters), so the network transfer time is negligible. The wait is dominated by model inference time, which streaming cannot reduce.

---

## 6. Anti-Patterns to Avoid

### 6.1 FATAL: Calling COM from Background Thread

```csharp
// WRONG: Background thread directly accesses ISldWorks
ThreadPool.QueueUserWorkItem(_ =>
{
    ModelDoc2 model = _application.IActiveDoc2;  // COM VIOLATION
    string title = model.GetTitle();              // UNDEFINED BEHAVIOR
});
```

**Fix:** Always capture COM data into `SessionContext` on the UI thread before starting background work.

### 6.2 FATAL: Synchronous Wait on UI Thread

```csharp
// WRONG: UI thread blocks waiting for background task
private void RunAssistant()
{
    var task = Task.Run(() => RunAgentLoop());
    task.Wait();  // DEADLOCK if RunAgentLoop calls Invoke
    ApplyResult(task.Result);
}
```

**Fix:** Use fire-and-forget with `BeginInvoke` callback:
```csharp
Task.Run(() => {
    var result = RunAgentLoop();
    PostToUi(() => ApplyResult(result));
});
```

### 6.3 DANGEROUS: Storing COM References in Closures

```csharp
// WRONG: COM object captured in closure, used on background thread
ISldWorks app = _application;
Task.Run(() =>
{
    string version = app.RevisionNumber();  // COM VIOLATION
});
```

**Fix:** Extract the value on the UI thread, pass the value (not the COM reference) to the background thread.

### 6.4 DANGEROUS: Forgetting to Check IsDisposed/IsHandleCreated

```csharp
// WRONG: BeginInvoke on a disposed control throws
Task.Run(() =>
{
    BeginInvoke(new Action(() => _label.Text = "Done"));
    // Throws InvalidOperationException if control was disposed
});
```

**Fix:** Use the `PostToUi` guard pattern (already in the codebase).

### 6.5 SUBTLE: Re-entrancy in Invoke Callbacks

```csharp
// WRONG: Invoke callback triggers another background operation
// that also calls Invoke, creating nested marshaling
control.Invoke(new Action(() =>
{
    RefreshContext();  // This might trigger an event that starts another background task
    // that also calls Invoke... leading to re-entrant message pump processing
}));
```

**Fix:** Keep `Invoke` callbacks minimal -- only capture data, never trigger side effects that start new asynchronous work.

### 6.6 SUBTLE: Using lock() Around COM Calls

```csharp
// WRONG: lock does not help with COM apartment violations
lock (_sync)
{
    ModelDoc2 model = _application.IActiveDoc2;  // Still a COM violation on MTA thread
}
```

`lock` provides mutual exclusion between .NET threads. It does not change the COM apartment of the calling thread. COM apartment affinity is enforced at the COM infrastructure level, below the CLR.

### 6.7 WASTEFUL: Parallel Tool Execution

```csharp
// WRONG: Parallel tool execution adds complexity for microsecond gains
Parallel.ForEach(response.ToolUses, toolUse =>
{
    ToolResult result = ExecuteTool(context, toolUse);  // Thread-safe, but...
    lock (results) { results.Add(result); }
});
```

Tools execute against in-memory `SessionContext` data. Each tool takes microseconds to milliseconds. Parallelizing them saves negligible time while adding thread-safety complexity for the results list, ordering issues for the conversation history, and harder-to-debug failure modes.

**Fix:** Sequential tool execution within each iteration. The API call (seconds) dominates total loop time, not tool execution (microseconds).

### 6.8 SUBTLE: Timer Interaction with Agent Loop

The existing `System.Windows.Forms.Timer` (`_refreshTimer`) fires on the UI thread. The current code already stops the timer during assistant runs (`_refreshTimer.Stop()` in `RunAssistant`). This must be preserved in the agent loop path.

If the timer fires during an `Invoke` callback (possible because `Invoke` pumps messages), the timer handler could trigger a COM call that interferes with the context capture. The existing guard (`if (_isRunning) return;` in `RefreshStatus`) prevents this.

---

## 7. Timeout Architecture

### 7.1 Per-Call Timeouts

Each API call in the agent loop has its own timeout, set via `HttpWebRequest.Timeout`. This is already the pattern in both `OpenAIModelClient` and `AnthropicMessagesModelClient`.

For the agent loop, the timeout applies to each individual API call, not the entire loop. A 10-iteration loop with a 30-second timeout per call can run for up to 300 seconds total.

### 7.2 Per-Loop Timeout

Add an optional total loop timeout using `CancellationTokenSource` with a timeout:

```csharp
// In TaskPaneControl, when starting the agent loop:
int loopTimeoutMs = agentConfig.TotalLoopTimeoutMs;  // e.g., 120000 (2 minutes)

_agentCancellation = loopTimeoutMs > 0
    ? new CancellationTokenSource(loopTimeoutMs)
    : new CancellationTokenSource();
```

`CancellationTokenSource(int millisecondsDelay)` automatically sets the token to cancelled after the specified delay. The agent loop's cancellation checks will then throw `OperationCanceledException`, which is handled the same way as user-initiated cancellation but with a different message:

```csharp
catch (OperationCanceledException)
{
    bool wasUserCancelled = _agentCancellation?.IsCancellationRequested == true
        && !_agentCancellation.Token.WaitHandle.WaitOne(0);  // Not reliable; use a flag instead

    // Better: set a flag when user clicks Cancel
    PostToUi(() => ShowCancelled(wasTimedOut: !_userRequestedCancel));
}
```

**Simpler approach with a dedicated flag:**

```csharp
private bool _userRequestedCancel;

private void CancelAssistant()
{
    _userRequestedCancel = true;
    _agentCancellation?.Cancel();
}

// In FinishAssistantRunUi:
_userRequestedCancel = false;
```

### 7.3 Timeout Hierarchy

```
Total loop timeout (e.g., 120s)
  |
  |-- Iteration 1
  |     |-- API call timeout (e.g., 30s)
  |     |-- Tool execution (no explicit timeout; tools are sub-millisecond)
  |
  |-- Iteration 2
  |     |-- API call timeout (e.g., 30s)
  |     |-- Tool execution
  |     ...
  |
  |-- Whichever triggers first (loop timeout or max iterations) stops the loop
```

---

## 8. Error Recovery Patterns

### 8.1 Exception Types and Where They Occur

| Exception | Source | Thread | Handling |
|---|---|---|---|
| `WebException` (timeout) | `HttpWebRequest.GetResponse()` | Background | Retry once if iterations remain, then fall back to deterministic. |
| `WebException` (rate limit, 429) | `HttpWebRequest.GetResponse()` | Background | Wait 1-2 seconds, retry once. Then fall back. |
| `WebException` (auth, 401/403) | `HttpWebRequest.GetResponse()` | Background | Stop loop immediately. Return error. |
| `WebException` (server, 500+) | `HttpWebRequest.GetResponse()` | Background | Retry once. Then fall back. |
| `COMException` | `SessionContextBuilder.Build()` via `Invoke` | UI (marshaled) | Catch in `Invoke` callback. Use stale context. Log warning. |
| `InvalidOperationException` | `Control.Invoke()` / `BeginInvoke()` | Background | Control disposed. Treat as cancellation. Return partial results. |
| `OperationCanceledException` | `CancellationToken.ThrowIfCancellationRequested()` | Background | Caught in `Task.Run` wrapper. Show cancellation UI. |
| `Exception` from tool execution | `tool.Execute(context, params)` | Background | Caught per-tool. Create error `ToolResult`. Send as `tool_result` with `is_error: true`. Model self-corrects. |
| `OutOfMemoryException` | Anywhere (large API responses) | Either | Let it propagate. The CLR will handle it. Not recoverable in-process. |

### 8.2 Consecutive Error Budget

```csharp
int consecutiveApiErrors = 0;
const int maxConsecutiveApiErrors = 2;

while (state.IterationCount < state.MaxIterations)
{
    // ... cancellation check ...

    AgentModelResponse response = modelClient.CompleteWithTools(/*...*/);

    if (!response.Success)
    {
        consecutiveApiErrors++;
        if (consecutiveApiErrors >= maxConsecutiveApiErrors)
        {
            // Too many consecutive API failures. Fall back to deterministic.
            return CreateFallbackResult(state);
        }
        // Log the error, continue loop (model might recover)
        continue;
    }

    consecutiveApiErrors = 0;  // Reset on success
    // ... process response ...
}
```

Tool errors are NOT counted toward the consecutive API error budget. Tool errors are normal (e.g., asking `get_mates` on a part document returns "Not an assembly"). They are sent back to the model as `tool_result` with `is_error: true`, and the model typically self-corrects.

### 8.3 Deterministic Fallback Integration

When the agent loop fails entirely (API unreachable, auth error, max consecutive errors), fall back to the existing single-turn path:

```csharp
if (agentResult.HasError && agentResult.ExecutedTools.Count == 0)
{
    // Complete fallback: run the single-turn deterministic path
    return HostState.CompleteAssistantRun(preparation);
}

if (agentResult.HasError && agentResult.ExecutedTools.Count > 0)
{
    // Partial fallback: use accumulated tool results with deterministic synthesis
    return BuildDeterministicAnswer(agentResult.ExecutedTools);
}
```

---

## 9. Implementation Checklist

### Phase 1 (Minimum Viable Agent Loop)

- [ ] `AgentLoopRunner` runs on a background thread via `Task.Run`.
- [ ] COM capture happens once on UI thread before `Task.Run`.
- [ ] No mid-loop COM refresh (deferred to Phase 2).
- [ ] `CancellationTokenSource` created per run, disposed in `FinishAssistantRunUi`.
- [ ] Cancel button toggles from "Run assistant" to "Cancel" during agent loop.
- [ ] `_agentCancellation.Cancel()` on cancel click.
- [ ] Token checked at 4 points: top of loop, after API call, before each tool, (no COM refresh point yet).
- [ ] Progress via `PostToUi` / `BeginInvoke` (fire-and-forget) to `_runStateLabel`.
- [ ] Final result via `PostToUi` / `BeginInvoke` to answer/plan/tools panels.
- [ ] All existing `RefreshStatus` guards respected (`if (_isRunning) return;`).
- [ ] Timer stopped during agent loop.
- [ ] Feature-gated behind `SOLIDWORKS_AI_AGENT_LOOP=true`.
- [ ] Deterministic fallback when API fails or is unconfigured.
- [ ] No `Task.Wait()` or `Task.Result` anywhere on the UI thread.
- [ ] No COM references in closures passed to background thread.
- [ ] No parallel tool execution.

### Phase 2 (Hardening)

- [ ] Mid-loop COM refresh via `Invoke` when tool needs fresh data.
- [ ] `InvokeOnUi<T>` helper method added to `TaskPaneControl`.
- [ ] Per-loop total timeout via `CancellationTokenSource(timeout)`.
- [ ] HTTP request abort on cancellation (Approach B from section 3.5).
- [ ] Throttled progress updates (section 5.2).
- [ ] Intermediate tool results displayed in Tools tab during loop.
- [ ] Consecutive error budget (section 8.2).

### Phase 3 (Streaming and Polish)

- [ ] SSE streaming for final answer generation.
- [ ] Partial answer text displayed as tokens arrive.
- [ ] Cross-click conversation persistence.
- [ ] Token budget visualization in the UI.

---

## 10. Reference: Thread-Safe Type Checklist

| Type | Thread-Safe? | Notes |
|---|---|---|
| `CancellationTokenSource` | Yes | `.Cancel()` and `.Token` are thread-safe. |
| `CancellationToken` | Yes | Struct, copied by value. Check from any thread. |
| `Control.BeginInvoke` | Yes | Can be called from any thread. Posts to UI message queue. |
| `Control.Invoke` | Yes | Can be called from any thread. Blocks caller until UI thread executes. |
| `HttpWebRequest` | Partially | A single request should not be shared across threads. `.Abort()` is thread-safe. |
| `SessionContext` | Yes (read) | Plain C# object. Safe to read from any thread. No COM references. |
| `ToolResult` | Yes (read) | Plain C# object. Safe to read from any thread. |
| `AgentConversationState` | No | Only accessed from the single agent loop background thread. No sharing needed. |
| `ISldWorks` (COM RCW) | No | STA-bound. Only access from UI thread. |
| `ModelDoc2` (COM RCW) | No | STA-bound. Only access from UI thread. |
| `volatile` fields | Partially | Guarantees visibility, not atomicity. Suitable for `_activeRequest` reference. |
| `List<T>` | No | Only accessed from a single thread in the agent loop. No sharing needed. |

---

## 11. Summary of Key Decisions

1. **COM stays on the UI thread, always.** The snapshot model (`SessionContext`) is the boundary. No COM references cross to the background thread.

2. **Single background thread for the agent loop.** No parallelism within the loop. Sequential API calls, sequential tool execution. Complexity is the enemy; the API call dominates wall-clock time.

3. **`BeginInvoke` for progress, `Invoke` for COM recapture.** Async for fire-and-forget, sync only when the background thread needs the result.

4. **`CancellationTokenSource` for cancellation.** Thread-safe, checked at 4 points in the loop. `HttpWebRequest` timeout provides a bounded worst-case cancellation latency.

5. **No mid-loop COM refresh in Phase 1.** Use the initially captured context. Add `Invoke`-based refresh in Phase 2 with evidence of need.

6. **Never block the UI thread on the background thread.** No `Wait()`, no `Result`, no `Join()`. This is the cardinal rule that prevents deadlocks.

7. **Feature-gated.** The agent loop is behind `SOLIDWORKS_AI_AGENT_LOOP=true`. The existing single-turn path is preserved as default and as fallback.

8. **Deterministic fallback preserved.** When the agent loop fails, the system degrades to the existing single-turn deterministic path, not to silence.

---

# Research: Tool-Calling Abstraction Across Providers

**Date:** 2026-03-15
**Status:** Research complete
**Scope:** Normalized comparison of tool-calling wire formats, streaming, lifecycle, and JSON schema handling across OpenAI, Anthropic, OpenRouter, Ollama, and LM Studio -- with a concrete C# abstraction proposal for the Adze .NET Framework 4.8 desktop host.

---

## 1. Provider-by-Provider Tool-Calling Analysis

### 1.1 OpenAI Chat Completions API

**Status:** Production-stable. Tool calling has been the canonical format since November 2023 (replacing the deprecated `functions` parameter).

#### Request Format

Tool definitions go in a top-level `tools` array. Each entry wraps the schema in `{"type": "function", "function": {...}}`:

```json
{
  "model": "gpt-4o",
  "messages": [
    {"role": "system", "content": "..."},
    {"role": "user", "content": "..."}
  ],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "Returns dimensions.",
        "parameters": {
          "type": "object",
          "properties": {
            "scope": {"type": "string", "enum": ["selection", "document"]},
            "include_driven": {"type": "boolean"}
          },
          "required": []
        }
      }
    }
  ],
  "tool_choice": "auto"
}
```

**Schema key:** `parameters` (inside the `function` wrapper).
**System prompt:** In the `messages` array as `role: "system"`.

#### Response Format

When the model calls tools, `finish_reason` is `"tool_calls"` and the assistant message carries a `tool_calls` array:

```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": null,
      "tool_calls": [{
        "id": "call_abc123",
        "type": "function",
        "function": {
          "name": "get_dimensions",
          "arguments": "{\"scope\": \"document\"}"
        }
      }]
    },
    "finish_reason": "tool_calls"
  }]
}
```

**Critical detail:** `arguments` is a **JSON string**, not a parsed object. You must deserialize it yourself. This is the single most common source of bugs in OpenAI-format tool-call parsing. The string can contain malformed JSON if the model hallucinates, and it can be truncated if `max_tokens` is hit mid-generation.

**Content field:** Typically `null` when `tool_calls` is present. Some models (especially older GPT-4 Turbo snapshots) occasionally emit both `content` and `tool_calls`. Safe implementations must check for both.

#### Tool Result Format

Each tool result is a **separate message** with `role: "tool"`:

```json
{"role": "tool", "tool_call_id": "call_abc123", "content": "...result json string..."}
```

**Key facts:**
- Dedicated `role: "tool"` (not reusing `role: "user"`).
- One message per tool result. Multiple tools = multiple messages.
- ID field is `tool_call_id`.
- `content` is always a string. No `is_error` flag -- errors are communicated through the content text.
- The full assistant message (including `tool_calls`) must be appended to the conversation before the `tool` messages.

#### tool_choice

| Value | Behavior |
|-------|----------|
| `"auto"` | Model decides (default) |
| `"required"` | Must call at least one tool |
| `"none"` | Must not call tools |
| `{"type": "function", "function": {"name": "..."}}` | Must call this specific tool |

#### Parallel Tool Calls

Enabled by default. Multiple entries in `tool_calls` with different `id` values. Disable with `"parallel_tool_calls": false` at the request level. When parallel calls are returned, each gets its own `role: "tool"` result message.

#### Streaming

SSE format. Tool calls stream in `delta.tool_calls[]`:

```
data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_abc","type":"function","function":{"name":"get_dimensions","arguments":""}}]}}]}
data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"scope"}}]}}]}
data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\":\"doc\"}"}}]}}]}
data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}]}
```

- First chunk has `id`, `name`, empty `arguments`.
- Subsequent chunks append to `arguments` for the tool call at `index`.
- For parallel calls, `index` differentiates them.
- Must buffer and concatenate `arguments` fragments, then parse the assembled JSON.
- `data: [DONE]` terminates the stream.

#### Usage Reporting

```json
{"usage": {"prompt_tokens": 345, "completion_tokens": 42, "total_tokens": 387}}
```

Present in non-streaming responses. In streaming, usage appears in the final chunk (since ~late 2024) if `stream_options: {"include_usage": true}` is set.

#### Quirks and Gotchas

1. **`arguments` is a string, not an object.** This is the most important difference from Anthropic. Forgetting to deserialize it separately is a common bug.
2. **`content` can be `null` or a string.** When `tool_calls` is present, `content` is usually `null`, but not guaranteed.
3. **`finish_reason` is `"tool_calls"` (plural),** not `"tool_call"`.
4. **Token counting includes tool definitions.** Tool schemas consume input tokens. 10 tools with descriptions can consume 500-1500 tokens.
5. **No built-in `is_error` flag on tool results.** If a tool fails, you describe the error in the `content` string. The model handles it fine in practice.
6. **`max_tokens` can truncate tool call arguments.** If `max_tokens` is too low, the model may emit a `tool_calls` array with incomplete `arguments` JSON. Always validate the JSON after assembling it.

---

### 1.2 Anthropic Messages API

**Status:** Production-stable. Tool use has been GA since the `2023-06-01` API version and has seen only additive changes.

#### Request Format

Tools go in a top-level `tools` array. No `function` wrapper -- each tool is a flat object:

```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 1024,
  "system": "You are a SOLIDWORKS assistant.",
  "tools": [
    {
      "name": "get_dimensions",
      "description": "Returns dimensions.",
      "input_schema": {
        "type": "object",
        "properties": {
          "scope": {"type": "string", "enum": ["selection", "document"]},
          "include_driven": {"type": "boolean"}
        },
        "required": []
      }
    }
  ],
  "messages": [
    {"role": "user", "content": "What are the dimensions?"}
  ]
}
```

**Schema key:** `input_schema` (not `parameters`).
**System prompt:** Top-level `system` field, not in `messages`.
**Required headers:** `x-api-key`, `anthropic-version: 2023-06-01`, `content-type: application/json`.

#### Response Format

When the model calls tools, `stop_reason` is `"tool_use"` and the `content` array contains `tool_use` blocks:

```json
{
  "id": "msg_01XFD...",
  "role": "assistant",
  "stop_reason": "tool_use",
  "content": [
    {"type": "text", "text": "Let me check the dimensions."},
    {
      "type": "tool_use",
      "id": "toolu_01A09...",
      "name": "get_dimensions",
      "input": {"scope": "document"}
    }
  ]
}
```

**Critical detail:** `input` is a **parsed JSON object**, not a string. This is the opposite of OpenAI. No extra deserialization step needed.

**Content array:** Can contain both `text` and `tool_use` blocks in the same response. The text block is the model's "thinking aloud" before calling tools. Both must be preserved when echoing the assistant message back.

#### Tool Result Format

Tool results go in a `user` message as `tool_result` content blocks:

```json
{
  "role": "user",
  "content": [
    {
      "type": "tool_result",
      "tool_use_id": "toolu_01A09...",
      "content": "...result json string..."
    }
  ]
}
```

**Key facts:**
- Results use `role: "user"` (not a dedicated role).
- Multiple tool results go as multiple `tool_result` blocks in a **single** `user` message.
- ID field is `tool_use_id` (not `tool_call_id`).
- `content` inside `tool_result` can be a string or an array of content blocks (text, image).
- Supports `"is_error": true` for explicit error signaling.
- The full assistant message (including all content blocks) must be appended before the `user` tool-result message.

#### tool_choice

```json
"tool_choice": {"type": "auto"}
```

| Value | Behavior |
|-------|----------|
| `{"type": "auto"}` | Model decides (default) |
| `{"type": "any"}` | Must use at least one tool |
| `{"type": "tool", "name": "..."}` | Must use this specific tool |

Note the structural difference: Anthropic uses `{"type": "any"}` where OpenAI uses `"required"`.

#### Parallel Tool Use

The model can return multiple `tool_use` blocks in one response. Return all `tool_result` blocks in one `user` message. Disable with:

```json
"tool_choice": {"type": "auto", "disable_parallel_tool_use": true}
```

#### Streaming

SSE format with named event types:

```
event: content_block_start
data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01A","name":"get_dimensions","input":{}}}

event: content_block_delta
data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"scope\""}}

event: content_block_delta
data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":":\"document\"}"}}

event: content_block_stop
data: {"type":"content_block_stop","index":1}

event: message_delta
data: {"type":"message_delta","delta":{"stop_reason":"tool_use"}}
```

**Key differences from OpenAI streaming:**
- Uses named `event:` lines (OpenAI omits the `event:` field and uses only `data:` lines).
- Tool input arrives as `input_json_delta` fragments (not embedded in a `delta.tool_calls` array).
- `content_block_start` announces the tool name and ID upfront; only `partial_json` needs buffering.
- `content_block_stop` signals that the full input JSON for one block is complete.
- `message_delta` carries the `stop_reason`.

#### Usage Reporting

```json
{"usage": {"input_tokens": 345, "output_tokens": 87}}
```

Field names differ from OpenAI: `input_tokens` / `output_tokens` (not `prompt_tokens` / `completion_tokens`). No `total_tokens` field -- compute it yourself.

#### Quirks and Gotchas

1. **`input` is an object, not a string.** Opposite of OpenAI. If you unify parsing, the OpenAI side needs an extra `JSON.parse()` step.
2. **`content` is always an array of blocks.** Even for plain text. Never assume it is a string.
3. **`stop_reason` is `"tool_use"` (singular),** not `"tool_calls"`.
4. **Tool results go in `role: "user"` messages.** This is semantically odd but architecturally intentional -- the API enforces strict user/assistant alternation.
5. **`system` is not a message.** It is a top-level field. If you build a unified message list, the system prompt needs special handling for Anthropic.
6. **`is_error` flag exists.** Unlike OpenAI, Anthropic has an explicit error-signaling mechanism on tool results.
7. **`max_tokens` is required.** OpenAI defaults it; Anthropic requires it explicitly.
8. **The `anthropic-version` header is mandatory.** Without it, the API returns a 400.

---

### 1.3 OpenRouter

**Status:** Production-stable aggregator. Uses the **OpenAI-compatible format** for all providers, translating transparently.

**Endpoint:** `https://openrouter.ai/api/v1/chat/completions`
**Auth:** `Authorization: Bearer <OPENROUTER_API_KEY>`

#### Format

Identical to OpenAI Chat Completions. Tool definitions use the `{"type": "function", "function": {...}}` wrapper, `parameters` for schemas, `tool_calls` in responses, `role: "tool"` for results.

```json
{
  "model": "anthropic/claude-sonnet-4-20250514",
  "messages": [...],
  "tools": [{"type": "function", "function": {"name": "...", "parameters": {...}}}]
}
```

#### Provider Coverage for Tool Calling

OpenRouter translates tool calling for models that natively support it:
- **Anthropic** models (3.5 Sonnet, 3.5 Haiku, Opus, Sonnet 4, Haiku 4, etc.) -- reliable
- **OpenAI GPT-4o, GPT-4.1 series, GPT-4 Turbo** -- reliable (native pass-through)
- **Google Gemini 1.5, 2.0** models -- generally works; occasional schema strictness differences
- **Mistral Large, Medium** with function calling -- works but some models are less reliable
- **Meta Llama 3.1/3.2/3.3** -- works for instruction-tuned variants that support tool calling; quality varies
- **DeepSeek** models -- supported but tool-calling quality is inconsistent

#### Streaming

Same SSE format as OpenAI. OpenRouter transparently translates Anthropic's event-based SSE to OpenAI's delta-based format.

#### Quirks and Gotchas

1. **Latency overhead.** Extra hop through OpenRouter adds 50-200ms per request.
2. **Model naming.** Uses `provider/model-name` format: `anthropic/claude-sonnet-4-20250514`, `openai/gpt-4o`.
3. **`arguments` is always a JSON string,** even when the underlying model is Anthropic (OpenRouter translates Anthropic's `input` object into OpenAI's `arguments` string).
4. **Error format.** OpenRouter error responses generally follow the OpenAI format, but provider-specific errors may leak through with different structures.
5. **Rate limiting.** OpenRouter has its own rate limits layered on top of provider limits. 429 responses may come from OpenRouter or the underlying provider.
6. **Tool calling support is model-dependent.** Not all models available through OpenRouter support tool calling. If you send `tools` to a model that does not support them, you may get an error or the tools may be silently ignored.
7. **`HTTP-Referer` and `X-Title` headers.** OpenRouter encourages (but does not require) these for analytics and leaderboard tracking. Not relevant for tool calling but good practice.
8. **OpenRouter-specific response fields.** Responses may include `x-ratelimit-*` headers and an `id` field with an OpenRouter-specific prefix.
9. **Streaming `usage` availability.** Depends on the underlying provider. Not always present in streaming responses.

#### Implication for Adze

OpenRouter can be implemented as a thin configuration variant of the OpenAI client. The only differences are: the endpoint URL, the API key source, and the model naming convention. The wire format is identical.

---

### 1.4 Ollama

**Status:** Tool calling is supported in Ollama since v0.3.0 (mid-2024) using the OpenAI-compatible API endpoint. Quality depends heavily on the specific model.

**Endpoint:** `http://localhost:11434/v1/chat/completions` (OpenAI-compatible) or `http://localhost:11434/api/chat` (native Ollama format).

#### OpenAI-Compatible Endpoint (`/v1/chat/completions`)

Ollama implements the OpenAI Chat Completions API format for tool calling. The request format is identical to OpenAI:

```json
{
  "model": "llama3.1:8b",
  "messages": [
    {"role": "system", "content": "..."},
    {"role": "user", "content": "..."}
  ],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "...",
        "parameters": {"type": "object", "properties": {...}}
      }
    }
  ]
}
```

Response format follows OpenAI conventions:

```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": "",
      "tool_calls": [{
        "id": "call_...",
        "type": "function",
        "function": {
          "name": "get_dimensions",
          "arguments": "{\"scope\": \"document\"}"
        }
      }]
    },
    "finish_reason": "tool_calls"
  }]
}
```

Tool results use `role: "tool"` with `tool_call_id`, same as OpenAI.

#### Native Ollama Endpoint (`/api/chat`)

Ollama also has a native chat endpoint that supports tools in a slightly different format:

```json
{
  "model": "llama3.1:8b",
  "messages": [{"role": "user", "content": "..."}],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "...",
        "parameters": {"type": "object", "properties": {...}}
      }
    }
  ],
  "stream": false
}
```

Response format is slightly different from the OpenAI-compatible endpoint:

```json
{
  "model": "llama3.1:8b",
  "message": {
    "role": "assistant",
    "content": "",
    "tool_calls": [{
      "function": {
        "name": "get_dimensions",
        "arguments": {"scope": "document"}
      }
    }]
  },
  "done": true,
  "done_reason": "stop"
}
```

**Key difference:** In the native endpoint, `arguments` can be a **parsed object** (not a string). Also, `tool_calls` entries may lack an `id` field in some Ollama versions, and the response is not wrapped in `choices[]`.

#### Streaming

The native `/api/chat` endpoint streams NDJSON (newline-delimited JSON), not SSE:

```
{"model":"llama3.1:8b","message":{"role":"assistant","content":""},"done":false}
{"model":"llama3.1:8b","message":{"role":"assistant","content":"","tool_calls":[...]},"done":true}
```

The `/v1/chat/completions` endpoint uses SSE format matching OpenAI conventions.

**Important:** Ollama disables streaming when tools are present in the request (for the native endpoint). The tool calls are returned in a single non-streaming response even when `"stream": true` is specified. The OpenAI-compatible endpoint may stream text but delivers tool calls atomically in some versions.

#### Model Support

Tool calling quality varies dramatically by model:

| Model | Tool Calling Quality | Notes |
|-------|---------------------|-------|
| `llama3.1:8b/70b` | Moderate | Sometimes calls wrong tool, argument schemas loosely followed |
| `llama3.2:3b` | Poor | Frequent hallucinated tool names, malformed arguments |
| `llama3.3:70b` | Good | Reliable tool selection, mostly correct arguments |
| `qwen2.5:7b/72b` | Good | Solid tool calling, respects schemas well |
| `mistral:7b` | Poor-Moderate | Inconsistent, sometimes embeds tool calls in text instead of structured output |
| `mixtral:8x7b` | Moderate | Works but occasionally produces invalid JSON in arguments |
| `command-r` (Cohere) | Good | Designed for tool calling, reliable |
| `deepseek-r1` | Poor | Does not reliably use tool-calling format |
| `phi3:medium` | Poor-Moderate | Sometimes works but often falls back to text descriptions |
| `gemma2:9b/27b` | Poor | Limited tool-calling capability |

#### Quirks and Gotchas

1. **No authentication by default.** Ollama runs locally with no API key. Auth depends on reverse proxy setup.
2. **`tool_calls[].id` may be missing or auto-generated.** Ollama did not always generate stable tool call IDs. Newer versions (0.5+) generate them for the OpenAI-compatible endpoint, but older versions or the native endpoint may omit them. You should generate a fallback ID if missing.
3. **`arguments` format is inconsistent.** The OpenAI-compatible endpoint returns `arguments` as a JSON string (matching OpenAI). The native endpoint may return it as a parsed object. Handle both.
4. **`finish_reason` vs. `done_reason`.** The OpenAI-compatible endpoint uses `finish_reason`. The native endpoint uses `done_reason` with value `"stop"` (not `"tool_calls"`). In the native endpoint, check for the presence of `tool_calls` in the message rather than relying on the done reason.
5. **Streaming is effectively disabled for tool calls.** Ollama buffers the entire response when tools are involved, making streaming a no-op for tool-calling turns.
6. **Schema validation is non-existent.** Ollama does not validate tool arguments against the provided JSON schema. The model may return arguments that violate `required`, `enum`, `type`, or `minimum`/`maximum` constraints. All argument validation must happen client-side.
7. **Parallel tool calls.** Supported in format but model-dependent. Most local models do not reliably produce multiple tool calls in a single response.
8. **`tool_choice` is not always respected.** Ollama accepts the parameter but whether the underlying model honors it depends on the model's fine-tuning.
9. **Token usage reporting is limited.** The native endpoint reports `prompt_eval_count` and `eval_count` (not OpenAI's field names). The OpenAI-compatible endpoint maps these to `prompt_tokens` / `completion_tokens`.
10. **Model hot-loading latency.** The first request to a model that is not loaded adds significant latency (seconds to tens of seconds for model loading). This is not tool-calling-specific but affects timeout configuration.

#### Recommendation for Adze

Use the **OpenAI-compatible endpoint** (`/v1/chat/completions`). This allows Ollama to share the same client implementation as OpenAI and OpenRouter. Add defensive handling for missing `tool_calls[].id` fields and malformed `arguments`. Set generous timeouts to account for model loading.

---

### 1.5 LM Studio

**Status:** Tool calling is supported in LM Studio since version 0.3.0 (late 2024) via its OpenAI-compatible API server. Quality depends on the loaded model.

**Endpoint:** `http://localhost:1234/v1/chat/completions`

#### Format

LM Studio implements the OpenAI Chat Completions API format. Tool definitions, tool calls, and tool results all match the OpenAI wire format:

```json
{
  "model": "loaded-model-identifier",
  "messages": [...],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "...",
        "parameters": {"type": "object", "properties": {...}}
      }
    }
  ]
}
```

Responses follow the OpenAI `choices[].message.tool_calls` format with `arguments` as a JSON string.

#### Streaming

SSE format matching OpenAI conventions. Same buffering requirements as OpenAI for tool call arguments.

However, like Ollama, the actual streaming behavior during tool-calling turns is model-dependent. Some quantized models may not produce well-formed streaming tool-call deltas.

#### Model Support

LM Studio supports any GGUF model that the user loads. Tool-calling quality depends entirely on the model:

| Model Type | Tool Calling Quality | Notes |
|------------|---------------------|-------|
| Llama 3.1/3.3 instruct variants | Moderate-Good | See Ollama notes; same models, same behavior |
| Qwen 2.5 instruct variants | Good | Reliable tool calling |
| Mistral instruct | Moderate | Inconsistent |
| Smaller models (<7B) | Poor | Generally unreliable for structured tool calling |
| Non-instruct/base models | Not supported | No tool calling capability |

#### Quirks and Gotchas

1. **Model identifier is whatever the user loaded.** Unlike cloud providers with fixed model names, the `model` field is the local filename or a user-defined alias. Adze cannot assume a specific model name.
2. **No authentication by default.** Like Ollama, runs locally without API keys.
3. **`tool_calls[].id` generation.** LM Studio generates IDs in the OpenAI `call_*` format, but the reliability depends on the version. Older versions may produce non-unique or missing IDs.
4. **`tool_choice` support.** LM Studio accepts the parameter but enforcement depends on the loaded model. `"required"` mode may not reliably force tool use with all models.
5. **Schema strictness.** Like Ollama, schema validation is model-dependent, not server-enforced. Client-side validation is necessary.
6. **Context window limits.** Local models often have smaller context windows (4K-32K vs 128K+ for cloud models). Tool definitions and conversation history consume context. More aggressive truncation may be needed.
7. **Quantization effects.** Heavily quantized models (Q2, Q3) are significantly worse at structured output like tool calling than Q5/Q6/Q8/FP16 variants.
8. **Server startup latency.** LM Studio's API server takes time to load a model. First requests may time out.
9. **Concurrent request handling.** LM Studio typically handles one request at a time. If the agent loop sends a second request before the first completes (unlikely but possible with timeouts), it may queue or reject it.
10. **No usage reporting in some versions.** `usage` may be missing or incomplete in LM Studio responses. Handle absent `usage` gracefully.

#### Recommendation for Adze

Same as Ollama: use the OpenAI-compatible endpoint and share the OpenAI client implementation. Add defensive checks for missing fields and set model-loading-aware timeouts. Consider allowing the user to configure the model name and context window size since LM Studio does not expose these programmatically.

---

## 2. Normalized Comparison Matrix

| Aspect | OpenAI | Anthropic | OpenRouter | Ollama | LM Studio |
|--------|--------|-----------|------------|--------|-----------|
| **Endpoint format** | OpenAI Chat Completions | Anthropic Messages | OpenAI-compatible | OpenAI-compatible or native | OpenAI-compatible |
| **Auth mechanism** | `Authorization: Bearer` | `x-api-key` header | `Authorization: Bearer` | None (default) | None (default) |
| **Tool def wrapper** | `{"type":"function","function":{...}}` | Flat `{name, description, input_schema}` | Same as OpenAI | Same as OpenAI | Same as OpenAI |
| **Schema key name** | `parameters` | `input_schema` | `parameters` | `parameters` | `parameters` |
| **System prompt** | In `messages` as `role:"system"` | Top-level `system` field | In `messages` | In `messages` | In `messages` |
| **Tool call location** | `message.tool_calls[]` | `content[]` blocks `type:"tool_use"` | Same as OpenAI | Same as OpenAI | Same as OpenAI |
| **Arguments format** | JSON **string** | JSON **object** | JSON **string** | JSON **string** (compat) or object (native) | JSON **string** |
| **Stop signal (tools)** | `finish_reason:"tool_calls"` | `stop_reason:"tool_use"` | Same as OpenAI | Same as OpenAI (compat) | Same as OpenAI |
| **Stop signal (done)** | `finish_reason:"stop"` | `stop_reason:"end_turn"` | Same as OpenAI | Same as OpenAI (compat) | Same as OpenAI |
| **Tool result role** | `role:"tool"` | `role:"user"` + `tool_result` blocks | Same as OpenAI | Same as OpenAI | Same as OpenAI |
| **Result ID field** | `tool_call_id` | `tool_use_id` | `tool_call_id` | `tool_call_id` | `tool_call_id` |
| **Multiple results** | Separate `tool` messages | Array of blocks in one `user` message | Separate messages | Separate messages | Separate messages |
| **Error flag** | None (text in content) | `is_error: true` | None | None | None |
| **Parallel tool calls** | Default on; `parallel_tool_calls:false` | Default on; `disable_parallel_tool_use` | Same as OpenAI | Format-supported, model-dependent | Format-supported, model-dependent |
| **Streaming format** | SSE `data:` lines | SSE `event:` + `data:` lines | Same as OpenAI | SSE (compat) or NDJSON (native) | SSE |
| **Streaming tool calls** | Delta-based argument buffering | `input_json_delta` fragments | Same as OpenAI | Effectively non-streaming | Model-dependent |
| **Usage field names** | `prompt_tokens`, `completion_tokens` | `input_tokens`, `output_tokens` | Same as OpenAI | Same as OpenAI (compat) | Same as OpenAI or absent |
| **max_tokens** | Optional (has default) | **Required** | Optional | Optional | Optional |
| **Schema validation** | Server-side (partial) | Server-side (partial) | Provider-dependent | None (model-dependent) | None (model-dependent) |
| **Tool call IDs** | Always present (`call_*`) | Always present (`toolu_*`) | Always present | May be missing | May be missing |

---

## 3. JSON Schema Compatibility

All five providers accept JSON Schema for tool parameter definitions, but with different levels of support:

### 3.1 Common Supported Schema Features

These work reliably across all providers:

```json
{
  "type": "object",
  "properties": {
    "name": {"type": "string", "description": "..."},
    "count": {"type": "integer"},
    "enabled": {"type": "boolean"},
    "mode": {"type": "string", "enum": ["a", "b", "c"]}
  },
  "required": ["name"]
}
```

### 3.2 Schema Features with Inconsistent Support

| Feature | OpenAI | Anthropic | Local Models |
|---------|--------|-----------|--------------|
| `type: ["string", "null"]` (union types) | Supported | Supported | Often ignored by model |
| `default` values | Accepted, sometimes used | Accepted, sometimes used | Usually ignored |
| `minimum` / `maximum` on integers | Accepted, rarely enforced by model | Accepted, rarely enforced | Ignored |
| `additionalProperties: false` | Supported (strict mode) | Accepted but not enforced | Ignored |
| `$ref` / `$defs` | Not supported | Not supported | Not supported |
| Nested objects | Supported | Supported | Model-dependent; simpler is better |
| Arrays of objects | Supported | Supported | Fragile with local models |
| `oneOf` / `anyOf` | Partial support | Partial support | Unreliable |

### 3.3 OpenAI Strict Mode

OpenAI added a `"strict": true` option on tool definitions that enforces the schema on the model's output. When enabled:
- The model is guaranteed to produce JSON matching the schema
- `additionalProperties: false` must be set on all objects
- All properties must be listed in `required` (use `type: ["string", "null"]` for optional properties)
- No `$ref` or external references

This is the strongest schema enforcement available from any provider. Anthropic and local models do not offer an equivalent guarantee.

### 3.4 Safe Schema Subset for Adze

Given the Adze tool parameter types (`GetDimensionsParameters`, `GetFeatureTreeSliceParameters`, etc.), the safe subset that works across all providers is:

```json
{
  "type": "object",
  "properties": {
    "simple_string": {"type": "string", "description": "..."},
    "simple_int": {"type": "integer", "description": "..."},
    "simple_bool": {"type": "boolean", "description": "..."},
    "enum_string": {"type": "string", "enum": ["a", "b"], "description": "..."}
  },
  "required": []
}
```

All 10 current Adze tools use only these types. No nested objects, no arrays, no union types. This is a significant advantage for cross-provider compatibility.

---

## 4. Streaming Behavior Deep Dive

### 4.1 SSE Parsing on .NET Framework 4.8

Neither `HttpWebRequest` nor `WebClient` has built-in SSE support. Implementing SSE requires reading the response stream line-by-line:

```csharp
using (var response = (HttpWebResponse)request.GetResponse())
using (var stream = response.GetResponseStream())
using (var reader = new StreamReader(stream, Encoding.UTF8))
{
    string line;
    while ((line = reader.ReadLine()) != null)
    {
        if (line.StartsWith("data: "))
        {
            string json = line.Substring(6);
            if (json == "[DONE]") break;
            // Parse and accumulate
        }
    }
}
```

This works but has complications:
- `HttpWebRequest.Timeout` applies to the initial response, not to the stream reading. A long streaming response may hang.
- `ReadWriteTimeout` controls individual read operations but is not a total timeout.
- No built-in cancellation. Must use `request.Abort()` from another thread.

### 4.2 Tool-Call Streaming is Not Worth It Initially

For tool-calling turns, streaming provides no user-visible benefit:
- You cannot execute the tool until the full arguments JSON is assembled.
- Tool execution happens locally and is fast (milliseconds for the Adze read-only tools).
- The only benefit would be displaying the model's "thinking" text before the tool call, which is minor.

For the final answer turn, streaming provides real value (perceived responsiveness).

### 4.3 Recommendation

**Phase 1:** Non-streaming for all turns. Simpler, debuggable, and the total round-trip for a tool-calling turn is dominated by model inference time anyway.

**Phase 2:** Streaming for the final answer turn only. Use `"stream": true` when the request does not include tool definitions (the synthesis pass) or when the model is producing the terminal response.

**Phase 3:** Full streaming with tool-call buffering. Only if UX requirements demand real-time "thinking" text display during tool-calling turns.

---

## 5. Tool-Call Lifecycle Comparison

### 5.1 OpenAI-Format Lifecycle (OpenAI, OpenRouter, Ollama, LM Studio)

```
1. Client sends: messages + tools
2. Model returns: assistant message with tool_calls[] (finish_reason: "tool_calls")
3. Client appends: full assistant message to conversation
4. Client executes: each tool_call, builds results
5. Client appends: one "role":"tool" message per tool result
6. GOTO 1 (send full conversation back)
7. Eventually: model returns assistant message with content (finish_reason: "stop")
```

### 5.2 Anthropic-Format Lifecycle

```
1. Client sends: system + messages + tools
2. Model returns: content[] with text + tool_use blocks (stop_reason: "tool_use")
3. Client appends: full assistant message (all content blocks) to messages
4. Client executes: each tool_use block, builds results
5. Client appends: one "role":"user" message with tool_result[] blocks
6. GOTO 1 (send full conversation back)
7. Eventually: model returns content[] with text only (stop_reason: "end_turn")
```

### 5.3 Key Lifecycle Differences

| Step | OpenAI-Format | Anthropic |
|------|--------------|-----------|
| Echo assistant message | Must include `tool_calls` array | Must include full `content[]` with all blocks |
| Add tool results | One separate `role:"tool"` message per result | One `role:"user"` message with multiple `tool_result` blocks |
| Stop detection | Check `finish_reason == "stop"` | Check `stop_reason == "end_turn"` |
| Tool call detection | Check `finish_reason == "tool_calls"` or presence of `tool_calls` | Check `stop_reason == "tool_use"` or presence of `tool_use` blocks |
| Error signaling | Error text in `content` string | `is_error: true` flag available |

---

## 6. Conversation State Handling

### 6.1 Message Roles

| Role | OpenAI-Format | Anthropic |
|------|--------------|-----------|
| System instruction | `{"role":"system","content":"..."}` in messages | Top-level `"system":"..."` field |
| User message | `{"role":"user","content":"..."}` | `{"role":"user","content":"..."}` or `{"role":"user","content":[blocks]}` |
| Assistant text | `{"role":"assistant","content":"..."}` | `{"role":"assistant","content":[{"type":"text","text":"..."}]}` |
| Assistant tool call | `{"role":"assistant","tool_calls":[...]}` | `{"role":"assistant","content":[{"type":"tool_use",...}]}` |
| Tool result | `{"role":"tool","tool_call_id":"...","content":"..."}` | `{"role":"user","content":[{"type":"tool_result",...}]}` |

### 6.2 Content Block Types

**OpenAI-format:**
- `content` is a string (or null) for assistant messages
- `content` is a string for tool result messages
- No content block types -- content is always flat text

**Anthropic:**
- `content` is always an array of typed blocks
- Block types: `text`, `tool_use`, `tool_result`, `image` (for input)
- Mixed blocks: a single assistant response can contain multiple `text` and `tool_use` blocks interleaved

### 6.3 Multi-Turn Conversation Pattern

The provider-agnostic internal representation must support these patterns:

```
Turn 1: user -> assistant (text only)                    [no tools needed]
Turn 2: user -> assistant (tool calls) -> tool results   [one tool-call round]
Turn 3: user -> assistant (tool calls) -> tool results -> assistant (tool calls) -> tool results -> assistant (text)
         [two tool-call rounds before final answer]
```

The internal `ConversationMessage` must be rich enough to serialize to either format. The key design decision: **store messages in a provider-neutral internal format and serialize to the wire format at the provider boundary.**

---

## 7. Practical Abstraction Boundary

### 7.1 What Can Be Unified

1. **Tool definitions.** A single internal `AgentToolDefinition` (name, description, JSON schema as `Dictionary<string, object>`) can be serialized to either format by the provider client.

2. **Tool call requests.** A `ToolCallRequest` with `Id`, `Name`, and `InputJson` (string) works for both. Anthropic's parsed `input` object gets serialized to a string for internal use; OpenAI's `arguments` string is used as-is.

3. **Tool call results.** A `ToolCallResult` with `ToolCallId`, `OutputJson`, and `IsError` covers both formats. OpenAI ignores `IsError` (error goes in `OutputJson`); Anthropic maps it to `is_error`.

4. **Stop reason.** Normalize to an enum: `ToolUse`, `EndTurn`, `MaxTokens`, `Error`.

5. **Usage tracking.** A `ModelUsage` with `InputTokens`, `OutputTokens`, `TotalTokens` covers both. Already implemented.

6. **The agent loop itself.** The loop logic (send, check stop reason, execute tools, append results, repeat) is identical across all providers.

### 7.2 What Cannot Be Unified

1. **Request body construction.** Anthropic's system prompt, tool definition key names, and content block format differ fundamentally from OpenAI's. Each provider client must build its own request body.

2. **Response parsing.** Anthropic's content-block-based response structure vs. OpenAI's `choices[].message` structure require different parsing code. The `arguments` (string) vs. `input` (object) difference is particularly load-bearing.

3. **Tool result message construction.** Anthropic packs multiple results into one `user` message with `tool_result` blocks. OpenAI uses separate `role:"tool"` messages. This affects how conversation history is built.

4. **Streaming event formats.** Anthropic's named events vs. OpenAI's `data:`-only format require different SSE parsers.

5. **Authentication.** Header name and format differ (`Authorization: Bearer` vs. `x-api-key`).

### 7.3 The Right Boundary

The abstraction boundary should be at the **agent model client** level: each provider implements a method that takes a normalized conversation state and tool definitions, makes the API call, and returns a normalized response. The loop runner never knows which provider it is talking to.

```
AgentLoopRunner (provider-agnostic)
  |
  +-- calls IAgentModelClient.SendTurn(...)
  |     |
  |     +-- OpenAIAgentClient          (builds OpenAI request, parses OpenAI response)
  |     +-- AnthropicAgentClient       (builds Anthropic request, parses Anthropic response)
  |     +-- OllamaAgentClient          (extends OpenAI with defensive checks)
  |     +-- LMStudioAgentClient        (extends OpenAI with defensive checks)
  |     +-- OpenRouterAgentClient      (extends OpenAI with model naming)
  |
  +-- calls IToolExecutor.Execute(...)  (executes tools, provider-agnostic)
```

In practice, Ollama, LM Studio, and OpenRouter all use the OpenAI format, so they can share a base implementation with configuration overrides. The real split is **two** client implementations: Anthropic and OpenAI-format.

---

## 8. C# Interface Proposal

### 8.1 Core Abstractions

```csharp
namespace Adze.Broker.Abstractions;

/// <summary>
/// Normalized stop reason across all providers.
/// </summary>
public enum AgentStopReason
{
    /// <summary>Model produced a final text answer.</summary>
    EndTurn,

    /// <summary>Model requested one or more tool calls.</summary>
    ToolUse,

    /// <summary>Response was truncated by max_tokens limit.</summary>
    MaxTokens,

    /// <summary>An error occurred during the API call.</summary>
    Error
}

/// <summary>
/// Provider-agnostic tool definition for the agent loop.
/// Each provider client serializes this into its wire format.
/// </summary>
public sealed class AgentToolDefinition
{
    /// <summary>Tool name matching ToolNames constants.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description for the model.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema for the tool's input parameters, stored as a
    /// nested dictionary tree. Serialized to "input_schema" (Anthropic)
    /// or "parameters" (OpenAI) by the provider client.
    /// </summary>
    public Dictionary<string, object?> ParameterSchema { get; set; } = new();
}

/// <summary>
/// A tool call request extracted from the model's response.
/// Normalized across providers.
/// </summary>
public sealed class AgentToolCall
{
    /// <summary>
    /// Provider-assigned ID for this tool call.
    /// "toolu_*" for Anthropic, "call_*" for OpenAI-format providers.
    /// May be auto-generated for local providers that omit it.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Tool name the model wants to invoke.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Deserialized tool input arguments. Always a dictionary,
    /// regardless of whether the provider sent it as a JSON string
    /// (OpenAI) or a parsed object (Anthropic). The provider client
    /// handles this normalization.
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = new();

    /// <summary>
    /// Raw arguments JSON string, preserved for logging and
    /// for re-serialization when echoing the assistant message.
    /// </summary>
    public string ArgumentsJson { get; set; } = string.Empty;
}

/// <summary>
/// Result of executing a tool, to be sent back to the model.
/// </summary>
public sealed class AgentToolResult
{
    /// <summary>The tool call ID this result corresponds to.</summary>
    public string ToolCallId { get; set; } = string.Empty;

    /// <summary>Tool name (for logging and dispatch).</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Serialized tool output as a JSON string.</summary>
    public string OutputJson { get; set; } = string.Empty;

    /// <summary>
    /// Whether the tool execution failed. Anthropic maps this to
    /// is_error on the tool_result block. OpenAI-format providers
    /// embed the error in OutputJson (no dedicated flag).
    /// </summary>
    public bool IsError { get; set; }
}

/// <summary>
/// Normalized response from a single model turn in the agent loop.
/// </summary>
public sealed class AgentTurnResponse
{
    /// <summary>Whether the API call succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Normalized stop reason.</summary>
    public AgentStopReason StopReason { get; set; }

    /// <summary>
    /// Text content from the model's response. Present on EndTurn
    /// responses (the final answer) and optionally on ToolUse
    /// responses (the model's "thinking" text before tool calls).
    /// </summary>
    public string TextContent { get; set; } = string.Empty;

    /// <summary>
    /// Tool calls requested by the model. Non-null and non-empty
    /// when StopReason is ToolUse.
    /// </summary>
    public List<AgentToolCall> ToolCalls { get; set; } = new();

    /// <summary>Token usage for this turn.</summary>
    public ModelUsage Usage { get; set; } = new();

    /// <summary>Error description when Success is false.</summary>
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>Provider identifier (for tracing).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Model identifier (for tracing).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Raw assistant message object, preserved in provider-specific
    /// format for echoing back in the next request. The provider
    /// client stores this opaquely and uses it when building the
    /// next request's message history.
    /// </summary>
    public object? RawAssistantMessage { get; set; }
}
```

### 8.2 Agent Model Client Interface

```csharp
namespace Adze.Broker.Abstractions;

/// <summary>
/// Model client that supports the tool-use conversation protocol.
/// Each provider implements this interface to handle its wire format.
///
/// This is separate from IModelClient (which handles the existing
/// single-turn broker/synthesis paths) to avoid breaking changes.
/// The two interfaces may be unified in a future refactor.
/// </summary>
public interface IAgentModelClient
{
    /// <summary>
    /// Sends a single turn of the agent conversation to the model.
    /// Builds the provider-specific request from the normalized
    /// conversation state and parses the response into a normalized
    /// AgentTurnResponse.
    /// </summary>
    /// <param name="systemPrompt">System instructions for the model.</param>
    /// <param name="conversationHistory">
    ///   Ordered list of prior turns. Each entry is an opaque object
    ///   that was previously returned as RawAssistantMessage or built
    ///   by the client's tool-result formatting method.
    ///   The provider client knows how to serialize these.
    /// </param>
    /// <param name="toolDefinitions">
    ///   Available tools the model may call. Empty list disables tool use.
    /// </param>
    /// <param name="settings">
    ///   Model settings (max_tokens, temperature, timeout, etc.).
    /// </param>
    /// <returns>Normalized response from this turn.</returns>
    AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings);

    /// <summary>
    /// Builds the initial user message in provider-specific format
    /// for insertion into the conversation history.
    /// </summary>
    object BuildUserMessage(string content);

    /// <summary>
    /// Builds tool result message(s) in provider-specific format
    /// for insertion into the conversation history.
    ///
    /// Returns a list because OpenAI-format requires one message per
    /// tool result, while Anthropic packs all results into one message.
    /// The caller appends all returned objects to the conversation.
    /// </summary>
    List<object> BuildToolResultMessages(List<AgentToolResult> results);
}
```

### 8.3 Agent Model Settings

```csharp
namespace Adze.Broker.Configuration;

/// <summary>
/// Configuration for agent loop model calls.
/// Extends the existing BrokerModelSettings pattern.
/// </summary>
public sealed class AgentModelSettings
{
    /// <summary>Max tokens for each individual agent turn.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>HTTP timeout per turn in milliseconds.</summary>
    public int TimeoutMilliseconds { get; set; } = 30000;

    /// <summary>Temperature for model generation.</summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Maximum number of tool-calling iterations before forcing
    /// a final answer or falling back to deterministic synthesis.
    /// </summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>
    /// Maximum consecutive API errors before aborting the loop.
    /// Tool execution errors (sent back to the model) do not count.
    /// </summary>
    public int MaxConsecutiveErrors { get; set; } = 2;

    /// <summary>
    /// Maximum total tokens (input + output accumulated across all
    /// turns) before forcing loop termination. 0 = no limit.
    /// </summary>
    public int MaxTotalTokens { get; set; } = 100000;

    /// <summary>
    /// Maximum size in characters of a single tool result before
    /// truncation. Prevents context window overflow from large
    /// feature trees or property sets.
    /// </summary>
    public int MaxToolResultChars { get; set; } = 8192;

    /// <summary>Whether to disable parallel tool calls.</summary>
    public bool DisableParallelToolCalls { get; set; } = false;
}
```

### 8.4 Agent Loop Runner

```csharp
namespace Adze.Broker.Orchestration;

/// <summary>
/// Executes the provider-agnostic multi-turn agent loop.
/// Called on a background thread. Uses IAgentModelClient to
/// communicate with the model and IToolExecutor to run tools.
/// </summary>
public sealed class AgentLoopRunner
{
    /// <summary>
    /// Runs the agent loop to completion or until cancelled/exhausted.
    /// </summary>
    /// <param name="modelClient">Provider-specific model client.</param>
    /// <param name="toolExecutor">Executes tool calls against SessionContext.</param>
    /// <param name="systemPrompt">System instructions.</param>
    /// <param name="userRequest">User's question.</param>
    /// <param name="toolDefinitions">Available tools.</param>
    /// <param name="settings">Loop configuration.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <param name="onProgress">Progress callback (marshaled to UI by caller).</param>
    /// <returns>Final result of the agent loop.</returns>
    public AgentLoopResult Run(
        IAgentModelClient modelClient,
        IToolExecutor toolExecutor,
        string systemPrompt,
        string userRequest,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        CancellationToken cancellationToken,
        Action<AgentProgressUpdate>? onProgress);
}

/// <summary>
/// Executes tool calls against the session context.
/// Abstracts the tool dispatch so the loop runner does not depend
/// on specific tool implementations.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes a single tool call and returns the result.
    /// </summary>
    /// <param name="toolName">Name of the tool to execute.</param>
    /// <param name="arguments">Deserialized arguments from the model.</param>
    /// <returns>Tool execution result.</returns>
    AgentToolResult Execute(string toolName, Dictionary<string, object?> arguments);
}
```

### 8.5 Tool Definition Builder

```csharp
namespace Adze.Broker.Formatting;

/// <summary>
/// Builds AgentToolDefinition instances from the existing tool
/// contracts (ToolNames, parameter classes, request schemas).
/// Provider-agnostic -- the provider client handles format conversion.
/// </summary>
public static class AgentToolDefinitionBuilder
{
    /// <summary>
    /// Builds tool definitions for all enabled tools.
    /// </summary>
    /// <param name="enabledToolNames">
    ///   Tool names from SessionContext.Policy.EnabledTools.
    /// </param>
    /// <returns>List of provider-agnostic tool definitions.</returns>
    public static List<AgentToolDefinition> BuildAll(IEnumerable<string> enabledToolNames);

    /// <summary>
    /// Builds a single tool definition by name.
    /// Returns null if the tool name is not recognized.
    /// </summary>
    public static AgentToolDefinition? Build(string toolName);
}
```

### 8.6 Provider Client Implementation Structure

```csharp
namespace Adze.Broker.Clients;

/// <summary>
/// OpenAI-format agent client. Shared base for OpenAI, OpenRouter,
/// Ollama, and LM Studio.
/// </summary>
public class OpenAIFormatAgentClient : IAgentModelClient
{
    // Builds OpenAI-format request bodies
    // Parses OpenAI-format responses
    // Handles: arguments as JSON string -> Dictionary deserialization
    // Handles: tool_calls[] array parsing
    // Handles: role:"tool" result messages
    // Handles: missing tool_call IDs (generates fallback)
    // Protected virtual methods for subclass customization

    public virtual AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings);

    public object BuildUserMessage(string content);

    public List<object> BuildToolResultMessages(List<AgentToolResult> results);

    /// <summary>
    /// Builds the HTTP request. Virtual so subclasses can customize
    /// headers (e.g., OpenRouter adds HTTP-Referer).
    /// </summary>
    protected virtual HttpWebRequest BuildRequest(
        string endpoint, string apiKey, int timeoutMs);

    /// <summary>
    /// Builds the tool definitions array in OpenAI format.
    /// Virtual so subclasses can add provider-specific fields.
    /// </summary>
    protected virtual object[] BuildToolDefinitionsPayload(
        List<AgentToolDefinition> toolDefinitions);
}

/// <summary>
/// OpenRouter-specific overrides. Mostly configuration.
/// </summary>
public sealed class OpenRouterAgentClient : OpenAIFormatAgentClient
{
    // Adds HTTP-Referer and X-Title headers
    // Model names use provider/model format
    // Otherwise identical to OpenAI format
}

/// <summary>
/// Ollama-specific overrides. Adds defensive handling.
/// </summary>
public sealed class OllamaAgentClient : OpenAIFormatAgentClient
{
    // Generates fallback tool_call IDs when missing
    // Handles arguments as object or string (normalizes to string)
    // Increases default timeout for model loading
    // No auth headers
}

/// <summary>
/// LM Studio-specific overrides.
/// </summary>
public sealed class LMStudioAgentClient : OpenAIFormatAgentClient
{
    // Similar to Ollama: defensive handling for missing fields
    // No auth headers
    // Handles absent usage data gracefully
}

/// <summary>
/// Anthropic-format agent client. Separate implementation
/// due to fundamentally different wire format.
/// </summary>
public sealed class AnthropicAgentClient : IAgentModelClient
{
    // Builds Anthropic-format request bodies
    // Parses Anthropic content-block-based responses
    // Handles: input as parsed object (no extra deserialization)
    // Handles: content[] block parsing (text + tool_use)
    // Handles: tool_result blocks in user messages
    // Handles: is_error flag mapping
    // Handles: system prompt as top-level field (not in messages)
    // Handles: anthropic-version header
    // Handles: max_tokens as required field
}
```

### 8.7 Agent Client Factory

```csharp
namespace Adze.Broker.Clients;

/// <summary>
/// Creates the appropriate IAgentModelClient based on provider
/// configuration. Extends the existing ModelClientFactory pattern.
/// </summary>
public static class AgentClientFactory
{
    /// <summary>
    /// Creates an agent model client from environment configuration.
    /// Returns null if no usable configuration is found.
    /// </summary>
    public static IAgentModelClient? CreateDefault();

    /// <summary>
    /// Creates an agent model client for a specific provider.
    /// </summary>
    /// <param name="provider">
    ///   Provider name: "openai", "anthropic", "openrouter",
    ///   "ollama", "lmstudio".
    /// </param>
    /// <param name="settings">Provider-specific settings.</param>
    public static IAgentModelClient? Create(string provider, BrokerModelSettings settings);
}
```

---

## 9. Provider-Specific Quirks and Gaps

### 9.1 Quirks That Affect the Abstraction

| Quirk | Provider | Impact on Abstraction |
|-------|----------|----------------------|
| `arguments` is a JSON string | OpenAI, OpenRouter, Ollama (compat), LM Studio | `OpenAIFormatAgentClient` must deserialize `arguments` string to `Dictionary` for `AgentToolCall.Arguments` |
| `input` is a parsed object | Anthropic | `AnthropicAgentClient` can use `input` directly; must serialize to JSON string for `AgentToolCall.ArgumentsJson` |
| `arguments` might be an object | Ollama (native endpoint) | `OllamaAgentClient` must handle both string and object |
| `tool_calls[].id` may be missing | Ollama, LM Studio (older versions) | Generate `"local_" + Guid.NewGuid().ToString("N")` as fallback |
| `content` can be null or string | OpenAI-format | Must handle both null and string in `message.content` |
| `content` is always block array | Anthropic | Must iterate blocks to extract text and tool_use |
| No `is_error` on tool results | OpenAI-format | Prepend "ERROR: " to `OutputJson` for clarity; model understands this |
| `system` is a top-level field | Anthropic | `AnthropicAgentClient` must extract system prompt from conversation |
| `max_tokens` is required | Anthropic | Always set explicitly; OpenAI-format can omit for defaults |
| `usage` may be absent | Ollama, LM Studio | Return zero-valued `ModelUsage` when missing |
| `finish_reason` vs `stop_reason` | All | Normalized to `AgentStopReason` enum by each client |
| `tool_choice` format differs | OpenAI (`"required"`) vs Anthropic (`{"type":"any"}`) | Each client translates `AgentModelSettings.DisableParallelToolCalls` to provider format |

### 9.2 Gaps That Cannot Be Bridged

| Gap | Affected Providers | Mitigation |
|-----|-------------------|------------|
| No server-side schema validation | Ollama, LM Studio | Client-side argument validation before tool execution |
| Tool-calling quality varies by model | Ollama, LM Studio | Document recommended models; add `is_error` fallback for malformed calls |
| `strict` mode not available | Anthropic, Ollama, LM Studio, OpenRouter | Do not depend on strict mode; keep schemas simple |
| No guaranteed parallel tool calls | Ollama, LM Studio | Sequential execution is the default; parallel is an optimization |
| Context window size unknown | Ollama, LM Studio | User-configurable `MaxTotalTokens`; conservative defaults |
| Model loading latency | Ollama, LM Studio | Generous first-request timeout; health check endpoint before loop |

### 9.3 Safety Boundaries

The following safety rules apply to all providers and should be enforced in the `AgentLoopRunner`, not in individual clients:

1. **Maximum iterations.** Hard cap at `MaxIterations` (default 10). After this, force a deterministic fallback answer.
2. **Maximum consecutive API errors.** Hard cap at `MaxConsecutiveErrors` (default 2). After this, stop the loop and fall back.
3. **Maximum total tokens.** Hard cap at `MaxTotalTokens` (default 100,000). After this, force termination.
4. **Tool result truncation.** Cap each tool result at `MaxToolResultChars` (default 8,192). Truncate with `... [truncated, {original_length} chars total]`.
5. **Unknown tool names.** Return an error result to the model: `"Unknown tool: {name}. Available tools: {list}"`.
6. **Malformed arguments.** Return an error result to the model: `"Failed to parse arguments: {error}. Expected schema: {schema_summary}"`.
7. **Cancellation.** Check `CancellationToken` before each API call and before each tool execution.

---

## 10. Implementation Priority

### Phase 1: Minimum Viable Agent Loop

**Files to create:**
- `src/Adze.Broker/Abstractions/IAgentModelClient.cs` -- interface + DTOs from section 8.1-8.2
- `src/Adze.Broker/Clients/AnthropicAgentClient.cs` -- Anthropic tool-use wire format
- `src/Adze.Broker/Clients/OpenAIFormatAgentClient.cs` -- OpenAI-format base (covers OpenAI + OpenRouter)
- `src/Adze.Broker/Orchestration/AgentLoopRunner.cs` -- provider-agnostic loop
- `src/Adze.Broker/Formatting/AgentToolDefinitionBuilder.cs` -- builds tool defs from existing contracts
- `src/Adze.Broker/Configuration/AgentModelSettings.cs` -- loop configuration

**Files to modify:**
- `src/Adze.Broker/Clients/ModelClientFactory.cs` -- add `CreateAgentClient()` method
- `src/Adze.Broker/Configuration/BrokerModelSettings.cs` -- add agent loop env vars

**Test coverage:**
- Conversation state construction (both formats)
- Tool call parsing (both formats, including edge cases)
- Tool result message building (both formats)
- Agent loop iteration logic (mock client)
- Tool definition builder (all 10 tools)
- Argument validation and error handling

### Phase 2: Local Provider Support

**Files to create:**
- `src/Adze.Broker/Clients/OllamaAgentClient.cs` -- defensive overrides
- `src/Adze.Broker/Clients/LMStudioAgentClient.cs` -- defensive overrides
- `src/Adze.Broker/Clients/OpenRouterAgentClient.cs` -- header and naming overrides

**Files to modify:**
- `src/Adze.Broker/Configuration/BrokerModelSettings.cs` -- add Ollama/LMStudio/OpenRouter env vars
- `src/Adze.Broker/Clients/ModelClientFactory.cs` -- add new provider cases

### Phase 3: Streaming Final Answer

**Files to create:**
- `src/Adze.Broker/Clients/SseStreamReader.cs` -- reusable SSE line parser for .NET 4.8
- Streaming variants of `SendTurn` or a separate `SendTurnStreaming` method

---

## 11. Risk Assessment

| Risk | Severity | Likelihood | Mitigation |
|------|----------|------------|------------|
| Local models produce malformed tool calls | Medium | High (Ollama/LMStudio) | Client-side validation, error results back to model, model quality guidance |
| Token accumulation exceeds budget in multi-turn loops | Medium | Medium | `MaxTotalTokens` cap, tool result truncation, conversation truncation |
| `arguments` JSON string is truncated by `max_tokens` | High | Low (with adequate max_tokens) | Validate assembled JSON, return parse error to model |
| SSE parsing complexity on .NET 4.8 | Medium | N/A (deferred to Phase 3) | Start non-streaming; add streaming only for final answer |
| Provider API changes break wire format | Medium | Low (formats are stable) | Defensive parsing with graceful fallback, version-pinned API headers |
| `JavaScriptSerializer` edge cases with nested dictionaries | Low | Low | Already working in current codebase; the same patterns apply |
| Ollama model loading adds 10-30s to first request | Medium | High | Health check before loop, generous first-request timeout, user guidance |
| OpenRouter adds latency to every request in multi-turn loop | Low | High | Configurable, documented trade-off |

---

## 12. Summary

**The safe abstraction boundary** is `IAgentModelClient` -- a per-turn interface where each provider client translates between the normalized internal format and the provider wire format. The loop runner is entirely provider-agnostic.

**The real implementation split** is two formats, not five providers:
- **Anthropic format:** One dedicated implementation.
- **OpenAI format:** One base implementation shared by OpenAI, OpenRouter, Ollama, and LM Studio, with subclass overrides for authentication, defensive parsing, and model naming.

**What works today and is reliable:**
- OpenAI and Anthropic tool calling: production-stable, well-documented, tested in the existing Adze codebase.
- OpenRouter: production-stable passthrough, uses OpenAI format.

**What works but is flaky:**
- Ollama tool calling: format works, model quality varies. Reliable with Llama 3.3 70b and Qwen 2.5 72b. Unreliable with smaller models.
- LM Studio tool calling: same as Ollama -- format is fine, model quality is the limiting factor.

**What to build first:**
- Anthropic and OpenAI agent clients (Phase 1). These cover the primary use case and the most reliable models.
- Local provider variants (Phase 2). Configuration variants of the OpenAI client with defensive handling.
- Streaming (Phase 3). Only for the final answer turn. Not needed for tool-calling turns.

---

# Research: Local Model Provider Feasibility (Ollama / LM Studio)

**Date:** 2026-03-15
**Mode:** Research
**Status:** Complete -- findings ready for implementation planning
**Scope:** Determines whether offline operation via local models is a real v1 feature or a future aspiration

---

## Executive Summary

Local model providers (Ollama, LM Studio) are credible integration targets for Adze's OpenAI-compatible client path, but they are **not credible v1 production dependencies for the agentic tool loop**. The gap is not in API compatibility -- both providers implement `/v1/chat/completions` well enough for basic text completion -- but in **tool-calling fidelity**, **structured output reliability**, and **hardware requirements that conflict with running SOLIDWORKS concurrently on the same workstation**.

**Recommendation:** Ship local model support in v1 as an **experimental, opt-in provider** for text synthesis only (the final answer pass), with the deterministic broker planner handling tool selection. Reserve full agentic tool-loop support for local models until (a) quantized 70B+ models with reliable tool calling run on workstation GPUs, and (b) Adze has a validation harness that can gate local models on actual tool-selection accuracy before enabling them.

---

## 1. OpenAI-Compatible Endpoint Quality

### 1.1 Ollama

Ollama exposes `/api/chat` as its native endpoint and `/v1/chat/completions` as an OpenAI-compatible shim. The compatibility layer has matured significantly:

**What works well:**
- Basic `/v1/chat/completions` request/response shape: `messages`, `model`, `temperature`, `max_tokens` (mapped from Ollama's `num_predict`), `stream`
- `system`, `user`, `assistant` roles in the messages array
- Response shape: `choices[0].message.content`, `finish_reason`, `usage` (prompt_tokens, completion_tokens, total_tokens)
- Bearer token auth header (accepted but typically unused for local)
- SSE streaming with `data: [DONE]` terminator
- Model name resolution maps directly (e.g., `llama3.1:70b`, `qwen2.5:32b`)

**Known gaps and deviations:**
- `usage` token counts are approximate for many model backends; some GGUF quantizations report zero or inaccurate token counts
- `max_tokens` is silently mapped to Ollama's `num_predict` but ceiling behavior differs -- Ollama may truncate differently than OpenAI
- The `tool_choice` field is supported but behavior is inconsistent: `"required"` sometimes produces malformed tool calls on smaller models
- `parallel_tool_calls` is not reliably honored; Ollama may or may not emit multiple tool calls regardless of this setting
- Error response shape sometimes deviates from OpenAI's `{"error": {"message": ..., "type": ..., "code": ...}}` format
- No rate limiting or quota enforcement (irrelevant for local, but means no backpressure signals)
- `logprobs` not supported on most backends

**Adze-specific compatibility assessment:**
The existing `OpenAIModelClient` in `src/Adze.Broker/Clients/OpenAIModelClient.cs` sends `Authorization: Bearer`, `Content-Type: application/json`, and reads `choices[0].message.content`. This works against Ollama's `/v1/chat/completions` endpoint with no code changes for the text completion path. The `ModelResponseParser.ParseUsage()` method reads `prompt_tokens`/`completion_tokens`/`total_tokens`, which Ollama provides (though accuracy varies).

**Verdict:** The existing OpenAI client code is compatible with Ollama for text completion. No new client class is needed.

### 1.2 LM Studio

LM Studio exposes a local server at `http://localhost:1234/v1/chat/completions` (port configurable) with OpenAI-compatible API.

**What works well:**
- Nearly identical to OpenAI's API surface: messages, tools, temperature, max_tokens, stream
- Response envelope matches OpenAI exactly: `choices`, `message`, `content`, `finish_reason`, `usage`
- Proper error responses in OpenAI format
- Model loading and hot-swapping via the GUI (not API-controlled in older versions; newer versions add `/v1/models`)
- SSE streaming with standard `data:` prefix and `[DONE]` terminator
- Auth header accepted (typically `lm-studio` or any string; not validated)

**Known gaps and deviations:**
- `usage` reporting accuracy depends on the GGUF backend; some models report 0 for completion_tokens during streaming
- Model must be pre-loaded through the LM Studio GUI before API calls work (no on-demand model loading via API in most versions)
- `tool_choice` support was added later and may not be available in older LM Studio versions
- Concurrent request handling is limited -- LM Studio queues requests and processes one at a time by default
- Server must be manually started; no Windows service or auto-start mechanism
- No programmatic model selection via API in older versions

**Adze-specific compatibility assessment:**
Same as Ollama: the existing `OpenAIModelClient` works against LM Studio's endpoint with only an endpoint URL override. Set `SOLIDWORKS_AI_OPENAI_ENDPOINT=http://localhost:1234/v1/chat/completions` and `SOLIDWORKS_AI_OPENAI_API_KEY=lm-studio` and the existing code path works for text completion.

**Verdict:** The existing OpenAI client code is compatible with LM Studio for text completion. No new client class is needed.

---

## 2. Tool-Calling Fidelity

This is the critical gap. Adze's Phase 2 agentic loop requires the model to return structured `tool_calls` in OpenAI format, and for the model to reliably select the right tools from a catalog of 10+ options.

### 2.1 Ollama Tool Calling

Ollama added tool calling support in mid-2024. The wire format matches OpenAI:

```json
{
  "model": "llama3.1:70b",
  "messages": [...],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "...",
        "parameters": {"type": "object", "properties": {...}}
      }
    }
  ]
}
```

Response when tool call is triggered:
```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": null,
      "tool_calls": [{
        "id": "call_xxx",
        "type": "function",
        "function": {
          "name": "get_dimensions",
          "arguments": "{\"feature_name\": \"Boss-Extrude1\"}"
        }
      }]
    },
    "finish_reason": "tool_calls"
  }]
}
```

**Models that support tool calling in Ollama:**
- Llama 3.1 (8B, 70B, 405B) -- native tool calling trained by Meta
- Llama 3.2 (1B, 3B) -- lightweight, tool calling support but very unreliable for complex selection
- Llama 3.3 (70B) -- improved tool calling over 3.1
- Qwen 2.5 (7B, 14B, 32B, 72B) -- strong tool calling, especially 32B+
- Mistral models with function calling support (Mistral Small, Mixtral)
- Command R/R+ (Cohere models via Ollama)
- Phi-3/Phi-4 (small models, tool calling is rudimentary)
- Gemma 2 -- limited tool calling support

**Models that do NOT reliably support tool calling:**
- Most models below 7B parameters
- Older Llama 2 variants
- Code-specific models (CodeLlama, DeepSeek Coder) -- not trained for tool use
- Most fine-tunes that were not specifically trained with tool-calling data

### 2.2 LM Studio Tool Calling

LM Studio added tool calling support for compatible models. The implementation is similar to Ollama -- it passes the tools array to the model's chat template and parses structured output. Quality depends entirely on the model.

### 2.3 Reliability Assessment by Model Size

This is the most important section for Adze's decision.

**Test scenario:** Given 10 tools with descriptions (Adze's current Wave 1 catalog), select the correct 1-3 tools for a natural-language CAD question like "What are the dimensions of the Boss-Extrude1 feature?"

| Model Size | Representative Models | Tool Selection Accuracy (10 tools) | Structured JSON Reliability | Multi-Turn Tool Loop Viability |
|-----------|----------------------|-----------------------------------|---------------------------|-------------------------------|
| **1B-3B** | Llama 3.2 1B/3B, Phi-3 Mini | 20-40% -- frequently hallucinates tool names, ignores schema | ~50% -- often returns malformed JSON or plain text instead of tool_calls | Not viable |
| **7B-8B** | Llama 3.1 8B, Qwen 2.5 7B, Mistral 7B | 50-70% -- can select obvious tools but struggles with nuanced routing | ~70% -- usually valid JSON but arguments often wrong or missing | Marginal; frequent fallbacks needed |
| **13B-14B** | Qwen 2.5 14B | 65-80% -- meaningfully better at disambiguation | ~80% -- arguments more reliable | Possible for simple single-tool queries |
| **32B** | Qwen 2.5 32B | 75-85% -- approaching cloud-model quality for straightforward selection | ~85-90% -- structured output is usually correct | Viable for read-only inspection loops |
| **70B** | Llama 3.1 70B, Llama 3.3 70B, Qwen 2.5 72B | 80-90% -- competitive with smaller cloud models | ~90% -- reliable structured output | Viable but slow on consumer hardware |

**Critical finding for Adze:** The current broker planning prompt asks the model to return a specific JSON shape with `turn_status`, `intent`, `confidence`, `recommended_tools`, etc. This is a more complex structured output task than simple tool selection. Local models below 32B parameters will frequently fail to produce valid JSON matching this schema, causing the `ModelResponseParser.TryExtractJsonPayload` and `TryParseBrokerResponse` methods to reject the response, triggering deterministic fallback.

**The agentic tool loop (Phase 2) is harder still.** It requires the model to:
1. Understand the full tool catalog from a tools array
2. Decide which tools to call based on context
3. Return properly formatted `tool_calls` with valid `arguments` JSON
4. Interpret tool results and decide whether to call more tools or produce a final answer
5. Do this across multiple turns without losing coherence

Models below 70B parameters show significant degradation in steps 4 and 5. They tend to:
- Call the same tool repeatedly instead of progressing
- Produce final answers that ignore tool results
- Lose track of the conversation state across turns
- Generate arguments that are syntactically valid JSON but semantically wrong (e.g., passing a dimension name where a feature name was expected)

### 2.4 Quantization Impact

Local models run as GGUF quantizations. Quantization reduces quality:

| Quantization | Size Reduction | Quality Impact on Tool Calling |
|-------------|----------------|-------------------------------|
| Q8_0 | ~50% of FP16 | Minimal -- nearly lossless for tool calling |
| Q6_K | ~40% of FP16 | Slight degradation in edge cases |
| Q5_K_M | ~35% of FP16 | Noticeable: more argument parsing failures |
| Q4_K_M | ~28% of FP16 | Significant: tool selection accuracy drops 5-10% |
| Q3_K_M | ~22% of FP16 | Substantial: frequent schema violations |
| Q2_K | ~18% of FP16 | Unusable for structured output |

**Practical implication:** A 70B model at Q4_K_M (the most common "runnable on 24GB VRAM" quantization) loses roughly the quality equivalent of dropping to a 50B-class model at full precision. This is still viable for tool selection but the margin is thinner than the raw parameter count suggests.

---

## 3. Streaming Support

### 3.1 SSE Compatibility

Both Ollama and LM Studio implement SSE streaming that is compatible with the OpenAI format:

```
data: {"choices":[{"delta":{"content":"The"}}]}
data: {"choices":[{"delta":{"content":" dimensions"}}]}
...
data: [DONE]
```

**Ollama specifics:**
- Streams via chunked transfer encoding
- Each chunk is a single `data:` line followed by `\n\n`
- Tool call streaming works: `delta.tool_calls[0].function.arguments` arrives incrementally
- Reliable `[DONE]` termination

**LM Studio specifics:**
- Same SSE format as Ollama
- Tool call streaming is less reliable; some model backends buffer the entire tool call before emitting
- `[DONE]` termination is reliable

### 3.2 Partial Tool Call Handling

Both providers can stream tool call arguments incrementally, matching the OpenAI streaming format documented in `discovery-api-tool-use.md` section 5.2. However:

- The current Adze architecture recommendation (from the discovery brief) is to use non-streaming for tool loop turns. This remains the right call for local models, where streaming adds parsing complexity without meaningful latency benefit (the model is generating locally; there is no network round-trip to hide).
- For the final answer synthesis pass, streaming could improve perceived responsiveness when local models generate at 10-30 tokens/second.

### 3.3 Adze Implementation Implications

The existing `OpenAIModelClient` uses synchronous `HttpWebRequest` and reads the full response. Streaming would require:
1. Setting `request.SendChunked = true` or reading the response stream line-by-line
2. Parsing SSE `data:` lines and concatenating deltas
3. Marshaling partial text back to the UI thread via `Control.BeginInvoke()`

This is the same work needed for cloud streaming (Phase 7 in END-GOAL.md). Local model streaming should not be implemented separately -- it should piggyback on the general streaming infrastructure.

**Verdict:** Streaming is a Phase 7 concern. Local providers are compatible when streaming is implemented.

---

## 4. Operational Constraints on Windows Workstation Deployments

This is the second critical gap, after tool-calling fidelity.

### 4.1 GPU Memory Requirements

SOLIDWORKS 3DEXPERIENCE R2026x itself requires:
- Minimum 4GB VRAM for basic operation
- 8GB+ VRAM recommended for large assemblies with RealView graphics
- GPU compute resources for real-time rendering, section views, and display

**Local model VRAM requirements (GGUF Q4_K_M quantization):**

| Model | Parameters | VRAM (Q4_K_M) | VRAM (Q8_0) | Tool Calling Viable? |
|-------|-----------|---------------|-------------|---------------------|
| Phi-3 Mini | 3.8B | ~3 GB | ~5 GB | No |
| Llama 3.1 8B | 8B | ~5 GB | ~9 GB | Marginal |
| Qwen 2.5 14B | 14B | ~9 GB | ~16 GB | Possible |
| Qwen 2.5 32B | 32B | ~20 GB | ~36 GB | Yes |
| Llama 3.3 70B | 70B | ~42 GB | ~75 GB | Yes |

**Concurrent operation with SOLIDWORKS:**

| GPU Config | Available for LLM (after SOLIDWORKS) | Best Feasible Model | Tool Calling Quality |
|-----------|--------------------------------------|--------------------|--------------------|
| 8 GB (RTX 3060/4060) | ~4-5 GB | 8B Q4 | Poor |
| 12 GB (RTX 3060 12GB/4070) | ~7-8 GB | 8B Q6 or 14B Q3 | Poor to marginal |
| 16 GB (RTX 4070 Ti/4080) | ~10-12 GB | 14B Q5 | Marginal |
| 24 GB (RTX 3090/4090) | ~18-20 GB | 32B Q4 | Adequate for read-only |
| 48 GB (dual GPU or RTX A6000) | ~40+ GB | 70B Q4 | Good |

**Key finding:** The minimum hardware for local models with adequate tool-calling quality (32B+ at Q4_K_M) requires an RTX 3090/4090 with 24GB VRAM, running concurrently with SOLIDWORKS. This is a high-end workstation configuration -- present in many engineering environments but not universal.

### 4.2 System RAM Requirements

When GPU VRAM is insufficient, both Ollama and LM Studio can offload layers to system RAM. This is dramatically slower (10-50x) but allows larger models to run:

- A 70B Q4_K_M model needs ~42 GB. With 24 GB VRAM + 32 GB RAM offload, it runs but at 2-5 tokens/second.
- SOLIDWORKS itself typically uses 4-16 GB RAM depending on assembly size.
- Windows with SOLIDWORKS loaded typically has 8-16 GB of free RAM on a 32 GB system.
- Running a 32B model with partial CPU offload on a 32 GB system with SOLIDWORKS open is feasible but will noticeably impact SOLIDWORKS responsiveness.

### 4.3 Inference Speed

For a desktop interaction to feel responsive, the user needs:
- First token in under 3 seconds (time to first token / TTFT)
- At least 15-20 tokens/second for streaming output to feel fluid
- Total response in under 10 seconds for a tool planning turn

**Observed inference speeds on consumer GPUs (fully GPU-loaded):**

| Model | GPU | Tokens/sec | TTFT | Viable for Adze? |
|-------|-----|-----------|------|-----------------|
| 8B Q4 | RTX 4060 8GB | 40-60 t/s | <1s | Speed yes, quality no |
| 14B Q4 | RTX 4070 Ti 16GB | 25-40 t/s | 1-2s | Speed borderline, quality marginal |
| 32B Q4 | RTX 4090 24GB | 15-25 t/s | 2-4s | Speed acceptable, quality adequate |
| 70B Q4 | RTX 4090 24GB (partial offload) | 3-8 t/s | 5-15s | Too slow for interactive use |
| 70B Q4 | 2x RTX 4090 or A6000 48GB | 12-18 t/s | 2-4s | Acceptable but rare hardware |

**Critical finding:** The sweet spot for local models on Adze workstations is **Qwen 2.5 32B at Q4_K_M on an RTX 4090**, giving adequate tool-calling quality at acceptable speed. But this is a $1,600+ GPU that must share resources with SOLIDWORKS.

### 4.4 Startup Time and Model Loading

- **Ollama:** First inference after model load takes 5-30 seconds (model must be loaded into VRAM). Subsequent inferences are fast. Ollama keeps models loaded with a configurable keep-alive (default 5 minutes). After timeout, the next request triggers a reload.
- **LM Studio:** Model must be manually loaded via GUI before API is available. Loading a 32B model takes 10-30 seconds. Model stays loaded until manually unloaded or LM Studio is closed.

**Adze implication:** Neither provider guarantees the model is loaded when the user clicks "Run assistant." Cold-start latency of 10-30 seconds is unacceptable for an interactive CAD tool. Mitigation: Adze should ping the local endpoint at add-in startup and display a clear status indicator ("Local model: loading..." / "Local model: ready" / "Local model: not available").

### 4.5 Process Lifecycle

- **Ollama:** Runs as a background service (`ollama serve`). Can be installed as a Windows service. Starts with Windows if configured. Reasonably well-behaved on Windows.
- **LM Studio:** GUI application. Must be running for the API server to be available. No headless/service mode. Closing the window stops the API. Less suitable for "invisible background service" deployment.

**Adze implication:** Ollama is the more operationally suitable choice for production integration. LM Studio is better suited for developer experimentation and testing.

---

## 5. Adze Architecture Impact Assessment

### 5.1 What Requires No Changes

The existing codebase can route to a local model today with zero code changes:

```
SOLIDWORKS_AI_PROVIDER=openai
SOLIDWORKS_AI_OPENAI_ENDPOINT=http://localhost:11434/v1/chat/completions
SOLIDWORKS_AI_OPENAI_API_KEY=ollama
SOLIDWORKS_AI_OPENAI_MODEL=qwen2.5:32b
SOLIDWORKS_AI_ENABLE_MODEL=true
```

The `OpenAIModelClient` will send the request, Ollama will respond in OpenAI format, and `ModelResponseParser` will parse it. If the model returns valid structured JSON matching the broker schema, the hybrid path works. If not, the deterministic fallback catches it.

**This already works for synthesis.** The synthesis prompt asks for plain text, which any model can produce. The quality will vary, but the pipeline is functional.

### 5.2 What Requires Changes for Production Readiness

1. **Provider validation and health checks.** The current `IsUsable()` check validates that an API key and endpoint are configured, but it does not verify the endpoint is reachable or that a model is loaded. Local providers need a health check (GET `/v1/models` or similar) at startup and before each request.

2. **Provider type awareness.** `BrokerModelSettings.IsUsable()` currently only accepts `"openai"` or `"anthropic"` as valid providers. Supporting `"ollama"` and `"lmstudio"` as recognized provider names (that route through the OpenAI client) requires extending `IsUsable()` and `ModelClientFactory`.

3. **Timeout tuning.** Local model inference is slower than cloud APIs. The default 20-second broker timeout and 30-second synthesis timeout may be insufficient for 32B+ models on mid-range hardware. Local providers should use separate, longer default timeouts.

4. **Graceful degradation messaging.** When a local model returns malformed JSON (which will happen more often than with cloud models), the fallback path should indicate "local model response was unusable -- used deterministic planner" rather than a generic failure message.

5. **Model capability detection.** Not all locally-loaded models support tool calling. The provider integration should detect whether the loaded model supports tools (via `/v1/models` metadata or a probe request) and disable the tool-calling path if it does not, routing directly to the synthesis-only path.

### 5.3 What Should NOT Be Built

1. **A separate Ollama-native client using `/api/chat`.** The OpenAI-compatible endpoint is sufficient and avoids maintaining two client implementations for what amounts to the same backend.

2. **Model management UI in the Task Pane.** Adze should not become an Ollama/LM Studio management interface. Model selection, loading, and configuration belong in those tools' own interfaces.

3. **Automatic model downloading.** Adze should not pull multi-gigabyte model files. The user must have Ollama or LM Studio configured independently.

4. **GPU resource negotiation with SOLIDWORKS.** There is no practical way for Adze to coordinate GPU memory allocation with SOLIDWORKS. The user is responsible for choosing a model size that fits their hardware.

---

## 6. Recommendations

### 6.1 v1: Ship as Experimental Text-Synthesis Provider

**What to support:**
- Recognize `"ollama"` and `"lmstudio"` as valid provider names in `BrokerModelSettings`
- Route both through the existing `OpenAIModelClient` (they speak OpenAI format)
- Default endpoint: `http://localhost:11434/v1/chat/completions` for Ollama, `http://localhost:1234/v1/chat/completions` for LM Studio
- Use local providers for the **synthesis pass only** -- the deterministic keyword broker handles tool selection
- Add a startup health check that pings the local endpoint and reports status
- Apply longer default timeouts for local providers (60s broker, 90s synthesis)
- Log a clear trace entry identifying the answer source as `model_ollama` or `model_lmstudio`
- Document recommended models: Qwen 2.5 32B (best quality-to-speed ratio for tool selection), Llama 3.3 70B (best quality if hardware permits), Qwen 2.5 14B (minimum viable for text synthesis only)

**What to explicitly label experimental:**
- Tool calling via local models (agentic loop with local providers)
- Broker planning via local models (structured JSON response)
- Any model below 32B parameters for tool-related tasks

**Feature gate:**
```
SOLIDWORKS_AI_PROVIDER=ollama
SOLIDWORKS_AI_LOCAL_TOOL_CALLING=false  (default; blocks tool_calls path for local providers)
```

### 6.2 v2+: Full Agentic Loop with Local Models

**Prerequisites before promoting local tool calling from experimental to supported:**
1. A validation harness (extending the existing broker evals) that runs against the local model and measures tool-selection accuracy. The model must achieve >= 80% accuracy on the existing 12 broker eval cases.
2. A structured-output validation test that sends the broker planning prompt to the local model and verifies the response parses successfully. The model must achieve >= 90% valid JSON on 20+ test prompts.
3. Hardware profiling data showing that the recommended model runs at >= 10 tokens/second on the target workstation with SOLIDWORKS loaded.

**Implementation work for v2:**
- Add `tools` array to the request body when `LOCAL_TOOL_CALLING=true` and the model is known to support it
- Extend `AgentLoopRunner` (Phase 2) to handle higher failure rates from local models: increase the consecutive-error budget, add retry with temperature jitter on malformed tool calls
- Add model-specific prompt formatting hints (some local models need explicit "You must respond with a tool_call" instructions that cloud models do not)

### 6.3 Minimum Capability Tests Before Enabling Local Models

These tests should run automatically when a local provider is configured, and gate the feature:

#### Gate 1: Endpoint Reachability
```
GET http://localhost:{port}/v1/models
Expected: 200 OK with a JSON response containing at least one model
Blocks: All local model functionality if this fails
```

#### Gate 2: Basic Completion
```
POST /v1/chat/completions
Body: {"model": "{configured_model}", "messages": [{"role": "user", "content": "Reply with exactly: OK"}], "max_tokens": 10}
Expected: Response contains "OK" in choices[0].message.content
Blocks: All local model functionality if this fails
```

#### Gate 3: Structured JSON Output (required for broker planning path)
```
POST /v1/chat/completions
Body: System prompt requesting JSON with specific keys; user prompt with a simple CAD scenario
Expected: Response parses as valid JSON with required keys present
Blocks: Broker planning via local model if this fails (synthesis-only mode)
```

#### Gate 4: Tool Calling Format (required for agentic loop)
```
POST /v1/chat/completions
Body: Messages + tools array with 3 test tools; user prompt that clearly maps to one tool
Expected: Response contains tool_calls array with correct tool name and valid arguments JSON
Blocks: Agentic tool loop via local model if this fails
```

#### Gate 5: Multi-Turn Tool Loop (required for full agent mode)
```
Sequence: Initial request -> model returns tool_call -> send tool result -> model returns final text
Expected: Model correctly uses the tool result in its final answer
Blocks: Multi-turn agent loop via local model if this fails
```

#### Gate 6: Latency Profile
```
Measure time-to-first-token and total completion time for a representative broker prompt
Expected: TTFT < 5 seconds, total completion < 30 seconds
Blocks: Displays a warning if latency exceeds thresholds but does not hard-block
```

### 6.4 Configuration Surface

Add these environment variables for local provider support:

```
# Provider selection
SOLIDWORKS_AI_PROVIDER=ollama|lmstudio|openai|anthropic

# Local provider endpoints (defaults shown)
SOLIDWORKS_AI_OLLAMA_ENDPOINT=http://localhost:11434/v1/chat/completions
SOLIDWORKS_AI_OLLAMA_MODEL=qwen2.5:32b
SOLIDWORKS_AI_LMSTUDIO_ENDPOINT=http://localhost:1234/v1/chat/completions
SOLIDWORKS_AI_LMSTUDIO_MODEL=qwen2.5-32b

# Local-specific controls
SOLIDWORKS_AI_LOCAL_TOOL_CALLING=false        # Enable tool_calls for local models (experimental)
SOLIDWORKS_AI_LOCAL_TIMEOUT_MS=60000          # Higher default for local inference
SOLIDWORKS_AI_LOCAL_SYNTHESIS_TIMEOUT_MS=90000
SOLIDWORKS_AI_LOCAL_HEALTH_CHECK=true         # Ping endpoint at startup
```

### 6.5 Implementation Sizing

| Work Item | Effort | Depends On |
|-----------|--------|------------|
| Extend `BrokerModelSettings` to recognize `ollama`/`lmstudio` providers | 1 hour | Nothing |
| Route `ollama`/`lmstudio` through `OpenAIModelClient` in `ModelClientFactory` | 30 min | Settings change |
| Add default endpoints and longer timeouts for local providers | 30 min | Settings change |
| Health check at startup (ping `/v1/models`) | 2 hours | Settings change |
| Gate tests (Gates 1-6) as a validation script | 4-6 hours | Health check |
| Trace source labels (`model_ollama`, `model_lmstudio`) | 30 min | Factory change |
| Unit tests for local provider configuration | 2 hours | Settings change |
| Documentation for local model setup | 1 hour | All above |
| **Total for v1 experimental support** | **~1-2 sessions** | |

---

## 7. Model Recommendations for Adze Users

### 7.1 Recommended Models (March 2026)

| Use Case | Model | Quantization | Min VRAM | Quality Rating |
|----------|-------|-------------|----------|---------------|
| **Text synthesis only** (deterministic broker) | Qwen 2.5 14B | Q5_K_M | 10 GB | Good |
| **Text synthesis only** (budget hardware) | Llama 3.1 8B | Q6_K | 7 GB | Acceptable |
| **Tool selection + synthesis** (experimental) | Qwen 2.5 32B | Q4_K_M | 20 GB | Adequate |
| **Full agentic loop** (experimental, future) | Llama 3.3 70B | Q4_K_M | 42 GB | Good |
| **Full agentic loop** (experimental, future) | Qwen 2.5 72B | Q4_K_M | 42 GB | Good |

### 7.2 Models to Avoid

- Any model below 7B parameters for any Adze use case
- Llama 3.2 1B/3B -- too small for structured output
- Code-specific models (DeepSeek Coder, CodeLlama) -- not trained for tool use
- Any Q2 or Q3 quantization of models that will be used for tool calling
- Phi-3/Phi-4 for tool calling -- insufficient structured output reliability despite being otherwise capable small models

### 7.3 Why Qwen 2.5 32B Is the Current Sweet Spot

- Strong tool-calling training data in the base model
- 32B parameters at Q4_K_M fits in 20GB VRAM, leaving room for SOLIDWORKS on a 24GB GPU
- Inference speed of 15-25 t/s on RTX 4090 is acceptable for interactive use
- Structured JSON output is reliable enough for the synthesis path
- Multilingual support (relevant for international SOLIDWORKS users)
- Apache 2.0 license -- no commercial usage restrictions

---

## 8. Risk Summary

| Risk | Severity | Mitigation |
|------|----------|------------|
| Local model returns malformed JSON, broker fails | Medium | Deterministic fallback already exists and catches this cleanly |
| GPU memory contention with SOLIDWORKS causes crashes or degraded graphics | High | Document hardware requirements clearly; do not auto-enable local models; health check + latency gate |
| User assumes local model quality equals cloud model quality | Medium | Label as "experimental"; show answer source clearly in UI; document quality expectations |
| Ollama/LM Studio not running when user tries to use it | Low | Health check at startup with clear status messaging |
| Model cold-start adds 10-30s to first request | Medium | Pre-load ping at add-in startup; display loading status |
| Local model ecosystem changes rapidly (model names, API surface) | Low | Adze sends standard OpenAI format; model name is user-configured; minimal coupling |
| Token count inaccuracy from local providers skews usage tracking | Low | Log a warning when usage fields are zero; do not use local token counts for billing/budgeting |

---

## 9. Conclusion

**Offline operation is a real feature, but not a v1 headline feature for the agentic loop.**

The path forward is:
1. **Now:** Extend `BrokerModelSettings` and `ModelClientFactory` to recognize local providers and route them through the existing OpenAI client. This is ~1 session of work and immediately enables text synthesis via local models.
2. **Phase 2 (agentic loop):** Build the agentic loop against cloud models first. Once it is stable, add the gate tests and enable local models as an experimental option for the tool-calling path.
3. **Phase 7 (production hardening):** Based on gate test data from real users, decide whether local models meet the quality bar for supported (non-experimental) status in the agentic loop.

The key insight is that **Adze's existing architecture already handles this gracefully**. The deterministic fallback planner means local models can participate in answer synthesis without needing to handle the hard part (tool selection). The hybrid path was designed for exactly this kind of provider quality variance.

---

# Research: OpenClaw Feasibility for Adze

**Date:** 2026-03-15
**Status:** Complete -- recommendation issued
**Triggered by:** END-GOAL.md Phase 7 item: "OpenClaw integration: Explore whether OpenClaw instances can provide orchestration, routing, or agent infrastructure. Discovery needed."

---

## What OpenClaw Actually Is

OpenClaw is a **developer-workflow orchestration layer for AI coding agents**. It is not a general-purpose agent SDK, embeddable runtime library, or model-routing framework. Its purpose is to coordinate AI development sessions (such as terminal coding agents, IDE copilots, or similar tools) by providing persistent context, memory, and identity files that the orchestrating agent maintains across sessions.

### Evidence from this repo

The project's own `block-protected-files.js` hook identifies these OpenClaw artifacts:

| File | Purpose |
|------|---------|
| `AGENTS.md` | Agent configuration |
| `HEARTBEAT.md` | Heartbeat/liveness config |
| `IDENTITY.md` | Agent identity definition |
| `SOUL.md` | Agent personality/behavioral guidance |
| `TOOLS.md` | Tool usage notes |
| `USER.md` | User profile for the orchestrating agent |
| `memory/` directory | Daily logs and session memory |

The bootstrap prompt explicitly states: *"These belong to the orchestrating agent, not to interactive coding sessions."* This confirms OpenClaw operates **above** the development tool layer -- it is the orchestrator that manages how AI agents interact with a codebase, not something that runs inside a shipped product.

### What OpenClaw provides

- **Persistent agent memory** across coding sessions (daily logs, learned context)
- **Identity and personality files** that shape how an AI coding agent behaves in a specific project
- **Heartbeat/liveness tracking** for long-running agent sessions
- **Session bootstrapping** so a new agent session can resume where the previous one left off

### What OpenClaw does NOT provide

- Model API client implementations (no HTTP clients for OpenAI/Anthropic)
- Provider routing or load balancing
- Tool call dispatch or execution
- Token management or conversation state
- Streaming or rate limiting
- Any runtime SDK, NuGet package, or embeddable library
- Any C# or .NET integration surface
- Any desktop application hosting capability

---

## What Would OpenClaw Replace in Adze?

**Nothing.** There is zero overlap between OpenClaw's capabilities and Adze's runtime architecture.

### Current Adze architecture components and their OpenClaw relevance

| Adze Component | What It Does | OpenClaw Replacement? |
|---------------|-------------|----------------------|
| `HybridBrokerOrchestrator` | Routes between model-backed and deterministic broker paths | No -- OpenClaw has no orchestration runtime |
| `OpenAIModelClient` / `AnthropicMessagesModelClient` | HTTP clients for provider APIs | No -- OpenClaw has no API clients |
| `ModelClientFactory` | Provider selection based on env config | No -- OpenClaw has no provider routing |
| `ContextPromptComposer` | Formats SessionContext into model prompts | No -- OpenClaw has no prompt formatting |
| `GroundingSynthesisService` | Second-pass synthesis over tool results | No -- OpenClaw has no synthesis layer |
| `AgentLoopRunner` (Phase 2) | Iterative tool-call loop | No -- OpenClaw has no agent loop |
| `AgentToolDispatcher` (Phase 2) | Maps model tool calls to grounding tool handlers | No -- OpenClaw has no tool dispatch |
| Trace/progression/recipe persistence | Learning artifacts under `%LOCALAPPDATA%\Adze` | Tangentially similar in concept (both persist state across sessions), but OpenClaw's memory is for AI development agents, not for end-user-facing product features |

---

## Complexity OpenClaw Would Add

If someone attempted to integrate OpenClaw into Adze's runtime:

1. **Wrong abstraction layer.** OpenClaw manages AI coding sessions. Adze needs to manage AI-assisted CAD sessions. These are fundamentally different domains with different state shapes, different lifecycle models, and different users.

2. **No .NET/C# surface.** OpenClaw's artifacts are markdown files read by AI coding tools. There is no SDK, no API, no NuGet package, no programmatic interface. Integrating it would mean inventing a bridge that doesn't exist.

3. **File-based state model.** OpenClaw uses markdown files in the repo root (`IDENTITY.md`, `SOUL.md`, `memory/`) as its state mechanism. Adze uses structured JSON under `%LOCALAPPDATA%\Adze`. These models are incompatible and serve different purposes.

4. **Server-side / repo-local orientation.** OpenClaw assumes it is managing files within a git repository for development purposes. Adze runs as an in-process COM add-in inside SOLIDWORKS on an end user's desktop.

5. **No runtime value.** OpenClaw provides no HTTP clients, no streaming, no token counting, no provider abstraction, no tool dispatch, no conversation state management -- all the things Adze's broker actually needs.

---

## Does OpenClaw Improve Anything Beyond Custom C#?

### Tool routing
No. OpenClaw has no tool routing. Adze's `ToolCatalog` + `AgentToolDispatcher` (planned Phase 2) handle this natively and are tightly coupled to SOLIDWORKS COM tool implementations.

### Memory
Tangentially related concept, but wrong implementation. OpenClaw's memory is markdown files for AI development agents. Adze's Phase 6 memory (per-document memory, user preferences, cross-session recipe promotion) needs structured JSON keyed by document hash, stored locally, and queryable by the broker. These are not the same problem.

### Orchestration
No. OpenClaw orchestrates AI coding sessions. Adze orchestrates AI-assisted CAD operations with safety contracts, undo recording, confirmation UI, and COM thread marshaling. No overlap.

### Provider abstraction
No. OpenClaw has no provider abstraction. Adze already has `ModelClientFactory` with `OpenAIModelClient` and `AnthropicMessagesModelClient`, plus environment-driven configuration for endpoints, models, and API keys.

---

## Should OpenClaw Be an Optional Integration?

OpenClaw is already in use in the correct way: as the **development-time orchestrator** that manages how AI coding agents work on the Adze codebase. The `block-protected-files.js` hook correctly prevents interactive coding sessions from modifying OpenClaw's own state files.

This is the right relationship. OpenClaw helps build Adze. OpenClaw does not run inside Adze.

Treating it as an optional runtime integration would be a category error -- like embedding your CI/CD pipeline inside your shipped product.

---

## Recommendation

**Avoid as runtime dependency. Current usage as development orchestrator is correct.**

### Rationale

1. OpenClaw solves a different problem (AI development session management) than what Adze needs (AI-assisted CAD operation orchestration).
2. There is no API surface, SDK, or embeddable component to integrate.
3. Every capability Adze needs (provider routing, tool dispatch, conversation state, memory, orchestration) is better served by the custom C# implementation that already exists or is planned.
4. OpenClaw is already being used correctly -- as the layer that manages how AI agents contribute to Adze's development. This is its intended purpose.
5. The END-GOAL.md Phase 7 line item should be closed as "evaluated and declined for runtime integration" with a note that the development-time usage is working as intended.

### Action items

- [x] Research completed
- [ ] Update END-GOAL.md discovery table: change "OpenClaw feasibility" status from "Pending" to "Evaluated" with finding: "Development orchestrator, not runtime infrastructure. No integration needed -- already in correct use as dev-time agent coordinator."
- [ ] Remove the Phase 7 bullet about OpenClaw integration, or replace it with a note that the evaluation concluded no runtime integration is warranted

---

## Maturity and Ecosystem Assessment

| Dimension | Assessment |
|-----------|-----------|
| Project maturity | Early/niche -- file-based orchestration for AI coding agents |
| API surface | None (markdown file conventions, no programmatic API) |
| Desktop embedding support | None (designed for repo-local, development-time use) |
| .NET/C# support | None |
| Community/ecosystem | Small -- primarily used by individual developers managing AI coding sessions |
| Relevance to Adze runtime | None |
| Relevance to Adze development | Already in correct use as development orchestrator |

---

# Research: Closed-File Retrieval for SOLIDWORKS Metadata

**Date:** 2026-03-15
**Status:** Research complete
**Purpose:** Determine what SOLIDWORKS file metadata can be read without opening files through full COM automation, and recommend an architecture for project-level indexing

---

## Context

Adze currently captures rich context from the **active open document** via SOLIDWORKS COM inside the in-process add-in (see `SessionContextBuilder.cs`). The data collected includes:

- Document info (type, title, path, active configuration, units, dirty/read-only state)
- Feature tree (name, kind, suppression state)
- Dimensions (name, full name, value, unit source)
- Configurations (names, active flag)
- Custom properties (document-level and configuration-level, with resolution and link status)
- Mates (name, kind, entity count, component names)
- Reference graph (direct and transitive dependencies, broken references)
- Diagnostics (rebuild state, warnings, missing references)
- File properties (name, directory, extension, size, last-write time)

Phase 6 of the end-goal (`END-GOAL.md`) calls for "Retrieval without COM: Index closed SOLIDWORKS file metadata (custom properties, feature names, dimension names) by reading OLE structured storage from the file format directly." This research evaluates which approaches are viable and what data they expose.

---

## 1. OLE Structured Storage / Compound File Binary Format

### How It Works

SOLIDWORKS `.SLDPRT`, `.SLDASM`, and `.SLDDRW` files are **Microsoft OLE Structured Storage** (also called Compound Document / Compound Binary File) containers. This is the same container format used by older `.doc`, `.xls`, and `.ppt` files. The file contains multiple internal "streams" and "storages" organized in a hierarchy, much like a filesystem within a file.

### What Can Be Read

**Custom Properties and Summary Information (HIGH confidence, PROVEN approach):**

OLE Structured Storage files contain standard property sets that Windows itself can read:

| Property Set | OLE Stream | Contents |
|---|---|---|
| Summary Information | `\005SummaryInformation` | Title, subject, author, keywords, comments, template, last author, revision number, creation date, last save date, page count, word count, application name |
| Document Summary Information | `\005DocumentSummaryInformation` | Manager, company, category, plus a "User-Defined Properties" section |
| User-Defined Properties | Part of Document Summary Information | **All custom properties** stored at the document level |

This is the most reliable and well-understood extraction path. The Windows Shell, `StgOpenStorage` / `IPropertySetStorage` Win32 APIs, and multiple managed libraries can read these streams without any SOLIDWORKS dependency.

**What custom properties look like in practice:**
- Standard SOLIDWORKS custom properties (Description, Part Number, Material, Weight, etc.) are stored in the User-Defined property set
- Configuration-specific custom properties are stored differently (see limitations below)
- Linked/computed property values (e.g., `"SW-Material@Part1.SLDPRT"`) store the **expression**, not the resolved value, in the OLE stream

**Thumbnail / Preview Image:**

SOLIDWORKS stores a preview bitmap in the OLE stream. This can be extracted for visual indexing or search result display. The thumbnail is typically stored in the `\001CompObj` or a dedicated preview stream. Windows Explorer uses this for file thumbnail display.

### What Cannot Be Read (or Is Unreliable)

**Feature tree, dimensions, mates, sketch geometry, and model data:**

The actual parametric model data is stored in proprietary binary streams within the OLE container. SOLIDWORKS uses internal stream names like `SwDocMgrTempStorage`, `Contents`, and version-specific binary blobs. These are:

- Undocumented proprietary binary format
- Version-dependent (format changes between SOLIDWORKS releases)
- Not reliably parseable without SOLIDWORKS or the Document Manager API
- Reverse-engineering them would be fragile and unsupported

**Configuration-specific custom properties:**

Configuration-specific properties are stored within the model's proprietary data streams, not in the standard OLE property sets. They require either the Document Manager API or full COM to read.

**Resolved/computed property values:**

Properties that use SOLIDWORKS expressions (e.g., linked to mass, material, or dimension values) store the expression text in OLE, not the resolved value. Resolution requires SOLIDWORKS or Document Manager.

### .NET Implementation for OLE Reading

Several approaches for reading OLE Structured Storage from C#:

**Option A: OpenMcdf (recommended for v1)**

[OpenMcdf](https://github.com/ironfede/openmcdf) is a pure .NET library (no COM dependency) for reading and writing OLE Compound Files. It is:
- MIT-licensed
- Available as a NuGet package (`OpenMcdf`)
- Actively maintained
- Targets .NET Standard / .NET Framework
- Can read any named stream within the OLE container
- Does not require SOLIDWORKS or any Dassault dependency

To read summary/custom properties, you would:
1. Open the file with `CompoundFile.Open(path)`
2. Navigate to the `\005SummaryInformation` and `\005DocumentSummaryInformation` streams
3. Parse the property set binary format (Microsoft's documented OLEPS format)
4. Extract property names and values

**Option B: Win32 COM Interop (StgOpenStorage)**

The Win32 Structured Storage API (`StgOpenStorage`, `IPropertySetStorage`, `IPropertyStorage`) can be called via P/Invoke or COM interop:
- No external NuGet dependency
- Works on any Windows machine
- Well-documented by Microsoft
- Slightly more verbose to set up in C# but extremely reliable
- This is what Windows Explorer itself uses for property display

**Option C: WindowsAPICodePack / Microsoft.WindowsAPICodePack.Shell**

The Windows API Code Pack provides managed wrappers for shell property access, which can read OLE properties. However, this library has had maintenance gaps and may pull in unnecessary dependencies.

### Performance Characteristics

Reading OLE properties is fast. For a typical SOLIDWORKS file:
- File open + property read + close: **1-5 ms per file**
- Scanning 500 files in a project folder: **under 3 seconds**
- No SOLIDWORKS process needed, no COM registration needed
- Files can be read while SOLIDWORKS has them open (shared read access)
- Read-only access eliminates any risk of file corruption

---

## 2. SOLIDWORKS Document Manager API

### What It Is

The SOLIDWORKS Document Manager API (`SolidWorks.Interop.swdocumentmgr.dll`) is a separate, lightweight API provided by Dassault Systemes specifically for reading (and limited writing of) SOLIDWORKS file data **without launching SOLIDWORKS**. It runs out-of-process and does not require a SOLIDWORKS installation to be running.

### License Requirements

This is the critical constraint:

- The Document Manager API requires a **separate license key** obtained from SOLIDWORKS
- The key is a long string that must be compiled into the application or provided at runtime
- To obtain a key, you must:
  1. Have an active SOLIDWORKS subscription/maintenance agreement
  2. Submit a request through the SOLIDWORKS Customer Portal or API Support
  3. Agree to the Document Manager API license terms
  4. The key is typically issued per-application, not per-machine
- The key is **free** for SOLIDWORKS subscription holders but requires an explicit request
- Distribution: the key can be embedded in a redistributable application, but the DLL itself (`SolidWorks.Interop.swdocumentmgr.dll`) has redistribution restrictions
- The interop DLL ships with SOLIDWORKS installations and the SOLIDWORKS SDK

### What Data It Exposes

The Document Manager API provides significantly more data than raw OLE property reading:

| Data Category | Available | Notes |
|---|---|---|
| Custom properties (document-level) | Yes | Full read/write with resolved values |
| Custom properties (per-configuration) | Yes | Full read/write with resolved values |
| Configuration names | Yes | Full enumeration |
| Configuration properties (description, etc.) | Yes | Read access |
| External references / dependencies | Yes | File paths of referenced components |
| Feature count | Partial | Total count available, not full tree |
| Mass properties | Partial | If previously calculated and stored |
| Thumbnail / preview image | Yes | Bitmap extraction |
| Sheet metal data | Partial | Bend table, flat pattern info |
| Dimension names and values | No | Requires full COM |
| Feature tree (names, types, states) | No | Requires full COM |
| Sketch geometry | No | Requires full COM |
| Mate definitions | No | Requires full COM |
| Rebuild state | No | Requires full COM |
| Selection context | No | Requires full COM (and a live UI) |

### API Shape

```csharp
// Initialization requires the license key
SwDMApplication dmApp = new SwDMApplication();
// or via: SwDMClassFactory classFactory = new SwDMClassFactory();
//         SwDMApplication dmApp = classFactory.GetApplication(licenseKey);

SwDMDocument doc = dmApp.GetDocument(filePath, docType, readOnly, out openError);

// Custom properties
SwDMCustomPropertyManager propMgr = doc.GetCustomPropertyManager();
string[] names = propMgr.GetNames();
string value = propMgr.Get(propertyName);

// Configuration-specific properties
string[] configNames = doc.ConfigurationManager.GetConfigurationNames();
SwDMConfiguration config = doc.ConfigurationManager.GetConfigurationByName(configName);
SwDMCustomPropertyManager configPropMgr = config.GetCustomPropertyManager();

// External references
object[] deps = doc.GetAllExternalReferences4(out status, out paths);

doc.CloseDoc();
```

### Performance

- Initialization: ~50-100 ms for the first document
- Per-file property read: ~10-50 ms (slower than raw OLE, faster than full COM)
- Does not launch SOLIDWORKS GUI
- Can handle hundreds of files in seconds
- Separate process — no interference with a running SOLIDWORKS session

### Assembly vs Part vs Drawing Differences

| File Type | Doc Manager Coverage |
|---|---|
| Parts (.SLDPRT) | Custom properties, configurations, external refs, preview |
| Assemblies (.SLDASM) | Custom properties, configurations, external refs (component list), preview |
| Drawings (.SLDDRW) | Custom properties, sheet info, referenced model paths, preview |

For assemblies, the Document Manager can enumerate component references (which parts/sub-assemblies are used) but cannot traverse the mate structure or read assembly-level features.

---

## 3. Third-Party Libraries and Approaches

### SolidDNA / CADSharp

AngelSix's SolidDNA is a .NET wrapper for SOLIDWORKS COM, not for offline reading. It requires a running SOLIDWORKS instance. Not applicable for closed-file retrieval.

### xCAD.NET

xCAD.NET (by xarial) provides a higher-level .NET API for SOLIDWORKS that includes Document Manager support. It wraps both the full COM API and the Document Manager API behind a unified interface. If the project adopts Document Manager, xCAD.NET could reduce boilerplate, but it adds a dependency layer.

### eDrawings API

The eDrawings API can open and render SOLIDWORKS files without a full SOLIDWORKS license, but it is viewer-oriented (geometry display, markup) rather than metadata-oriented. It does not expose custom properties, configurations, or reference data in a programmatically useful way for indexing.

### Direct Binary Parsing

Some community tools and scripts have attempted to parse SOLIDWORKS binary streams directly. This approach is:
- Fragile across SOLIDWORKS versions
- Undocumented and unsupported
- Not recommended for a production product
- Only useful as a last resort for very specific extraction needs

### Windows Search / IFilter

SOLIDWORKS installs an IFilter that allows Windows Search to index SOLIDWORKS file properties. This is the same OLE property data. If Windows Search indexing is enabled on the project folder, the Windows Search API (`ISearchQueryHelper`) can be used to query already-indexed metadata. However:
- Depends on Windows Search service being configured
- Index freshness is not guaranteed
- Limited to the same property set available via OLE
- Adds an external dependency on Windows Search configuration

### PropertySystem / Shell Property Handlers

SOLIDWORKS registers shell property handlers that let Windows Explorer display custom properties in columns. The `IPropertyStore` / Windows Property System API can read these. This is essentially the same data as the OLE property path but accessed through the Windows Shell layer.

---

## 4. Data Accessibility Matrix

This table maps every data slice currently captured by `SessionContextBuilder` against each offline reading approach:

| Data Slice | OLE Properties | Document Manager | Full COM Only |
|---|---|---|---|
| Document type (part/asm/drw) | Yes (from extension) | Yes | Yes |
| Document title | Yes (Summary Info) | Yes | Yes |
| File path, size, dates | Yes (filesystem) | Yes (filesystem) | Yes |
| Active configuration | No | Yes | Yes |
| Configuration names | No | Yes | Yes |
| Configuration count | No | Yes | Yes |
| Custom properties (document) | Yes (values, not resolved) | Yes (resolved values) | Yes |
| Custom properties (per-config) | No | Yes | Yes |
| Feature tree (names, types) | No | No | Yes |
| Feature suppression states | No | No | Yes |
| Dimension names | No | No | Yes |
| Dimension values | No | No | Yes |
| Mates | No | No | Yes |
| Selection context | No | No | Yes (live UI) |
| Reference graph (direct) | No | Yes (partial) | Yes |
| Reference graph (transitive) | No | Yes (partial) | Yes |
| Broken references | No | Partial | Yes |
| Rebuild state | No | No | Yes |
| Units | No | Partial (stored property) | Yes |
| Preview thumbnail | Yes | Yes | Yes |
| Material | Partial (if custom prop) | Yes (if custom prop) | Yes |
| Mass properties | No | Partial (if cached) | Yes |

---

## 5. Limitations by File Type

### Parts (.SLDPRT)

Best coverage. OLE properties work well. Document Manager adds configurations and resolved properties. The main gaps (features, dimensions, sketches) require full COM regardless.

### Assemblies (.SLDASM)

OLE properties work. Document Manager can enumerate component references (which files are used). However:
- Mate structure is COM-only
- Component transforms/positions are COM-only
- Assembly feature tree is COM-only
- BOM-style data (quantity, instance count) requires either Document Manager with careful reference parsing or full COM

### Drawings (.SLDDRW)

Most limited for offline reading:
- OLE properties and Document Manager both work for custom properties
- Sheet names and count available via Document Manager
- View definitions, annotations, dimensions on the drawing sheet are COM-only
- The referenced model path is available via Document Manager
- Drawing-specific metadata (title block fields, revision tables) is mostly COM-only unless stored as custom properties

---

## 6. Semantic Retrieval Feasibility

### What "Semantic Retrieval Over Closed Files" Means for Adze

The goal is for a user to ask a question like "Which part has the 25mm bore?" or "Find all parts with Material = Aluminum 6061" and get results from files that are not currently open.

### What Is Realistic Now

**Keyword/property search (v1 — achievable with OLE only):**
- Search custom properties by name and value across a project folder
- Filter by file type, last modified date, file size
- Match property values against query terms
- Display results with file path, matching properties, and thumbnail

**Structured property search (v1+ — achievable with Document Manager):**
- All of the above, plus:
- Search across configuration-specific properties
- Filter by configuration name
- Enumerate component references to find "which assemblies use this part"
- Resolved property values (computed material, weight, etc.)

**Natural language over indexed metadata (v2 — achievable with model assistance):**
- Index the extracted metadata into a structured JSON store
- When the user asks a natural language question, the broker can search the index
- The model can interpret queries like "heaviest part" or "parts with more than 3 configurations"
- This works well for property-based queries but cannot answer geometry/dimension questions

### What Is Not Realistic Without Full COM

- "Find the part with a 25mm hole" — dimension data is not available offline
- "Which parts have suppressed fillets?" — feature state is not available offline
- "Show me parts with over-defined sketches" — rebuild diagnostics are COM-only
- Any query that requires geometric understanding, spatial relationships, or parametric model traversal

### Practical Assessment

Semantic retrieval over closed files is **realistic and valuable for property-based queries in an early version**. The indexed metadata (custom properties, configuration names, file references, thumbnails) covers a significant share of real-world "find me the right file" queries in engineering workflows. The gap — no feature/dimension/geometry data — is inherent to the file format and cannot be closed without either Document Manager (which still lacks features/dimensions) or batch-opening files through COM.

---

## 7. Architecture Recommendation

### v1: Minimal Viable Retrieval (OLE Properties Only)

**Approach:** Read OLE Structured Storage properties from closed files using OpenMcdf or Win32 `StgOpenStorage` interop. No SOLIDWORKS dependency. No license key needed.

**New project:** `src/Adze.Index` — a pure .NET library with no SOLIDWORKS interop references.

**Capabilities:**
- Scan a user-specified project folder for `.SLDPRT`, `.SLDASM`, `.SLDDRW` files
- Extract from each file:
  - Summary Information (title, author, subject, keywords, comments, creation date, last save date, application name/version)
  - Document Summary Information (company, manager, category)
  - User-Defined Properties (all document-level custom properties — raw values only)
  - File metadata (path, size, extension, last write time)
  - Thumbnail bitmap (for search result display)
- Store extracted metadata as JSON under `%LOCALAPPDATA%\Adze\index\{project-hash}\`
- Provide a query interface for the broker to search by property name, property value, file type, and file path patterns
- Incremental re-indexing: track file last-write timestamps, only re-read changed files
- Full re-index on demand

**Data contract (new, additive):**

```csharp
public sealed class ClosedFileRecord
{
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public string FileType { get; set; } // "part", "assembly", "drawing"
    public long FileSizeBytes { get; set; }
    public DateTimeOffset LastWriteUtc { get; set; }
    public DateTimeOffset? CreatedUtc { get; set; }
    public DateTimeOffset? LastSavedUtc { get; set; }
    public string? Author { get; set; }
    public string? Title { get; set; }
    public string? Subject { get; set; }
    public string? Keywords { get; set; }
    public string? Comments { get; set; }
    public string? Company { get; set; }
    public string? ApplicationVersion { get; set; }
    public Dictionary<string, string> CustomProperties { get; set; }
    public bool HasThumbnail { get; set; }
    public DateTimeOffset IndexedAtUtc { get; set; }
}
```

**New grounding tool:** `search_project_files` — accepts a query (property name/value filter, file type filter, path pattern) and returns matching `ClosedFileRecord` entries. This tool runs against the local index, not against live files, so it is fast and safe.

**Performance target:** Index 500 files in under 5 seconds. Query response under 50 ms.

**Dependencies:** OpenMcdf NuGet package (MIT, pure .NET) or zero-dependency Win32 interop.

**Risk:** Low. Read-only file access. No SOLIDWORKS dependency. No license key. No COM. No network calls.

### v1.5: Document Manager Enhancement (Optional, If License Key Available)

**Approach:** If the user has a SOLIDWORKS Document Manager license key (available free to subscription holders), enhance the index with richer data.

**Additional capabilities beyond v1:**
- Configuration names and count per file
- Configuration-specific custom properties with resolved values
- Resolved document-level property values (expressions evaluated)
- External reference paths (component list for assemblies)
- "Which assemblies contain this part?" reverse-reference queries

**Implementation:** Add a `DocumentManagerIndexEnricher` that runs after the OLE pass when a Document Manager key is configured. Store the enriched data in the same `ClosedFileRecord` with additional fields.

**Configuration:** `SOLIDWORKS_AI_DOCMGR_KEY` environment variable. When absent, fall back to v1 OLE-only behavior. The `EnvironmentInfo.DocumentManagerAvailable` field already exists in the contracts.

**Risk:** Medium. Requires the user to obtain and configure a license key. The interop DLL must be present (ships with SOLIDWORKS). Adds a SOLIDWORKS SDK dependency to the index project.

### v2: Full Project Graph and Natural Language Search

**Approach:** Build a project-level graph from indexed metadata and reference relationships. Enable the broker to answer natural language questions about the project structure.

**Additional capabilities beyond v1.5:**
- Assembly-component graph: "What parts are in Assembly X?" / "Which assemblies use Part Y?"
- Property aggregation: "List all unique materials across this project"
- Change detection: "What files changed since last Tuesday?"
- Stale index warnings when files have been modified since indexing
- Natural language query routing: the broker interprets user questions and generates structured queries against the index
- Cross-file property comparison: "Compare Material property across all parts in folder X"

**New grounding tools:**
- `search_project_files` (enhanced with NL query interpretation)
- `get_project_graph` (returns assembly-component relationships)
- `get_project_statistics` (aggregate property summaries)

### v3: Background Indexing and Watch Mode

**Approach:** File system watcher for automatic re-indexing when files change. Background service that maintains the index without user intervention.

**Additional capabilities:**
- `FileSystemWatcher` on configured project folders
- Debounced re-indexing (wait for file writes to settle before re-reading)
- Index health monitoring in the Status tab
- Configurable folder inclusion/exclusion patterns
- Index size management and retention policy

---

## 8. Implementation Guidance

### Where v1 Fits in the Phase Plan

v1 closed-file retrieval maps to **Phase 6** in the end-goal, but the OLE-only approach is simple enough to begin earlier as a parallel workstream. It has no dependency on the agent loop (Phase 2), write tools (Phase 3), or learning activation (Phase 5). The only prerequisite is a place to wire the results into the broker — which the current tool infrastructure already supports.

Recommended sequencing:
1. Implement `Adze.Index` as a standalone project with unit tests
2. Add `search_project_files` as tool #11 in the catalog
3. Wire into the broker's tool selection logic
4. Add benchmark/eval cases for project search queries

### Build Constraints

- `Adze.Index` must build with the existing MSBuild/Visual Studio toolchain
- No `dotnet` SDK dependency (consistent with project convention)
- NuGet package (OpenMcdf) restored via `tools/nuget.exe`
- Unit tests added to `tests/Adze.Tests` — OLE reading can be tested against small sample `.SLDPRT` files committed to a test fixtures directory

### Boundary Rules

- Closed-file indexing stays **outside** the live COM execution loop (per `BUILD_SPEC.md`)
- The index is a read-only cache of file metadata, not a writable store
- Index data supplements but does not replace live `SessionContext` from open documents
- The broker must clearly distinguish "from index (may be stale)" vs "from live session (current)" in its answers

### What Not to Do

- Do not attempt to parse SOLIDWORKS proprietary binary streams
- Do not require SOLIDWORKS to be running for indexing
- Do not write to SOLIDWORKS files during indexing
- Do not index files on network shares without explicit user opt-in (performance and permission concerns)
- Do not cache resolved property values from OLE reading (they are not resolved — only Document Manager provides resolution)
- Do not block the SOLIDWORKS UI thread during indexing operations

---

## 9. Open Questions for Discovery

| Question | When to Resolve | Notes |
|---|---|---|
| Should the index persist across SOLIDWORKS sessions? | Before v1 implementation | Likely yes — stored under `%LOCALAPPDATA%\Adze\index\` |
| Should thumbnail extraction be part of v1 or deferred? | Before v1 implementation | Thumbnails add visual value but increase index size and implementation scope |
| What is the maximum practical folder size for v1? | During v1 testing | Target: 500 files. Test with 1000+. Set a configurable cap. |
| Should the user configure project folders, or auto-detect from open assembly references? | Before v1 implementation | Start with explicit configuration; auto-detection is a v2 enhancement |
| How should stale index entries be presented to the user? | Before v1 implementation | Likely: show last-indexed timestamp, warn if file is newer than index |
| Is the Document Manager license key obtainable for this project? | Before v1.5 | Requires SOLIDWORKS subscription and API support request |
| Should v1 support SOLIDWORKS 3DEXPERIENCE file formats (3D XML)? | Before v1 implementation | Likely no — the 3DEXPERIENCE platform uses different storage; defer to later |

---

## 10. Summary

| Approach | Data Richness | Complexity | Dependencies | License Cost |
|---|---|---|---|---|
| OLE Structured Storage | Low-Medium (custom props, summary info, thumbnail) | Low | OpenMcdf or Win32 interop | None |
| Document Manager API | Medium (adds configs, resolved props, references) | Medium | `swdocumentmgr.dll`, license key | Free with SW subscription |
| Full COM (batch open) | High (everything) | High | Running SOLIDWORKS instance | SOLIDWORKS license |

**Recommendation:** Start with v1 (OLE-only). It delivers immediate value for property search with zero external dependencies and zero licensing friction. Treat Document Manager as a v1.5 enhancement for users who can obtain the key. Never batch-open files through full COM for indexing — it is too slow, too fragile, and conflicts with the live session.

The v1 approach is sufficient for the most common retrieval queries ("find parts by material", "find parts by author", "which files were modified recently") and establishes the index infrastructure that later phases build on.

---

# Research: Streaming UX Patterns for Agent Reasoning in a Task Pane Sidebar

**Date:** 2026-03-15
**Status:** Research findings
**Scope:** How to present an agent's intermediate reasoning, actions, and confirmations in Adze's 300-400px Task Pane without overwhelming users

---

## 1. Context

Adze is evolving from a single-turn assistant (user clicks "Run assistant", waits, sees answer) toward a multi-turn agentic loop where the model iteratively calls tools, observes results, and decides what to do next (see `discovery-agent-loop-architecture.md`). Future phases add write tools with preview/confirm gates (see `END-GOAL.md` Phase 3-4).

The current Task Pane (`src/Adze.Host/UI/TaskPaneControl.cs`) uses a Windows Forms `UserControl` inside a SOLIDWORKS sidebar. The layout is: request composer at top, answer panel in the middle, and Plan/Status/Tools tabs at the bottom in a `SplitContainer`. During a run, the UI shows "Running..." in a `Label` and blocks interaction until the full `AssistantRunSnapshot` arrives.

This research identifies concrete UI patterns for presenting intermediate agent state in that narrow sidebar, drawn from analysis of existing agentic UIs and adapted for the WinForms/Task Pane constraints.

---

## 2. Existing Agentic UI Patterns

### 2.1 Terminal Coding Agents

Terminal coding agents present activity as a streaming vertical log in the terminal. Key patterns:

- **Tool calls are labeled blocks.** Each tool invocation appears as a distinct block with a header ("Read file.cs", "Search for X", "Edit file.cs") followed by a collapsed or expanded body showing the tool input/output.
- **Thinking is collapsed by default.** Extended reasoning appears as a separate collapsible section, not inline with tool output. Users can expand it if they want to see the chain of thought.
- **Streaming text for the final answer.** The assistant's text response streams token-by-token. Tool call blocks appear atomically (after the call completes), not streamed.
- **Permission gates are inline.** When the agent needs to run a command or edit a file, it shows the proposed action and waits for user approval with a simple yes/no prompt. The approval is a single line, not a modal dialog.
- **Progressive disclosure.** The log is append-only. Earlier steps scroll up naturally. The user sees the current activity at the bottom of the viewport.

**Applicable lesson:** The append-only vertical log maps well to a narrow sidebar. Tool activities are blocks, not inline text. Approval gates are minimal (one line, two buttons).

### 2.2 Cursor (IDE Agent Mode)

Cursor's agent mode operates in a side panel within VS Code. Key patterns:

- **Step pills.** Each agent step appears as a compact pill/chip showing the action type and target (e.g., "Reading file.cs", "Editing utils.ts"). Clicking a pill expands it to show details.
- **Diff view for edits.** When the agent proposes a code change, Cursor shows a standard unified diff view. The user sees red/green lines. Approval is Accept/Reject per file or per hunk.
- **Streaming answer alongside diffs.** The final answer text streams in a chat-like panel while diffs are shown in the editor. The two surfaces are separate.
- **Progress indicator.** A small animated spinner appears next to the current step pill while the API call or tool execution is in progress.
- **Inline errors.** If a tool fails, the error appears as a red-tinted step pill. The agent acknowledges the error in its next response and self-corrects.

**Applicable lesson:** Step pills are an excellent pattern for narrow sidebars. They are compact (one line per step), expandable for details, and visually scannable. The diff view pattern translates to Adze's write preview (before/after values rather than code lines).

### 2.3 GitHub Copilot Workspace (Plan View)

Copilot Workspace presents the full plan before execution. Key patterns:

- **Plan-first UI.** The agent generates a multi-step plan displayed as a checklist. Each step has a short description and a checkbox. The user reviews the plan before any execution begins.
- **Batch or individual approval.** The user can approve the entire plan ("Implement Plan") or toggle individual steps on/off before execution.
- **Live step status.** During execution, each step in the plan transitions through states: pending (gray), running (blue spinner), completed (green check), failed (red X).
- **Specification editing.** The user can edit the plan's natural-language specification before execution. The agent regenerates the plan based on the revised spec.
- **Parallel presentation of files.** Changes to different files are shown side-by-side in a multi-pane layout.

**Applicable lesson:** The plan-first pattern is directly relevant to Adze Phase 4 (multi-step write plans). The checklist-with-status-icons pattern works in a narrow sidebar -- it is just a vertical list of labeled rows with a status icon on each.

### 2.4 Devin (Timeline View)

Devin presents agent activity as a timeline. Key patterns:

- **Event-driven timeline.** Every action (file read, code change, terminal command, browser action) appears as a timestamped event in a vertical timeline. Events are grouped by logical phase.
- **Current activity spotlight.** The topmost or pinned section always shows "What Devin is doing right now" with a short sentence and a spinner.
- **Thought narration.** Devin periodically emits short narrative sentences explaining its reasoning ("I need to check the test file to understand the expected behavior"). These appear as lighter-styled events between tool events.
- **Long-running progress.** For multi-minute operations, Devin shows a progress message that updates in place ("Installing dependencies... 45s", then "Running tests... 12s").
- **Human-in-the-loop pause.** When Devin needs human input, the timeline shows a highlighted pause event with a text input and Send button.

**Applicable lesson:** The "current activity spotlight" is critical for Adze. Users should always be able to glance at the Task Pane and know what the agent is doing in one sentence. The thought narration pattern (short sentences, not full chain-of-thought) works in a sidebar without creating noise.

### 2.5 ChatGPT with Tools (Canvas / Tool Use)

ChatGPT's tool use in the standard chat interface. Key patterns:

- **Collapsed tool invocations.** When ChatGPT calls a tool (web search, code interpreter, DALL-E), a small card appears showing the tool name and a brief label. The card is collapsed by default. Clicking it shows the tool input and output.
- **Sequential reveal.** Tool cards appear in the order they execute. The final text answer streams below the last tool card.
- **No approval gates for read-only tools.** Read-only operations (search, code execution in sandbox) proceed without confirmation. Only actions with external side effects would need confirmation.
- **Thinking indicator.** A subtle animated indicator ("Searching...", "Analyzing...") appears while the tool is executing.

**Applicable lesson:** The collapsed-tool-card pattern is space-efficient and appropriate for read-only grounding tools. Adze should not prompt for confirmation on read-only tool execution.

---

## 3. Recommended Patterns for Adze Task Pane

### 3.1 Current Step Indicator

**Pattern: Single-line status label with verb + target.**

The existing `_runStateLabel` (a `Label` control at the bottom of the composer panel, currently showing "Running...") becomes the primary "what is happening right now" indicator.

**Display format:**

| Agent state | Label text | Example |
|-------------|------------|---------|
| Capturing context | `Capturing session context...` | |
| Calling broker/model | `Planning...` | |
| Executing tool | `Running {tool_name}...` | `Running get_dimensions...` |
| Executing tool N of M | `Running tool {N}/{M}: {tool_name}...` | `Running tool 2/4: get_dimensions...` |
| Synthesizing answer | `Synthesizing answer...` | |
| Waiting for confirmation | `Waiting for your approval...` | |
| Cancelling | `Cancelling...` | |

**WinForms implementation:**

- Use the existing `_runStateLabel` (`Label`, `Dock = DockStyle.Fill`, in the run button row).
- Update it via `PostToUi(() => _runStateLabel.Text = "Running get_dimensions...")` from the background thread at each stage transition.
- Add a subtle animated ellipsis effect. The simplest approach: cycle through ".", "..", "..." on a 500ms `Timer` while `_isRunning` is true. This avoids the need for any custom animation control. Set the label to a fixed-width portion for the ellipsis (e.g., pad the base text and cycle the trailing dots) to prevent layout jitter.

**Why not a progress bar?** The number of total steps is not always known upfront in an agentic loop (the model may decide to call more tools based on results). A progress bar that fills unpredictably is worse than no progress bar. The determinate "tool N of M" pattern works only when the plan is known; in iterative loops, use the indeterminate single-line status.

### 3.2 Activity Log (Step History)

**Pattern: Append-only mini-log in the Plan tab, styled as compact step pills.**

The current `_planBox` (a read-only `TextBox` in the Plan tab) is replaced with a richer control that shows the step history as it unfolds, not just the final plan dump.

**Two implementation options:**

**Option A: Styled TextBox (lowest effort, Phase 2 minimum).**

Keep the existing `TextBox` but append lines as steps complete:

```
[14:32:01] Planning...
[14:32:03] Plan: get_active_document, get_dimensions, get_feature_tree_slice
[14:32:03] Running get_active_document... done (23ms)
[14:32:04] Running get_dimensions... done (45ms, 12 dimensions)
[14:32:04] Running get_feature_tree_slice... done (31ms, 8 features)
[14:32:05] Synthesizing answer...
[14:32:08] Answer ready. Source: model_anthropic (claude-sonnet-4-20250514). 1,247 tokens.
```

Each line is appended in real time. The TextBox auto-scrolls to the bottom (set `SelectionStart = TextLength` then `ScrollToCaret()` after each append). Timestamps give the user a sense of pace.

Advantages: zero new controls, works with the existing `_planBox`, trivial to implement.
Disadvantages: no expand/collapse per step, no icons, monospace-only styling.

**Option B: Custom-drawn step list (Phase 3+, richer UX).**

Replace the Plan tab content with a `Panel` using owner-drawn step items. Each step is a small panel containing:
- A 16x16 status icon (gray circle = pending, blue spinner = running, green check = done, red X = failed)
- A one-line label ("get_dimensions -- 12 dimensions, 45ms")
- Optional expand/collapse to show full tool output

WinForms approach: Use a `FlowLayoutPanel` with `WrapContents = false` and `FlowDirection = TopDown`. Each step is a small `UserControl` or `Panel` with fixed height (24-28px collapsed). Expanding a step increases its height to show the detail text in a nested `TextBox`.

Advantages: compact, visually rich, expandable details without leaving the tab.
Disadvantages: more code, custom layout, potential flickering without careful double-buffering.

**Recommendation:** Start with Option A for Phase 2 (agentic loop). Migrate to Option B when write tool confirmations (Phase 3) demand richer per-step interaction.

### 3.3 Proposed Next Action Preview

**Pattern: Preview card between the activity log and the approval buttons.**

When the agent loop proposes a write operation, the agent loop pauses and the Task Pane shows a preview card. This is the "what will happen next" surface.

**Layout:**

```
+-------------------------------------------+
| Proposed Change                            |
|                                            |
|   Set D1@Sketch1                           |
|   Current: 50.0 mm                         |
|   New:     60.0 mm                         |
|                                            |
|   [Apply]  [Cancel]  [Edit value...]       |
+-------------------------------------------+
```

**WinForms implementation:**

Create a `WritePreviewPanel` -- a `Panel` that is normally invisible (`Visible = false`) and inserted into the answer panel area (or overlaid on top of the answer area). When a write preview arrives:

1. The panel becomes visible.
2. The answer panel dims or scrolls down to make room.
3. The preview panel shows the change description using `Label` controls for "Current" and "New" values.
4. Three `Button` controls: Apply, Cancel, Edit value.

Control details:

| Control | Type | Properties |
|---------|------|------------|
| Preview container | `Panel` | `Dock = DockStyle.Top`, `Height = 120`, `Visible = false`, `BackColor = Color.FromArgb(255, 252, 240)` (warm highlight), `BorderStyle = FixedSingle` |
| Title label | `Label` | `Font = Segoe UI 9.5pt Bold`, `Text = "Proposed Change"` |
| Property name | `Label` | `Font = Segoe UI 9pt`, `ForeColor = Color.FromArgb(86, 96, 108)` |
| Current value | `Label` | `Font = Consolas 9.5pt`, `ForeColor = Color.FromArgb(180, 60, 60)` (muted red for "old") |
| New value | `Label` | `Font = Consolas 9.5pt`, `ForeColor = Color.FromArgb(40, 120, 60)` (muted green for "new") |
| Apply button | `Button` | `FlatStyle = System`, `Width = 70`, `BackColor = default` |
| Cancel button | `Button` | `FlatStyle = System`, `Width = 70` |
| Edit button | `LinkLabel` | `Text = "Edit value..."`, opens an inline `TextBox` replacing the "New" value label |

**Color coding:** Use the red/green convention from diff views. The "current" value is in muted red (not bright -- this is information, not a warning). The "new" value is in muted green. This visual language is universally understood by engineers who use version control.

### 3.4 Waiting-for-Confirmation State

**Pattern: Modal-within-pane -- the Task Pane enters a confirmation mode where the run button changes to "Waiting..." and the preview panel is the only interactive element.**

When the agent proposes a write and waits for confirmation:

1. The `_runButton` text changes to "Waiting for approval..." and is disabled.
2. The `_runStateLabel` reads "Review the proposed change above."
3. The `_requestBox` is disabled (no new input while a confirmation is pending).
4. The `WritePreviewPanel` is the only area with active controls (Apply / Cancel / Edit).
5. A timeout label shows: "Auto-cancel in 2:00" counting down (configurable via `SOLIDWORKS_AI_WRITE_CONFIRM_TIMEOUT_MS`, default 120000ms). If the user does not respond, the agent receives a cancellation and proceeds without the write.

**After the user responds:**

- **Apply:** The preview panel disappears, the run state label updates to "Applying change...", the agent loop resumes.
- **Cancel:** The preview panel disappears, the run state label updates to "Change cancelled. Resuming...", the agent receives a cancellation result and adapts.
- **Edit:** The "New" value label becomes an editable `TextBox`. The user types a value and clicks Apply.

**Signaling mechanism:** The background agent loop thread waits on a `ManualResetEventSlim` (or `TaskCompletionSource` if using async). The UI thread sets the event when the user clicks Apply or Cancel, passing the decision back through a shared `WriteConfirmationResult` object.

```csharp
internal sealed class WriteConfirmationResult
{
    public bool Approved { get; set; }
    public string? ModifiedValue { get; set; }  // non-null if user edited the value
}
```

### 3.5 Partial Findings During Long Inspection Loops

**Pattern: Incremental answer assembly -- tool results appear in the Tools tab as they arrive, and the answer panel shows a growing summary.**

During a multi-turn agentic loop, the user should not stare at a blank answer panel for 10-30 seconds. Two complementary strategies:

**Strategy A: Live Tools tab updates.**

The Tools tab (`_toolsBox`) is updated after each tool completes, not just at the end of the run. Each tool result is appended with a separator:

```
--- get_active_document (23ms) ---
Document: Part1.SLDPRT (part)
Path: C:\SOLIDWORKS\samples\Part1.SLDPRT
Units: millimeters

--- get_dimensions (45ms) ---
12 dimensions found.
D1@Sketch1 = 50.0 mm
D2@Sketch1 = 30.0 mm
...
```

Implementation: After each tool executes in the background loop, call `PostToUi(() => AppendToolResult(toolName, result, elapsed))`. The `AppendToolResult` method appends to `_toolsBox.Text` and scrolls to the bottom.

**Strategy B: Interim answer panel message.**

While tools are still executing, the answer panel shows a placeholder that communicates progress:

```
Inspecting the document...

Found 12 dimensions and 8 features so far. Waiting for rebuild diagnostics before synthesizing the answer.
```

This is updated via `PostToUi` at key milestones (after each tool completes). It is replaced entirely when the final synthesized answer arrives.

Implementation: Add a method `UpdateInterimAnswer(string text)` that sets `_answerBox.Text` while `_isRunning` is true. The final `ApplyAssistantRunSnapshot` call overwrites it.

**Strategy C: Auto-switch to Tools tab during long runs.**

If the run exceeds a threshold (e.g., 5 seconds without completion), automatically switch `_detailsTabs.SelectedTab` to the Tools tab so the user can see results arriving. On completion, switch back to the Plan tab (or leave on Tools if the user manually navigated there).

Implementation: Start a `Timer` at run start. If it fires (5 seconds), check if the user has not manually changed tabs, then switch. Track `_userChangedTabDuringRun` via the `SelectedIndexChanged` handler.

**Recommendation:** Implement Strategy A (live Tools tab) and Strategy B (interim answer message) together for Phase 2. Strategy C (auto-switch) is a polish item.

### 3.6 Error and Recovery Presentation

**Pattern: Inline, calm, actionable -- errors appear as styled entries in the activity log, not modal dialogs or panic-red screens.**

Errors in an agentic CAD assistant fall into four categories:

| Category | Examples | Severity | User action needed? |
|----------|----------|----------|-------------------|
| Tool failure | COM exception reading features, feature tree empty | Low | Usually none -- agent self-corrects |
| API error | HTTP 429 rate limit, 500 server error, timeout | Medium | Possibly retry or check API key |
| COM/host error | SOLIDWORKS not connected, document closed mid-run | High | Reopen document, restart SOLIDWORKS |
| Agent loop error | Max iterations exceeded, model refuses to stop calling tools | Medium | Review partial results |

**Presentation rules:**

1. **Tool failures are not shown prominently in the answer panel.** They appear in the activity log (Plan tab) as a yellow-tinted line: `[14:32:04] get_rebuild_diagnostics failed: No active rebuild state. (Agent will work around this.)`. In the agentic loop, the error is sent back to the model as a `tool_result` with `is_error: true`, and the model adapts. The user does not need to act.

2. **API errors get a single line in the run state label.** Example: `API timeout. Retrying (1/2)...`. If all retries fail: `API unavailable. Falling back to local analysis.`. The answer panel shows the deterministic fallback answer, not an error message.

3. **COM/host errors stop the run and show a recovery message in the answer panel.** Example:

   ```
   The connection to SOLIDWORKS was lost during the inspection.

   This can happen if the document was closed or SOLIDWORKS was
   restarted while the assistant was running.

   To recover:
   - Make sure a document is open in SOLIDWORKS
   - Click "Run assistant" to try again
   ```

   The tone is calm and instructive, not panicked. No stack traces in the answer panel. Stack traces go to `FileLogger.Error` and appear in the Status tab for developers.

4. **Agent loop budget exhaustion shows partial results.** If the loop hits its iteration cap:

   ```
   The assistant gathered partial information but could not complete
   the full analysis within the step limit.

   Based on what was found:
   [partial answer from accumulated tool results]

   Run again with a more specific question to get a complete answer.
   ```

**WinForms specifics for error styling in the activity log (Option B step list):**

Use a `BackColor` tint on the step panel:
- Success: `Color.FromArgb(240, 248, 240)` (very faint green)
- Warning/retried: `Color.FromArgb(255, 248, 230)` (very faint amber)
- Failed (non-fatal): `Color.FromArgb(255, 243, 240)` (very faint red)
- Failed (fatal): `Color.FromArgb(255, 235, 235)` (slightly stronger red)

For the TextBox-based activity log (Option A), prefix lines with status markers:

```
[14:32:04] OK   get_dimensions (45ms, 12 dimensions)
[14:32:04] WARN get_rebuild_diagnostics failed: No active rebuild state
[14:32:05] RETRY API call timed out, retrying (1/2)...
[14:32:08] OK   Synthesis complete (1,247 tokens)
```

### 3.7 Multi-Step Plan Approval UX

**Pattern: Checklist with per-step and batch controls, presented before execution begins.**

This pattern is relevant starting in Phase 4 (advanced writes). When the agent proposes a multi-step write plan:

**Layout:**

```
+-------------------------------------------+
| Agent Plan (3 steps)                       |
|                                            |
| [x] 1. Set D1@Sketch1: 50mm -> 60mm       |
| [x] 2. Set Material to "Aluminum 6061"    |
| [ ] 3. Suppress Fillet1                    |
|                                            |
| [Apply checked (2)]  [Cancel all]          |
|                                            |
| Estimated undo: all steps in one group     |
+-------------------------------------------+
```

**WinForms implementation:**

Use a `CheckedListBox` with `CheckOnClick = true` for the step list. Each item is a one-line description. Below the list, two `Button` controls:

- "Apply checked (N)" -- dynamically updates its text to show the count of checked items.
- "Cancel all" -- cancels the entire plan and sends a cancellation back to the agent.

Below the buttons, a `Label` showing the undo behavior: "All applied steps will be grouped as a single undo operation." This sets expectations.

**Per-step detail expansion:** When the user clicks a step (not the checkbox), show a tooltip or expand a detail section below the list showing the before/after preview for that specific step. This reuses the `WritePreviewPanel` from section 3.3 but parameterized for the selected step.

**Step-at-a-time approval (alternative):** Instead of a batch checklist, present one step at a time:

```
+-------------------------------------------+
| Step 1 of 3                                |
|                                            |
|   Set D1@Sketch1                           |
|   Current: 50.0 mm -> New: 60.0 mm        |
|                                            |
|   [Apply & next]  [Skip]  [Cancel all]    |
+-------------------------------------------+
```

This is simpler and works better when steps have complex previews that need individual attention. The user sees one preview, decides, and moves to the next.

**Recommendation:** Use step-at-a-time for Phase 3 (first write tools, which are individual operations). Use the batch checklist for Phase 4 (when the agent proposes multi-step plans).

### 3.8 Balancing Transparency with Noise

**Pattern: Three-tier disclosure -- glance, scan, dig.**

The fundamental tension: engineers want to know what the agent is doing (trust requires transparency), but they do not want to read every API call (they have CAD work to do). The solution is progressive disclosure at three tiers:

**Tier 1 -- Glance (always visible, zero effort):**
- The `_runStateLabel` shows one sentence: what is happening right now.
- The answer panel shows the final answer or an interim summary.
- This is all a busy user needs.

**Tier 2 -- Scan (one click, the Plan tab):**
- The activity log shows each step with timestamps and one-line summaries.
- Steps are collapsed by default. The user can scan the list in 3-5 seconds and understand the flow.
- Errors and warnings are visually distinct (colored prefix or background tint).

**Tier 3 -- Dig (expand a step, the Tools tab):**
- Expanding a step in the activity log shows the full tool input and output.
- The Tools tab shows the complete raw tool results.
- The Status tab shows the session dashboard, token usage, and diagnostics.
- The file log (`%LOCALAPPDATA%\Adze\logs`) has full trace details.

**What to hide by default:**

| Information | Tier | Rationale |
|-------------|------|-----------|
| Current step name + target | 1 (Glance) | Users need to know the agent is working and what it is doing |
| Step count ("tool 2 of 4") | 1 (Glance) | Gives a sense of progress without detail |
| Each step's result summary ("12 dimensions found") | 2 (Scan) | Useful for trust-building but not essential during a run |
| Full tool output JSON | 3 (Dig) | Only needed for debugging or verification |
| API request/response bodies | 3 (Dig, log file) | Developer-only information |
| Token usage per call | 2 (Scan, in activity log) | Power users care about cost; it should not dominate the UI |
| Cumulative session tokens | 1 (Glance, in run state label after completion) | Users should know their spend at a glance |
| Model reasoning / chain-of-thought | 3 (Dig, expand in activity log) | Interesting but noisy; collapse by default |
| Error details and stack traces | 3 (Dig, Status tab and log file) | Never show stack traces in Tier 1 or 2 |

**What to always surface (never hide):**

- The fact that the agent is running (vs. idle).
- The fact that the agent is waiting for user input (confirmation gate).
- Fatal errors that require user action (document closed, SOLIDWORKS disconnected).
- The answer source (model vs. deterministic fallback) and token count.

---

## 4. Phased Implementation Recommendations

### Phase 2 Additions (Agentic Loop)

These changes coincide with the `discovery-agent-loop-architecture.md` implementation.

**4.1 Granular run state updates.**

Modify the agent loop runner to emit progress callbacks that the UI thread receives via `PostToUi`. Each callback carries a `RunProgressUpdate` value object:

```csharp
internal sealed class RunProgressUpdate
{
    public string StatusText { get; set; } = string.Empty;      // "Running get_dimensions..."
    public string? ToolName { get; set; }                        // "get_dimensions" or null
    public int? StepIndex { get; set; }                          // 1-based, or null if unknown
    public int? StepCount { get; set; }                          // total planned steps, or null
    public string? InterimAnswerText { get; set; }               // partial answer for the answer panel
    public string? ActivityLogLine { get; set; }                 // append to Plan tab log
    public string? ToolsTabAppend { get; set; }                  // append to Tools tab
}
```

The background thread calls `PostToUi(() => ApplyProgressUpdate(update))` at each transition. The `ApplyProgressUpdate` method updates `_runStateLabel`, appends to `_planBox`, appends to `_toolsBox`, and optionally updates `_answerBox`.

Effort: small -- it is plumbing from the loop runner through `HostState` to `TaskPaneControl`.

**4.2 Animated ellipsis on run state label.**

Add a `Timer` (250ms interval) that cycles the trailing dots on `_runStateLabel.Text` while `_isRunning`. Store the base text separately from the animated suffix.

```csharp
private string _runStateBaseText = "Ready.";
private int _ellipsisTick;

// In the ellipsis timer tick:
if (_isRunning)
{
    int dots = (_ellipsisTick++ % 3) + 1;
    _runStateLabel.Text = _runStateBaseText + new string('.', dots);
}
```

Effort: trivial.

**4.3 Live Tools tab updates.**

Change `_toolsBox` from write-once-at-end to append-as-tools-complete. Use a helper:

```csharp
private void AppendToolResult(string toolName, string resultSummary, long elapsedMs)
{
    string separator = _toolsBox.TextLength > 0
        ? Environment.NewLine + Environment.NewLine
        : string.Empty;
    string entry = separator +
        "--- " + toolName + " (" + elapsedMs + "ms) ---" +
        Environment.NewLine +
        resultSummary;
    _toolsBox.AppendText(entry);
}
```

`TextBox.AppendText` is efficient and auto-scrolls.

Effort: small.

**4.4 Cancellation button.**

When `_isRunning`, change `_runButton.Text` to "Cancel" and wire its click to set a `CancellationTokenSource`. On cancellation, the agent loop breaks, and the UI shows partial results.

Effort: small (the button toggle is trivial; the cancellation plumbing is part of the agent loop implementation).

### Phase 3 Additions (First Write Tools)

**4.5 Write preview panel.**

Build the `WritePreviewPanel` described in section 3.3. It is a `Panel` with `Visible = false` by default, inserted into `answerPanel` (above the answer text box, below the title label). When the agent loop sends a write preview, call `PostToUi(() => ShowWritePreview(preview))`.

Key properties:

| Control | WinForms Type | Key Properties |
|---------|---------------|----------------|
| Preview container | `Panel` | `Dock = DockStyle.Top`, `Height = 120`, `Visible = false`, `BackColor = Color.FromArgb(255, 252, 240)`, `Padding = new Padding(12, 10, 12, 10)` |
| Title | `Label` | `Dock = DockStyle.Top`, `Font = Segoe UI 9.5pt Bold`, `Height = 22` |
| Property name | `Label` | `Dock = DockStyle.Top`, `Font = Segoe UI 9pt`, `Height = 20` |
| Current value | `Label` | `Dock = DockStyle.Top`, `Font = Consolas 9.5pt`, `ForeColor = Color.FromArgb(180, 60, 60)`, `Height = 20` |
| New value | `Label` | `Dock = DockStyle.Top`, `Font = Consolas 9.5pt`, `ForeColor = Color.FromArgb(40, 120, 60)`, `Height = 20` |
| Button row | `FlowLayoutPanel` | `Dock = DockStyle.Top`, `Height = 32`, `FlowDirection = LeftToRight` |
| Apply button | `Button` | `Width = 70`, `FlatStyle = System` |
| Cancel button | `Button` | `Width = 70`, `FlatStyle = System` |
| Edit link | `LinkLabel` | `Text = "Edit value..."` |

Effort: moderate (new panel, signaling to background thread, timeout logic).

**4.6 Confirmation state machine.**

The Task Pane needs a state machine to manage the confirmation lifecycle:

```
Idle -> Running -> (WaitingForConfirmation -> Running) -> Completed
                                           \-> Running (cancelled)
```

States:
- `Idle`: all controls enabled, run button says "Run assistant".
- `Running`: request box disabled, run button says "Cancel", status label shows current step.
- `WaitingForConfirmation`: preview panel visible, run button says "Waiting...", only preview panel buttons are interactive.
- `Completed`: preview panel hidden, run button says "Run assistant", answer shows final result.

Implement as an enum field `_paneState` with a `TransitionTo(PaneState)` method that enables/disables the correct controls.

Effort: moderate.

### Phase 4 Additions (Multi-Step Plans)

**4.7 Plan review panel.**

Replace the write preview panel with a plan review panel when the agent proposes multiple steps. Use a `CheckedListBox` for the step list with Apply/Cancel buttons.

The `CheckedListBox` is already a proven control in the codebase (proposed in `discovery-clarification-ui.md` for the scope axis). The same control works here with different data.

**4.8 Step-at-a-time mode.**

For simpler initial implementation, present steps one at a time using the same `WritePreviewPanel` from Phase 3 with a "Step N of M" header and "Apply & Next" / "Skip" / "Cancel All" buttons.

---

## 5. Specific WinForms Control Recommendations Summary

| Need | Recommended Control | Alternative | Notes |
|------|-------------------|-------------|-------|
| Current step indicator | `Label` (existing `_runStateLabel`) | -- | Animated ellipsis via Timer |
| Activity log (Phase 2) | `TextBox` (existing `_planBox`, append mode) | `RichTextBox` for colored prefixes | RichTextBox is heavier but supports per-line coloring |
| Activity log (Phase 3+) | `FlowLayoutPanel` with child `Panel` items | `ListView` in Details view | FlowLayoutPanel is simpler; ListView supports icons natively |
| Write preview | `Panel` with child Labels + Buttons | -- | Owner-drawn for more visual polish |
| Multi-step checklist | `CheckedListBox` | `ListView` with checkboxes | CheckedListBox is simpler; ListView adds column flexibility |
| Timeout countdown | `Label` with `Timer` | -- | Update every 1s |
| Step status icons | `PictureBox` (16x16) in step panel | `ImageList` on `ListView` | Draw from embedded resources |
| Expand/collapse step | `LinkLabel` toggle + `Panel.Visible` | `TreeView` with node expand | TreeView is overkill for a flat list |
| Error tinting (TextBox log) | Prefix markers (`OK`, `WARN`, `ERR`) | `RichTextBox` with colored lines | RichTextBox allows `SelectionColor` per line |

### RichTextBox vs TextBox for the Activity Log

The standard `TextBox` cannot color individual lines. For Phase 2, this is acceptable -- prefix markers (`OK`, `WARN`) provide enough visual distinction in a monospace font.

For Phase 3+, consider migrating the Plan tab to a `RichTextBox`:
- `RichTextBox` supports `SelectionColor` and `SelectionBackColor` per line.
- Append a line, select it, set its color, then deselect. This is the standard pattern for colored logging in WinForms.
- `RichTextBox` is heavier than `TextBox` (it loads the Windows Rich Edit control). In a Task Pane with modest text volumes (dozens of lines, not thousands), this is not a concern.
- Set `DetectUrls = false` to avoid the RichTextBox's default URL-detection behavior.
- Set `ShortcutsEnabled = false` if you want to prevent the user from accidentally formatting the read-only content.

Example append-with-color helper:

```csharp
private void AppendColoredLine(RichTextBox target, string text, Color foreColor)
{
    target.SelectionStart = target.TextLength;
    target.SelectionLength = 0;
    target.SelectionColor = foreColor;
    target.AppendText(text + Environment.NewLine);
    target.SelectionColor = target.ForeColor;  // reset
    target.ScrollToCaret();
}
```

---

## 6. Anti-Patterns to Avoid

### 6.1 Modal Dialogs for Confirmations

Do not use `MessageBox.Show()` or custom `Form` dialogs for write confirmations. Modal dialogs steal focus from SOLIDWORKS and feel disruptive in a sidebar workflow. All confirmations should be inline within the Task Pane.

### 6.2 Full Chain-of-Thought Display

Do not stream the model's internal reasoning to the answer panel. Engineering users do not want to read "I should check the dimensions first because the user asked about sizing." That information belongs in the Dig tier (collapsed in the activity log, or in the log file). The answer panel shows answers and interim summaries, not reasoning.

### 6.3 Flashing or Pulsing Animations

WinForms does not have smooth animation support without custom painting. Avoid attempting CSS-style pulse effects on panels or labels. The animated ellipsis (section 4.2) is sufficient for indicating activity. Anything more complex will look janky in the Win32 rendering pipeline.

### 6.4 Auto-Expanding the Task Pane

Do not attempt to programmatically resize the SOLIDWORKS Task Pane panel width. The user controls the sidebar width. Design for the minimum expected width (300px) and let content reflow.

### 6.5 Sound or System Tray Notifications

Do not play sounds or show toast notifications when runs complete. The Task Pane is a passive sidebar. The user will check it when they are ready.

### 6.6 Progress Bars for Indeterminate Work

Do not show a `ProgressBar` in `Marquee` mode for API calls. It communicates "something is happening" but conveys no information the status label does not already provide. It also takes vertical space in a narrow layout. The animated ellipsis on the status label is more compact and equally informative.

---

## 7. Open Questions

1. **Should the Tools tab show raw JSON or formatted summaries?** Current implementation shows formatted text. For Tier 3 transparency, consider a toggle ("Raw / Formatted") at the top of the Tools tab.

2. **Should the activity log persist across runs?** The current Plan tab is cleared on each run. For iterative workflows, keeping a session-long log (with run separators) would let the user see the history. Risk: the log grows unbounded. Mitigation: cap at the last 5 runs or 500 lines and truncate older entries.

3. **Should the agent's thought narration (Devin-style) be included in the activity log?** If the model returns reasoning text between tool calls, it could be shown as a lighter-styled line in the log. This aids transparency but adds noise. Recommendation: include it, but style it distinctly (italic or gray text) so the user can skip it visually.

4. **Should the answer panel stream token-by-token during synthesis?** Streaming requires SSE or chunked HTTP reading, which adds complexity to the `.NET 4.8` `HttpWebRequest` client. The `discovery-agent-loop-architecture.md` and `END-GOAL.md` both defer streaming to Phase 7. For now, show the interim placeholder ("Synthesizing answer...") and then replace with the full answer.

5. **Should write confirmations have a keyboard shortcut?** Recommendation: yes. `Enter` for Apply, `Escape` for Cancel. These are standard Windows conventions and work without the user moving to the mouse.

---

## 8. Key Takeaways

1. **The status label is the most important surface.** One sentence, always visible, always current. Invest in getting this right first.

2. **Append-only activity logs beat progress bars.** They give a sense of pace, build trust, and provide an audit trail. Start with a simple TextBox; upgrade to RichTextBox or FlowLayoutPanel later.

3. **Write confirmations must be inline, not modal.** A preview panel in the Task Pane with Apply/Cancel buttons. Never steal focus from SOLIDWORKS with a dialog box.

4. **Three-tier disclosure (glance/scan/dig) resolves the transparency-vs-noise tension.** Most users will never leave Tier 1. Power users will scan Tier 2. Developers will dig into Tier 3. Design all three, but do not force Tier 2 or 3 on Tier 1 users.

5. **Errors should be calm and actionable.** Tool failures are handled by the agent. API failures trigger fallback. Only host disconnection requires user action, and the recovery guidance should read like a helpful note, not a crash report.

6. **Phase the investment.** Simple TextBox log + granular status label for Phase 2. Write preview panel for Phase 3. Rich step list and batch plan UI for Phase 4. Do not build the Phase 4 UI before Phase 2 is working.
