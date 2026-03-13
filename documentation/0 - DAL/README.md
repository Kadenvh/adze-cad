# 0 - DAL — Deployment Reference

Deployment playbook for the Data Access Layer (DAL) system. Consult this when rolling out the DAL to a new project.

## Contents

| File | Description |
|------|-------------|
| `dal-setup.md` | Complete DAL architecture, CLI commands, hook integration, migration pattern, and session lifecycle (~1600 lines) |
| `dal/` | Original DAL exploration documents (11 design docs from initial architecture phase) |

## When to Use

Use `dal-setup.md` as the reference when:
- Setting up `.ava/brain.db` in a new project
- Wiring the DAL CLI into session-init and session-closeout
- Migrating an existing project to use the DAL

## What the DAL Provides

Per-project persistent storage for sessions, tasks, facts, decisions, explorations, and notes. Each project gets its own `.ava/brain.db` — scopes are never cross-pollinated.

## Schema Version

Current schema: **v3** (migration 003 — dual-session support)

Migration 003 adds:
- `facts.permanence` column: `ephemeral|standard|persistent|immutable` — controls injection and pruning
- `sessions.agent_role` column: tracks which cognitive mode ran the session (e.g., `general`, `dev`)
- `explorations` table: general agent's exploration domain (open-ended proposals, vision, questions)
- `fact audit` and `fact prune` commands: lifecycle management for facts with permanence tiers
- Role-aware context generation: `dal.mjs context --role general|dev` shapes output by cognitive mode

See `dal-setup.md` for the full `context.mjs` reference implementation with role-aware `generateContext()`.

See `../2 - DUAL SESSION/` for the dual-session deployment guide.
