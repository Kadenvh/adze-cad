# Adze.UiHarness

Out-of-SOLIDWORKS WinForms dev shell for iterating on `Adze.UI` (the v1.1 native sidebar) without the 30-second SW launch-and-attach round-trip.

**Type:** WinExe (.NET Framework 4.8). Not shipped to end users — dev-only.

## What it does

Mounts `Adze.UI.V2.NativeTaskPaneControl` inside a standalone `MainForm`, backed by `StubHostState` (a `ITaskPaneHost` implementation that returns canned data instead of touching SW COM). Lets you:

- See visual changes to controls without restarting SOLIDWORKS
- Replay real `SessionContext` JSON snapshots from `%LOCALAPPDATA%\Adze\snapshots\` via `SnapshotLoader`
- Inject canned tool results / write previews / chat history to test UI states
- Verify color tokens, theme switching, and layout against `UiPalette` Light/Dark

## Files

| File | Role |
|---|---|
| `Program.cs` | Standard WinForms entry point. Configures DPI awareness, launches `MainForm`. |
| `MainForm.cs` | Hosts the `NativeTaskPaneControl`. Wires the stub host. |
| `StubHostState.cs` | `ITaskPaneHost` implementation with canned `SessionContext`, no SW dependency. |
| `SnapshotLoader.cs` | Loads `SessionContext` JSON snapshots persisted by `Adze.Trace.ModelJsonMapper`. |
| `Properties/AssemblyInfo.cs` | Standard assembly metadata. |

## Run

```powershell
# Build + run via Visual Studio (F5) or:
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' `
    src\Adze.UiHarness\Adze.UiHarness.csproj /p:Configuration=Debug
.\src\Adze.UiHarness\bin\Debug\Adze.UiHarness.exe
```

Or from a clean repo:

```powershell
pwsh -NoProfile -File scripts\setup\build-all.ps1
.\src\Adze.UiHarness\bin\Debug\Adze.UiHarness.exe
```

## Conventions

- **No SOLIDWORKS interop here.** `Adze.UiHarness` references `Adze.Contracts`, `Adze.Trace`, and `Adze.UI` — all SW-interop-free libraries. If you find yourself needing `SolidWorks.Interop.*`, you're testing the wrong thing in the wrong place.
- **Stub data, not mocks.** `StubHostState` returns plausible canned values. Don't add interaction tracking — write a proper integration test in `tests/Adze.Tests/UI/` instead.
- **Snapshot replay over hand-coded fixtures.** When testing a real scenario, capture a `SessionContext` from live SW via `Adze.Trace`, drop the JSON in `%LOCALAPPDATA%\Adze\snapshots\`, and replay it. Faster than synthesizing data manually.

## Cross-refs

- `plans/project-index.md` §1 — project inventory
- `plans/ui-rebuild-v1.1.md` — v1.1 UI rebuild plan; harness is part of Chunk 1
- `src/Adze.UI/` — the library this harness mounts
- `tests/Adze.Tests/UI/` — automated UI tests (separate from this manual iteration loop)
