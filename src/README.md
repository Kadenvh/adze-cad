# Source Layout

This tree contains the active C# implementation projects for Adze. **10 production projects + 1 test project** as of Phase 11.

## Contents

| File/Folder | Purpose |
|-------------|---------|
| `Adze.Host/` | In-process SOLIDWORKS add-in, Task Pane UI, live context capture, host-side orchestration. Hosts ribbon-direct flows (`InvokeMeshImport`, `InvokeAutoDrawing`). |
| `Adze.Contracts/` | Shared models, enums, benchmark definitions, tool names, and `IWriteTool<T>` / `ITaskPaneHost` interfaces. |
| `Adze.Tools/` | Read tools (`Grounding/`), compute tools (`Compute/`), mesh-analysis tools (`Mesh/`), and write tool implementations (`Write/`). |
| `Adze.Trace/` | JSON persistence for traces, grounding snapshots, progression state, and recipe candidates. |
| `Adze.Broker/` | Prompt formatting, provider clients, hybrid structured broker turn boundary, agent loop, dispatcher, feature-gate registry. |
| `Adze.Index/` | Closed-file OLE Structured Storage indexer (offline metadata search). |
| `Adze.Mesh/` | **Phase 11.** Pure-managed mesh I/O + analysis + primitive fitting. No SW interop. |
| `Adze.Manager/` | Standalone WinForms control panel (`Adze.Manager.exe`) — 4 tabs: Logs / Settings / Agent Profile / Status. |
| `Adze.UI/` | Native WinForms sidebar v2. No SW interop. |
| `Adze.UiHarness/` | Out-of-SOLIDWORKS dev shell — mounts `Adze.UI` against a stub host. |

## Conventions

- Keep SOLIDWORKS COM access in `Adze.Host`. Compute tools (`Adze.Tools/Compute/`) receive a live `ISldWorks` reference via `ToolExecutionContext.Application` but do not import the COM types directly — they go through `dynamic`.
- Keep broker code free of COM, registry, and direct UI dependencies.
- Keep `Adze.Mesh` and `Adze.UI` SW-interop-free. They must compile and run anywhere .NET Framework 4.8 does.
- Update schema files under `schemas/` when contract shapes change here.
- Add a directory README when a subdirectory becomes a boundary or grows to 3+ files with shared conventions.

## Adding New Items

- **Read tools that use only `SessionContext`** → `Adze.Tools/Grounding/`, implement `IReadOnlyTool<T>`.
- **Read tools that need live SW COM** (mass props, measure, etc.) → `Adze.Tools/Compute/`, implement `IComputeTool<T>`.
- **Write tools** → `Adze.Tools/Write/`, implement `IWriteTool<T>`. Add to `WriteTrackingToolExecutor.WriteToolNames`; if Class 3, also add to `ElevatedToolNames`. Add an `ApplyWriteToolDirect` case in `Adze.Host/Infrastructure/HostState.cs`.
- **Mesh I/O / analysis / geometry** → `Adze.Mesh/`. Pure managed only.
- **Host lifecycle, Task Pane, context-builder changes** → `Adze.Host/`.
- **Shared payloads and enums** → `Adze.Contracts/`.
- **Persistence, progression, recipe logic** → `Adze.Trace/`.
- **Orchestration or prompt-building logic** → `Adze.Broker/`.

## Wiring a new tool — checklist

1. Add `ToolNames` constant in `Adze.Contracts/Tooling/ToolNames.cs`.
2. Add parameters class in `Adze.Contracts/Models/ToolContracts.cs`.
3. Implement the tool in the appropriate `Adze.Tools/{Grounding|Compute|Mesh|Write}/` folder.
4. Add a JSON request schema under `schemas/tools/`.
5. Wire dispatch + parameter deserialization in `Adze.Broker/Orchestration/AgentToolDispatcher.cs`.
6. Add the tool definition in `Adze.Broker/Formatting/ToolDefinitionBuilder.cs`.
7. For write tools: add to `HostState.WriteTrackingToolExecutor.WriteToolNames`, add `ApplyWriteToolDirect` case, and (if Class 3) add to `ElevatedToolNames`.
8. For grounding / compute tools: register in `Adze.Tools/ToolCatalog.cs`.
9. Write tests in `tests/Adze.Tests/Tools/`.
10. Run `pwsh -NoProfile -File scripts\setup\validate-json-schemas.ps1` and `pwsh -NoProfile -File scripts\setup\run-tests.ps1`.
