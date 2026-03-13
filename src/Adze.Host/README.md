# Adze.Host

This project is the native in-process SOLIDWORKS add-in host.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `AddIn/` | COM-visible add-in entry point and registration hooks |
| `Infrastructure/` | Logging, current host-state helpers, and Task Pane icon assets |
| `Services/` | Session-context building, assistant response/plan execution, and Task Pane status text |
| `UI/` | Task Pane control implementation |
| `Adze.Host.csproj` | Host project definition and interop references |

## Conventions

- Keep direct SOLIDWORKS COM interaction here.
- Build compact, typed context in `Services/` before handing data to tools or the broker.
- Treat the Task Pane as the native assistant and review surface for current and future actions.

## Adding New Items

- Add lifecycle or registration changes under `AddIn/`.
- Add host-wide helpers under `Infrastructure/`.
- Add context or dashboard builders under `Services/`.
- Add native UI controls under `UI/`.
