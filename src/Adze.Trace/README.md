# Adze.Trace

This project owns JSON persistence for traces, snapshots, progression state, and recipe candidates.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `Tracing/` | Trace recording and grounding snapshot persistence |
| `Progression/` | Achievement, exploration, and unlock-tier updates |
| `Recipes/` | Recipe-candidate generation and review-ready counting |
| `Serialization/` | JSON read/write and contract-mapper helpers |
| `Storage/` | Runtime path helpers for the `%LOCALAPPDATA%\\Adze` store |
| `Adze.Trace.csproj` | Trace-layer project definition |

## Conventions

- Persist schema-aligned JSON only.
- Treat `%LOCALAPPDATA%\Adze` as the runtime store; do not invent alternate local stores ad hoc.
- Keep progression monotonic unless a deliberate downgrade policy is introduced.

## Adding New Items

- Add new persisted payloads only after adding or updating the matching schema and contract model.
- Add storage-path helpers in `Storage/` before scattering new file locations through the codebase.
- Keep replay, eval, and progression needs in mind when changing trace shapes.
