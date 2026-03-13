# 2 - DUAL SESSION — Deployment Guide

Dual-session cognitive architecture: run the general agent (default mode network — curious, relational) alongside a dev agent (focused attention) for parallel cognitive modes on the same project or across sibling projects.

## What Dual-Session Is

Traditional single-session work forces one cognitive mode: focused execution. Dual-session splits this into two parallel modes:

| Mode | Cognitive Analogy | Focus |
|------|-------------------|-------|
| **General** (default) | Default mode network / pattern recognition | Vision, architecture, exploration. The curious, relationship-oriented agent. Immutable facts, persistent architecture, open explorations, decisions. |
| **Dev** | Working memory / focused attention | Tasks, bugs, implementation. Activated by `CLAUDE_AGENT_ROLE=dev`. Full task table, recent facts, interrupted session recovery. |

Both modes always receive **immutable facts** (Tier 1 — the spark) at the top of their context. The spark never leaves.

## How to Run It

### Same project, two terminals

```bash
# Terminal 1: General session (default — the curious/relational agent)
cd /path/to/project
claude

# Terminal 2: Dev session (focused execution)
cd /path/to/project
CLAUDE_AGENT_ROLE=dev claude
```

The `CLAUDE_AGENT_ROLE` env var tells `session-context.js` to pass `--role dev` to `dal.mjs context`, which shapes the injected context for focused task execution instead of the broader general view.

### Sibling projects (cross-project awareness)

Two different projects, each with their own `.ava/brain.db`, aware of each other:

```bash
# Terminal 1: Project A (general)
cd /path/to/project-a
claude

# Terminal 2: Project B (dev, focused execution on Project B)
cd /path/to/project-b
CLAUDE_AGENT_ROLE=dev claude
```

Cross-project awareness comes from the sibling registry (see below).

## Communication Channels

| Channel | Scope | How It Works |
|---------|-------|--------------|
| **Explorations table** | Intra-project | General agent creates explorations (`dal.mjs exploration add`). Dev reviews and accepts/rejects. |
| **Sibling DAL reads** | Cross-project | `session-context.js` reads sibling brain.db summaries at init via the sibling registry. |
| **Facts** | Both | Dev creates operational facts. General creates vision/architecture facts. Both share the same brain.db. |

## Sibling Registry Setup

Create `.ava/siblings.json` in each project that needs cross-project awareness:

```json
{
  "siblings": [
    {
      "name": "ProjectA",
      "path": "/path/to/project-a",
      "role": "Primary application"
    },
    {
      "name": "ProjectB",
      "path": "/path/to/project-b",
      "role": "Documentation framework"
    }
  ]
}
```

### Schema

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `siblings` | array | yes | List of sibling project entries |
| `siblings[].name` | string | yes | Display name for context injection |
| `siblings[].path` | string | yes | Absolute path to sibling project root |
| `siblings[].role` | string | no | Brief description of what the sibling does |

### How It Works

1. `session-context.js` reads `.ava/siblings.json` at session start
2. For each sibling with a valid path, runs `node <path>/.ava/dal.mjs context --brief`
3. Appends a 2-3 line summary under `## Sibling Projects` in the injected context
4. If the file doesn't exist or a sibling path is invalid — skips silently, no errors

### Notes

- `.ava/siblings.json` is per-deployment config, NOT part of the template. Each project fills it in after deployment.
- The file lives inside `.ava/` which is gitignored — sibling paths are machine-specific.
- The `--brief` flag on `dal.mjs context` returns a condensed summary (last session + task count + active explorations). If the sibling's DAL doesn't support `--brief`, it falls back to the full context output.

## Context Shapes

### General Context (default)

```
Mission & Vision (immutable):     ← Tier 1, always present
- project-identity: ...
- project-mission: ...

Architecture Facts (persistent):  ← Structural truths
Recent Decisions (5):             ← More decisions, broader view
Task Summary: N active across M   ← Count only, no table
Open Explorations (N):            ← General agent's domain
Last Session: ...                 ← Brief context
```

### Dev Context

```
Mission & Vision (immutable):     ← Tier 1, always present
- project-identity: ...
- project-mission: ...

Last Session: ...                 ← Recency
⚠ INTERRUPTED SESSION DETECTED:  ← Recovery (if applicable)
Active Tasks (N):                 ← Full table, max 15 rows
Recent Facts (N):                 ← Recency + confidence filtered
Recent Decisions (3):             ← Operational decisions
```

## Permanence Tiers

Facts are classified by how long they survive:

| Tier | Injection | Pruning | Examples |
|------|-----------|---------|----------|
| `immutable` | Always, both roles | Never | Mission, vision, identity, founding principles |
| `persistent` | General context, full | Only when explicitly superseded | Architecture patterns, service ports, tech stack |
| `standard` | Dev context, recency-filtered | Normal lifecycle | Current patterns, recent learnings |
| `ephemeral` | Same session only | After 3 sessions without reconfirmation | Debug findings, temp workarounds |

Permanence is assigned during session closeout (Part D-2 of the closeout prompt).

## Fact Audit & Pruning

Facts accumulate over sessions. The audit and prune system keeps them healthy.

### Commands

| Command | What It Does |
|---------|-------------|
| `node .ava/dal.mjs fact audit` | Shows unclassified facts (permanence='standard' that may need reclassification), stale facts (>90 days without confirmation, not immutable/persistent), and ephemeral facts past their 3-session window |
| `node .ava/dal.mjs fact prune --dry-run` | Shows which ephemeral facts would be deleted (past 3-session window) |
| `node .ava/dal.mjs fact prune --execute` | Deletes expired ephemeral facts |
| `node .ava/dal.mjs fact list --permanence <tier>` | Filter facts by permanence tier (immutable, persistent, standard, ephemeral) |

### Recommended Cadence

- **Every closeout:** Run `fact audit` as part of Part D-2. Classify any unclassified facts.
- **Every 3-5 sessions:** Run `fact prune --dry-run` to check for expired ephemeral facts.
- **Before major milestones:** Run full audit + prune to keep the fact store lean.

### Ephemeral Decay Mechanism

Ephemeral facts are designed to expire. The system counts sessions elapsed since a fact's `last_confirmed_at` timestamp:

```sql
SELECT COUNT(*) FROM sessions WHERE start_time > f.last_confirmed_at
```

If 3 or more sessions have occurred since the fact was last confirmed, it becomes eligible for pruning. To keep an ephemeral fact alive, reconfirm it: `node .ava/dal.mjs fact confirm "<key>"`.

### Classification Heuristics

| Content Pattern | Recommended Permanence |
|----------------|----------------------|
| Mission, vision, identity, founding principles | `immutable` |
| Architecture patterns, tech stack, service ports | `persistent` |
| Current implementation patterns, recent learnings | `standard` |
| Debug findings, temp workarounds, session-specific context | `ephemeral` |
