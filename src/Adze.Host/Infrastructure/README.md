# Host Infrastructure

This directory contains small host-wide helpers that are not themselves add-in lifecycle code or business logic.

## Contents

| File | Purpose |
|------|---------|
| `FileLogger.cs` | Writes host-side log output |
| `HostState.cs` | Holds the current application reference and builds snapshot/plan text |
| `TaskPaneIcon.cs` | Supplies the Task Pane icon resource |

## Conventions

- Keep infrastructure helpers small and reusable.
- Avoid putting broker logic or context-building logic here; those belong in `Services/`.
- Keep file and registry side effects explicit and easy to trace.

## Adding New Items

- Add a helper here only if it supports multiple host flows without becoming domain logic.
- Prefer narrow utility types over large static dumping grounds.
