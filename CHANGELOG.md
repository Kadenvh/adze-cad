# Changelog

All notable changes to Adze are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] — 2026-03-24

First tagged release. Native AI assistant for SOLIDWORKS with agentic tool loop, governed writes, and production hardening.

### Added

- **Native SOLIDWORKS add-in** with Task Pane UI, pre-prompt clarification controls, and collapsible-sections layout
- **11 read-only grounding tools:** get_active_document, get_document_summary, get_selection_context, get_feature_tree_slice, get_dimensions, get_configurations, get_custom_properties, get_mates, get_rebuild_diagnostics, get_reference_graph, and search_project_files (retrieval)
- **7 write tools** with full preview/apply/verify/undo lifecycle:
  - `set_custom_property` — no rebuild required
  - `set_dimension_value` — rebuild required, config-scoped
  - `suppress_feature` / `unsuppress_feature` — rebuild required, cascade warning via DependencyAnalyzer, config-scoped
  - `rename_object` — name collision and dimension reference warnings
  - `insert_component` — assembly-only, Class 3 elevated confirmation
  - `create_drawing_view` — drawing-only, 9 standard view types, Class 3 elevated confirmation
- **Agentic tool loop** (feature-gated): model-driven iterative tool calling with cancel/error budgets, progress UI, and tool result truncation
- **Write safety infrastructure:** IStateSnapshotService, StateDiffService, DefaultVerificationPolicy, WriteTraceRecordBuilder, WriteExecutionCoordinator (8-step lifecycle)
- **Write confirmation cards** with inline before/after preview, Apply/Cancel buttons, and direct COM apply
- **Write plan review UI** with Apply All / Cancel All for multi-step write plans
- **Elevated confirmation UI** for Class 3 tools (orange-bordered cards with warning header)
- **Dependency preview** via DependencyAnalyzer — cascade risk analysis for suppression and dimension changes
- **Configuration-scoped writes** — suppress/unsuppress and dimension changes can target specific configurations
- **HTML answer panel** with Markdown-to-HTML conversion and chat-style conversation thread
- **Conversational chat history** with document-aware clearing and sliding window truncation
- **Diagnostic intent routing** ("What's Wrong" detection) with clarification prefix parsing and diagnostic tool boosting
- **Multi-turn agent context** bridging chat history into the agentic loop with ConversationTruncator
- **SSE streaming synthesis** for final answer text (works with OpenAI, OpenRouter, Ollama, LM Studio)
- **Agentic loop final-turn streaming** — text streams live while tool-calling turns remain buffered
- **Hybrid provider routing:** OpenAI, Anthropic, OpenRouter, Ollama (experimental), LM Studio (experimental) with deterministic fallback
- **Local model support** with LocalEndpointHealthCheck, ToolCallCapabilityProbe, and automatic fallback to synthesis-only when tool calling unsupported
- **Local endpoint health check UI** with styled banners (ready/warning/error) and actionable guidance in Status section
- **Rate limiting** with 429 detection, Retry-After parsing, retry with backoff, and request queuing during active rate limit windows
- **Tool result truncation** (default 8192 chars) and pagination for GetDimensionsTool/GetMatesTool
- **Session telemetry dashboard** tracking tool call frequency, run outcomes, write apply/cancel rates, cancellation phases, recipe capture/promotion, agentic vs classic path counts
- **Cost budget UI** with token breakdown, session budget progress bar, warning/error banners when approaching or exceeding limits
- **Error presentation tiers** (ErrorClassifier): ToolError (non-prominent), ApiError (with guidance), HostError (calm recovery) — never stack traces
- **IUiThreadInvoker abstraction** for clean COM threading — automatic UI-thread marshaling on write apply
- **Learning system:** ITrustService with TrustedBounded tier, AgentRecipeCaptureService, write tool achievements, recipe suggestions UI with Run/Promote
- **Per-document memory** (DocumentMemory, MemoryStore) and user preference storage
- **OLE closed-file indexer** (Adze.Index project) — reads SOLIDWORKS files without COM via OpenMcdf
- **Feature gate registry** for AgentLoop, FirstWaveWrites, Retrieval, LocalModels, StreamFinalText
- **Trace/progression/recipe persistence** under %LOCALAPPDATA%\Adze
- **Token usage monitoring** from API response through session accumulation to answer footer and Status tab
- **Launcher interruption hardening** with multi-pattern blocker detection, JSON preflight report, retry with timeout
- **Beta install/uninstall/packaging** workflow (install-adze.ps1, uninstall-adze.ps1, package-release.ps1)
- **Agent progress UI** showing live tool names and iteration counts during agentic runs
- **Undo label tracking** in write history for matching SOLIDWORKS Edit menu entries
- **Cancel button** with CancellationTokenSource lifecycle
- **616 unit tests** covering all layers (broker, tools, write infrastructure, agent loop, learning, memory, indexer, telemetry, error classification, and more)
- **6 live provider smoke tests** (OpenRouter) with graceful skip when no API key present
- **12 broker eval cases** and **12 grounding benchmark tasks**
- **Comprehensive documentation:** END-GOAL-FINAL.md (700-line agentic vision), IMPLEMENTATION-BLUEPRINT.md (C# contracts), TASK-INDEX.md (60+ tasks), 4 discovery briefs, 8 research briefs

### Rollback Plan

This is a desktop add-in — rollback is via `uninstall-adze.ps1` or deploying a previous build from `install/dist/`.
