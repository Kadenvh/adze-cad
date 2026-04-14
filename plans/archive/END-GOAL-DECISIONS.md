# END-GOAL-DECISIONS: Final Decisions, Risks, Gates, and Non-Goals

**Date:** 2026-03-15  
**Status:** Final curated decision log  
**Purpose:** Capture the architecture decisions that are now locked, the risks that still matter, and the phase gates that must be met before enabling more autonomy.

---

## 1. Locked decisions

### D1. Use a custom host-managed runtime inside the add-in

**Decision**  
Adze will use a **custom C# host-managed agent loop** rather than embedding a third-party runtime framework as the shipped execution engine.

**Why**
- the host must own SOLIDWORKS threading and COM safety,
- the host must own preview/approval/undo/verification behavior,
- the product needs CAD-specific policy enforcement that generic agent runtimes do not provide.

**Consequence**
- agent frameworks may still influence patterns,
- but the final runtime boundary is local and product-specific.

---

### D2. Provider abstraction uses two protocol families

**Decision**  
Support two normalized provider families:
- **OpenAI-compatible**: OpenAI, OpenRouter, Ollama, LM Studio
- **Anthropic-native**: direct Anthropic Messages/tool-use path when needed

**Why**
- this captures the real wire-format split,
- prevents unnecessary branching per provider,
- keeps the client layer simple and testable.

**Consequence**
- OpenRouter is useful but not mandatory,
- local providers stay behind the OpenAI-compatible adapter.

---

### D3. COM stays on the UI/STA thread

**Decision**  
All SOLIDWORKS COM calls remain on the SOLIDWORKS/UI thread.

**Why**
- background-thread COM access is unsafe,
- correctness matters more than speculative parallelism.

**Consequence**
- session capture and writes run via explicit UI-thread marshaling,
- loop execution, HTTP, argument validation, and trace assembly run off-thread.

---

### D4. First-wave write tools are narrowly bounded

**Decision**  
The first write wave is limited to:
1. `set_custom_property`
2. `set_dimension_value`
3. `suppress_feature` / `unsuppress_feature`
4. optional `rename_object`

**Why**
- these have the best blend of reversibility, previewability, and verification potential,
- they avoid modal creation states and large hidden blast radius.

**Consequence**
- the product should not market broad “model editing autonomy” early,
- creation/mating/equation changes stay gated later.

---

### D5. OpenClaw is out of runtime scope

**Decision**  
Do not integrate OpenClaw as runtime infrastructure for Adze.

**Why**
- it is useful as development-time orchestration,
- it is not the right runtime substrate for a shipped in-process SOLIDWORKS assistant.

**Consequence**
- remove OpenClaw from runtime roadmap obligations,
- keep it only as an optional dev workflow concern.

---

### D6. Retrieval begins as property/index retrieval

**Decision**  
Closed-file retrieval begins with **metadata/property indexing**, not deep feature/dimension/geometry retrieval.

**Why**
- this is realistic without opening files in COM,
- it solves meaningful project-search workflows quickly.

**Consequence**
- project search is in scope,
- offline geometry understanding is not.

---

### D7. Local models are optional, experimental, and late-bound

**Decision**  
Cloud-first for the core agent loop. Local providers are supported later as optional advanced integrations.

**Why**
- quality and hardware requirements vary too much,
- v1 success should not depend on high-end workstation GPUs.

**Consequence**
- local providers must pass capability tests before being considered “trusted” for tool calling,
- offline mode is valuable but not foundational to v1 launch quality.

---

### D8. Streaming final text is later; streaming tool calls is not required

**Decision**  
Do not block core implementation on streaming the agent loop.

**Why**
- buffered tool turns are operationally fine,
- the tool loop, safety, and verification layers matter more than token-by-token UX early.

**Consequence**
- final-answer streaming can be added later,
- partial progress updates should come from the host, not from forcing streamed tool turns.

---

## 2. Deferred or explicitly rejected items

### Rejected for current roadmap
- OpenClaw runtime integration
- automatic save as part of ordinary write tools
- broad autonomous writes without preview
- geometry-semantic closed-file retrieval claims
- early sketch/feature/mate automation as “core v1” scope

### Deferred until stronger infrastructure exists
- sketch creation
- feature creation
- mate creation
- equation editing
- unbounded multi-file writes
- high-trust autonomous batch changes

---

## 3. Top risks that still matter

### R1. UI-thread misuse / COM violation

**Severity:** Critical  
**Failure mode:** freezes, corruption, deadlocks, crashes  
**Mitigation:** strict UI-thread boundary, no background COM, targeted marshaling, tests that exercise cancellation and refresh patterns

### R2. False-positive verification

**Severity:** High  
**Failure mode:** system thinks a write succeeded when it only partially succeeded or caused downstream damage  
**Mitigation:** snapshot/diff layer, rebuild/error checks, targeted verification policies, trace capture

### R3. Ambiguous target resolution

**Severity:** High  
**Failure mode:** wrong feature/dimension/component is modified  
**Mitigation:** fail closed on ambiguity, clarification UI, explicit target labels in preview, no hidden “best guess” writes

### R4. Undo is mistaken for transactionality

**Severity:** High  
**Failure mode:** team assumes any multi-step write can always be fully and safely rolled back like a database transaction  
**Mitigation:** keep writes grouped per approved action, avoid nested undo groups, document rollback limits, keep advanced writes gated

### R5. Over-trusting local models

**Severity:** Medium-High  
**Failure mode:** malformed or weak tool-calling behavior is treated as production-grade  
**Mitigation:** keep local models behind experimental flags, require capability tests, clearly label provider quality in UI/settings

### R6. Recipe promotion too early

**Severity:** Medium-High  
**Failure mode:** unstable or context-specific workflows become promoted recipes and teach the wrong behavior  
**Mitigation:** require repeated verified success, track failure/cancellation rates, require review before broader trust expansion

### R7. Retrieval overclaim

**Severity:** Medium  
**Failure mode:** users expect offline answers to geometry questions that the retrieval layer cannot answer  
**Mitigation:** product language must distinguish property/index retrieval from open-file CAD inspection

### R8. Large assembly performance degradation

**Severity:** Medium  
**Failure mode:** oversized contexts, slow inspections, UI stutter, timeouts  
**Mitigation:** lazy inspection, capped result sizes, targeted tool outputs, pagination, tool result truncation limits

---

## 4. Phase gates

Each phase must have explicit exit criteria.

### Gate A — Clarification and conversation state

Before Phase 2 is considered complete enough to build on:
- clarification controls populate from live session data,
- session conversation history persists for follow-up turns,
- truncation preserves system prompt + initial intent + recent turns,
- no additional COM chatter is introduced just to populate the composer.

### Gate B — Runtime loop correctness

Before any write tools are enabled:
- iterative tool loop works reliably with read-only tools,
- cancellation works,
- error budgets and fallback work,
- provider abstraction supports at least one cloud path robustly,
- traces capture each tool turn and stop reason.

### Gate C — Verification layer

Before first-wave writes are enabled:
- before/after snapshots exist,
- diffs are recorded,
- verification policies are implemented,
- failure traces can distinguish “apply failed” vs “apply succeeded but verify failed.”

### Gate D — First-wave write safety

Before moving past first-wave writes:
- each write has preview/apply/verify/rollback wiring,
- undo labels are correct and human-readable,
- rebuild-required tools verify fresh state,
- at least one eval suite covers wrong-target, cancellation, and verification-failure cases.

### Gate E — Learning activation

Before recipes/trust progression are surfaced heavily:
- trace quality is high enough to support evidence-based promotion,
- unstable recipes are filtered out,
- trust tier logic cannot silently widen permissions.

### Gate F — Retrieval expansion

Before retrieval is described as project understanding:
- closed-file index is fast enough to feel interactive,
- property queries are accurate,
- optional Document Manager enhancement is isolated cleanly,
- non-supported query classes are clearly rejected rather than guessed.

### Gate G — Advanced writes

Before advanced creation or assembly-changing operations are introduced:
- first-wave writes have proven safe in practice,
- multi-step plan review UI exists,
- approval UX is clear and not overwhelming,
- dependency previews and rollback behavior are understood for the new operation class.

---

## 5. Acceptance criteria by capability area

### Runtime quality
- no UI freezes during normal read loops,
- cancellation produces a clean end state,
- tool errors can be surfaced back to the model or user without corrupting run state,
- deterministic fallback remains available.

### Write quality
- every write produces a preview,
- every approved write creates one understandable undo unit,
- every write records a verification outcome,
- every write trace contains before/after state and undo label.

### UX quality
- user always understands whether the system is thinking, waiting, blocked, or finished,
- the difference between inspection, suggestion, and actual write is obvious,
- plan review is readable even in a narrow Task Pane.

### Trust quality
- trust tiers widen capability only through evidence,
- nothing about a prior success silently removes approval requirements,
- policies remain inspectable and adjustable by the host.

### Retrieval quality
- closed-file search answers the kinds of questions it claims to answer,
- unsupported geometry/dimension questions are redirected to open-file inspection instead of guessed.

---

## 6. Recommended eval categories

### Read-loop evals
- choose correct read tool for common CAD questions
- select proper scope when a feature/configuration is named
- compare configurations without unnecessary tool calls

### Write-loop evals
- identify the correct target dimension/property/feature
- reject ambiguous write requests
- build correct preview text and values
- verify correct application after rebuild
- surface warnings when the change is invisible or cascading

### Recovery evals
- handle missing tool-call IDs
- recover from malformed tool arguments
- recover from provider timeouts
- cancel during read loop
- cancel during waiting-for-confirmation state
- handle approval denial cleanly

### Retrieval evals
- property search on closed files
- configuration-aware search where supported
- refuse unsupported geometry-semantic offline queries

### Trust/recipe evals
- only promote recipes after repeated verified success
- do not promote recipes with frequent cancellation or rollback

---

## 7. What must remain true even as the system expands

These invariants should never be broken:

1. **The host is the safety authority.**
2. **COM stays on the UI thread.**
3. **Hard writes require preview and verification.**
4. **The model may suggest; the host must authorize.**
5. **Undo is helpful but not magical.**
6. **Memory is evidence-based, not speculative.**
7. **Retrieval must not claim geometry knowledge it does not have.**
8. **Trust progression is earned, not toggled casually.**

---

## 8. Final call

The system is ready to move from “validated architecture” to “implementation-ready architecture” because the major open questions have been reduced to bounded engineering work rather than product-shape uncertainty.

The remaining work is not “decide what the agent is.”
The remaining work is:
- implement the runtime correctly,
- preserve the boundaries above,
- and refuse to widen scope faster than the safety/eval evidence supports.
