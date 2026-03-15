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
