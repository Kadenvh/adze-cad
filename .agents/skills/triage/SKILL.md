---
name: triage
description: "Project status assessment for adze-cad — open notes, active plans, recent decisions, recommended priority stack"
allowed-tools:
  - Read
  - Bash
  - Glob
  - Grep
  - Agent
---

# Triage — Project Status & Priority Routing

Read state across adze-cad — open notes, active plans, recent sessions, decisions — and produce a prioritized status report. (PE/ecosystem-wide triage was decommissioned in Session 6; this skill is now scoped to a single project.)

## Instructions

Follow the protocol below. For the full detailed version, read `.Codex/.prompts/triage.md`.

### Protocol
- **Read continuity brief** → `node .ava/dal.mjs continuity brief`
- **Read open notes** → `node .ava/dal.mjs note list`
- **Read active decisions** → `node .ava/dal.mjs decision list`
- **Read latest session note** → newest file in `sessions/`
- **Read active plans** → all `.md` files in `plans/` (excluding `archive/`)
- **Synthesize** → status, blockers, stale items, priority stack
- **Recommend** → ordered priority list with rationale

## Key Rules

- **Read everything, change nothing.** Triage is observation, not action.
- **Be honest about staleness.** Notes older than 14 days get flagged.
- **Recommend, don't decide.** The priority stack is a recommendation. The user chooses.

## Priority Ranking

1. Broken (test failures, crashed sessions, build errors)
2. Stale (no activity >7 days with open notes)
3. Bugs/issues (`category=bug` or `issue` in open notes)
4. Active plan progress (next actions in `plans/`)
5. Improvements (`category=improvement`)
6. Ideas (`category=idea` — lowest priority, surface for awareness)

## Quick Start

```bash
# Continuity at a glance
node .ava/dal.mjs continuity brief

# Open notes
node .ava/dal.mjs note list

# Active decisions
node .ava/dal.mjs decision list

# Latest session note
ls -t sessions/*.md 2>/dev/null | head -1

# Active plans (exclude archive/)
ls plans/*.md 2>/dev/null
```

## Error Handling

If any step fails (command errors, file not found, brain.db unreachable):
1. Record the failure in the session summary
2. Do NOT continue silently — report the error to the user with what failed and a suggested fix
3. If brain.db is unreachable, run `/dal-doctor`
