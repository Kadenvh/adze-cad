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
| `src/Adze.Host` | SOLIDWORKS add-in lifecycle, Task Pane UI, COM access |
| `src/Adze.Broker` | AI orchestration, provider clients, prompt composition |
| `src/Adze.Tools` | Tool implementations (read and write) |
| `src/Adze.Trace` | Trace persistence, recipes, progression |
| `src/Adze.Index` | Closed-file OLE indexer |
| `src/Adze.Contracts` | Shared types, enums, contracts |
| `tests/Adze.Tests` | NUnit 3 unit tests |
| `schemas/` | JSON schemas for context, tools, and traces |
| `scripts/setup/` | Build, test, validation, and registration scripts |

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
