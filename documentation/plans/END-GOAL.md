# END-GOAL: Full Agentic SOLIDWORKS Assistant

**Date:** 2026-03-15
**Status:** Vision document — phases require discovery and validation
**Purpose:** Define the complete path from current grounded alpha to the fully realized product vision

---

## The Vision

Adze becomes a fully agentic AI assistant that lives inside SOLIDWORKS as a native partner. It understands the live CAD session, asks intelligent questions before acting, inspects and modifies the model through governed tools, learns from interactions, and progressively earns trust to handle more complex operations. It feels like a knowledgeable colleague who happens to have perfect recall of every dimension, mate, and reference in your assembly.

## What Exists Today (v0.1.0)

- Native SOLIDWORKS add-in with Task Pane UI
- 10 live read-only grounding tools (inspection surface complete)
- Hybrid broker with OpenAI/Anthropic/OpenRouter provider routing
- Model-backed answer synthesis with deterministic fallback
- Token usage monitoring and session tracking
- Trace/progression/recipe/achievement persistence
- 175 unit tests + 6 live provider smoke tests
- Beta install/uninstall/packaging workflow
- Launcher interruption detection and recovery

## What Needs to Exist (End State)

### Pillar 1: Interactive Clarification

**Current:** User types prompt → model answers immediately.
**End state:** User states intent → assistant asks contextual clarifying questions populated with live session data → user refines via UI controls → enriched prompt goes to model.

**Components:**
- Pre-prompt clarification panel in Task Pane
- Dynamic question generation from SessionContext (real feature names, real dimensions, real file names)
- Intent classification: Inspect / Modify / Explain / Compare / Diagnose
- Scope narrowing: which feature, which dimension, which configuration
- Output mode selection: brief answer / detailed analysis / file modification
- Mid-execution clarification: agent asks follow-up questions during multi-step plans

### Pillar 2: Agentic Tool Loop

**Current:** Single-turn. Broker recommends tools → host executes all → synthesis generates answer.
**End state:** Multi-step, iterative, goal-directed. Model drives the loop.

**Components:**
- API-native tool use (Anthropic tool_use / OpenAI function_calling)
- Tool definitions as API schemas (all 10 read tools + future write tools)
- Agent loop: model calls tool → host executes → result returned to model → model decides next action
- Conversation state: message history maintained across turns within a session
- Goal tracking: agent knows when a multi-step plan is complete
- Streaming: partial results shown as the agent works (not all-or-nothing)
- Cancellation: user can stop a multi-step plan mid-execution

### Pillar 3: Write Tools

**Current:** Read-only. 10 inspection tools, no modification capability.
**End state:** Governed write tools with preview/apply/verify/rollback.

**First write tools (reversible, single-document):**
1. `set_dimension_value` — change a dimension value
2. `set_custom_property` — add/modify/delete a custom property
3. `suppress_feature` / `unsuppress_feature` — toggle feature suppression
4. `set_configuration_parameter` — modify configuration-specific values
5. `add_note` — add a text note to the model

**Later write tools (more complex):**
6. `create_sketch` — start a new sketch on a reference plane/face
7. `add_extrusion` — create a boss or cut extrude from a sketch
8. `add_mate` — create an assembly mate between components
9. `insert_component` — add a part to an assembly
10. `create_drawing_view` — generate a drawing view from a model

**Safety contract for ALL write tools:**
- **Preview:** Show the user exactly what will change before execution
- **Apply:** Execute the COM operation
- **Verify:** Re-read the affected data to confirm the change took effect
- **Rollback:** Undo if verification fails or user requests rollback
- **Trace:** Record every write operation with before/after state

### Pillar 4: Confirmation and Preview UI

**Current:** Answer-only display. No interactive confirmation flow.
**End state:** Rich confirmation UI for write operations.

**Components:**
- Preview panel: "I'm about to change D1@Sketch1 from 50mm to 75mm"
- Before/after comparison view
- Apply / Cancel / Modify buttons
- Multi-step plan display: show all planned steps, let user approve individually or batch
- Undo button: rollback the last write operation
- History panel: timeline of all modifications made this session

### Pillar 5: Learning and Trust Progression

**Current:** Trace capture, recipe candidates, achievements exist but are passive.
**End state:** Active learning system that surfaces insights and earns capabilities.

**Components:**
- Successful multi-step workflows captured as recipe candidates
- Reviewed and approved recipes become one-click repeatable workflows
- Achievement system tracks mastery areas (dimensioning, assemblies, diagnostics)
- Trust tiers gate capability:
  - Tier 0 (Baseline): Read-only inspection
  - Tier 1 (Assisted): Simple reversible writes with confirmation
  - Tier 2 (Reviewed): Complex writes, multi-step modifications
  - Tier 3 (Trusted): Autonomous operations within reviewed recipe bounds
- Exploration percentage tracks how much of the tool surface has been exercised
- Session memory: agent remembers what it learned about this specific model

### Pillar 6: Multi-Session Memory and Context

**Current:** Each assistant run is independent. No memory across sessions.
**End state:** Agent remembers user preferences, model history, and learned patterns.

**Components:**
- Per-document memory: what the agent learned about this specific file
- Per-user preferences: verbosity, focus areas, common workflows
- Cross-session recipe promotion: patterns that work repeatedly get promoted
- Project context: understanding of multi-file assemblies and their relationships
- Retrieval: search across closed files for relevant context (without opening in COM)

### Pillar 7: Infrastructure and Integration

**Current:** Direct HTTP to OpenAI/Anthropic/OpenRouter. No agent framework.
**End state:** Production-grade agent infrastructure.

**Components:**
- Agent loop with proper error handling, timeout, and cancellation
- Conversation history management (token-aware truncation)
- Cost tracking and budget controls (per-session, per-day limits)
- Support for local models (Ollama, LM Studio) as additional providers
- OpenClaw integration (if applicable — explore for orchestration/agent infrastructure)
- Claude Agent SDK patterns (ported to C# or used via interop)
- Telemetry and diagnostics for agent behavior analysis
- Rate limiting and retry logic for API calls

---

## Phase Sequence

### Phase 1: Clarification UI + Conversation State
**Prerequisite:** None (current baseline is sufficient)
**Deliverables:**
- Pre-prompt clarification controls in Task Pane
- Dynamic question population from SessionContext
- Conversation history in HostState
- Follow-up turn support in the broker

### Phase 2: Native Tool Use (Agentic Loop)
**Prerequisite:** Phase 1 (conversation state)
**Deliverables:**
- Tool schemas in API tool use format
- Extended model clients for tool use messages
- Agent loop: tool call → execute → return result → iterate
- Streaming partial results to Task Pane

### Phase 3: First Write Tools
**Prerequisite:** Phase 2 (agent loop for iterative execution)
**Deliverables:**
- Preview/apply/verify/rollback contract
- `set_dimension_value` implementation
- `set_custom_property` implementation
- `suppress_feature` / `unsuppress_feature`
- Confirmation UI in Task Pane
- Write operation tracing

### Phase 4: Advanced Write Tools + Multi-Step Plans
**Prerequisite:** Phase 3 (basic writes proven safe)
**Deliverables:**
- Sketch creation and feature creation tools
- Assembly mate and component insertion tools
- Multi-step plan display and approval UI
- Undo/rollback history

### Phase 5: Learning System Activation
**Prerequisite:** Phase 3 (write operations generate meaningful recipes)
**Deliverables:**
- Recipe promotion workflow
- Achievement tracking driven by real usage
- Trust tier progression
- Recipe-suggested workflows in Task Pane

### Phase 6: Memory and Retrieval
**Prerequisite:** Phase 2 (conversation state infrastructure)
**Deliverables:**
- Per-document and per-user memory persistence
- Closed-file indexing for retrieval
- Cross-session context continuity
- Project-level understanding

### Phase 7: Production Hardening
**Prerequisite:** All prior phases stable
**Deliverables:**
- Cost controls and budget management
- Local model support
- Advanced telemetry
- OpenClaw / agent framework integration exploration
- Performance optimization for large assemblies

---

## Open Questions Requiring Discovery

1. **API Tool Use Format:** What's the exact schema format for Anthropic tool_use vs OpenAI function_calling? How do we abstract across both?
2. **COM Write Safety:** What are the actual rollback mechanisms available through the SOLIDWORKS COM API? Can we undo arbitrary operations?
3. **Agent Loop Architecture:** How should the C# host manage a multi-turn agent loop without blocking the UI thread? What's the threading model?
4. **Streaming:** Can we stream partial agent responses to the Task Pane during multi-step execution?
5. **Token Management:** How do we manage conversation history length as sessions get long? What's the truncation strategy?
6. **OpenClaw Feasibility:** Can OpenClaw provide orchestration infrastructure for the agent loop?
7. **Local Model Support:** What's needed to support Ollama/LM Studio as providers alongside cloud APIs?
8. **Retrieval Architecture:** How do we index closed SOLIDWORKS files without opening them in COM? Can we read metadata from the file format directly?

---

## Success Criteria

The end-goal is reached when a SOLIDWORKS user can:

1. Open a part and say "Make the base 10mm wider"
2. See the agent ask "Do you mean D1@Sketch1 (currently 50mm → 60mm)?"
3. Click "Yes" on the preview
4. Watch the dimension update in real-time
5. See the agent verify the change and log it
6. Later say "Actually, undo that" and have it restored
7. Have the agent remember this pattern for next time
8. Trust the agent enough to batch-approve a multi-step modification plan

That's the dream. Everything above is the path to get there.
