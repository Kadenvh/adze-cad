# Adze

**Version:** 0.1.1 shipped · v1.0.0 PAUSED (Decision #22) · v1.1 UI rebuild + Phase 11 embedded surfaces = primary track (Decision #21) | **Status:** Phase 11 in flight — **27 typed tools** (was 18), new `Adze.Mesh` project, new `IComputeTool` category, 5 new write tools (`create_sketch`, `add_sketch_line/circle/dimension`, `create_drawing_document`, `import_mesh`) + 2 compute tools (`get_mass_properties`, `measure_distance`) + read-only `analyze_mesh_file`. Two new direct-action ribbon buttons (`Import Mesh`, `Auto-Drawing`) bypass the chat sidebar entirely — first concrete examples of the "embedded AI tool, not chatbot" mission framing (memory `feedback_mission_embedded_not_chat` saved 2026-05-10). v1.1 UI rebuild Chunks 1+2+3 + Round 2 all landed; native sidebar gated behind `SOLIDWORKS_AI_NATIVE_SIDEBAR` (default OFF) until founder verifies live. Indigo `#4F46E5` visual refresh, `UiPalette` Light/Dark/System tokens, owner-drawn tabs, Claude-style chat bubbles. **823 tests green** (817 passed + 6 inconclusive smoke). **11 projects in `Adze.sln`** (10 production + 1 test). 0 open CodeQL alerts. **All 27 typed tools have JSON request schemas.** v1.1.0.0 release zip built (`install/adze-v1.1.0.0.zip`, 14 files, smoke-tested). | **Updated:** 2026-05-10 (Sessions 7+8)

---

## Critical Rules

### DO NOT
- Do not write files directly into `C:\Program Files\Dassault Systemes\...`. Treat the installed SOLIDWORKS tree as a read-only dependency and integration target.
- Do not modify `C:\SOLIDWORKS\settings\swSettings.sldreg` or shared data under `C:\SOLIDWORKS\SOLIDWORKS Data` unless the task explicitly requires it.
- Do not let the broker, scripts, Python, Node, or any external process call SOLIDWORKS COM directly. COM execution stays inside the in-process C# add-in.
- Do not use UI automation, desktop clicking, or computer-use control as the core product path.
- Do not add write-capable tools until preview, apply, verify, trace logging, and benchmark coverage are defined together.
- Do not default to machine-scope registration during development. User-scope registration is the normal path; machine-scope is for packaging or explicit beta validation.
- Do not assume a host validation failure is a code regression before checking launcher blockers. `3DEXPERIENCE Update` and login windows can prevent SOLIDWORKS from launching far enough to load the add-in.
- Do not assume a `Claude Max` subscription covers this app's model usage. This custom app uses Anthropic API credentials and billing, not consumer-plan limits.
- Do not duplicate core documentation scopes. Architecture rationale belongs in brain.db decisions, tactical next steps belong in brain.db notes and `plans/`.

### ALWAYS
- Always run project scripts with `pwsh -NoProfile -File <script.ps1>` unless a script explicitly depends on Windows PowerShell.
- Always update C# contracts and JSON schemas together.
- Always keep traces, logs, generated benchmark artifacts, and diagnostics inside the repo root or `%LOCALAPPDATA%\Adze`.
- Always follow `plan -> preview -> apply -> verify -> log` for any future write-capable CAD action.
- Always read the nearest directory `README.md` before editing a boundary directory.
- Always treat learning as trace capture, reviewed recipe promotion, achievements, exploration percentage, and trust-tier unlocks, not self-modifying autonomy.

---

## Quick Reference

Native desktop AI tool embedded in SOLIDWORKS — founder framing as of 2026-05-10 is "AI does CAD work, not a chat panel that talks about it" (memory: `feedback_mission_embedded_not_chat`). Chat sidebar is implementation scaffolding; the product is direct-action ribbon buttons + agent-callable tools that execute real geometry / dimension / drawing / mesh operations. The current repo contains a working raw add-in host, typed contracts and schemas, **27 typed tools** (10 grounding + 2 compute + 1 retrieval + 2 mesh + 12 write), trace/progression storage, a hybrid broker turn layer with structured status and recovery output, provider-routed model-backed answer synthesis with deterministic fallback, the new **`Adze.Mesh` library** (STL I/O + analysis + plane fitting, no SW interop), the **`Adze.UI` runtime library** (native WinForms sidebar), the **`Adze.Manager` 4-tab control panel**, the **`Adze.UiHarness` out-of-SOLIDWORKS dev shell**, and script-based validation harnesses. Two ribbon buttons (`Import Mesh`, `Auto-Drawing`) execute their flows directly without opening the chat panel.

**Build:** `pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks`
**Unit Tests:** `pwsh -NoProfile -File scripts\setup\run-tests.ps1`
**Host Validate:** `powershell.exe -NoProfile -File scripts\setup\validate-host-spike.ps1`
**Benchmarks:** `powershell.exe -NoProfile -File scripts\setup\run-grounding-benchmarks.ps1`
**Broker Evals:** `powershell.exe -NoProfile -File scripts\setup\run-broker-evals.ps1`
**Provider Smoke:** `pwsh -NoProfile -File scripts\setup\run-provider-smoke.ps1` (requires API key in env)
**Package Release:** `pwsh -NoProfile -File scripts\setup\package-release.ps1`
**Beta Install:** `powershell.exe -NoProfile -File install\install-adze.ps1`
**Trace Output:** `%LOCALAPPDATA%\Adze`
**Regression Reports:** `benchmarks\reports`

### Current Working Baseline
- `Adze.sln` builds successfully with local `MSBuild.exe` (**11 projects: 10 production + 1 test** — `Adze.Contracts`, `Adze.Tools`, `Adze.Trace`, `Adze.Broker`, `Adze.Host`, `Adze.Index`, `Adze.Mesh`, `Adze.Manager`, `Adze.UiHarness`, `Adze.UI`, plus `Adze.Tests`).
- The host add-in can register, load, create a Task Pane, collect live session context, execute the 11 read-only tools (10 grounding + 1 retrieval), and run a deliberate traced assistant turn when SOLIDWORKS launches cleanly.
- The broker now emits a structured turn with intent, confidence, blockers, recovery suggestions, and prioritized tool recommendations. The host uses a hybrid path: a provider-routed model client when API configuration is present, with the local deterministic planner preserved as fallback and as the stable broker-eval baseline.
- Assistant runs now attempt a second provider-routed synthesis pass over the executed tool results and fall back to the deterministic answer builder when the synthesis path is disabled, times out, or returns unusable output.
- The trace layer writes grounding snapshots, trace events, progression state, achievements, and recipe candidates.
- The curated benchmark and broker-eval scripts now write machine-readable JSON reports under `benchmarks/reports`.
- The compiled NUnit 3 unit test suite (`tests/Adze.Tests`) contains **767 tests local** (745 passed, 6 inconclusive smoke; **CI count proportionally lower** when `IncludeHostTests=false`). Covers broker orchestration, model response parsing, configuration, prompt composition, all 10 grounding tools, 7 write tools, 1 retrieval tool, write execution coordinator, snapshot/diff/verification, learning system (trust, recipe capture), per-document memory, cost budgets, feature gates, conversation state, agent loop, diagnostic intent routing, multi-turn context, OLE indexer, pre-update eject, **HostStateAdapter ↔ ITaskPaneHost mapping, ModelJsonMapper round-trip (Chunk 1), MarkdownToRichText, TaskPaneHost contract, QuickActionsBar (Chunks 2/3 UI tests under `tests/Adze.Tests/UI/`)**, and trace serialization. All pass in under 8 seconds. An additional 6 live provider smoke tests (Category: LiveProvider) exercise real API calls and skip gracefully when no API key is present (status: inconclusive, not failed).
- Both model clients (OpenAI and Anthropic) now parse `usage` fields from API responses and thread token counts through to the answer footer and Status tab. Session-level cumulative usage is tracked per add-in session.
- Launcher interruption hardening: multi-pattern blocker detection (Login/Update/Platform + CATSTART), JSON preflight report at `%LOCALAPPDATA%\Adze\logs\launcher-preflight.json`, retry with configurable timeout, validation preflight gate (exit code 3 distinguishes launcher block from code failure).
- Beta install/uninstall/packaging: `install/install-adze.ps1` (pre-flight checks, DLL copy, COM registration), `install/uninstall-adze.ps1` (cleanup with optional `-RemoveUserData`), `scripts/setup/package-release.ps1` (build Release, validate, stage, zip — moved from `install/` in session 10 per refactor Track A).
- Task Pane visual acceptance confirmed 2026-03-15. Run state label shows meaningful status for blocked/disconnected states and token counts for normal runs.
- Current validated state on 2026-03-23: build PASS (0 errors, 0 warnings), unit tests `616` (610 passed, 6 inconclusive smoke), broker evals `12/12`, live provider smoke `6/6` via OpenRouter, visual acceptance PASS, all 8 Task Pane features validated in live SOLIDWORKS.
- **Agentic tool loop implemented (Phase 2 complete).** Set `SOLIDWORKS_AI_AGENT_LOOP=true` to enable iterative model-driven tool calling. The model calls tools, observes results, and iterates until it produces a final answer. Existing single-turn path remains as default fallback.
- New agentic infrastructure: `OpenAIFormatAgentClient` (HTTP tool calling), `AgentLoopRunner` (iterative loop with cancellation/error budgets), `AgentToolDispatcher` (maps to 10 grounding tools + 4 write tools), `ToolDefinitionBuilder` (schema generation for read and write tools), `AgentModelClientFactory` (feature-gated).
- **Write tool safety infrastructure (Phase 3):** `IStateSnapshotService`, `IStateDiffService` (`StateDiffService`), `IVerificationPolicy` (`DefaultVerificationPolicy`), `WriteTraceRecordBuilder`. All write contracts in `Adze.Contracts/Models/WriteContracts.cs` and `Adze.Contracts/Abstractions/IWriteServices.cs`.
- **First-wave write tools (Phase 4 core):** `SetCustomPropertyTool`, `SetDimensionValueTool`, `SuppressFeatureTool`, `UnsuppressFeatureTool` in `Adze.Tools/Write/`. Full `IWriteTool<TParams>` interface with Preview/Apply/Verify/BuildUndoLabel. `WriteExecutionCoordinator` orchestrates the 8-step lifecycle. Feature-gated behind `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true`.
- Pre-prompt clarification UI: collapsible panel with Intent/Scope/Output/Diagnostics controls populated from live SessionContext data. Prefix-based integration with zero broker changes.
- Cancel button: Run button toggles to Cancel during execution, triggers CancellationTokenSource.
- **HTML answer panel (T9-01):** WebBrowser control with Markdown→HTML conversion and chat-style rendering.
- **Conversational chat history (T9-05):** `ChatEntry` tracking in `HostState` with document-aware clearing. User/assistant bubble rendering with per-message footer.
- **Write confirmation cards (T4-09):** `PendingWriteAction` tracking with `WriteTrackingToolExecutor`. Inline before/after confirmation cards with Apply/Cancel buttons. Direct COM apply for all 4 first-wave write tools.
- **Diagnostic intent (T9-02):** Clarification prefix parsing (`ExtractClarificationIntent`), expanded keyword detection ("what's wrong", "broken", "failed"), diagnostic tool boosting, intent-aware agent and synthesis prompts.
- **Multi-turn agent context:** `AgentLoopRunner` accepts prior conversation history. `HostState.BuildPriorConversation()` bridges chat history → `ConversationTruncator` (max 20 messages, 6 protected recent) → OpenAI-format messages.
- **Write history (T4-10):** `CompletedWriteEntry` tracking, auto-record on successful apply, `GetWriteHistory()`/`ClearWriteHistory()`.
- **OLE closed-file indexer (T6-03):** `Adze.Index` project with OpenMcdf NuGet. `OlePropertyReader` reads SOLIDWORKS files without COM. `ClosedFileIndexer` scans folders and persists JSON index. `ClosedFileSearchService` queries by property/keyword/type/path.
- **Collapsible-sections UI redesign:** Replaced tab bar with collapsible Write History, Plan, Tools Log, Status sections below conversation thread. Single-scroll layout gives conversation full height.
- **SearchProjectFilesTool (T6-04):** `search_project_files` wired as 16th tool. Wraps `ClosedFileIndexer` + `ClosedFileSearchService`. Feature-gated behind `SOLIDWORKS_AI_RETRIEVAL=true`. Dispatched via `AgentToolDispatcher`, schema via `ToolDefinitionBuilder.BuildRetrievalToolDefinitions()`.
- **Recipe suggestions UI (T5-04/T9-03):** Collapsible "Suggested Recipes" section in Task Pane. Shows promoted and review-ready recipes with Run/Promote buttons. `AgentRecipeCaptureService.ListReviewReady()` lists candidates. One-click recipe execution via `RunRecipe()` JS bridge.
- **Local model support (T8-02):** Ollama (`SOLIDWORKS_AI_PROVIDER=ollama`) and LM Studio (`SOLIDWORKS_AI_PROVIDER=lmstudio`) as experimental providers. Routes through `OpenAIModelClient`. Default endpoints: `localhost:11434` (Ollama), `localhost:1234` (LM Studio). Longer default timeouts (60s broker, 90s synthesis). `BrokerModelSettings.IsLocalProvider` / `UsesOpenAIFormat`.
- **Streaming synthesis (T8-03):** SSE streaming for final answer synthesis via `IStreamingModelClient` / `SseStreamReader`. Text streams to WebBrowser via `InvokeScript("appendStreamChunk")`. Feature-gated behind `SOLIDWORKS_AI_STREAM_FINAL_TEXT=true`. Works with OpenAI, OpenRouter, Ollama, and LM Studio.
- **Local endpoint health check:** `LocalEndpointHealthCheck.Check()` pings `GET /v1/models` to verify Ollama/LM Studio server readiness, model availability. Returns `LocalHealthStatus` (Ready, Unreachable, NoModels, ModelNotFound, Error).
- **Graceful degradation:** Local model synthesis failures show "Local model (provider) response was unusable — used deterministic planner" instead of generic error messages.
- **Agentic loop final-turn streaming:** `IStreamingAgentModelClient` extends `IAgentModelClient` with `SendTurnStreaming`. `OpenAIFormatAgentClient` detects text vs tool-call responses from SSE deltas — text streams via callback, tool calls buffer internally. `AgentLoopRunner` streaming overload routes to streaming when client supports it. Same feature gate (`SOLIDWORKS_AI_STREAM_FINAL_TEXT=true`) and same TaskPaneControl JS functions as classic streaming.
- **Local endpoint health check UI:** `HostState.RunLocalHealthCheckAsync()` runs `LocalEndpointHealthCheck.Check()` on background thread at startup. Results display in Status section with styled health banners (ready/warning/error) and actionable guidance (e.g., "Start the server: `ollama serve`").
- **Capability gate probing (T8-02b):** `ToolCallCapabilityProbe` sends a minimal tool-calling request to local models before enabling the agentic loop. If the model doesn't support tool calling (responds with text instead of `tool_calls`), falls back to classic synthesis-only path automatically. Results cached per provider+model.
- **Experimental label (T8-02c):** Local providers (Ollama/LM Studio) show `[Experimental]` in answer footer (both classic and agentic paths). Health check ready banner shows "Local model support is experimental." guidance.
- **Rate-limit handling (T8-05):** `RateLimitHelper` detects HTTP 429, parses `Retry-After` header (capped at 15s), retries once with backoff. Applied in `OpenAIModelClient`, `AnthropicMessagesModelClient`, `OpenAIFormatAgentClient`. Exhausted retries show "Rate limited by provider. Try again in a few seconds."
- **Tool result truncation (T8-04):** `AgentLoopRunner` enforces `MaxToolResultChars` (default 8192, env `SOLIDWORKS_AI_AGENT_MAX_TOOL_RESULT_CHARS`) on tool results before they enter conversation history. `GetReferenceGraphTool` has `Limit` parameter (default 100).
- **Write plan review UI (T7-01):** When 2+ actionable pending writes exist, shows "Write Plan (N steps)" header with Apply All / Cancel All buttons above individual confirmation cards.
- **Batch write execution (T7-02):** `ApplyAllPendingWrites()` executes approved steps sequentially via `ApplyPendingWrite`. Stops on first failure, records partial progress in write history. `CancelAllPendingWrites()` cancels all unapplied writes.
- **IUiThreadInvoker (TX-02):** `IUiThreadInvoker` interface in Contracts for clean COM threading abstraction. `WinFormsUiThreadInvoker` in Host marshals to UI thread via `Control.Invoke`. `SynchronousUiThreadInvoker` in Broker for unit tests. `HostState.ApplyPendingWrite` uses it for automatic UI-thread marshaling when available.
- **Session telemetry (T8-06):** `SessionTelemetry` in `Adze.Broker/Models/` tracks tool call frequency (ranked), run outcomes (success/cancelled/failed/fallback), write apply/cancel/fail rates, cancellation phase detail (API call vs tool execution vs user), recipe capture/promotion counts, agentic vs classic path counts. Dashboard renders in Status section with top tools, success rates, and write stats.
- **Cost budget UI (T8-01):** Usage dashboard in Status section shows run count, token breakdown (prompt/completion), session budget progress bar (color-coded green/yellow/red), and estimated cost. Warning banner (`health-warning`) when approaching budget limit. Error banner (`health-error`) when budget exhausted. Built on existing `CostBudgetSettings`/`BudgetStatus`.
- **Error presentation tiers (TX-03):** `ErrorClassifier` in `Adze.Broker/Orchestration/` classifies exceptions into 3 tiers: ToolError (non-prominent), ApiError (rate limit/auth/timeout with guidance), HostError (COM/SOLIDWORKS with recovery guidance). Never shows stack traces. `FormatForUser()` produces clean user-facing messages. `FormatAgentOutcomeMessage()` provides friendly text for agent loop failures.
- **Dependency preview (T7-04):** `DependencyAnalyzer` in `Adze.Tools/Write/` analyzes cascade risk for suppression and dimension changes. Checks feature tree ordering, type heuristics (sketch→extrusion, extrusion→fillet/shell/pattern), dimension references, mate references. `CascadeRisk` enum (None/Low/Medium/High). Integrated into `SuppressFeatureTool.Preview`.
- **Rename object tool (T7-03):** `RenameObjectTool` — 5th write tool. Preview validates feature exists, checks name collisions, warns about dimension references. Apply sets `feature.Name` via COM. Verify confirms new name in refreshed feature tree. Wired into `AgentToolDispatcher`, `ToolDefinitionBuilder`, `HostState.ApplyWriteToolDirect`.
- **Insert component tool (T7-03b):** `InsertComponentTool` — 6th write tool, Class 3 (HardWriteAdvanced). Assembly-only. Preview validates assembly doc type, file extension, duplicate detection, shows insertion coordinates. Apply uses `AddComponent5()` via COM. Verify checks reference graph. Elevated confirmation UI with warning styling.
- **Elevated confirmation UI:** `PendingWriteAction.IsElevated` flag. Class 3 tools get orange-bordered cards with "Elevated Change" header. `WriteTrackingToolExecutor.ElevatedToolNames` set controls which tools trigger elevated treatment.
- **Drawing view tool (T7-03c):** `CreateDrawingViewTool` — 7th write tool, Class 3 (HardWriteAdvanced). Drawing-only. Creates standard views (front/back/top/bottom/left/right/isometric/trimetric/dimetric) via `CreateDrawViewFromModelView3()`. Validates doc type, view type, scale. Elevated confirmation.
- **Pagination (T8-04c):** `GetDimensionsTool` and `GetMatesTool` now support `offset`/`limit` parameters for pagination. Response includes `total_count`, `offset`, `returned_count`, `has_more`. Max limit capped at 200. Tool definitions updated. 18 typed tools total (10 read + 1 retrieval + 7 write).
- **Configuration-scoped writes (T7-03d):** Suppress/unsuppress tools use `swThisConfiguration` (1) with named config array when `configuration_name` specified, `swAllConfiguration` (2) when not. `SetDimensionValue` uses `SetSystemValue3` with config-specific mode. HostState COM apply paths updated. Preview summaries include config name.
- **Agent progress UI:** `AgentLoopRunner` progress callbacks now wired through `HostState.CompleteAssistantRun` → `TaskPaneControl.UpdateRunProgress`. Run state label shows: "Calling get_dimensions...", "Thinking (turn 2)...", "Generating answer...", "Error encountered."
- **Undo label tracking:** Write history entries now include undo labels extracted from write preview data. Displayed in the Write History section with italic styling. Users can find the matching undo entry in SOLIDWORKS Edit menu.
- **Request queuing (T8-05b):** `RateLimitHelper` now tracks active rate limit windows via `RecordRateLimitWindow`/`IsInRateLimitWindow`/`WaitIfRateLimited`. All model clients (OpenAI, Anthropic, Agent) call `WaitIfRateLimited()` before making API requests. Prevents hammering the provider during active cooldowns.
- **v1.1 UI rebuild — Chunk 1 (foundation, Session 4):** `ModelJsonMapper.ToSessionContext` + `DeserializeSessionContextJson` deserializer. `Adze.Manager` refactored from a single `MainForm` into a 4-tab `TabControl` (Logs / Settings / Agent Profile / Status) backed by `LogsTab.cs`, `SettingsTab.cs`, `AgentProfileTab.cs`, `StatusTab.cs`.
- **v1.1 UI rebuild — Chunk 2 (sidebar foundation, Session 4):** New project `src/Adze.UI/` (Class Library, .NET 4.8, AssemblyVersion 1.1.0.0, no SOLIDWORKS interop). `ITaskPaneHost` interface in `Adze.Contracts.Abstractions`. `ChatEntry` + `PendingWriteAction` promoted to public types in `Adze.Contracts.Models`. `Adze.UI/V2/` hosts `NativeTaskPaneControl`, `ChatMessageView`, `WriteCardView`, `QuickActionsBar`, `RefinementPanel`, `UiPalette` (with Light/Dark/System tokens). Markdown rendering by `Adze.UI/Rendering/MarkdownToRichText.cs`. `src/Adze.UiHarness/` is the out-of-SW dev shell with `StubHostState` for hot iteration without SOLIDWORKS.
- **v1.1 UI rebuild — Chunk 3 (production cutover + visual refresh, Session 4):** `HostStateAdapter` (instance bridge to static `HostState`) implements `ITaskPaneHost`. `NativeTaskPaneControlShim` is the COM-registered shim (`ProgID Adze.Host.NativeTaskPaneControl`, `Guid C8B41F45-D2A6-4B5E-9F7C-3E0A1D8B2F61`) that mounts the inner native control on `OnHandleCreated`, wires banners (`SetProbeBanner`, `SetHealthBanner`, `SetBudgetBanner`), and refreshes on `StateChanged`. Real broker wiring through `HostState`'s existing `CompleteAssistantRun` path. `FinalizeStream` atomic finalization. Indigo accent `#4F46E5`, refreshed palette in `UiPalette.cs`, owner-drawn tabs, Claude-style chat bubbles, refreshed write cards, `QuickActionsBar` with 5 chips below the prompt.
- **NativeSidebar feature gate:** `SOLIDWORKS_AI_NATIVE_SIDEBAR` constant in `FeatureGateRegistry`, default OFF (`IsDefaultEnabled` returns `false`). When ON, SW loads `NativeTaskPaneControlShim` instead of legacy `TaskPaneControl`. The legacy 1912-line WebBrowser surface remains the default until the founder verifies the new sidebar live in SOLIDWORKS — deletion deferred.
- Full agentic vision: `plans/END-GOAL-FINAL.md` (700 lines), `plans/IMPLEMENTATION-BLUEPRINT.md` (contracts), `plans/tasks/TASK-INDEX.md` (60+ tasks). Supported by 4 discovery + 8 research briefs.
- v1.1 UI rebuild plan: `plans/ui-rebuild-v1.1.md` (master plan, Session 4) and `plans/adr-001-native-winforms-ui-authority.md` (architecture decision record for the WebBrowser→native pivot).

### Local Environment

| Path | Role |
|------|------|
| `C:\adze-cad` | Repo root and canonical location for source, docs, schemas, scripts, and benchmark metadata |
| `C:\SOLIDWORKS` | Local user data, sample files, shared data, and settings references |
| `C:\Program Files\Dassault Systemes\SOLIDWORKS 3DEXPERIENCE R2026x` | Installed SOLIDWORKS host and interop dependency source |

### Toolchain Facts
- `MSBuild.exe` is available from Visual Studio Community at `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`.
- No local `dotnet` SDK is installed. Treat the repo as a Visual Studio/MSBuild-first workflow.
- PowerShell execution policy is compatible with direct script runs; the known failure mode is detached launch context, launcher state, or elevation, not a baseline execution-policy block.

### Model Path Configuration
- Set `SOLIDWORKS_AI_ENABLE_MODEL=true` to let the host attempt the model-backed broker path.
- Set `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true` to enable write tool definitions in the agentic loop (requires `SOLIDWORKS_AI_AGENT_LOOP=true`). Write tools return previews; actual apply requires host confirmation UI.
- Use `SOLIDWORKS_AI_PROVIDER=openai` or `SOLIDWORKS_AI_PROVIDER=anthropic` to force a provider. If unset, Adze picks `openai` when only an OpenAI key is present, otherwise `anthropic` when an Anthropic key is present.
- OpenAI auth: `SOLIDWORKS_AI_OPENAI_API_KEY` or `OPENAI_API_KEY`.
- Anthropic auth: `SOLIDWORKS_AI_ANTHROPIC_API_KEY` or `ANTHROPIC_API_KEY`.
- Provider-specific overrides:
  `SOLIDWORKS_AI_OPENAI_MODEL`, `SOLIDWORKS_AI_OPENAI_ENDPOINT`
  `SOLIDWORKS_AI_ANTHROPIC_MODEL`, `SOLIDWORKS_AI_ANTHROPIC_ENDPOINT`, `SOLIDWORKS_AI_ANTHROPIC_VERSION`
- Shared broker/synthesis behavior overrides:
  `SOLIDWORKS_AI_MAX_TOKENS`
  `SOLIDWORKS_AI_SYNTHESIS_MAX_TOKENS`
  `SOLIDWORKS_AI_TIMEOUT_MS`
  `SOLIDWORKS_AI_SYNTHESIS_TIMEOUT_MS`
  `SOLIDWORKS_AI_TEMPERATURE`
- Backward compatibility: the existing `SOLIDWORKS_AI_ANTHROPIC_*` timeout/token/temperature variables are still accepted as fallback names.
- Runtime answer sources are provider-specific: `model_openai`, `model_anthropic`, or `deterministic_fallback`.
- If the model path is unavailable, invalid, times out, or returns unusable output, the host falls back to the deterministic broker automatically. Local model failures show "Local model (provider) response was unusable — used deterministic planner".
- **Streaming synthesis:** Set `SOLIDWORKS_AI_STREAM_FINAL_TEXT=true` to enable SSE streaming for the synthesis pass. Text tokens stream to the WebBrowser as they arrive. Works with OpenAI, OpenRouter, Ollama, and LM Studio. Tool-calling turns remain fully buffered.
- **Local model providers (experimental):** Set `SOLIDWORKS_AI_PROVIDER=ollama` or `SOLIDWORKS_AI_PROVIDER=lmstudio`. Default endpoints: `http://localhost:11434/v1/chat/completions` (Ollama), `http://localhost:1234/v1/chat/completions` (LM Studio). Custom model/endpoint via `SOLIDWORKS_AI_OLLAMA_MODEL`, `SOLIDWORKS_AI_OLLAMA_ENDPOINT`, `SOLIDWORKS_AI_LMSTUDIO_MODEL`, `SOLIDWORKS_AI_LMSTUDIO_ENDPOINT`. Local providers use longer default timeouts (60s broker, 90s synthesis). `LocalEndpointHealthCheck.Check()` verifies server/model readiness. Best for synthesis only — tool calling via local models is experimental. See `plans/research-local-model-feasibility.md`.

---

## File Structure

```text
.
├── CLAUDE.md
├── README.md
├── SETUP.md
├── LICENSE
├── CONTRIBUTING.md
├── SECURITY.md
├── .github/                     # Issue templates, PR template
├── Adze.sln
├── src/
│   ├── README.md
│   ├── Adze.Host/        # In-process add-in, Task Pane UI, context capture, COM shims
│   ├── Adze.Contracts/   # Shared models, enums, tool names, ITaskPaneHost
│   ├── Adze.Tools/       # Grounding tool handlers and catalog
│   ├── Adze.Trace/       # Trace recording, progression, recipes, storage
│   ├── Adze.Broker/      # Prompt formatting and broker turn orchestration
│   ├── Adze.Index/       # OLE Structured Storage closed-file indexing
│   ├── Adze.Manager/     # 4-tab control panel (Logs/Settings/Agent Profile/Status)
│   ├── Adze.UiHarness/   # Out-of-SOLIDWORKS dev shell (hot iteration, stub host)
│   ├── Adze.UI/          # Native WinForms sidebar v2 (NativeTaskPaneControl + supporting controls)
│   └── Adze.Mesh/        # Mesh I/O + analysis (Phase 11 — STL reader, stats, primitive plane fitting)
├── schemas/
│   ├── context/                  # Broker-facing session context schema
│   ├── tools/                    # Tool request schemas and result envelope
│   └── traces/                   # Trace, recipe, and progression schemas
├── benchmarks/
│   ├── grounding/                # Curated corpus manifest and grounding tasks
│   └── reports/                  # Persisted JSON benchmark and eval reports
├── scripts/
│   └── setup/                    # Build, registration, launch, validation, and test runner scripts
├── tests/
│   └── Adze.Tests/               # NUnit 3 compiled unit tests (767 local, all layers — incl. UI/ subdir for Adze.UI tests)
├── tools/
│   └── nuget.exe                 # NuGet CLI for package restoration (auto-downloaded)
├── install/                      # Reserved packaging/install boundary
├── solidworks-partner/           # Partner application materials (GITIGNORED — confidential)
└── plans/                       # Active strategy documents and research briefs
    ├── archive/                  # Superseded plans with extraction receipts
    └── tasks/
        └── TASK-INDEX.md        # 60+ task breakdown across all phases
```

---

## Runtime and Schema Reference

### Source Projects

| Project | Purpose |
|---------|---------|
| `src/Adze.Host` | SOLIDWORKS add-in lifecycle, Task Pane, live session collection, host-side orchestration; hosts `NativeTaskPaneControlShim` (v1.1 COM shim), `HostStateAdapter` (v1.1 instance bridge), `InvokeMeshImport` + `InvokeAutoDrawing` (Phase 11 ribbon-direct flows). |
| `src/Adze.Contracts` | Shared typed boundary for context, tools, traces, progression, benchmark contracts; defines `ITaskPaneHost` plus `ChatEntry` + `PendingWriteAction` public models. `ToolExecutionContext.Application` (Phase 11) carries live `ISldWorks` to compute tools. |
| `src/Adze.Tools` | Read tools (`Grounding/`), compute tools (`Compute/` — Phase 11, `IComputeTool<T>`), mesh-analysis tool (`Mesh/`), and write tool implementations (`Write/`). |
| `src/Adze.Trace` | JSON persistence for traces, snapshots, recipes, and progression state; `ModelJsonMapper` round-trips `SessionContext` ↔ JSON for the harness. |
| `src/Adze.Broker` | Prompt formatting, OpenAI/Anthropic client integration, hybrid structured broker turn layer; defines `FeatureGateRegistry` (incl. `SOLIDWORKS_AI_NATIVE_SIDEBAR`, `SOLIDWORKS_AI_MESH_IMPORT`). |
| `src/Adze.Index` | Closed-file OLE Structured Storage indexing for offline property/metadata search. |
| `src/Adze.Mesh` | **Phase 11.** Pure-managed mesh I/O + analysis (no SW interop). `Models/` (Vector3d, Triangle, Mesh, AABB, Statistics, Analysis, PlaneFit, PrimitiveFitResults), `Io/StlReader` (binary + ASCII), `Analysis/MeshStatsCalculator`, `Analysis/MeshAnalyzer`, `Analysis/PrimitiveFitter`. |
| `src/Adze.Manager` | Standalone WinForms control panel (`Adze.Manager.exe`) — 4 tabs: Logs / Settings / Agent Profile / Status; install / uninstall / eject-for-update orchestration. |
| `src/Adze.UiHarness` | Out-of-SOLIDWORKS dev shell — mounts `NativeTaskPaneControl` against `StubHostState` for hot iteration without SW. |
| `src/Adze.UI` | Native WinForms sidebar v2 (`NativeTaskPaneControl`, `ChatMessageView`, `WriteCardView`, `QuickActionsBar`, `RefinementPanel`, `MarkdownToRichText`, `UiPalette` Light/Dark/System) — runtime UI library, no SOLIDWORKS interop. |
| `tests/Adze.Tests` | NUnit 3 unit tests (823 local). Phase 11 additions: `Mesh/StlReaderTests` + `Mesh/MeshStatsCalculatorTests` + `Mesh/MeshAnalyzerTests` + `Mesh/PrimitiveFitterTests` + `Tools/AnalyzeMeshFileToolTests` + `Tools/ImportMeshToolTests` + `Tools/SketchToolTests` + `Tools/CreateDrawingDocumentToolTests` + `Tools/GetMassPropertiesToolTests` + `Tools/MeasureDistanceToolTests`. |

### Session Context Slices
- `session`, `environment`, `document`, `selection`
- `feature_tree`, `configurations`, `dimensions`, `mates`
- `reference_graph`, `properties`, `diagnostics`, `policy`

### Live Wave 1 Tools (Read — Grounding, snapshot-only)
1. `get_active_document`
2. `get_document_summary`
3. `get_selection_context`
4. `get_feature_tree_slice`
5. `get_dimensions`
6. `get_configurations`
7. `get_custom_properties`
8. `get_mates`
9. `get_rebuild_diagnostics`
10. `get_reference_graph`

### Compute Tools (Phase 11 — read-only, require live `ISldWorks` via `IComputeTool<T>`)
11. `get_mass_properties` — volume / area / mass / COM / principal axes (Evaluate-tab parity)
12. `measure_distance` — distance / projections / angle / area between selected entities (Evaluate-tab parity)

### Retrieval Tool (Phase 6, gated behind `SOLIDWORKS_AI_RETRIEVAL`)
13. `search_project_files` — scans closed SOLIDWORKS files via OLE, searches by keyword/property/type

### Mesh Tools (Phase 11, gated behind `SOLIDWORKS_AI_MESH_IMPORT`)
14. `analyze_mesh_file` — STL stats, manifold/closure, convertibility verdict, advisories. No SW required.
15. `import_mesh` — Class 3 elevated. Opens STL as part document via 3D Interconnect.

### Write Tools — First Wave (Phase 4, gated behind `SOLIDWORKS_AI_FIRST_WAVE_WRITES`)
16. `set_custom_property` — no rebuild required
17. `set_dimension_value` — rebuild required
18. `suppress_feature` — rebuild required, cascade warning
19. `unsuppress_feature` — rebuild required
20. `rename_object` — no rebuild required
21. `insert_component` — Class 3 elevated, assembly only
22. `create_drawing_view` — Class 3 elevated, drawing only

### Write Tools — Phase 11 (gated behind `SOLIDWORKS_AI_FIRST_WAVE_WRITES`)
23. `create_sketch` — Class 3 elevated, creates sketch on plane/face
24. `add_sketch_line` — sketch-plane local mm
25. `add_sketch_circle` — sketch-plane local mm
26. `add_sketch_dimension` — drives sketch entity to target value
27. `create_drawing_document` — Class 3 elevated, new drawing with optional standard views

### Learning and Progression Artifacts
- `schemas/traces/trace-event.schema.json`
- `schemas/traces/recipe-candidate.schema.json`
- `schemas/traces/progression-state.schema.json`
- Runtime storage under `%LOCALAPPDATA%\Adze\state` and `%LOCALAPPDATA%\Adze\traces`

---

## Common Tasks

### Validate JSON schemas
```powershell
pwsh -NoProfile -File scripts\setup\validate-json-schemas.ps1
```

### Run compiled unit tests
```powershell
pwsh -NoProfile -File scripts\setup\run-tests.ps1
```

### Build the full solution
```powershell
pwsh -NoProfile -File scripts\setup\build-all.ps1 -StopSolidWorks
```

### Register, launch, and reload the host
```powershell
powershell.exe -NoProfile -File scripts\setup\reload-host.ps1 -SamplePath C:\SOLIDWORKS\penjamin\Part1.SLDPRT
```

### Check whether launcher state is blocking host validation
```powershell
powershell.exe -NoProfile -File scripts\setup\launch-and-check-host.ps1
```

### Run the end-to-end host validation
```powershell
powershell.exe -NoProfile -File scripts\setup\validate-host-spike.ps1
```

### Run the curated grounding benchmark suite
```powershell
powershell.exe -NoProfile -File scripts\setup\run-grounding-benchmarks.ps1
```

### Run broker tool-selection evals
```powershell
powershell.exe -NoProfile -File scripts\setup\run-broker-evals.ps1
```

### Inspect the latest regression reports
```powershell
Get-ChildItem benchmarks\reports
```

---

## Known Issues and Watch Items
- The launcher can currently block live validation with an update or login window before SOLIDWORKS loads the add-in.
- Legacy `HKLM\Software\Classes` and `HKLM\Software\SolidWorks\AddIns` entries from earlier machine-scope registration attempts may still exist on this machine.
- **Maker license may block COM add-in loading** — untested. If confirmed, Adze's community audience is limited to commercial license holders. Test plan in `solidworks-partner/maker-addin-test-plan.md`.
- **SOLIDWORKS Solution Partner application in progress** — draft complete in `solidworks-partner/` (gitignored). Blocked on web presence (public GitHub repo, VH Tech site) and personal details (TIN). See `solidworks-partner/application-draft.md`.
- **Multi-CAD expansion planned** — Fusion 360 (Python add-in) target decided but deferred until SOLIDWORKS is stable. Broker/AI logic portable, tools/host are platform-specific rewrites.
- **Simulation grounding tools designed but blocked** — 3 tool contracts ready (`get_simulation_studies`, `get_topology_study_params`, `get_simulation_results`). Requires SOLIDWORKS Simulation Professional license. API verified from 2026 docs. See `solidworks-partner/sim-tools-design.md`.
- **MCP server design complete** — 638-line design document in `plans/design-mcp-server.md`. Sidecar architecture (.NET 8 console app + named pipes to .NET 4.8 add-in). Phase 10+.
- **Public repo prep complete** — README.md, LICENSE (MIT), CONTRIBUTING.md, SECURITY.md, .github/ templates all created. Ready for `git push` and GitHub visibility.
- **AgentPolicyEngine (T4-05) implemented** — trust-gated tool access. Read tools always allowed, first-wave writes require Assisted tier, advanced writes require Reviewed tier. Read-only documents block writes. 27 tests.
- `tests/Adze.Tests` is the compiled NUnit 3 unit test project (**767 tests local**, covering all 7 write tools (set_custom_property, set_dimension_value, suppress/unsuppress_feature, rename_object, insert_component, create_drawing_view) plus pre-update eject service, conversation state, agent contracts, tool dispatcher, loop runner, usage parsing, write tool infrastructure, write execution coordinator, learning system, memory store, cost budgets, feature gates, diagnostic intent, multi-turn context, OLE indexer, search tool, recipe listing, local provider config, SSE streaming, local endpoint health check, agentic streaming, capability gate probing, rate limiting, tool result truncation, UI thread invoker, session telemetry, error classifier, dependency analyzer, agent policy engine, large assembly pagination, write tool dispatch, **HostStateAdapter mapping, ModelJsonMapper round-trip, MarkdownToRichText, TaskPaneHost contract, QuickActionsBar**, and 6 live provider smoke tests). **Known issue:** NUnit runs of PreUpdateEjectServiceTests pollute production `%LOCALAPPDATA%\Adze\logs\host.log` (see `dal arch get known-issue.filelogger-test-pollution`).
- **v1.1 UI rebuild — Round 2 LANDED.** `Adze.UI/V2/RefinementPanel.cs` is the rebuilt clarification UI (replaces the placeholder MessageBox path from Chunk 3). `UiPalette.cs` ships Light/Dark/System theme tokens (`UiPaletteTokens.Light`/`UiPaletteTokens.Dark`) with OS theme detection at startup; live OS-theme tracking deferred to v1.2. Founder must still verify the new sidebar live in SW before flipping `SOLIDWORKS_AI_NATIVE_SIDEBAR` from default-OFF to default-ON.
- **Legacy `TaskPaneControl.cs` (1912 lines) untouched** — still the default sidebar until the founder verifies the new native sidebar live. Several brain.db `criticism subagent v0.1.2 UI review` items become moot post-cutover; deletion of the legacy control deferred until the new sidebar is verified.
- **RESOLVED — SW add-in enable state persistence (Session 6, 2026-05-04).** Root cause: `install-adze.ps1` was writing `Title` and `Description` REG_SZ values under `HKCU\Software\SolidWorks\AddInsStartup\{GUID}` in addition to the `(Default)` REG_DWORD. Native SOLIDWORKS R2026x add-ins use ONLY the `(Default)` DWORD on this key; the extra REG_SZ entries caused SW to treat the entry as malformed and normalize `(Default)` back to 0 on shutdown. Fix in `install/install-adze.ps1`; Decision #23 codifies the invariant.
- `bin/` and `obj/` outputs are present in the repo tree. Ignore them unless the task is specifically about build output or registration artifacts.

---

## Docs Map

**Start here:** [`plans/project-index.md`](plans/project-index.md) — the canonical "where things live" map across the entire repo. Built session 10 from a parallel folder audit. Sections cover src/, tests/, schemas/, install/, scripts/, plans/, root docs, benchmarks/, and the continuity layer. Every doc below is indexed there.

- [`install/CLAUDE.md`](install/CLAUDE.md) — release procedure (build → validate → package → install → verify). Validated end-to-end session 10 with DLL SHA256 traceability.
- [`plans/phase11-embedded-ai.md`](plans/phase11-embedded-ai.md) — current execution track (Wave 1 landed, Wave 2-5 scoped).
- [`plans/refactor-and-cleanup.md`](plans/refactor-and-cleanup.md) — next-session dejunk scope.
- [`plans/ui-rebuild-v1.1.md`](plans/ui-rebuild-v1.1.md) — v1.1 native sidebar rebuild.
- [`plans/END-GOAL-FINAL.md`](plans/END-GOAL-FINAL.md) — 700-line agentic vision.
- [`plans/IMPLEMENTATION-BLUEPRINT.md`](plans/IMPLEMENTATION-BLUEPRINT.md) — C# interface contracts.
- `plans/tasks/TASK-INDEX.md` — 60+ task breakdown.
- `plans/design-mcp-server.md` — MCP sidecar design (Phase 10+).
- `solidworks-partner/` (GITIGNORED) — partner application materials, sim tool designs, competitor analysis, Maker add-in test plan.
- Directory `README.md` files under `src/`, `schemas/`, `benchmarks/`, and `scripts/setup/` are the local navigation layer for those folders.

## Session Export

After significant sessions (decisions made, version changed, cross-project work), export a structured session note:
```bash
node .ava/dal.mjs session-export session "summary"
```

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **adze-cad** (4548 symbols, 17580 relationships, 300 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> If any GitNexus tool warns the index is stale, run `npx gitnexus analyze` in terminal first.

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `gitnexus_impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `gitnexus_detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `gitnexus_query({query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `gitnexus_context({name: "symbolName"})`.

## When Debugging

1. `gitnexus_query({query: "<error or symptom>"})` — find execution flows related to the issue
2. `gitnexus_context({name: "<suspect function>"})` — see all callers, callees, and process participation
3. `READ gitnexus://repo/adze-cad/process/{processName}` — trace the full execution flow step by step
4. For regressions: `gitnexus_detect_changes({scope: "compare", base_ref: "main"})` — see what your branch changed

## When Refactoring

- **Renaming**: MUST use `gitnexus_rename({symbol_name: "old", new_name: "new", dry_run: true})` first. Review the preview — graph edits are safe, text_search edits need manual review. Then run with `dry_run: false`.
- **Extracting/Splitting**: MUST run `gitnexus_context({name: "target"})` to see all incoming/outgoing refs, then `gitnexus_impact({target: "target", direction: "upstream"})` to find all external callers before moving code.
- After any refactor: run `gitnexus_detect_changes({scope: "all"})` to verify only expected files changed.

## Never Do

- NEVER edit a function, class, or method without first running `gitnexus_impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `gitnexus_rename` which understands the call graph.
- NEVER commit changes without running `gitnexus_detect_changes()` to check affected scope.

## Tools Quick Reference

| Tool | When to use | Command |
|------|-------------|---------|
| `query` | Find code by concept | `gitnexus_query({query: "auth validation"})` |
| `context` | 360-degree view of one symbol | `gitnexus_context({name: "validateUser"})` |
| `impact` | Blast radius before editing | `gitnexus_impact({target: "X", direction: "upstream"})` |
| `detect_changes` | Pre-commit scope check | `gitnexus_detect_changes({scope: "staged"})` |
| `rename` | Safe multi-file rename | `gitnexus_rename({symbol_name: "old", new_name: "new", dry_run: true})` |
| `cypher` | Custom graph queries | `gitnexus_cypher({query: "MATCH ..."})` |

## Impact Risk Levels

| Depth | Meaning | Action |
|-------|---------|--------|
| d=1 | WILL BREAK — direct callers/importers | MUST update these |
| d=2 | LIKELY AFFECTED — indirect deps | Should test |
| d=3 | MAY NEED TESTING — transitive | Test if critical path |

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/adze-cad/context` | Codebase overview, check index freshness |
| `gitnexus://repo/adze-cad/clusters` | All functional areas |
| `gitnexus://repo/adze-cad/processes` | All execution flows |
| `gitnexus://repo/adze-cad/process/{name}` | Step-by-step execution trace |

## Self-Check Before Finishing

Before completing any code modification task, verify:
1. `gitnexus_impact` was run for all modified symbols
2. No HIGH/CRITICAL risk warnings were ignored
3. `gitnexus_detect_changes()` confirms changes match expected scope
4. All d=1 (WILL BREAK) dependents were updated

## Keeping the Index Fresh

After committing code changes, the GitNexus index becomes stale. Re-run analyze to update it:

```bash
npx gitnexus analyze
```

If the index previously included embeddings, preserve them by adding `--embeddings`:

```bash
npx gitnexus analyze --embeddings
```

To check whether embeddings exist, inspect `.gitnexus/meta.json` — the `stats.embeddings` field shows the count (0 means no embeddings). **Running analyze without `--embeddings` will delete any previously generated embeddings.**

> Claude Code users: A PostToolUse hook handles this automatically after `git commit` and `git merge`.

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

## graphify

This project has a graphify knowledge graph at graphify-out/.

Rules:
- Before answering architecture or codebase questions, read graphify-out/GRAPH_REPORT.md for god nodes and community structure
- If graphify-out/wiki/index.md exists, navigate it instead of reading raw files
- For cross-module "how does X relate to Y" questions, prefer `graphify query "<question>"`, `graphify path "<A>" "<B>"`, or `graphify explain "<concept>"` over grep — these traverse the graph's EXTRACTED + INFERRED edges instead of scanning files
- After modifying code files in this session, run `graphify update .` to keep the graph current (AST-only, no API cost)
