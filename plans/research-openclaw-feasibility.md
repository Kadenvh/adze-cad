# Research: OpenClaw Feasibility for Adze

**Date:** 2026-03-15
**Status:** Complete -- recommendation issued
**Triggered by:** END-GOAL.md Phase 7 item: "OpenClaw integration: Explore whether OpenClaw instances can provide orchestration, routing, or agent infrastructure. Discovery needed."

---

## What OpenClaw Actually Is

OpenClaw is a **developer-workflow orchestration layer for AI coding agents**. It is not a general-purpose agent SDK, embeddable runtime library, or model-routing framework. Its purpose is to coordinate AI development sessions (such as terminal coding agents, IDE copilots, or similar tools) by providing persistent context, memory, and identity files that the orchestrating agent maintains across sessions.

### Evidence from this repo

The project's own `block-protected-files.js` hook identifies these OpenClaw artifacts:

| File | Purpose |
|------|---------|
| `AGENTS.md` | Agent configuration |
| `HEARTBEAT.md` | Heartbeat/liveness config |
| `IDENTITY.md` | Agent identity definition |
| `SOUL.md` | Agent personality/behavioral guidance |
| `TOOLS.md` | Tool usage notes |
| `USER.md` | User profile for the orchestrating agent |
| `memory/` directory | Daily logs and session memory |

The bootstrap prompt explicitly states: *"These belong to the orchestrating agent, not to interactive coding sessions."* This confirms OpenClaw operates **above** the development tool layer -- it is the orchestrator that manages how AI agents interact with a codebase, not something that runs inside a shipped product.

### What OpenClaw provides

- **Persistent agent memory** across coding sessions (daily logs, learned context)
- **Identity and personality files** that shape how an AI coding agent behaves in a specific project
- **Heartbeat/liveness tracking** for long-running agent sessions
- **Session bootstrapping** so a new agent session can resume where the previous one left off

### What OpenClaw does NOT provide

- Model API client implementations (no HTTP clients for OpenAI/Anthropic)
- Provider routing or load balancing
- Tool call dispatch or execution
- Token management or conversation state
- Streaming or rate limiting
- Any runtime SDK, NuGet package, or embeddable library
- Any C# or .NET integration surface
- Any desktop application hosting capability

---

## What Would OpenClaw Replace in Adze?

**Nothing.** There is zero overlap between OpenClaw's capabilities and Adze's runtime architecture.

### Current Adze architecture components and their OpenClaw relevance

| Adze Component | What It Does | OpenClaw Replacement? |
|---------------|-------------|----------------------|
| `HybridBrokerOrchestrator` | Routes between model-backed and deterministic broker paths | No -- OpenClaw has no orchestration runtime |
| `OpenAIModelClient` / `AnthropicMessagesModelClient` | HTTP clients for provider APIs | No -- OpenClaw has no API clients |
| `ModelClientFactory` | Provider selection based on env config | No -- OpenClaw has no provider routing |
| `ContextPromptComposer` | Formats SessionContext into model prompts | No -- OpenClaw has no prompt formatting |
| `GroundingSynthesisService` | Second-pass synthesis over tool results | No -- OpenClaw has no synthesis layer |
| `AgentLoopRunner` (Phase 2) | Iterative tool-call loop | No -- OpenClaw has no agent loop |
| `AgentToolDispatcher` (Phase 2) | Maps model tool calls to grounding tool handlers | No -- OpenClaw has no tool dispatch |
| Trace/progression/recipe persistence | Learning artifacts under `%LOCALAPPDATA%\Adze` | Tangentially similar in concept (both persist state across sessions), but OpenClaw's memory is for AI development agents, not for end-user-facing product features |

---

## Complexity OpenClaw Would Add

If someone attempted to integrate OpenClaw into Adze's runtime:

1. **Wrong abstraction layer.** OpenClaw manages AI coding sessions. Adze needs to manage AI-assisted CAD sessions. These are fundamentally different domains with different state shapes, different lifecycle models, and different users.

2. **No .NET/C# surface.** OpenClaw's artifacts are markdown files read by AI coding tools. There is no SDK, no API, no NuGet package, no programmatic interface. Integrating it would mean inventing a bridge that doesn't exist.

3. **File-based state model.** OpenClaw uses markdown files in the repo root (`IDENTITY.md`, `SOUL.md`, `memory/`) as its state mechanism. Adze uses structured JSON under `%LOCALAPPDATA%\Adze`. These models are incompatible and serve different purposes.

4. **Server-side / repo-local orientation.** OpenClaw assumes it is managing files within a git repository for development purposes. Adze runs as an in-process COM add-in inside SOLIDWORKS on an end user's desktop.

5. **No runtime value.** OpenClaw provides no HTTP clients, no streaming, no token counting, no provider abstraction, no tool dispatch, no conversation state management -- all the things Adze's broker actually needs.

---

## Does OpenClaw Improve Anything Beyond Custom C#?

### Tool routing
No. OpenClaw has no tool routing. Adze's `ToolCatalog` + `AgentToolDispatcher` (planned Phase 2) handle this natively and are tightly coupled to SOLIDWORKS COM tool implementations.

### Memory
Tangentially related concept, but wrong implementation. OpenClaw's memory is markdown files for AI development agents. Adze's Phase 6 memory (per-document memory, user preferences, cross-session recipe promotion) needs structured JSON keyed by document hash, stored locally, and queryable by the broker. These are not the same problem.

### Orchestration
No. OpenClaw orchestrates AI coding sessions. Adze orchestrates AI-assisted CAD operations with safety contracts, undo recording, confirmation UI, and COM thread marshaling. No overlap.

### Provider abstraction
No. OpenClaw has no provider abstraction. Adze already has `ModelClientFactory` with `OpenAIModelClient` and `AnthropicMessagesModelClient`, plus environment-driven configuration for endpoints, models, and API keys.

---

## Should OpenClaw Be an Optional Integration?

OpenClaw is already in use in the correct way: as the **development-time orchestrator** that manages how AI coding agents work on the Adze codebase. The `block-protected-files.js` hook correctly prevents interactive coding sessions from modifying OpenClaw's own state files.

This is the right relationship. OpenClaw helps build Adze. OpenClaw does not run inside Adze.

Treating it as an optional runtime integration would be a category error -- like embedding your CI/CD pipeline inside your shipped product.

---

## Recommendation

**Avoid as runtime dependency. Current usage as development orchestrator is correct.**

### Rationale

1. OpenClaw solves a different problem (AI development session management) than what Adze needs (AI-assisted CAD operation orchestration).
2. There is no API surface, SDK, or embeddable component to integrate.
3. Every capability Adze needs (provider routing, tool dispatch, conversation state, memory, orchestration) is better served by the custom C# implementation that already exists or is planned.
4. OpenClaw is already being used correctly -- as the layer that manages how AI agents contribute to Adze's development. This is its intended purpose.
5. The END-GOAL.md Phase 7 line item should be closed as "evaluated and declined for runtime integration" with a note that the development-time usage is working as intended.

### Action items

- [x] Research completed
- [ ] Update END-GOAL.md discovery table: change "OpenClaw feasibility" status from "Pending" to "Evaluated" with finding: "Development orchestrator, not runtime infrastructure. No integration needed -- already in correct use as dev-time agent coordinator."
- [ ] Remove the Phase 7 bullet about OpenClaw integration, or replace it with a note that the evaluation concluded no runtime integration is warranted

---

## Maturity and Ecosystem Assessment

| Dimension | Assessment |
|-----------|-----------|
| Project maturity | Early/niche -- file-based orchestration for AI coding agents |
| API surface | None (markdown file conventions, no programmatic API) |
| Desktop embedding support | None (designed for repo-local, development-time use) |
| .NET/C# support | None |
| Community/ecosystem | Small -- primarily used by individual developers managing AI coding sessions |
| Relevance to Adze runtime | None |
| Relevance to Adze development | Already in correct use as development orchestrator |
