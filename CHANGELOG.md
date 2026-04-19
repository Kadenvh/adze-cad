# Changelog

All notable changes to Adze are documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Target: v1.0.0. **Not released** — release code is complete and R2026x crash is resolved; gated only on R5 live update-cycle validation (applying a real 3DX update against an installed Adze build, end to end) before tagging. See `plans/polish-and-v1-path.md`.

Zero-config first-run work below is code-complete in the tree.

### R-phase (2026-04-19) — R2026x crash resolution + update-lifecycle cooperation
- **`CompatibilityProbe`** — `src/Adze.Host/AddIn/CompatibilityProbe.cs` runs a read-only smoke test at `ConnectToSW` (RevisionNumber → GetCommandManager → CreateCommandGroup2 → RemoveCommandGroup2). Decorated with `[HandleProcessCorruptedStateExceptions]` + `[SecurityCritical]` so native CSEs surface as typed results instead of silent process termination. Probe gates ribbon + context-menu registration. Confirmed that `CreateCommandGroup2` is the R2026x SW 34.1.0.0140 interop break.
- **Task Pane probe-failure banner** — `health-warning` banner above the conversation area when the probe detected incompatibility. Users see which SOLIDWORKS build and which step failed; Task Pane remains fully functional.
- **`SwBuildStateService`** — persists last-verified SW build at `%LOCALAPPDATA%\Adze\state\sw-build.txt`. Changes trigger re-verification on next launch.
- **Pre-update eject (`PreUpdateEjectService`)** — detects `swxdesktopupdate.exe` during disconnect and clears the persisted build so the next launch re-verifies. Production uses `Process.GetProcessesByName`; tests inject a fake.
- **`Adze.Manager`** — new WinForms installer/manager app. Single window with install state, SW + 3DX-updater process state, last-verified SW build, API key presence, config path, and Install / Uninstall / Eject for Update / Refresh buttons. When the updater is detected running: Install disables + grays, Eject promotes to red bold with a ⚠ prefix. Release zip bundles `Adze.Manager.exe`; `.bat` wrappers prefer the Manager and fall back to the PowerShell script for headless environments.
- **R3.4 docs** — `SETUP.md` and `PRIVACY.md` document the update flow, the Eject-for-Update button, and what the compatibility probe reads/writes locally.

### Added
- **In-app Settings panel** — collapsible section in the Task Pane. Provider dropdown (OpenAI, Anthropic, Ollama, LM Studio), masked API key input, Save button. Stored provider displayed; Clear button removes stored key. Live read-only view of all feature gate states.
- **DPAPI-encrypted API key store** — keys saved at `%LOCALAPPDATA%\Adze\keys.dat`, encrypted under the current Windows user scope. Never leaves your machine, never syncs, never appears in env vars or logs.
- **User-facing feature gate config** — `%LOCALAPPDATA%\Adze\config.json` holds gate preferences. Resolution order: environment variable → config file → baked-in default. Power users keep their env var overrides; casual users never see env vars.
- **Safe defaults for zero-config first run** — ribbon tab, feature-tree context menu, AI model, agent loop, write tools, streaming, and retrieval are ON by default. Toast notifications, PropertyManager Page writes, and local-model providers remain opt-in.
- **Double-click installer wrappers** — `install-adze.bat` and `uninstall-adze.bat` ship alongside the PowerShell scripts. End users never have to open a terminal.
- **CommandManager ribbon tab** — persistent "Adze" tab in the SOLIDWORKS ribbon with Ask, Diagnose, Mates, Dimensions, Properties, and Explain buttons. Routes through the QuickAction COM bridge so intent parsing stays centralized. Default on; disable via Settings or `SOLIDWORKS_AI_RIBBON=false`.
- **Feature-tree context menu** — right-click any feature in the FeatureManager tree → "Ask Adze about this feature." Right-click in the graphics area → "Diagnose this model." Selected entity name flows into the prompt automatically. Default on.
- **Tray toast notifications** — optional balloon popup on run completion when the SOLIDWORKS window is not foregrounded. Opt-in via Settings.
- **PropertyManager Page write confirmation (proof-of-path)** — optional native SW PropertyManager Page for `set_dimension_value`. When enabled, writes surface in a native modal instead of the Task Pane HTML card. Opt-in via Settings. Full coverage for the other 6 write tools lands in v1.1.
- **`PRIVACY.md`** — formal privacy policy documenting zero telemetry transmission, local-only trace storage, and cloud-provider data flow explanation.
- **`CODE_OF_CONDUCT.md`** — Contributor Covenant 2.1 reference, reporting contact.
- **`docs/index.html`** — single-page landing at https://kadenvh.github.io/adze-cad/.

### Fixed
- **`get_mates` subassembly recursion** — previously returned empty on assemblies whose mates live inside subassembly components (observed on SpdrBot v14.SLDASM). `SessionContextBuilder.BuildMates` now walks `AssemblyDoc.GetComponents(false)` and recurses into each subassembly ModelDoc, deduping by PathName and respecting the 150-mate budget.

### Changed
- **User-facing tool outcome labels** — 10 grounding tools now report in plain language (for example "Read the active document" instead of "Active document resolved", "Read the feature tree" instead of "Feature-tree slice generated"). Internal identifiers in traces are unchanged.
- **Answer source footer** — internal identifiers like `deterministic_fallback` and `model_openai` now render as "Built-in broker" and "OpenAI" in the Task Pane. Traces continue to store the internal identifier.
- **Task Pane sub-header** — replaced the redundant "Adze — SOLIDWORKS AI" line (SOLIDWORKS already assigns "Adze for SOLIDWORKS" as the outer title) with the action-oriented "Ask anything about your model".
- **Suggested Recipes section** — count removed from the header to reduce noise on large recipe lists. List still expands on click.
- **Tool count** — public count reconciled from "19" to honest **18** (10 read + 1 retrieval + 7 write). `search_project_files` is retrieval, not double-counted as read + retrieval.
- **GitHub repo metadata** — description updated to reflect 18 tools, `mcp` added to topics (8 total), homepage pointed at the Pages landing page.
- **Test count** — 666 → 702 unit tests (18 new tests for FeatureGateConfigService + ApiKeyStore + updated gate registry; 8 tests for SwBuildStateService; 10 tests for HostState probe state + PreUpdateEjectService detection/clear-state/exception-tolerance).

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
