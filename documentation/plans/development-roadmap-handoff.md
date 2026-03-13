# Development Roadmap Handoff

**Status:** Active  
**Last Updated:** 2026-03-13  
**Primary Next Focus:** Synthesis eval coverage, launcher/install hardening, and safe-write contract definition

## Validated Baseline

Current green baseline:

- full solution build via MSBuild
- JSON schema validation
- broker evals `12/12`
- grounding benchmarks `12/12`
- host validation spike PASS
- mocked Anthropic broker turn PASS
- mocked Anthropic answer synthesis PASS
- support-bundle generation PASS

The system now has:

- 5 C# projects
- native host execution inside SOLIDWORKS
- 10 live read-only grounding tools
- hybrid deterministic + Anthropic planning
- model-backed final answer synthesis with deterministic fallback
- answer-first Task Pane layout
- trace/progression/recipe persistence

## Completed Phases

### Phase 1 - Security Pre-Publish

Completed:
- repo cleanup
- ignore coverage
- sensitive artifact removal
- first git baseline preparation

### Phase 2 - Rename To Adze

Completed:
- solution, projects, namespaces, ProgIds, scripts, and docs renamed from `SolidWorksAi` to `Adze`
- validations rerun successfully after rename

### Phase 3 - Model-Backed Answer Synthesis

Completed:
- second model call added after tool execution
- synthesis prompt path added
- separate synthesis token/timeout settings added
- deterministic final-answer fallback retained
- answer source metadata surfaced in assistant runs

### Phase 4 - Assistant-First Task Pane

Completed baseline:
- answer-first layout in the Task Pane
- plan and status moved away from the primary answer surface
- run-state messaging added
- blocked/no-document guidance improved

## Active Phases

### Phase 5 - Grounded Quality And Hardening

Next tasks:
1. Add answer-quality evals for synthesis output.
2. Add explicit timeout/failure coverage for the synthesis path.
3. Harden launcher/login/update interruption handling.
4. Add install/update assets under `install/`.
5. Decide whether answer evidence snippets belong in the Task Pane.

### Phase 6 - Safe Write Design

Next tasks:
1. Pick the first reversible write-capable tool.
2. Define preview/apply/verify/rollback contracts.
3. Extend trace and benchmark expectations for write paths.

## Constraints That Still Apply

- COM execution stays inside the C# add-in.
- Tool surface remains read-only until the safe-write contract exists.
- Learning remains trace-driven and policy-governed.
- Contracts and schemas must evolve together.
- Launcher state can still block live validation even when code is healthy.

## Where To Continue

Start from the canonical docs first:

1. `documentation/README.md`
2. `documentation/PROJECT_ROADMAP.md`
3. `documentation/BUILD_SPEC.md`
4. `documentation/IMPLEMENTATION_PLAN.md`
5. `documentation/FIRST_USABLE_BUILD.md`

Then use this file as the short engineering handoff.
