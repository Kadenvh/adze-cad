# Adze

**Native AI assistant for SOLIDWORKS** — grounded in your live CAD session, not generic chat.

Adze is a free, in-process SOLIDWORKS add-in that reads your active document through 19 typed tools, reasons over the results with AI, and presents grounded answers in a conversational Task Pane. Write operations follow an 8-step safety lifecycle: plan, preview, approve, apply, verify, trace, undo-label, and history.

---

## Features

- **19 tools** — 11 read-only grounding tools, 7 write tools, 1 file search tool
- **Agentic loop** — the AI iteratively calls tools, observes results, and decides next actions
- **Write safety** — preview before apply, verification after, undo tracking, cascade analysis
- **Multi-provider AI** — OpenAI, Anthropic, OpenRouter, Ollama, LM Studio
- **Offline capable** — deterministic fallback works without internet or API keys
- **Streaming** — SSE streaming for real-time answer delivery
- **Local models** — experimental support for Ollama and LM Studio (no data leaves your machine)
- **Session telemetry** — tool usage stats, cost tracking, budget controls

### Read Tools

| Tool | What it reads |
|------|--------------|
| `get_active_document` | Document type, path, configuration, rebuild state |
| `get_document_summary` | Feature count, body count, material, mass properties |
| `get_selection_context` | Currently selected entities and their properties |
| `get_feature_tree_slice` | Feature tree with types, suppression state, errors |
| `get_dimensions` | All dimensions with values, tolerances, driven state |
| `get_configurations` | Configuration names, parameters, active config |
| `get_custom_properties` | Document and configuration-specific custom properties |
| `get_mates` | Assembly mates with types, status, references |
| `get_rebuild_diagnostics` | Rebuild errors and warnings with affected features |
| `get_reference_graph` | Component references and dependency relationships |
| `search_project_files` | Search closed SOLIDWORKS files by keyword/property |

### Write Tools

| Tool | What it does | Safety class |
|------|-------------|-------------|
| `set_custom_property` | Set/create custom properties | Standard |
| `set_dimension_value` | Change dimension values (triggers rebuild) | Standard |
| `suppress_feature` | Suppress features with cascade analysis | Standard |
| `unsuppress_feature` | Unsuppress features (triggers rebuild) | Standard |
| `rename_object` | Rename features with collision detection | Standard |
| `insert_component` | Insert components into assemblies | Elevated |
| `create_drawing_view` | Create standard drawing views | Elevated |

---

## Quick Start

### Beta Install (no source code required)

1. Download the latest release zip from [Releases](https://github.com/kadenvh/adze-cad/releases)
2. Extract and run:

```powershell
powershell.exe -NoProfile -File install-adze.ps1
```

3. Launch SOLIDWORKS — the **Adze** tab appears in the right sidebar

No admin rights required. Installs per-user to `%LOCALAPPDATA%\Adze\bin`.

### Build from Source

**Prerequisites:** Windows 10/11, SOLIDWORKS 2025+, Visual Studio 2022+ or MSBuild, PowerShell 7+, .NET Framework 4.8

```powershell
# Build
pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks

# Register add-in (per-user, no admin)
powershell.exe -NoProfile -File scripts\setup\register-host-addin.ps1

# Run tests (616 unit tests)
pwsh -NoProfile -File scripts\setup\run-tests.ps1
```

See [SETUP.md](SETUP.md) for full configuration, model setup, and troubleshooting.

---

## AI Configuration

Adze works without any AI configuration using its deterministic fallback. To enable AI-powered answers:

```
SOLIDWORKS_AI_ENABLE_MODEL=true
SOLIDWORKS_AI_PROVIDER=openai          # or: anthropic, ollama, lmstudio
SOLIDWORKS_AI_OPENAI_API_KEY=sk-...    # your key
```

**Local models** (no data leaves your machine):
```
SOLIDWORKS_AI_PROVIDER=ollama
# Requires Ollama running locally: ollama serve
```

See [SETUP.md](SETUP.md) for all provider options and environment variables.

---

## Architecture

```
SOLIDWORKS (host process)
  └── Adze.Host (COM add-in, Task Pane UI)
        ├── Adze.Broker (AI orchestration, provider routing)
        ├── Adze.Tools (19 typed tool implementations)
        ├── Adze.Trace (traces, recipes, progression)
        ├── Adze.Index (closed-file OLE indexer)
        └── Adze.Contracts (shared types and schemas)
```

| Project | Role |
|---------|------|
| **Adze.Host** | SOLIDWORKS lifecycle, Task Pane, COM context capture |
| **Adze.Broker** | Prompt composition, OpenAI/Anthropic/local model clients, agentic loop |
| **Adze.Tools** | Read and write tool implementations |
| **Adze.Trace** | JSON persistence for traces, snapshots, recipes, achievements |
| **Adze.Index** | OLE Structured Storage file indexer (no COM dependency) |
| **Adze.Contracts** | Shared models, enums, tool contracts |

---

## Feature Gates

Advanced features are opt-in via environment variables:

| Variable | Default | What it enables |
|----------|---------|----------------|
| `SOLIDWORKS_AI_ENABLE_MODEL` | `false` | AI-powered broker and synthesis |
| `SOLIDWORKS_AI_AGENT_LOOP` | `false` | Iterative agentic tool calling |
| `SOLIDWORKS_AI_FIRST_WAVE_WRITES` | `false` | Write tool definitions in agent loop |
| `SOLIDWORKS_AI_RETRIEVAL` | `false` | Closed-file search tool |
| `SOLIDWORKS_AI_STREAM_FINAL_TEXT` | `false` | SSE streaming for answers |

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, coding conventions, and how to submit changes.

---

## License

MIT License. See [LICENSE](LICENSE) for details.

---

## Status

**v0.1.0** — First tagged release. 19 tools, 616 tests, agentic loop, streaming, local model support, full production hardening.

Built by [VH Tech LLC](https://github.com/kadenvh) as a free community tool for the SOLIDWORKS ecosystem.
