# Schemas

This directory holds the JSON contracts that mirror the shared C# models used by the host, broker, tools, traces, and benchmarks.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `context/` | Broker-facing session-context schemas |
| `tools/` | Tool request schemas and the common result envelope |
| `traces/` | Trace, recipe-candidate, and progression-state schemas |

## Conventions

- Keep schema names aligned with the matching contract types under `src/Adze.Contracts`.
- Validate schema changes with `scripts/setup/validate-json-schemas.ps1`.
- Treat schemas as the source of truth for cross-process and persisted payload shapes.

## Adding New Items

- Add a schema under the correct boundary folder.
- Update the matching C# model and any serializer or mapper code.
- Extend benchmarks, scripts, or host code that depend on the changed payload shape.
