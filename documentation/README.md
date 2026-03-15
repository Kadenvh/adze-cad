# Documentation

**Version:** 0.1.0  
**Last Updated:** 2026-03-15  
**Role:** Canonical product documentation for Adze

This directory is the source of truth for project documentation. The root `CLAUDE.md` remains the auto-read agent entrypoint, but the durable product, architecture, build, and tactical records belong here.

## Current Baseline

As of 2026-03-15, the repo has a real working implementation, not just planning material:

- a buildable 6-project C# solution (5 production + 1 test)
- a native SOLIDWORKS add-in host with a Task Pane UI
- 10 live read-only grounding tools
- a hybrid broker that can use OpenAI or Anthropic for planning while preserving deterministic fallback
- model-backed final answer synthesis over executed tool results, again with deterministic fallback
- an assistant-first Task Pane workspace with `Plan`, `Status`, and `Tools` tabs
- background model execution after UI-thread context capture so slow network calls do not freeze the pane
- trace, recipe-candidate, achievement, exploration, and unlock persistence
- 175 compiled NUnit unit tests covering broker orchestration, response parsing, configuration, prompt composition, all 10 grounding tools, trace serialization, usage parsing, and live provider smoke tests
- green scripted validation for build, schemas, unit tests, broker evals, host validation, and grounding benchmarks
- one-command support bundle collection for logs, traces, snapshots, reports, and launcher preflight output
- explicit COM cleanup and logged graceful degradation across the session-context builder

## Related Root Docs

These live at the repo root because they serve different audiences:

| Path | Role |
|------|------|
| `CLAUDE.md` | Agent entrypoint, execution rules, current quick-reference commands |
| `SETUP.md` | User-facing setup, registration, model configuration, validation, and troubleshooting guide |

## Read In This Order

1. `PROJECT_ROADMAP.md`  
   Why the product exists, the major architectural choices, and the future roadmap.

2. `BUILD_SPEC.md`  
   The concrete runtime/build contract: repo shape, boundaries, commands, artifacts, tools, and phase gates.

3. `IMPLEMENTATION_PLAN.md`  
   The live tactical status: what is done, what is blocked, what is next, and how to validate the current baseline.

4. `FIRST_USABLE_BUILD.md`  
   The short-horizon roadmap from today’s grounded alpha to the first build that feels like the intended assistant product.

5. `plans/`  
   Discovery briefs, diagnostics, and historical handoff material that informed the canonical docs.

## Documentation Routing

| Need | Document |
|------|----------|
| Product thesis, architecture rationale, long-term phases | `PROJECT_ROADMAP.md` |
| Runtime/build boundaries, commands, artifact locations, tool inventory | `BUILD_SPEC.md` |
| Current implementation state, blockers, validations, handoff | `IMPLEMENTATION_PLAN.md` |
| Near-term path to the first usable internal/beta build | `FIRST_USABLE_BUILD.md` |
| Historical planning context, diagnostics, discovery notes | `plans/` |
| Local developer/operator setup steps | root `SETUP.md` |

## Directory Map

| Path | Purpose |
|------|---------|
| `documentation/PROJECT_ROADMAP.md` | Strategic architecture and roadmap record |
| `documentation/BUILD_SPEC.md` | Implementation-grade build/runtime specification |
| `documentation/IMPLEMENTATION_PLAN.md` | Tactical execution record |
| `documentation/FIRST_USABLE_BUILD.md` | Short-horizon release-target roadmap |
| `documentation/plans/` | Historical planning artifacts, discovery briefs, diagnostics |
| `documentation/.prompts/` | Prompt templates from the documentation system |
| `documentation/0 - DAL/` | Template/reference material, not project code |
| `documentation/1 - NOTES/` | Template/reference material, not canonical product docs |
| `documentation/2 - DUAL SESSION/` | Template/reference material, not canonical product docs |

## Maintenance Rules

- Keep project documentation in `documentation/` unless the file must live elsewhere for tooling reasons.
- Update date-stamped claims when validations or milestones change.
- Keep the docs aligned with the real repo and machine state. If a blocker has been cleared, remove it instead of leaving contradictory notes.
- Treat `plans/` as supporting material, not the canonical product record.
- Prefer exact paths, exact script names, and exact dates over vague wording.
