# Adze.Tools

This project contains the current read-only grounding tool handlers and the catalog that exposes them to the host.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `Grounding/` | One handler per Wave 1 read-only grounding tool |
| `Abstractions/` | Shared tool interfaces |
| `ToolCatalog.cs` | Central construction point for the active grounding tool set |
| `Adze.Tools.csproj` | Tool-layer project definition |

## Conventions

- Keep one tool class per file and name it after the tool it implements.
- Treat the current tool surface as read-only; write tools belong here later but must follow the preview/apply/verify/log boundary from `BUILD_SPEC.md`.
- Update the catalog, contracts, schemas, benchmarks, and progression rules together when adding a tool.

## Adding New Items

- Add the handler under `Grounding/` or a future write-tool folder.
- Add the matching contract shape and tool name in `Adze.Contracts`.
- Add or update the request schema under `schemas/tools/`.
- Register the handler in `ToolCatalog.cs`.
- Extend benchmarks and validation scripts before calling the tool complete.
