# Trace Schemas

This directory contains the persisted contracts for traces, recipe candidates, and progression state.

## Contents

| File | Purpose |
|------|---------|
| `trace-event.schema.json` | Runtime trace-event envelope |
| `recipe-candidate.schema.json` | Reviewable recipe-candidate payload |
| `progression-state.schema.json` | Achievement, unlock-tier, and exploration-state payload |

## Conventions

- Keep these schemas aligned with the files written under `%LOCALAPPDATA%\Adze`.
- Extend payloads in a backward-aware way because replay and eval tooling will depend on them.
- Record durable state here; transient UI-only data belongs elsewhere.

## Adding New Items

- Add or revise the schema.
- Update the matching contract model and serialization code.
- Check whether benchmark, replay, or progression logic also needs to change.
