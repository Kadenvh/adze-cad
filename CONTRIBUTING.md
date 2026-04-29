# Contributing to Adze

Thanks for your interest in contributing to Adze! This document covers how to set up a development environment, run tests, and submit changes.

## Development Setup

**Requirements:**
- Windows 10 or 11
- SOLIDWORKS 2025 or 2026 (desktop edition)
- Visual Studio 2022+ or MSBuild via Build Tools
- PowerShell 7+ (`pwsh`)
- .NET Framework 4.8

**Build:**

```powershell
pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks
```

**Run tests:**

```powershell
pwsh -NoProfile -File scripts\setup\run-tests.ps1
```

**Register and launch:**

```powershell
powershell.exe -NoProfile -File scripts\setup\register-host-addin.ps1
# Then launch SOLIDWORKS
```

See [SETUP.md](SETUP.md) for detailed configuration and troubleshooting.

## Project Structure

| Directory | What goes here |
|-----------|---------------|
| `src/Adze.Host` | SOLIDWORKS add-in lifecycle, Task Pane UI, COM access; hosts both legacy `TaskPaneControl` and the v1.1 `NativeTaskPaneControlShim` |
| `src/Adze.Broker` | AI orchestration, provider clients, prompt composition, feature gates, UI preferences |
| `src/Adze.Tools` | Tool implementations (10 read + 1 retrieval + 7 write) |
| `src/Adze.Trace` | Trace persistence, snapshot serialization (`ModelJsonMapper`), recipes, progression |
| `src/Adze.Index` | Closed-file OLE indexer (no COM dependency) |
| `src/Adze.Contracts` | Shared types, enums, tool contracts, `ITaskPaneHost` interface |
| `src/Adze.UI` | Native WinForms sidebar v2 — `NativeTaskPaneControl`, `ChatMessageView`, `WriteCardView`, `QuickActionsBar`, `MarkdownToRichText`. No SOLIDWORKS interop, runs anywhere WinForms runs |
| `src/Adze.Manager` | Standalone WinForms control panel (`Adze.Manager.exe`) — install / uninstall / eject + 4 tabs (Logs / Settings / Agent Profile / Status) + Verify Setup button |
| `src/Adze.UiHarness` | Out-of-SOLIDWORKS dev shell — mounts the new sidebar against a stub `ITaskPaneHost` for hot iteration without SW |
| `tests/Adze.Tests` | NUnit 3 unit tests across all layers |
| `schemas/` | JSON schemas for context, tools, and traces |
| `scripts/setup/` | Build, test, validation, and registration scripts |
| `docs/adr/` | Architecture Decision Records (public) |
| `graphify-out/` | Knowledge graph — see "Code-orientation tools" below |

## Code-orientation tools (for new contributors and AI agents)

Adze ships two layers of code intelligence to help you find your way around:

- **`graphify-out/graph.html`** — interactive knowledge graph with full-text search, community clustering, and god-node analysis. Open it in any browser, no server needed. Best entry point for "how does X relate to Y across the codebase."
- **`graphify-out/GRAPH_REPORT.md`** — markdown report with the most-connected symbols (god nodes), surprising cross-community connections, and suggested orientation questions. Read this first if you're new.
- **`graphify-out/graph.json`** — raw graph data (nodes + edges + communities). Use programmatically.

The committed graph is a snapshot — it goes mildly stale between commits. To refresh against the current tree:

```bash
# Inside Claude Code (recommended): regenerates with full LLM extraction
/graphify .

# Or, via standalone CLI (no API cost, AST-only)
npx graphify analyze
```

The intermediate cache (`graphify-out/cache/`, `.graphify_chunk_*.json`, `manifest.json`, `cost.json`) is gitignored — only the user-facing outputs are tracked.

For symbol-level impact analysis (callers, blast radius, route maps), Adze is also indexed by **GitNexus**. Regenerate the index with `npx gitnexus analyze` — the `.gitnexus/` directory is gitignored because it's binary + machine-specific paths.

## Hot UI iteration (without SOLIDWORKS)

Adze's sidebar UI lives in `Adze.UI` and is mounted via the `ITaskPaneHost` interface — meaning you can iterate on UI changes without launching SOLIDWORKS at all. Use the dev harness:

```powershell
# Build the solution, then:
src\Adze.UiHarness\bin\Debug\Adze.UiHarness.exe
```

The harness loads real `SessionContext` JSON snapshots from `%LOCALAPPDATA%\Adze\snapshots\` (Adze.Trace writes these during normal SW sessions) and mounts the new sidebar against a stub host. UI iteration round-trip drops from minutes (rebuild → uninstall → reinstall → relaunch SW) to seconds.

When you're ready to test inside SOLIDWORKS, set the gate and reload:

```powershell
setx SOLIDWORKS_AI_NATIVE_SIDEBAR true
# Fully close SLDWORKS.exe AND 3DEXPERIENCE Launcher (env vars don't propagate to running processes)
# Then relaunch SW
```

The legacy `TaskPaneControl` (WebBrowser-based) stays registered as the default fallback. With the gate ON, `AdzeAddIn.CreateTaskPane()` mounts `NativeTaskPaneControlShim` → `NativeTaskPaneControl` instead.

## Conventions

- **COM stays in Host.** Only `Adze.Host` touches SOLIDWORKS COM interfaces directly.
- **Broker has no COM dependency.** Keep it testable without SOLIDWORKS installed.
- **Update contracts and schemas together.** If you change a C# contract shape, update the matching JSON schema.
- **Write tools follow the safety lifecycle.** Every write tool must implement preview, apply, verify, and undo label. See existing write tools in `src/Adze.Tools/Write/` for the pattern.
- **Feature-gate new capabilities.** Use `FeatureGateRegistry` for anything that changes runtime behavior.
- **Read the directory README** before editing a boundary directory.

## Testing

The test suite is in `tests/Adze.Tests/` (NUnit 3). Run with:

```powershell
pwsh -NoProfile -File scripts\setup\run-tests.ps1
```

Additional validation:

```powershell
# Broker tool-selection evals
powershell.exe -NoProfile -File scripts\setup\run-broker-evals.ps1

# Grounding benchmarks
powershell.exe -NoProfile -File scripts\setup\run-grounding-benchmarks.ps1

# Live provider smoke tests (requires API key)
pwsh -NoProfile -File scripts\setup\run-provider-smoke.ps1
```

All tests must pass before submitting a PR. The 6 live provider smoke tests are skipped automatically when no API key is configured.

## Submitting Changes

1. Fork the repo and create a feature branch
2. Make your changes
3. Run the full test suite — all tests must pass
4. Run `build-all.ps1` — zero errors and zero warnings
5. Open a pull request with a clear description of what changed and why

## Reporting Issues

Use [GitHub Issues](https://github.com/kadenvh/adze-cad/issues) for bug reports and feature requests. Include:

- SOLIDWORKS version and license type
- Steps to reproduce (for bugs)
- Relevant logs from `%LOCALAPPDATA%\Adze\logs\`

## Code of Conduct

Be respectful and constructive. We're building a community tool — treat everyone with the same consideration you'd want in return.
