# Adze.Contracts

This project defines the shared typed boundary used by the host, broker, tools, trace layer, benchmarks, and scripts.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `Models/` | Session context, tool contracts, traces, progression, recipes, snapshots, and benchmark definitions |
| `Enums/` | Shared approval, action-class, and unlock-tier enums |
| `Tooling/` | Stable tool-name constants |
| `Adze.Contracts.csproj` | Shared class-library project definition |

## Conventions

- Model names should stay aligned with their corresponding JSON schemas.
- Tool names are defined once in `Tooling/ToolNames.cs` and reused everywhere else.
- Keep the contracts layer dependency-light so it can be referenced by every other project.

## Adding New Items

- Add a new model under `Models/` and update the matching schema under `schemas/`.
- Add or extend enums only when the state machine or policy surface genuinely changes.
- Add new stable tool-name constants before wiring any new tool handler or benchmark.
