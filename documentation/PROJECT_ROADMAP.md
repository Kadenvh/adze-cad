# Adze - Project Roadmap

**Version:** 0.1.0
**Last Updated:** 2026-03-20
**Status:** Agentic alpha with collapsible UI — 15 tools (10 read + 4 write + 1 index), 404 tests, multi-turn context, diagnostic intent, write history, OLE indexer

## Product Thesis

Adze exists to be a native AI assistant inside desktop SOLIDWORKS, not an external chatbot with weak grounding and unreliable control. The product goal is straightforward:

- understand the live CAD session
- reason over typed local tools instead of vague screenshots or UI automation
- keep every action explainable, reviewable, and eventually reversible
- turn successful grounded workflows into governed recipes, achievements, and unlocks

The project deliberately treats "learning" as reviewed operational memory, not autonomous self-modification. In Adze, learning means traces, recipe candidates, achievements, exploration percentage, and trust-tier unlocks. It does not mean the model silently expanding tool access, editing code, or taking unmanaged control of SOLIDWORKS COM.

## Operating Principles

- **Native first:** SOLIDWORKS COM stays in the in-process add-in.
- **Grounding before automation:** read-only inspection and explanation come before write-capable tools.
- **Typed boundaries:** host, broker, tools, traces, and schemas stay aligned through explicit contracts.
- **Model assistance, not model control:** the model can plan and synthesize from serialized context; it does not directly execute CAD operations.
- **Fallbacks are product features:** deterministic planning and deterministic answer rendering remain available when the model path fails or is disabled.
- **Supportability matters:** traces, reports, logs, and support bundles are part of the product surface, not afterthoughts.

## Milestone Record

| Version | Date | Milestone |
|---------|------|-----------|
| 0.1.0 | 2026-03-11 | Discovery finalized and bootstrap documentation established |
| 0.1.0 | 2026-03-13 | Reached grounded assistant alpha: native host, 10 read-only tools, hybrid broker, model-backed answer synthesis, answer-first Task Pane, support bundle workflow |
| 0.1.0 | 2026-03-13 | Expanded alpha hardening: OpenAI plus Anthropic provider routing, assistant-workspace UI overhaul, background provider execution, and COM cleanup/logging across session-context traversal |
| 0.1.0 | 2026-03-15 | Compiled NUnit 3 unit test suite: 130 tests covering broker orchestration, response parsing, configuration, prompt composition, all 10 grounding tools, and trace serialization |
| 0.1.0 | 2026-03-15 | Synthesis answer-quality and failure coverage: moved pure-logic types from Host to Broker, added 36 tests for answer building, tool results formatting, and synthesis orchestration (166 total) |
| 0.1.0 | 2026-03-15 | Live provider validation and usage monitoring: 6 smoke tests via OpenRouter, full token tracking pipeline from API response to Status tab, 9 usage parsing tests (175 unit + 6 live) |
| 0.1.0 | 2026-03-15 | Phase 2A hardening complete: launcher interruption hardening (multi-pattern detection, JSON preflight, retry, validation gate), beta install/uninstall/packaging, Task Pane messaging, visual acceptance confirmed |
| 0.1.0 | 2026-03-15 | Full agentic vision validated: 4 discovery briefs + 7 research briefs + external agent review. END-GOAL-FINAL.md, IMPLEMENTATION-BLUEPRINT.md, and TASK-INDEX.md compiled. 8 architecture layers, 5 capability classes, 8 phase gates defined. |
| 0.1.0 | 2026-03-15 | Agentic tool loop implemented (Phase 1A+1B+2): clarification UI, conversation state with truncation, OpenAIFormatAgentClient, AgentLoopRunner, AgentToolDispatcher, ToolDefinitionBuilder, host integration with cancel support. 275 tests (100 new). |
| 0.1.0 | 2026-03-16 | Write tool safety infrastructure + first-wave write tools (Phase 3+4 core): IStateSnapshotService, StateDiffService, DefaultVerificationPolicy, WriteTraceRecordBuilder, WriteExecutionCoordinator, SetCustomPropertyTool, SetDimensionValueTool, SuppressFeatureTool, UnsuppressFeatureTool. Feature-gated behind SOLIDWORKS_AI_FIRST_WAVE_WRITES. |
| 0.1.0 | 2026-03-16 | Learning activation + memory + hardening (Phases 5-8 core): ITrustService, TrustService, AgentRecipeCaptureService, write tool achievements, TrustedBounded tier, DocumentMemory, MemoryStore, UserPreferenceMemory, CostBudgetSettings, BudgetStatus, FeatureGateRegistry. 378 tests (103 new this session). |
| 0.1.0 | 2026-03-18 | Conversational UI (T9-01, T9-05, T4-09): HTML answer panel with WebBrowser control, chat-style conversation thread with user/assistant bubbles, write confirmation cards with Apply/Cancel and direct COM apply. Tab state sync via ObjectForScripting, status auto-refresh via InvokeScript. |
| 0.1.0 | 2026-03-20 | Diagnostic intent, multi-turn context, OLE indexer, write history, UI redesign: T9-02 "What's Wrong" intent routing with clarification prefix parsing and prompt tuning. Multi-turn agent context via ConversationTruncator. Adze.Index project (OlePropertyReader, ClosedFileIndexer, ClosedFileSearchService). Write history persistence. Collapsible-sections UI replacing tab bar. 404 tests (26 new). |

## Current Architecture

### Implemented Layers

| Layer | Current Implementation | Purpose |
|------|-------------------------|---------|
| Native host | In-process C# SOLIDWORKS add-in with WebBrowser-based conversational UI | Owns lifecycle, COM access, Task Pane, chat history, and write confirmation |
| Context boundary | Shared C# contracts plus JSON schemas | Keeps host, broker, tools, traces, and scripts aligned |
| Tool layer | 10 read-only grounding tools + 4 first-wave write tools | Exposes auditable inspection and governed modification over the active CAD session |
| Broker layer | Hybrid deterministic + OpenAI/Anthropic planning | Produces structured turn state, tool recommendations, blockers, and recovery guidance |
| Answer layer | Provider-routed synthesis over executed tool results with deterministic fallback | Produces a grounded natural-language answer without giving the model direct CAD access |
| Write safety layer | Snapshot/diff/verification, WriteExecutionCoordinator, IWriteTool lifecycle | Ensures write tools follow preview/apply/verify/trace pattern |
| Trace/progression layer | Snapshots, traces, recipe candidates, achievements, trust tiers, per-document memory | Governs reuse, learning, and progression without autonomous capability expansion |
| Unit test layer | 378 NUnit 3 compiled tests across broker, tools (read + write), trace, learning, memory, cost budgets, and feature gates | Provides fast regression coverage for pure logic without requiring SOLIDWORKS |
| Live provider test layer | 6 NUnit 3 smoke tests against real provider APIs | Validates end-to-end model path with usage tracking under real network conditions |
| Validation/ops layer | PowerShell validation scripts, JSON reports, support bundle collection | Makes the system diagnosable and regression-testable in the real Windows/SOLIDWORKS environment |

### Runtime Loop

```text
User request
  -> Task Pane request surface
  -> host builds SessionContext
  -> hybrid broker plans the grounded turn
  -> in-process tool execution over typed contracts
  -> optional model-backed answer synthesis over tool results
  -> deterministic answer fallback if needed
  -> trace + snapshot + progression update
  -> benchmark/report/support artifact generation when requested
```

### Current Implemented Capability Surface

- native Task Pane host with deliberate `Run assistant` flow
- answer-first assistant UI with tabbed `Plan` / `Status` / `Tools` panels
- active-tab-only status refresh that preserves scroll position
- background model execution after live context capture so slow provider calls do not freeze the pane
- 10 live read-only grounding tools:
  - active document
  - document summary
  - selection context
  - feature-tree slice
  - dimensions
  - configurations
  - custom properties
  - mates
  - rebuild diagnostics
  - reference graph
- broker turn state with:
  - turn status
  - intent
  - confidence
  - blockers
  - recovery suggestions
  - follow-up questions
  - prioritized tool recommendations
- model-backed final answer synthesis over executed tool results
- deterministic fallback for both planning and final answer rendering
- trace/progression/recipe storage under `%LOCALAPPDATA%\Adze`
- regression reports under `benchmarks/reports`
- one-command support bundle collection for beta/support triage

## Strategic Decisions

### Native Host As The Execution Boundary

**Decision:** Keep SOLIDWORKS COM inside the add-in.  
**Reason:** This is the strongest boundary for lifecycle control, performance, safety, and auditability.

### Read Surface Before Write Surface

**Decision:** Finish and benchmark the inspection surface before introducing write-capable tools.  
**Reason:** Safe writes depend on trustworthy grounding, preview/apply/verify/rollback flow, and reliable regression coverage.

### Hybrid Model Path With Deterministic Fallback

**Decision:** Support provider-routed OpenAI or Anthropic planning and answer synthesis when configured, but preserve deterministic local behavior.  
**Reason:** This keeps the product usable and diagnosable when the model path is unavailable, degraded, intentionally disabled, or switched between providers.

### User-Scope Registration First

**Decision:** Use per-user registration during development and reserve machine-scope installation for packaging and beta.  
**Reason:** It lowers friction, lowers failure surface, and makes everyday iteration faster.

### Governed Learning Instead Of Autonomous Expansion

**Decision:** Promote reviewed recipes and trust-based unlocks instead of allowing the model to expand tool access on its own.  
**Reason:** This keeps the system legible and safe as capability grows.

### Supportability As A Core Product Concern

**Decision:** Treat traces, reports, launcher preflight, and support bundles as first-class workflows.  
**Reason:** A desktop CAD assistant that cannot be diagnosed quickly will fail in beta even if the core model behavior is strong.

## Near-Term Roadmap

### Track 1 - Grounded Assistant Hardening

Goal:
- implement pre-prompt clarification UI with live SessionContext data (Phase 1A)
- implement conversation state and follow-up turn support (Phase 1B)
- implement agentic tool loop with native API tool calling (Phase 2)
- implement first-wave write tools with preview/apply/verify/rollback (Phase 4)

### Track 2 - First Usable Beta Path

Goal:
- harden launcher/login/update interruption handling
- add installer/update assets under `install/`
- make support bundle collection and support instructions beta-ready
- capture a human desktop acceptance pass for the current Task Pane rendering and resize behavior

### Track 3 - Safe Write Tools

Goal:
- define preview/apply/verify/rollback contracts
- introduce the first reversible single-document write tools
- expand traces, achievements, and unlock policy to cover reviewed write workflows

### Track 4 - Retrieval And Guided Workflows

Goal:
- add closed-file indexing from approved local sources
- support guided exports, drawing creation, and recipe-assisted workflows
- keep retrieval separate from live COM execution

### Track 5 - Experimental Lanes

Goal:
- explore geometry-aware retrieval, semantic selection understanding, and more ambitious interaction patterns
- keep UI automation and other brittle approaches outside the core product path unless they can meet the same audit and safety standards

## Explicit Non-Goals For The Current Phase

- no unsupervised self-modifying behavior
- no external broker/process taking direct ownership of SOLIDWORKS COM
- no production-safe write tools until preview/apply/verify/rollback exists
- no dependence on Claude Max, ChatGPT Plus, or other consumer-plan usage limits for this application
- no UI-automation-first product path
