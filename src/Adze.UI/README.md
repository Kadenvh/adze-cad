# Adze.UI

Native WinForms sidebar v2 — `NativeTaskPaneControl` plus its supporting controls. **No SOLIDWORKS interop dependencies.** This library is what gets mounted inside SW (via `NativeTaskPaneControlShim` in `Adze.Host`) and inside `Adze.UiHarness` for dev iteration.

**Type:** Library (.NET Framework 4.8). AssemblyVersion 1.1.0.0.

## Layout

```
src/Adze.UI/
├── Rendering/
│   └── MarkdownToRichText.cs    # Markdown → RichTextBox formatted runs
└── V2/
    ├── NativeTaskPaneControl.cs # Top-level sidebar control (the v1.1 cutover)
    ├── ChatMessageView.cs       # Claude-style chat bubble (user + assistant)
    ├── WriteCardView.cs         # Pre/post write confirmation card (Apply/Cancel)
    ├── QuickActionsBar.cs       # Chip row below the prompt input
    ├── RefinementPanel.cs       # Inline clarification panel (Intent/Scope/Output/Diagnostics)
    └── UiPalette.cs             # Color tokens (Light/Dark/System) + theme switching
```

## What it depends on

| Dependency | Why |
|---|---|
| `Adze.Contracts` | `ITaskPaneHost`, `ChatEntry`, `PendingWriteAction`, `SessionContext` |
| `Adze.Broker` | Provider settings, feature gate registry (for runtime gate-aware UI bits) |
| `Adze.Trace` | `ModelJsonMapper` for snapshot serialization (used by the harness) |

**What it does NOT depend on:** `SolidWorks.Interop.*` (no SW API), `Adze.Host` (no upward dep). This is the discipline that lets `Adze.UiHarness` host the same library outside SW.

## Conventions

- **Owner-drawn tabs and bubbles.** Native WinForms tab controls are ugly; the `NativeTaskPaneControl` uses owner-drawn rendering with `UiPalette` tokens for the indigo accent (`#4F46E5`).
- **No HTML.** v1.1 explicitly rejected the legacy WebBrowser/HTML path. If you find yourself reaching for an HTML literal, you're working on `TaskPaneControl.cs` (legacy) in `Adze.Host`, not this library.
- **Theme tokens via `UiPalette.Current`.** Light/Dark/System detection happens once at startup; live theme switching is deferred to v1.2. Use the static accessor, don't pass palette objects around.
- **`ITaskPaneHost` is the only host coupling.** Controls accept an `ITaskPaneHost` reference; never `HostState` (static in `Adze.Host`). The harness substitutes `StubHostState`; SW substitutes `HostStateAdapter`.

## Wiring into SW

`NativeTaskPaneControlShim` (lives in `Adze.Host/UI/`, ProgID `Adze.Host.NativeTaskPaneControl`, GUID `{C8B41F45-D2A6-4B5E-9F7C-3E0A1D8B2F61}`) is the COM-registered shim that mounts the inner `NativeTaskPaneControl` on `OnHandleCreated`. Gated by `SOLIDWORKS_AI_NATIVE_SIDEBAR` feature gate (default OFF until founder verifies live).

## Cross-refs

- `plans/project-index.md` §1 — project inventory
- `plans/ui-rebuild-v1.1.md` — Chunks 1/2/3 + Round 2 design history
- `plans/adr-001-native-winforms-ui-authority.md` — the WebBrowser→native decision rationale
- `src/Adze.UiHarness/` — out-of-SW dev shell that mounts this library
- `src/Adze.Host/UI/NativeTaskPaneControlShim.cs` — the COM shim that mounts this in SW
- `tests/Adze.Tests/UI/` — automated tests for `MarkdownToRichText`, `QuickActionsBar`, `UiPalette`, `RefinementPanel`, `TaskPaneHost` contract
