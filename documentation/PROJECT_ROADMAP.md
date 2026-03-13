# Adze - Project Roadmap

**Version:** 0.1.0  
**Last Updated:** 2026-03-13  
**Status:** Grounded assistant alpha with native host execution, model-backed answer synthesis, and green scripted validation

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

## Current Architecture

### Implemented Layers

| Layer | Current Implementation | Purpose |
|------|-------------------------|---------|
| Native host | In-process C# SOLIDWORKS add-in | Owns lifecycle, COM access, Task Pane UI, and tool execution |
| Context boundary | Shared C# contracts plus JSON schemas | Keeps host, broker, tools, traces, and scripts aligned |
| Tool layer | 10 read-only grounding tools | Exposes auditable inspection over the active CAD session |
| Broker layer | Hybrid deterministic + Anthropic planning | Produces structured turn state, tool recommendations, blockers, and recovery guidance |
| Answer layer | Model-backed synthesis over executed tool results with deterministic fallback | Produces a grounded natural-language answer without giving the model direct CAD access |
| Trace/progression layer | Snapshots, traces, recipe candidates, achievements, exploration, unlock tiers | Governs reuse and progression without autonomous capability expansion |
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
- answer-first assistant UI with tabbed plan/status panels
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

**Decision:** Use Anthropic for planning and answer synthesis when configured, but preserve deterministic local behavior.  
**Reason:** This keeps the product usable and diagnosable when the model path is unavailable, degraded, or intentionally disabled.

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
- add answer-quality evals for model-backed synthesis
- add timeout/failure coverage for the synthesis path
- move from single-turn grounding to richer multi-step execution without breaking the COM boundary

### Track 2 - First Usable Beta Path

Goal:
- harden launcher/login/update interruption handling
- add installer/update assets under `install/`
- make support bundle collection and support instructions beta-ready

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
- no dependence on Claude Max consumer-plan usage limits for this application
- no UI-automation-first product path
