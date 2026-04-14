# Changelog

All notable changes to Adze are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.1] — 2026-04-14

### Added
- **AgentPolicyEngine** — trust-gated tool access enforcing read-always-allowed, first-wave writes at Assisted tier, advanced writes at Reviewed tier. 27 unit tests.
- **Quick-action toolbar** — persistent Diagnose / Mates / Dimensions / Properties buttons in Task Pane; one tap fires a full agentic run with a pre-built prompt.
- **Tool execution chips** — inline colored pills in the conversation thread showing each tool as it fires (blue for read tools, amber for write tools). Animated thinking indicator during model turns.
- **Large assembly pagination** — `get_dimensions` and `get_mates` now support `offset`/`limit` with `total_count`, `returned_count`, `has_more` in the response envelope. Max 200 per page.
- **MCP server design** — 638-line design document for Phase 10 sidecar architecture (.NET 8 console app communicating with .NET 4.8 add-in over named pipes). See `plans/design-mcp-server.md`.
- **Install Adze.bat** — double-click installer for Windows Explorer; no command line required.
- **Dev mode in installer** — `install\install-adze.ps1` auto-detects when run from the repo, builds Debug config via MSBuild, and registers from build output instead of requiring a pre-staged zip.
- **Public repo files** — README.md, LICENSE (MIT), CONTRIBUTING.md, SECURITY.md, issue and PR templates under `.github/`.
- **Adze.Index.dll** added to installer required-DLL list (missing since Phase 6).

### Changed
- **UI visual redesign** — deep navy user bubbles (#1B3A6B), white assistant cards with box-shadow, SOLIDWORKS blue (#0072C6) accent throughout. Section headers with left-border accent. Alternating table rows. Write confirmation cards with rounded shadows. Recipe tags as blue pills.
- **Run button** — SOLIDWORKS blue flat style with hover/press states.
- **Header** — dark navy background with "Adze — SOLIDWORKS AI" title in white.
- **Test suite** — 616 → 666 unit tests (50 new tests for AgentPolicyEngine, pagination, and supporting infrastructure).

### Fixed
- **Install script encoding** — non-ASCII characters (em dashes, box-drawing) caused Windows PowerShell 5.1 to misparse strings on Windows-1252 systems. All non-ASCII characters replaced with ASCII equivalents.
- **.NET registry check** — now probes both `HKLM\SOFTWARE\...` and `WOW6432Node` paths for 32-bit PowerShell compatibility.
- **SOLIDWORKS registry check** — also checks WOW6432Node path.

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
