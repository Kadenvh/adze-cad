# Contract Models

This directory contains the shared C# models that define the runtime and persisted payload shapes.

## Contents

| File | Purpose |
|------|---------|
| `SessionContext.cs` | Broker-facing live session context and related subtypes |
| `ToolContracts.cs` | Tool request and result payload models |
| `TraceEvent.cs` | Runtime trace-event payload model |
| `RecipeCandidate.cs` | Reviewable recipe-candidate payload model |
| `ProgressionState.cs` | Achievement, unlock-tier, and exploration-state payload model |
| `GroundingSnapshotRecord.cs` | Persisted grounding snapshot model |
| `BenchmarkDefinitions.cs` | Benchmark corpus and task-definition payload models |

## Conventions

- Keep model shapes aligned with the JSON schemas and persisted files.
- Prefer small nested subtypes over loosely typed dictionaries when the shape is stable.
- Treat contract changes as cross-boundary changes that require schema and mapper updates.

## Adding New Items

- Add the model here.
- Update the matching schema and any serialization or benchmark code that depends on it.
- Extend README coverage if a new model boundary appears.
