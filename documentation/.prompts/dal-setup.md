# DAL Setup — Durable Agentic Layer Reference

Guide for setting up, configuring, and using the DAL (brain.db) system. Covers the core DAL, notes, dual-session cognitive modes, and fact permanence.

---

## 1. WHAT THE DAL IS

The DAL is a SQLite-backed persistent state engine for Claude Code projects. It tracks sessions, tasks, facts, decisions, and notes across conversations — giving every new session full context on what happened before.

**How it works:**
1. A `SessionStart` hook fires when Claude Code opens → runs `node .ava/dal.mjs context` → injects state
2. During work, the agent records tasks, facts, and decisions via the CLI
3. At session close, the agent logs what was done → next session picks up where this one left off

**Single dependency:** `better-sqlite3` (zero-config, file-based, no server required)

---

## 2. DIRECTORY STRUCTURE

```
.ava/                           # DAL root (project root, gitignored)
  dal.mjs                       # CLI entry point (11 commands)
  package.json                  # { "dependencies": { "better-sqlite3": "^11" } }
  brain.db                      # SQLite DB (created by bootstrap)
  lib/
    db.mjs                      # Connection, migrations, integrity check
    sessions.mjs                # Start/close/list sessions
    tasks.mjs                   # Task CRUD + tree view + stale blocker detection
    facts.mjs                   # Fact upsert + FTS5 search + audit/prune
    decisions.mjs               # Decision CRUD + supersede
    notes.mjs                   # Per-tab sticky notes (4 categories)
    context.mjs                 # Role-aware context payload generator
    renderer.mjs                # Markdown delimiter injection
    explorations.mjs            # Open-ended exploration proposals (dual-session)
  migrations/
    001_initial.sql             # Core tables (tasks, sessions, facts, decisions, snapshots)
    002_add_notes.sql           # Notes table (4 categories, tab-scoped)
    003_dual_session.sql        # Permanence tiers, agent roles, explorations
```

---

## 3. SETUP

### Option A: Deploy via dal-doctor (preferred)

From a PE session, use the dal-doctor agent to deploy both template and DAL runtime:

```bash
# dal-doctor handles: copy dal.mjs + lib/ + migrations/, npm install, bootstrap
```

### Option B: Manual setup

```bash
mkdir -p .ava/lib .ava/migrations
# Copy dal.mjs, lib/*.mjs, migrations/*.sql from PE's .ava/
cd .ava && npm init -y && npm install better-sqlite3
node .ava/dal.mjs bootstrap
node .ava/dal.mjs status          # Verify: should show schema v3, 0KB, integrity OK
```

### Hook wiring

The `SessionStart` hook (`.claude/hooks/session-context.js`) auto-injects DAL context. It:
1. Checks for `.ava/brain.db`
2. Reads `CLAUDE_AGENT_ROLE` env var (default: `general`)
3. Runs `node .ava/dal.mjs context --role <role>`
4. Injects output as `## DAL State (auto-injected from brain.db)`

This hook is deployed with the template — no manual wiring needed.

Add `.ava/brain.db*` to `.gitignore` (brain.db is machine-specific state, not source).

---

## 4. CLI COMMAND REFERENCE

All commands: `node .ava/dal.mjs <command> [subcommand] [flags]`

### Sessions

```bash
node .ava/dal.mjs session start "description"    # Start tracked session
node .ava/dal.mjs session close                   # Close with summary prompt
node .ava/dal.mjs session list                    # Show session history
```

### Tasks

```bash
node .ava/dal.mjs task add --title "..." --priority high --component api
node .ava/dal.mjs task update <id> --status done
node .ava/dal.mjs task list                       # Active tasks
node .ava/dal.mjs task tree                       # Hierarchical view with subtasks
```

### Facts

```bash
node .ava/dal.mjs fact set "key" --value "v" --permanence persistent
node .ava/dal.mjs fact search "query"             # FTS5 full-text search
node .ava/dal.mjs fact list                       # All facts
node .ava/dal.mjs fact list --permanence immutable # Filter by tier
node .ava/dal.mjs fact confirm "key"              # Refresh last_confirmed_at
node .ava/dal.mjs fact delete "key"               # Remove a fact
node .ava/dal.mjs fact audit                      # Show unclassified, stale, expired
node .ava/dal.mjs fact prune --dry-run            # Preview ephemeral cleanup
node .ava/dal.mjs fact prune --execute            # Delete expired ephemeral facts
```

### Decisions

```bash
node .ava/dal.mjs decision add --title "..." --context "..." --chosen "..." --rationale "..."
node .ava/dal.mjs decision list                   # All active decisions
node .ava/dal.mjs decision supersede <id> --reason "..."
```

### Notes

```bash
node .ava/dal.mjs note list                       # All tabs summary
node .ava/dal.mjs note list <tab_key>             # Notes for specific context
node .ava/dal.mjs note counts                     # Counts by tab/category
```

Notes are scoped by `tab_key` (any context identifier). Four categories: `improvement`, `issue`, `bug`, `idea`. Notes can also be managed via REST API if the project has a server (see project-specific docs).

### Context & Status

```bash
node .ava/dal.mjs context                         # Generate context payload (general)
node .ava/dal.mjs context --role dev              # Dev-focused context shape
node .ava/dal.mjs status                          # DB health, schema version, size
node .ava/dal.mjs version                         # DAL version
node .ava/dal.mjs bootstrap                       # Initialize brain.db (first time)
node .ava/dal.mjs migrate                         # Run pending migrations
node .ava/dal.mjs render --file <path>            # Inject DB data into markdown delimiters
```

---

## 5. FACT PERMANENCE TIERS

Facts are classified by how long they survive in the knowledge base:

| Tier | Injection | Pruning | Examples |
|------|-----------|---------|----------|
| `immutable` | Always, both roles | Never | Mission, vision, identity, founding principles |
| `persistent` | General context, full | Only when explicitly superseded | Architecture patterns, tech stack, service ports |
| `standard` | Dev context, recency-filtered | Normal lifecycle | Current implementation patterns, recent learnings |
| `ephemeral` | Same session only | After 3 sessions without reconfirmation | Debug findings, temp workarounds |

### Classification heuristics

| Content pattern | Recommended tier |
|----------------|-----------------|
| Mission, vision, identity, founding principles | `immutable` |
| Architecture patterns, tech stack, service ports | `persistent` |
| Current patterns, recent learnings | `standard` (default) |
| Debug findings, temp workarounds, session-specific | `ephemeral` |

### Ephemeral decay

Ephemeral facts expire after 3 sessions without reconfirmation. To keep one alive: `node .ava/dal.mjs fact confirm "key"`. Classification happens during session closeout (Part D-2 of the closeout prompt).

### Recommended cadence

- **Every closeout:** Run `fact audit`, classify unclassified facts
- **Every 3-5 sessions:** Run `fact prune --dry-run` to check for expired ephemeral
- **Before milestones:** Full audit + prune to keep the fact store lean

---

## 6. DUAL-SESSION COGNITIVE MODES

Run two parallel cognitive modes on the same project:

| Mode | Env var | Cognitive analogy | Focus |
|------|---------|-------------------|-------|
| **General** (default) | (none) | Default mode network | Vision, architecture, exploration, relationships |
| **Dev** | `CLAUDE_AGENT_ROLE=dev` | Working memory | Tasks, bugs, implementation, focused execution |

### Running dual sessions

```bash
# Terminal 1: General (default — curious, relational)
cd /path/to/project && claude

# Terminal 2: Dev (focused execution)
cd /path/to/project && CLAUDE_AGENT_ROLE=dev claude
```

### Context shapes

**General context:** Immutable facts (always) → persistent architecture → decisions (5) → task summary (count only) → open explorations → last session

**Dev context:** Immutable facts (always) → last session → interrupted session recovery → active tasks (full table, max 15) → recent facts (7-day) → decisions (3)

Both modes always receive **immutable facts** at the top — the spark never leaves context.

### Sibling registry (cross-project awareness)

Create `.ava/siblings.json` (gitignored, machine-specific):

```json
{
  "siblings": [
    { "name": "ProjectA", "path": "/path/to/project-a", "role": "Primary application" },
    { "name": "ProjectB", "path": "/path/to/project-b", "role": "Documentation framework" }
  ]
}
```

At session start, `session-context.js` reads each sibling's `dal.mjs context --brief` and appends a summary under `## Sibling Projects`. If a sibling path is invalid, it skips silently.

---

## 7. VERIFICATION

After setup, verify everything works:

```bash
node .ava/dal.mjs status           # Schema v3, integrity OK
node .ava/dal.mjs session start "test session"
node .ava/dal.mjs fact set "test-fact" --value "works" --permanence ephemeral
node .ava/dal.mjs fact audit       # Should show 0 unclassified (test-fact is classified)
node .ava/dal.mjs note list        # Empty is fine
node .ava/dal.mjs context          # Should output formatted context block
node .ava/dal.mjs session close    # Clean close
```

If `session-context.js` is wired, restart Claude Code — the hook should inject `## DAL State` automatically.

---

## 8. RULES

- **brain.db is gitignored.** It contains machine-specific session state, not source code.
- **One brain.db per project.** Scopes are never cross-pollinated. Cross-project awareness uses the sibling registry.
- **Dry-run before destructive ops.** Always `fact prune --dry-run` before `--execute`.
- **Classify during closeout.** Every session should leave 0 unclassified facts.
- **The hook is the heart.** If context isn't injecting at session start, check `.claude/hooks/session-context.js` and `.claude/settings.json`.
