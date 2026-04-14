# Adze

**Native AI assistant for SOLIDWORKS** — grounded in your live CAD session, not generic chat.

Adze is a free, in-process SOLIDWORKS add-in. It reads your active document through 19 typed tools, reasons over the results with an agentic AI loop, and delivers grounded answers in a conversational Task Pane. Write operations follow an 8-step safety lifecycle: plan, preview, approve, apply, verify, trace, undo-label, and history. Nothing leaves your machine unless you choose a cloud provider.

> **Current status:** v0.1.1 public beta. 19 tools, 666 tests, agentic loop, governed writes, streaming, multi-provider AI. SOLIDWORKS Solution Partner application in progress.

---

## What Adze Does

| Capability | Details |
|-----------|---------|
| **Reads your document** | Feature trees, dimensions, mates, configs, diagnostics, custom properties, reference graphs |
| **Runs an agentic loop** | The model calls tools, observes results, and iterates — you see each tool chip as it fires |
| **Governs writes** | Preview before apply, verification after, cascade analysis, trust-tier gating, undo labels |
| **Works offline** | Deterministic fallback broker works without internet or API keys |
| **Runs local models** | Ollama and LM Studio support — no data leaves your machine |
| **Streams answers** | SSE real-time streaming from any supported provider |
| **Quick actions** | One-tap toolbar: Diagnose, Mates, Dimensions, Properties — no typing required |

---

## Read Tools (11)

| Tool | What it reads |
|------|--------------|
| `get_active_document` | Document type, path, configuration, rebuild state |
| `get_document_summary` | Feature count, body count, material, mass properties |
| `get_selection_context` | Currently selected entities and their properties |
| `get_feature_tree_slice` | Feature tree with types, suppression state, errors |
| `get_dimensions` | All dimensions with values, tolerances, driven state (paginated) |
| `get_configurations` | Configuration names, parameters, active config |
| `get_custom_properties` | Document and configuration-specific custom properties |
| `get_mates` | Assembly mates with types, status, references (paginated) |
| `get_rebuild_diagnostics` | Rebuild errors and warnings with affected features |
| `get_reference_graph` | Component references and dependency relationships |
| `search_project_files` | Search closed SOLIDWORKS files by keyword or property |

## Write Tools (7)

| Tool | What it does | Class |
|------|-------------|-------|
| `set_custom_property` | Set or create custom properties | Standard |
| `set_dimension_value` | Change dimension values (triggers rebuild, config-scoped) | Standard |
| `suppress_feature` | Suppress features with cascade risk analysis | Standard |
| `unsuppress_feature` | Unsuppress features (triggers rebuild) | Standard |
| `rename_object` | Rename features with collision and reference checks | Standard |
| `insert_component` | Insert components into assemblies | Elevated |
| `create_drawing_view` | Create standard drawing views (9 view types) | Elevated |

All write tools: preview → user approves → apply → verify → trace → undo label. Elevated tools show orange-bordered confirmation cards with explicit warnings.

---

## Quick Start

### Install (double-click, no admin required)

1. Download the latest release from [Releases](https://github.com/Kadenvh/adze-cad/releases)
2. Extract the zip
3. Double-click **`Install Adze.bat`** — or run:

```powershell
powershell.exe -NoProfile -File install-adze.ps1
```

4. Launch SOLIDWORKS — the **Adze** task pane appears automatically

Installs per-user to `%LOCALAPPDATA%\Adze\bin`. Uninstall: `powershell.exe -NoProfile -File install-adze.ps1 -Uninstall`

### Build from Source

**Prerequisites:** Windows 10/11 · SOLIDWORKS 2025+ · Visual Studio 2022+ · .NET Framework 4.8 · PowerShell 5.1+

```powershell
# Build the solution
pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks

# Install from repo (builds Debug, registers per-user)
powershell.exe -NoProfile -File install\install-adze.ps1

# Run tests
pwsh -NoProfile -File scripts\setup\run-tests.ps1
```

See [SETUP.md](SETUP.md) for full configuration, model setup, and troubleshooting.

---

## AI Configuration

Adze works without any AI configuration using its deterministic fallback. To enable AI-powered answers, set environment variables in a `.env` file at the repo root:

```
SOLIDWORKS_AI_ENABLE_MODEL=true
SOLIDWORKS_AI_AGENT_LOOP=true
SOLIDWORKS_AI_PROVIDER=openai          # openai | anthropic | ollama | lmstudio
SOLIDWORKS_AI_OPENAI_API_KEY=sk-...
```

**Local models — no data leaves your machine:**
```
SOLIDWORKS_AI_PROVIDER=ollama
# Requires: ollama serve && ollama pull <model>
```

**OpenRouter — single key for OpenAI and Anthropic models:**
```
SOLIDWORKS_AI_PROVIDER=openai
SOLIDWORKS_AI_OPENAI_ENDPOINT=https://openrouter.ai/api/v1
SOLIDWORKS_AI_OPENAI_API_KEY=sk-or-...
```

See [SETUP.md](SETUP.md) for all provider options, feature gates, and environment variables.

---

## Feature Gates

| Variable | Default | What it enables |
|----------|---------|----------------|
| `SOLIDWORKS_AI_ENABLE_MODEL` | `false` | AI-powered answers |
| `SOLIDWORKS_AI_AGENT_LOOP` | `false` | Iterative agentic tool calling |
| `SOLIDWORKS_AI_FIRST_WAVE_WRITES` | `false` | Write tool definitions in agent loop |
| `SOLIDWORKS_AI_RETRIEVAL` | `false` | Closed-file search tool |
| `SOLIDWORKS_AI_STREAM_FINAL_TEXT` | `false` | SSE streaming for real-time answers |

---

## Architecture

```
SOLIDWORKS (host process)
  └── Adze.Host (COM add-in, Task Pane UI)
        ├── Adze.Broker (AI orchestration, provider routing, agentic loop)
        ├── Adze.Tools (19 typed tool implementations)
        ├── Adze.Trace (traces, recipes, progression, memory)
        ├── Adze.Index (closed-file OLE indexer, no COM dependency)
        └── Adze.Contracts (shared types, schemas, tool contracts)
```

| Project | Role |
|---------|------|
| **Adze.Host** | SOLIDWORKS lifecycle, Task Pane, COM context capture, write UI |
| **Adze.Broker** | Prompt composition, OpenAI/Anthropic/local clients, agentic loop runner |
| **Adze.Tools** | Read and write tool implementations, dependency analyzer |
| **Adze.Trace** | JSON persistence for traces, snapshots, recipes, achievements |
| **Adze.Index** | OLE Structured Storage file indexer — reads SOLIDWORKS files without COM |
| **Adze.Contracts** | Shared models, enums, tool names, write contracts |

---

## Test Coverage

666 NUnit unit tests across all layers:

- Broker orchestration, model response parsing, prompt composition
- All 11 read tools and all 7 write tools
- Write execution coordinator, snapshot/diff/verification
- Agent loop runner, tool dispatcher, capability gate probing
- Learning system (trust tiers, recipe capture, achievements)
- Per-document memory, cost budgets, feature gates
- Conversation state, multi-turn context, OLE indexer
- Session telemetry, error classifier, dependency analyzer
- AgentPolicyEngine (trust-gated tool access, 27 tests)
- SSE streaming, rate limiting, tool result truncation
- 6 live provider smoke tests (skip gracefully without API key)

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, coding conventions, and how to submit changes.

---

## License

MIT — see [LICENSE](LICENSE).

---

## Status

**v0.1.1** — Public beta. 19 tools (11 read + 7 write + 1 retrieval), 666 unit tests, agentic loop, governed write lifecycle, SSE streaming, 5 AI providers, AgentPolicyEngine trust tiers, quick-action toolbar, live tool execution chips.

Built by [VH Tech](https://github.com/Kadenvh) as a free tool for the SOLIDWORKS engineering community.
