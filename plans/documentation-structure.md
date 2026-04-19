# Documentation Structure

**Created:** 2026-04-19 | **Status:** Active
**Decision:** [#11 Triangulate code intelligence](../.ava/brain.db) — GitHub Issues + Linear + GitNexus + brain.db, each answers a different question

---

## Why four layers

Every project accumulates the same information categories, whether the team admits it or not:

- **Session continuity** — what happened last time, what's still open, what decisions constrain the next move
- **Structural understanding** — how does the code connect, what breaks if I change X
- **Public engagement** — what does the outside world report / request / complain about
- **Private workflow** — what's on the roadmap, what's prioritized for the next cycle

Collapsing any of these into one tool forces compromise on at least one. Keeping them separate, with a clear rule for what lives where, keeps each one lean and answerable.

## The four layers

### 1. brain.db — agent session continuity

**Lives at:** `.ava/brain.db` (SQLite, gitignored). Accessed via `node .ava/dal.mjs <command>`.

**Answers:** "What happened last session? What decisions constrain this work? What's still open?"

**Content:**
- `identity` — minimal load-bearing project facts (name, version, vision)
- `decisions` — active architectural and strategic choices with rationale
- `sessions` — session tracking (start, close, summary)
- `notes` — open work items, blockers, handoff pointers
- `session_traces` — optional in-session breadcrumbs
- `handoffs/` — YAML session handoffs for continuity

**Lifecycle:** Written continuously during sessions via `dal.mjs trace add`, `dal.mjs decision add`, `dal.mjs note add`, `dal.mjs session start/close`. Read by the session-context hook at every session start.

**Do not** store code structure, long narrative docs, or content that belongs in `plans/` or `CHANGELOG.md`.

### 2. GitNexus — structural code intelligence

**Lives at:** `.gitnexus/` (SQLite index, gitignored). Accessed via `gitnexus_*` MCP tools from Claude Code, or `npx gitnexus` CLI.

**Answers:** "How does this function connect to others? What breaks if I change X? What execution flows touch this concept?"

**Content:** Symbol graph (functions, classes, references), execution flows (process-grouped call chains), semantic embeddings of code.

**Lifecycle:** Auto-reindexed after `git commit` by the `gitnexus-post-commit.js` hook. Stale indexes are detected by the MCP tools themselves; running `npx gitnexus analyze --embeddings` refreshes.

**Do not** use as a documentation layer. Do not manually maintain structural summaries in docs that GitNexus can derive — the index is always fresher than the prose.

### 3. GitHub Issues — public contribution surface

**Lives at:** `https://github.com/Kadenvh/adze-cad/issues`. Templates in `.github/ISSUE_TEMPLATE/`.

**Answers:** "What does the outside world report, request, or argue for?"

**Templates (authored and shipped):**
- `bug_report.md` — general bugs
- `feature_request.md` — enhancement suggestions
- `crash_report.md` — specific structured form for SW crashes with build version, repro steps, dump location

**Labels we recommend** (see `plans/github-labels.md`):
`bug`, `enhancement`, `documentation`, `crash`, `solidworks-version`, `installer`, `update-lifecycle`, `v1.0-blocker`, `good first issue`, `help wanted`, `wontfix`, `duplicate`.

**Lifecycle:** Created by community. Triaged by maintainer. Linked bidirectionally with Linear once Linear is set up (so that a Linear issue tracks the private roadmap fit for a given GitHub report).

**Do not** use GitHub Issues as a private roadmap — it's public, and using it to track internal priorities creates noise. Use Linear for that. Do not close GitHub issues silently; if something is "not planned," say so in a comment before closing.

### 4. Linear — private workflow + roadmap

**Lives at:** `linear.app` (free tier sufficient for solo dev).

**Answers:** "What's actually being worked on this cycle? What's the priority order? What's the roadmap from here?"

**Structure:**
- Workspace: `VH Tech`
- Project: `adze-cad`
- Cycles: one per release (v1.0, v1.1, v1.2, ...)
- Epics: tied to phase plans (e.g., `plans/polish-and-v1-path.md` phases R1–R6)
- Labels: `bug`, `blocker`, `research`, `docs`, `installer`, plus priority levels
- Public roadmap view: URL linked from `README.md` so the community sees what's coming without gaining write access

**Sync with GitHub:** Linear's GitHub integration creates a one-to-one map for issues you want visible both places. Not every Linear issue needs a GitHub mirror — only ones where community tracking matters.

**Do not** duplicate brain.db notes into Linear. brain.db is ephemeral session state; Linear is stable work-item state. A brain.db note like "remember to look at X next session" is not a Linear issue. A Linear issue like "fix R2026x crash" is not a brain.db note — it's a tracked piece of work.

### Plus supporting artifacts (not themselves layers, but tied into all four)

- **`plans/`** — living strategy documents. `plans/polish-and-v1-path.md` is currently the active plan. Cross-references Linear epics and brain.db decisions.
- **`sessions/`** — structured session notes written by `dal.mjs session-export session "summary"` at close. Indexed by GitNexus so future sessions can grep historical context.
- **`CHANGELOG.md`** — the official per-version changelog. Released state, not roadmap.
- **`CLAUDE.md`** — agent operating rules (auto-loaded). Not a documentation surface for humans; a rule surface for the assistant.

## How to know where something belongs

| Question | Lives in |
|----------|----------|
| What did I do in the last session? | brain.db (`sessions`, `handoffs`) |
| What decisions constrain this work? | brain.db (`decisions`) |
| What's still open / blocked? | brain.db (`notes`) + Linear |
| How does function X connect to Y? | GitNexus |
| What breaks if I refactor Z? | GitNexus (`impact`) |
| Someone in the community reported a bug | GitHub Issues (maybe mirrored to Linear) |
| I'm planning next quarter's work | Linear |
| I'm explaining a strategic direction across sessions | `plans/*.md` |
| I'm writing what shipped in v1.0 | `CHANGELOG.md` |
| I'm telling the assistant a rule | `CLAUDE.md` |
| I'm capturing session narrative for the future | `sessions/session-N.md` (via `session-export`) |

## Migration notes

The previous structure (pre-template-v7) included an Obsidian vault. That layer was retired when brain.db + plans + sessions + GitNexus together covered every concern the vault used to, with less sync friction and fewer places to look. See [Decision #13](../.ava/brain.db).

LangChain / LangGraph / LangFlow were considered and rejected. Those are agent-runtime orchestration frameworks (Python-first), not documentation tools. Adze's `AgentLoopRunner` is C#-native and embedded in the .NET 4.8 add-in — a rewrite would be multi-week for negligible value. If a Python MCP sidecar emerges post-v1.0, LangGraph might fit there; never in the add-in. See [Decision #5](../.ava/brain.db) and [Decision #13](../.ava/brain.db).

## When to update this doc

Rarely. The four-layer shape is a strategic decision; it shouldn't drift session-to-session. Update this file only when a new layer is adopted or an existing one retired, and always alongside a corresponding brain.db decision entry.
