# Plans

This directory holds Adze's living strategy and design documents. Every file here is intended to be read across sessions — nothing is a throwaway scratch pad. If a plan is complete or superseded, its durable value is extracted into code, README content, or `archive/` with an extraction receipt.

## Start here

| File | Purpose |
|------|---------|
| [`END-GOAL-FINAL.md`](END-GOAL-FINAL.md) | The long-term agentic vision for Adze. Architecture layers, capability classes, phase order, success criteria. |
| [`polish-and-v1-path.md`](polish-and-v1-path.md) | Current active execution plan — v1.0.0 release path, phases R1–R6 (held pending R2026x post-update crash fix). |
| [`IMPLEMENTATION-BLUEPRINT.md`](IMPLEMENTATION-BLUEPRINT.md) | C# interface contracts for all planned phases. |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | 60+ task breakdown spanning the full roadmap. |

## Process and structure

| File | Purpose |
|------|---------|
| [`documentation-structure.md`](documentation-structure.md) | The four-layer documentation stack (brain.db + GitNexus + GitHub Issues + Linear). Rule for what lives where. |
| [`release-process.md`](release-process.md) | Repeatable release checklist — pre-release gates, tagging, publication, post-publish hygiene. |
| [`github-labels.md`](github-labels.md) | Recommended GitHub label set + saved filters + anti-patterns. |
| [`linear-adoption-checklist.md`](linear-adoption-checklist.md) | Step-by-step for activating Linear (workspace, project, epics, GitHub sync, public roadmap). |
| [`github-issue-v1-crash-draft.md`](github-issue-v1-crash-draft.md) | Pre-drafted body for the public R1 crash-tracking issue — paste into GitHub once Linear + labels are set up. |

## Validation procedures

| File | For |
|------|-----|
| [`TESTING-MANUAL-USER.md`](TESTING-MANUAL-USER.md) | Hands-on validation checklist — run before each release. You drive, trust your judgment. |
| [`TESTING-PROCEDURE-AUTOMATED.md`](TESTING-PROCEDURE-AUTOMATED.md) | Deterministic agent-driven procedure using Windows MCP tooling. Covers the same surface more exhaustively. |

## Research and discovery briefs

Pre-implementation research that grounded the design decisions you see in code today.

| File | Topic |
|------|-------|
| `research-agent-loop-threading.md` | Threading model for the agentic tool loop |
| `research-closed-file-retrieval.md` | OLE Structured Storage indexing for SW files |
| `research-local-model-feasibility.md` | Ollama + LM Studio viability for tool calling |
| `research-openclaw-feasibility.md` | Claude-in-browser automation research |
| `research-solidworks-ai-ecosystem.md` | Competitor landscape (LEO, Fusion AI, etc.) |
| `research-streaming-ux-patterns.md` | SSE streaming UX for the Task Pane |
| `research-tool-calling-abstraction.md` | Multi-provider tool-calling surface design |
| `research-write-safety-rollback.md` | The 8-step write safety lifecycle rationale |
| `discovery-*.md` | Open-ended exploration that preceded each major phase |

## Design documents

| File | Topic |
|------|-------|
| `design-mcp-server.md` | MCP sidecar architecture (Phase 10+) |
| `phase10-ui-expansion.md` | Post-v1.0 UI surfaces beyond the Task Pane |

## Archive

`archive/` contains superseded plans preserved for historical context. Each archived plan includes an `ARCHIVE_RECEIPT.md` noting what was extracted to canonical docs before archival.

## Contributing

If you're opening a plan here for the first time:

1. Read `END-GOAL-FINAL.md` for the vision.
2. Read `polish-and-v1-path.md` for what's being executed right now.
3. Skim `TESTING-MANUAL-USER.md` to understand how we validate.
4. Drop into `src/` — the code layout mirrors the brief in `src/README.md`.

Plans evolve. If something here contradicts the current code, the code is correct — file an issue and we'll update the plan.
