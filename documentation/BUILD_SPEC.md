# Adze - Build Spec

**Version:** 0.1.0  
**Last Updated:** 2026-03-13  
**Status:** Active implementation baseline

## Purpose

This document is the concrete build/runtime contract for the current codebase. It answers:

- what exists now
- where it lives
- what the runtime boundaries are
- how to build and validate it
- which artifacts the system writes
- what the next technical expansion points are

Use this document when editing source, scripts, schemas, traces, validation harnesses, and packaging assets.

## Current Build Baseline

| Item | Current Value |
|------|---------------|
| Repo root | `C:\SW_plugin` |
| SOLIDWORKS data/config root | `C:\SOLIDWORKS` |
| SOLIDWORKS install tree | `C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x` |
| Native scaffold strategy | Raw native C# add-in |
| Current registration strategy | User-scope development registration under `HKCU` |
| Build toolchain | Visual Studio/MSBuild present |
| `dotnet` SDK | Not currently installed |
| Runtime data root | `%LOCALAPPDATA%\Adze` |
| Current safety rule | No write-capable tool ships before preview/apply/verify/rollback and logging are defined together |

## Actual Repo Layout

| Path | Current Role |
|------|--------------|
| `src/Adze.Host` | In-process add-in, Task Pane UI, host orchestration, answer synthesis integration |
| `src/Adze.Contracts` | Shared models, enums, tool names, context/result contracts |
| `src/Adze.Tools` | Read-only grounding tool implementations and tool catalog |
| `src/Adze.Broker` | Prompt composition, Anthropic client integration, hybrid planning |
| `src/Adze.Trace` | Trace, snapshot, recipe, progression, and state persistence |
| `schemas/context` | Broker-facing session context schema |
| `schemas/tools` | Tool request/result schemas |
| `schemas/traces` | Trace, recipe, and progression schemas |
| `benchmarks/grounding` | Curated grounding task corpus and manifests |
| `benchmarks/reports` | Machine-readable eval and benchmark outputs |
| `scripts/setup` | Build, registration, launch, validation, and support scripts |
| `tests/contracts` | Reserved compiled test boundary; current validation is still script-first |
| `install` | Packaging/install placeholder boundary; not yet implemented beyond documentation |

## Runtime Shape

```text
SOLIDWORKS in-process add-in
  -> Task Pane UI
  -> SessionContext builder
  -> hybrid broker turn planner
  -> typed tool execution
  -> optional model-backed answer synthesis
  -> deterministic fallback answer renderer
  -> trace / snapshot / progression persistence

Validation + support tooling
  -> broker eval reports
  -> grounding benchmark reports
  -> launcher preflight
  -> support bundle collection
```

## Boundary Rules

- All SOLIDWORKS COM execution stays inside the add-in.
- The model never touches COM directly.
- The broker reasons over typed contracts and serialized results only.
- Closed-file indexing and retrieval must stay outside the live COM execution loop.
- Learning is trace promotion and policy-driven unlocks, not self-modifying behavior.
- Use Anthropic API credentials for the model path. This app does not consume Claude Max plan usage limits.

## Runtime Artifact Locations

| Artifact | Location |
|----------|----------|
| Host logs | `%LOCALAPPDATA%\Adze\logs` |
| Traces | `%LOCALAPPDATA%\Adze\traces` |
| Progression state | `%LOCALAPPDATA%\Adze\state\progression-state.json` |
| Latest snapshot | `%LOCALAPPDATA%\Adze\snapshots\latest-grounding-snapshot.json` |
| Recipe candidates | `%LOCALAPPDATA%\Adze\recipes\candidates` |
| Support bundles | `%LOCALAPPDATA%\Adze\SupportBundles` |
| Benchmark/eval reports | `C:\SW_plugin\benchmarks\reports` |

## Build And Validation Commands

| Purpose | Command |
|---------|---------|
| Validate schemas | `pwsh -NoProfile -File scripts\setup\validate-json-schemas.ps1` |
| Build full solution | `pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks` |
| Launch preflight | `powershell.exe -NoProfile -File scripts\setup\launch-and-check-host.ps1` |
| Host validation | `powershell.exe -NoProfile -File scripts\setup\validate-host-spike.ps1` |
| Grounding benchmarks | `powershell.exe -NoProfile -File scripts\setup\run-grounding-benchmarks.ps1` |
| Broker evals | `powershell.exe -NoProfile -File scripts\setup\run-broker-evals.ps1` |
| Support bundle | `pwsh -NoProfile -File scripts\setup\collect-support-bundle.ps1` |

### Validation Floor

The minimum acceptable regression floor for the current baseline is:

1. schema validation passes
2. full solution build passes
3. host validation passes
4. grounding benchmarks pass
5. broker evals pass

If a launcher prerequisite window blocks the host, clear it before treating host validation as a code regression.

## Model Path Configuration

### Required

- `SOLIDWORKS_AI_ENABLE_MODEL=true`
- `SOLIDWORKS_AI_ANTHROPIC_API_KEY` or `ANTHROPIC_API_KEY`

### Optional

- `SOLIDWORKS_AI_ANTHROPIC_MODEL`
- `SOLIDWORKS_AI_ANTHROPIC_ENDPOINT`
- `SOLIDWORKS_AI_ANTHROPIC_VERSION`
- `SOLIDWORKS_AI_ANTHROPIC_TIMEOUT_MS`
- `SOLIDWORKS_AI_ANTHROPIC_MAX_TOKENS`
- `SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_TIMEOUT_MS`
- `SOLIDWORKS_AI_ANTHROPIC_SYNTHESIS_MAX_TOKENS`
- `SOLIDWORKS_AI_ANTHROPIC_TEMPERATURE`

Planning and final answer synthesis intentionally have separate timeout/token controls because the synthesis prompt is larger.

## Current Tool Inventory

### Implemented Wave 1 Grounding Tools

1. `get_active_document`
2. `get_document_summary`
3. `get_selection_context`
4. `get_feature_tree_slice`
5. `get_dimensions`
6. `get_configurations`
7. `get_custom_properties`
8. `get_mates`
9. `get_rebuild_diagnostics`
10. `get_reference_graph`

### Planned Wave 2 Safe Write Tools

11. `select_or_highlight_entities`
12. `set_dimension_value`
13. `suppress_feature`
14. `unsuppress_feature`
15. `rename_object`
16. `set_custom_property`

### Planned Wave 3 Guided Output Tools

17. `export_document`
18. `create_drawing_from_template`
19. `insert_drawing_views`
20. `run_approved_macro`

## Safety And Approval Model

| Action Class | Current State | Rule |
|-------------|---------------|------|
| `green` | Implemented | Read-only inspection may execute immediately if enabled by policy |
| `yellow` | Planned | Reversible edits must require preview and explicit confirmation |
| `red` | Planned | Destructive/admin-sensitive actions require stronger policy, richer logging, and stricter confirmation |

Every future write-capable tool must support:

1. preview
2. apply
3. verify
4. trace/log
5. rollback guidance

## Learning And Progression Contract

The current product already persists:

- traces
- grounding snapshots
- recipe candidates
- achievements
- exploration percentage
- trust-tier tool unlocks

Guardrails:

- no automatic tool-surface expansion
- no write-tool unlock from a single successful run
- no autonomous code mutation or silent capability addition

## Phase Gates

### Phase 1 - Host Foundation

Completed when:
- add-in loads/unloads cleanly
- Task Pane renders
- registration is repeatable
- active document changes are visible to the host

### Phase 2 - Grounded Assistant Alpha

Current baseline:
- 10 read-only tools implemented
- hybrid planning path implemented
- model-backed answer synthesis implemented
- traces/progression/recipes implemented
- host validation, broker evals, and grounding benchmarks green

### Phase 2A - Hardening To First Usable Build

Still required:
- answer-quality eval coverage for synthesis
- failure/timeout coverage for model paths
- launcher interruption hardening
- packaging/install assets for testers
- support/beta workflow polish

### Phase 3 - Safe Write Tools

Required before entry:
- preview/apply/verify/rollback contract defined
- first reversible tool slice chosen
- regression coverage extended for write paths

### Phase 4 - Retrieval And Guided Workflows

Required before entry:
- approved local indexing boundary defined
- retrieval stays separate from live COM execution
- benchmark questions exist for retrieval-backed workflows

## Immediate Next Deliverables

1. Add synthesis answer-quality evals and explicit failure-mode coverage.
2. Harden launcher/login/update interruption handling.
3. Add install/update assets under `install/` for a tester-friendly path.
4. Define the first write-capable tool contract and candidate tool.
5. Decide whether the Task Pane should expose evidence snippets or citations from tool results.
