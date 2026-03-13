# Source Layout

This tree contains the active C# implementation projects for Adze.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `Adze.Host/` | In-process SOLIDWORKS add-in, Task Pane UI, live context capture, and host-side orchestration |
| `Adze.Contracts/` | Shared models, enums, benchmark definitions, and tool names mirrored by the JSON schemas |
| `Adze.Tools/` | Read-only grounding tool handlers and the tool catalog |
| `Adze.Trace/` | JSON persistence for traces, grounding snapshots, progression state, and recipe candidates |
| `Adze.Broker/` | Prompt formatting, provider clients, and the current hybrid structured broker turn boundary |

## Conventions

- Keep SOLIDWORKS COM access in `Adze.Host`.
- Keep broker code free of COM, registry, and direct UI dependencies.
- Update schema files under `schemas/` when contract shapes change here.
- Add a directory README when a subdirectory becomes a boundary or grows to 3+ files with shared conventions.

## Adding New Items

- Put host lifecycle, Task Pane, and context-builder changes in `Adze.Host/`.
- Put shared payloads and enums in `Adze.Contracts/`.
- Put tool implementations in `Adze.Tools/`.
- Put persistence, progression, and recipe logic in `Adze.Trace/`.
- Put orchestration or prompt-building logic in `Adze.Broker/`.
