# END-GOAL-FINAL: Full Agentic SOLIDWORKS Assistant

**Date:** 2026-03-15  
**Status:** Final curated architecture and implementation target  
**Purpose:** Lock the end-state vision, the required architecture, the phase order, and the key decisions that were previously still open.

---

## 1. Executive summary

Adze should become a **host-governed, model-driven, bounded agent** inside SOLIDWORKS.

That means:
- the **model decides the next read/analysis step** within a controlled tool surface,
- the **host owns safety, permissions, threading, snapshots, confirmations, verification, and rollback**,
- the **UI makes intent, progress, and pending risk legible**,
- and the system **learns only from verified outcomes**, not from raw interaction noise.

The final shape is **not** “better prompting.” It is:

**clarification UI + iterative tool loop + governed writes + state snapshots/diffs + memory + evals + observability**.

The architecture is now stable enough to build against.

---

## 2. What is now decided

The major open questions from the original vision are now closed.

### Closed decisions

1. **Runtime model**
   - Build a **custom C# host-managed agent runtime** inside the add-in.
   - Do **not** depend on an external runtime framework to own the loop.
   - The host remains the source of truth for safety, policy, threading, and undo behavior.

2. **Provider abstraction**
   - Support **two protocol families**, not many bespoke ones:
     - **OpenAI-compatible**: OpenAI, OpenRouter, Ollama, LM Studio
     - **Anthropic-native**: Messages/tool-use path when used directly
   - OpenRouter is the easiest unified cloud path, but the abstraction should not hard-code it.

3. **Threading model**
   - **All SOLIDWORKS COM calls stay on the UI/STA thread.**
   - The agent loop, HTTP calls, schema handling, reasoning turns, and pure tool processing run on a background thread.
   - Any mid-loop fresh COM capture must marshal back to the UI thread explicitly.

4. **First-wave write tools**
   - First safe write wave is:
     1. `set_custom_property`
     2. `set_dimension_value`
     3. `suppress_feature` / `unsuppress_feature`
     4. optional `rename_object` if desired
   - High-risk creation tools stay later.

5. **OpenClaw**
   - OpenClaw is **not** a runtime dependency.
   - It may remain useful as a **development-time orchestration environment**, but not as shipped product infrastructure.

6. **Closed-file retrieval**
   - Early retrieval is realistic for **property/index metadata**, not feature/dimension/geometry understanding.
   - v1 closed-file retrieval should use **OLE structured storage**.
   - Document Manager enhancement is optional when a license key is available.

7. **Local models**
   - Local models are a real feature, but **not a v1 headline dependency for the agent loop**.
   - Cloud models should prove the loop first.
   - Local tool-calling support is experimental and hardware-sensitive.

8. **Streaming**
   - Do not make tool-call streaming a prerequisite.
   - Stream final text later; tool turns can remain buffered initially.

---

## 3. Cleaner conceptual organization

The final framework should be organized into **three layers plus governance**, not one blended list.

### A. Core cognitive steps

These are reusable thinking primitives:
- **Orient / Frame** — establish intent, context, constraints, policy, scope
- **Observe** — gather signals from the active model/session
- **Compress** — reduce noise and summarize state
- **Visualize / Externalize** — present structure in UI-friendly form
- **Cross-analyze** — compare configurations, features, files, or cases
- **Pattern extract** — identify repeatable structure
- **Decide / Prioritize** — choose the next action or query
- **Apply** — execute the chosen action
- **Verify** — check whether the action worked
- **Validate feedback** — confirm the measurement itself is trustworthy
- **Codify** — promote stable lessons into rules/recipes/policies
- **Update memory/policy** — store only what passed trust thresholds

### B. Operational control loop

This is the runtime loop the product should feel like:

**Orient → Observe → Decide → Act → Verify → Recover/Repeat**

This is the actual loop that should drive the host-managed agent.

### C. Learning loop

This runs after verified action and should not be treated as a peer of the main runtime loop:

**Pattern extraction → Rule codification → Feedback validation → Memory/policy update**

### D. Meta-learning / governance loop

This runs periodically, not every turn:
- Are the goals still right?
- Are the permissions right?
- Are the evals measuring the right outcomes?
- Are we trusting the system too early?
- Are recipes actually safe and reusable?

---

## 4. Product definition

The end-state product is a **native SOLIDWORKS Task Pane assistant** that can:
- understand the active CAD session,
- ask contextual clarifying questions before acting,
- inspect the model through structured read tools,
- propose bounded writes with previews,
- apply approved edits with undo grouping,
- verify outcomes with fresh state reads,
- surface progress and reasoning state in understandable UI,
- and remember safe patterns across sessions.

It should feel like a knowledgeable teammate, but it must operate with stricter safety than a teammate.

---

## 5. Architecture layers

### Layer 1 — Interaction and clarification

Responsibilities:
- request box
- pre-prompt clarification controls
- progress/status display
- write-preview panel
- plan review panel
- session history / recent action display
- undo/history surface

Required behaviors:
- use **live SessionContext data** to populate scope choices,
- keep the panel lightweight and collapsible,
- expose current step, next step, and waiting-for-confirmation clearly,
- keep the user in control of hard writes.

### Layer 2 — Agent runtime

Responsibilities:
- manage iterative model turns,
- hold normalized conversation state,
- dispatch tools,
- enforce iteration/error/timeout budgets,
- pause for approvals,
- resume after approvals,
- terminate cleanly with a final response or fallback.

Design rule:
- the model may choose the **next bounded step**,
- the host decides **whether that step is allowed, when it runs, and how it is verified**.

### Layer 3 — Tool and capability registry

Responsibilities:
- describe tools to the model,
- map tool names to executors,
- attach capability metadata,
- classify tools by permission/risk tier,
- validate tool arguments before execution,
- normalize tool outputs.

Tools should be:
- narrow,
- composable,
- strongly named,
- safe to verify,
- stable over time.

### Layer 4 — SOLIDWORKS integration

Responsibilities:
- capture session state from COM,
- execute UI-thread-only operations,
- manage write previews and actual writes,
- record undo groups,
- rebuild when required,
- re-read affected state for verification.

Non-negotiable rule:
- **No SOLIDWORKS COM access from background threads.**

### Layer 5 — State snapshots, diffs, and verification

Responsibilities:
- capture before-state,
- capture after-state,
- compute a minimal diff,
- verify the requested change actually happened,
- detect downstream damage where possible.

This layer is required before serious write expansion.

### Layer 6 — Memory and retrieval

Responsibilities:
- session conversation memory,
- per-document memory,
- user preference memory,
- recipe candidate storage,
- closed-file property retrieval/indexing,
- later: project graph and higher-level retrieval.

Memory should be conservative and evidence-based.

### Layer 7 — Policy, trust, and governance

Responsibilities:
- permissions matrix,
- confirmation thresholds,
- trust tiers,
- recipe promotion rules,
- model/provider eligibility rules,
- safe fallback behavior.

### Layer 8 — Observability and evals

Responsibilities:
- trace each run,
- record tool usage and outcomes,
- measure latency/cost/failure classes,
- support regression testing and eval suites,
- capture evidence for recipe promotion and trust progression.

---

## 6. Runtime model

The loop is:

1. Capture `SessionContext` on the UI thread.
2. Build the user turn with clarification prefix and system instructions.
3. Start the agent loop on a background thread.
4. Send a turn to the selected provider with tool definitions.
5. If the model requests tools:
   - validate the requests,
   - execute read-safe tools,
   - pause if a write-preview is needed,
   - append tool results,
   - continue.
6. If the model returns final text, end the loop.
7. If iteration/error/time limits are hit, stop cleanly and fall back if appropriate.
8. Post final result and trace data back to the UI.

### Stop conditions

The loop must stop when any of the following happens:
- final answer produced,
- user cancelled,
- max iterations hit,
- max consecutive API failures hit,
- max token budget hit,
- approval denied in a way that blocks further progress,
- a safety policy blocks continuation.

### Recovery states

The runtime should support:
- retry once with corrected arguments,
- send tool error back to the model for self-correction,
- switch to read-only diagnosis mode,
- cancel remaining steps in a plan,
- fall back to deterministic synthesis path.

---

## 7. Capability and permissions model

Every tool belongs to a capability class.

### Class 0 — Read-safe

Examples:
- inspect active document
- get dimensions
- list features
- inspect mates
- get custom properties
- compare configurations

Policy:
- auto-allowed
- no confirmation required

### Class 1 — Soft-write / transient UI state

Examples:
- select/highlight entities
- isolate/display helpers
- zoom/focus helpers

Policy:
- can be auto-allowed if reversible and clearly visible
- should not affect persisted model state

### Class 2 — Hard-write, first-wave

Examples:
- `set_custom_property`
- `set_dimension_value`
- `suppress_feature` / `unsuppress_feature`
- optionally `rename_object`

Policy:
- preview required
- explicit confirmation required
- undo group required
- verification required
- trace required

### Class 3 — Hard-write, advanced

Examples:
- component insertion
- drawing view creation
- active-configuration-scoped advanced edits
- batch-approved multi-step plans

Policy:
- elevated confirmation
- stronger preview and diffing
- more restrictive phase gating

### Class 4 — High-risk / deferred

Examples:
- sketch creation
- feature creation
- mate creation
- equation modification
- automatic save
- unbounded external file mutation

Policy:
- out of scope until dedicated infrastructure exists

---

## 8. Write lifecycle contract

Every hard write must follow the same lifecycle.

1. **Resolve target**
   - identify the object unambiguously
   - fail closed on ambiguity

2. **Capture before-state**
   - value/state/metadata required for verification and trace

3. **Build preview**
   - human-readable description
   - before/after values
   - any cascade or non-visibility warnings

4. **Get approval**
   - apply / cancel / modify

5. **Apply in undo scope**
   - start undo group
   - perform write on UI thread
   - rebuild if required
   - finish undo group in `finally`

6. **Verify**
   - re-read affected state
   - compare to expected result
   - detect related rebuild/dependency problems when possible

7. **Trace**
   - store before/after state, undo label, verification outcome, and policy tier

8. **Offer rollback path**
   - expose undo label/history
   - allow user-visible reversal where supported

### Important realities

- SOLIDWORKS undo is real and useful, but it is **not a transaction system**.
- Nested undo groups must be avoided.
- Some operations are reversible but still too risky for early automation.
- Verification must mean more than “COM call returned success.”

---

## 9. World-state snapshots and diffs

This is a required layer, not a nice-to-have.

The agent should not keep re-reading the entire model state blindly after each write.
Instead, capture targeted snapshots and compute minimal diffs.

### Before-state examples

- dimension value and units
- custom property name/type/value/link state
- suppression state of target + key dependents
- active configuration
- document diagnostics / rebuild flags

### After-state examples

- new value/state
- rebuild status
- downstream error indicators
- changed dependents where relevant

### Why this matters

Without diffs:
- the model wastes context,
- verification becomes vague,
- write traces become less trustworthy,
- multi-step plans become much harder to explain.

---

## 10. Tool strategy

The system should prefer **fewer, better tools** over broad, weak tools.

### Initial read tool families

- active document/context inspection
- feature tree inspection
- dimension/property/configuration inspection
- selection inspection
- diagnostics and rebuild-state inspection
- comparison tools (especially configs)
- project file search (closed-file property/index retrieval)

### Initial write tool set

Ship in this order:
1. `set_custom_property`
2. `set_dimension_value`
3. `suppress_feature`
4. `unsuppress_feature`
5. optional `rename_object`

### Deferred tools

Defer until dedicated infrastructure exists:
- sketch creation
- feature creation
- mate creation
- equation edits
- automatic save
- non-trivial multi-file write propagation

---

## 11. Memory and learning

### Memory scopes

#### Turn memory
- current request
- tool results
- pending approval state

#### Session memory
- selected document
- active configuration
- recently referenced entities
- user preferences discovered this session

#### Cross-session memory
- per-document memory
- user preferences
- validated recipe candidates
- promoted recipes

### What may be learned

Safe to learn:
- preferred answer depth
- common workflows
- frequently used scopes/configurations
- safe, repeated, user-approved sequences

Not safe to learn automatically:
- inferred permissions the user never approved
- risky behaviors from a single success
- broad geometry assumptions from one document
- anything that bypasses confirmation policy

### Recipe promotion rule

A recipe should only be promoted when it is:
- repeated,
- verified,
- stable across runs,
- and reviewed at the right trust tier.

---

## 12. Retrieval and project context

### v1 retrieval target

Support **closed-file property retrieval**, not deep semantic geometry understanding.

Capabilities:
- search closed `.SLDPRT`, `.SLDASM`, `.SLDDRW` by metadata and custom properties
- filter by file type, path, and property values
- surface likely matching project files quickly

### Optional enhancement

If a Document Manager key is available:
- enrich configuration data,
- read references more richly,
- support better project graph queries.

### Explicit non-goal for early retrieval

Do **not** promise offline answers to questions like:
- “which part has a 25mm hole?”
- “which file has a suppressed fillet?”
- “which sketch is over-defined?”

Those require open-file COM inspection.

---

## 13. Provider strategy

### Cloud-first for loop correctness

Primary production path:
- OpenAI-compatible cloud path and/or Anthropic-native path
- build and validate the agent loop here first

### Local providers

Supported later as experimental/advanced options:
- Ollama
- LM Studio

Local-provider rules:
- do not assume streaming parity,
- validate tool-call output client-side,
- generate fallback tool-call IDs if missing,
- treat local tool use as lower-trust until proven by evals.

### Hardware reality

High-quality local tool calling is feasible mainly on high-end workstations; it should not define the baseline product experience.

---

## 14. UI target: the minimum viable “feels agentic” experience

The assistant should feel agentic when a user can:
- ask for a model change in natural language,
- see the assistant narrow ambiguity using live CAD context,
- watch it gather evidence step-by-step,
- see exactly what it plans to change,
- approve that change in a preview panel,
- observe verification and history after the write,
- and later repeat a validated pattern as a recipe.

The UI should always make these visible:
- current goal,
- current step,
- next intended action,
- waiting-for-confirmation state,
- recent verified change,
- easy cancellation/undo access.

---

## 15. Phase order

### Phase 1 — Clarification UX + session conversation state

Deliver:
- clarification controls populated from `SessionContext`
- prompt-prefix integration
- session-scoped message history
- sliding-window truncation

### Phase 2 — Agent runtime and provider abstraction

Deliver:
- provider-agnostic loop runner
- tool schemas and registry
- OpenAI-compatible provider path
- Anthropic-native path as needed
- cancellation, error budgets, fallback behavior

### Phase 3 — Snapshot/diff and verification layer

Deliver:
- targeted before/after snapshot system
- verification contracts
- write-trace enrichment
- rebuild/error inspection improvements

### Phase 4 — First-wave writes and confirmation UI

Deliver:
- preview/apply/verify/rollback flow
- `set_custom_property`
- `set_dimension_value`
- `suppress_feature` / `unsuppress_feature`
- write history and explicit undo surface

### Phase 5 — Learning activation and trust policy

Deliver:
- recipe candidate capture
- promotion workflow
- trust tiers activated by evidence
- usage-driven achievements and exploration tracking

### Phase 6 — Retrieval and cross-session memory

Deliver:
- per-document memory
- user preference memory
- closed-file indexing (`Adze.Index` or equivalent)
- optional Document Manager enrichment

### Phase 7 — Advanced writes and bounded multi-step plans

Deliver:
- batch review UI
- reviewed multi-step execution
- selective advanced write introduction only after eval gates are met

### Phase 8 — Production hardening and provider expansion

Deliver:
- telemetry and eval dashboards
- cost controls
- large-assembly performance work
- experimental local provider support
- long-session hardening and polish

---

## 15b. Ecosystem-informed enhancements (from competitive research, 2026-03-16)

Based on landscape analysis of AURA, LEO, SOLIDWORKS Labs, Autodesk Assistant, Siemens Copilot, Leo AI, MecAgent, and Backflip:

### Adopt
- **HTML answer panel** — every competitor renders polished conversational UI. Replace raw TextBox with WebBrowser control. Highest UX impact.
- **"What's Wrong" diagnostic intent** — SOLIDWORKS Labs validates this use case. Adze already has the tools (rebuild diagnostics, feature tree). Add intent routing.
- **Conversational chat history** — surface existing AgentConversationState as a multi-turn thread instead of single Q&A.
- **Recipe suggestions** — SOLIDWORKS Labs "Command Predictor" validates predictive assistance. Accelerate T5-04.

### Watch / plan for later
- **MCP server exposure** — Autodesk is adopting MCP for Fusion extensibility. Adze's typed tool surface is already MCP-shaped. High strategic value, Phase 10+.
- **Auto-drawing assistance** — both Dassault (LEO) and Siemens (Solid Edge) shipping AI-driven drawing creation. Requires Phase 7+ advanced write tools.
- **Enterprise knowledge integration** — Leo AI (getleo.ai) proves the value prop with HP/Intel/Philips. Adze's recipe/memory system is a foundation for this.

### Avoid
- **Documentation search** — AURA and SOLIDWORKS Insight already do this well. Don't compete.
- **Cloud dependency** — Adze's key differentiator is desktop-only, no 3DEXPERIENCE required. Protect this.
- **Multi-CAD breadth** — depth in SOLIDWORKS > shallow coverage across platforms at this stage.

### Positioning
Adze = **deep grounding for desktop SOLIDWORKS users** who don't have or don't want 3DEXPERIENCE cloud dependencies. User-controlled AI provider. Governed writes. Offline-capable.

---

## 16. Non-goals until later

The following should stay out of scope until the core runtime is proven:
- unbounded autonomous writes
- hidden write execution with no preview
- sketch/feature creation as early milestones
- OpenClaw runtime integration
- geometry-semantic closed-file retrieval promises
- automatic save behavior inside ordinary write tools
- over-engineered multi-agent/sub-agent systems

---

## 17. Success criteria

The end-goal is reached when a user can:

1. Open a model and ask for a bounded change in natural language.
2. See clarification grounded in live feature/dimension/configuration context.
3. Watch the agent gather the minimum evidence needed.
4. See a precise preview with before/after state.
5. Approve the change and have it execute inside a named undo group.
6. See a verified result and a trustworthy trace.
7. Undo the change later through the surfaced history/undo path.
8. Reuse proven workflows as promoted recipes.
9. Continue working smoothly without UI freezes or unsafe COM access.
10. Trust the assistant because its behavior is legible, bounded, and reversible.

---

## 18. Final recommendation

Build toward **trusted bounded autonomy**, not broad autonomy.

The decisive architecture choice is this:
- let the **model drive the next bounded step**,
- let the **host enforce policy, verification, and reversibility**.

That is the cleanest path from today’s inspection assistant to a genuinely helpful agent inside SOLIDWORKS.
