---

# Adze MCP Server Design Document

## Task T9-04: Expose Adze Tool Surface as an MCP Server

**Date:** 2026-03-24
**Status:** Design
**MCP Spec Version Target:** 2025-11-25 (current)
**Adze Version:** 0.1.0

---

## 1. Executive Summary

This document designs how Adze's 19 SOLIDWORKS tools (11 read, 7 write, 1 retrieval) should be exposed as a Model Context Protocol server, enabling external MCP clients (desktop MCP clients, coding agents, IDE assistants, etc.) to inspect and modify the active SOLIDWORKS session through Adze's governed tool surface.

The core challenge is architectural: Adze runs as an in-process COM add-in inside SOLIDWORKS on the STA/UI thread, while an MCP server must handle async JSON-RPC requests from external processes. The solution is a **sidecar process** connected to the add-in via named pipes, with the sidecar hosting the MCP server (stdio or Streamable HTTP transport) and the add-in marshaling tool execution to the COM/UI thread.

---

## 2. Architecture: The Sidecar Bridge Pattern

### 2.1 Why Not In-Process?

The official MCP C# SDK (`ModelContextProtocol` NuGet package) targets .NET 8+ and .NET Standard 2.0. Adze targets **.NET Framework 4.8** (legacy, required for SOLIDWORKS COM interop). While .NET Standard 2.0 has theoretical compatibility with .NET Framework 4.8, the MCP SDK depends on modern .NET hosting infrastructure (`Microsoft.Extensions.Hosting`, async streams, `System.Text.Json`) that does not work reliably in old-style `.csproj` projects.

Additionally, hosting an HTTP server or stdio loop inside the SOLIDWORKS process introduces risk:
- The SOLIDWORKS process is STA COM. Hosting `HttpListener` or `System.IO.Pipes` server loops inside it requires careful background threading that competes with the existing add-in lifecycle.
- If the MCP server crashes, it should not take SOLIDWORKS down.
- Process isolation gives a clean security boundary.

### 2.2 The Two-Process Architecture

```
 External MCP Client          Adze MCP Sidecar              Adze Add-In
 (Desktop/IDE clients,  <-->  (Adze.Mcp.Server.exe)   <-->  (Adze.Host.dll)
  coding agents, etc.)         .NET 8 console app            .NET 4.8 in SOLIDWORKS
                               MCP protocol handler          COM/STA thread
                               stdio or HTTP transport        Named pipe client

  JSON-RPC over                JSON-RPC over                 Binary/JSON over
  stdio / HTTP                 MCP SDK                       Named Pipes
```

**Adze.Mcp.Server** (new .NET 8 console application):
- Hosts the MCP server using the official C# SDK
- Exposes tools, resources, and prompts via MCP protocol
- Communicates with the in-process add-in via named pipes
- Launched as a child process by the add-in or by the user/client directly

**Adze.Host** (existing .NET 4.8 add-in, modified):
- Starts a named pipe server on add-in load
- Receives tool execution requests from the sidecar
- Marshals execution to the UI/STA thread via `IUiThreadInvoker`
- Returns results over the pipe

### 2.3 Transport Choice for MCP Clients

The sidecar should support **both** MCP transports:

| Transport | Use Case | How |
|-----------|----------|-----|
| **stdio** | Desktop MCP clients, coding agents, most CLI clients | Sidecar is launched as a subprocess by the client. Reads JSON-RPC from stdin, writes to stdout. |
| **Streamable HTTP** | VS Code extensions, web-based clients, multi-client scenarios | Sidecar hosts HTTP endpoint on `http://127.0.0.1:{port}/mcp`. Bind to localhost only. |

The stdio transport is the primary target for Phase 1; Streamable HTTP is Phase 2.

### 2.4 Internal Bridge: Named Pipes

The communication between sidecar and add-in uses **named pipes** (`System.IO.Pipes`), which are:
- Available on both .NET 4.8 and .NET 8
- Fast (no network stack overhead)
- Local-only by default (no network exposure)
- Support bidirectional communication
- Support Windows ACLs for security

Pipe name convention: `\\.\pipe\adze-solidworks-{pid}` where `{pid}` is the SOLIDWORKS process ID. This supports multiple SOLIDWORKS instances.

The internal protocol over named pipes is a simple framed JSON-RPC subset:
1. 4-byte little-endian length prefix
2. UTF-8 JSON payload (request or response)

Message types:
- `tool/execute` -- execute a tool by name with arguments, returns tool result
- `resource/read` -- read a resource by URI, returns resource content
- `context/session` -- fetch current SessionContext snapshot
- `ping` -- health check

---

## 3. Threading Model

This is the most critical architectural concern. SOLIDWORKS COM is STA. All COM calls must happen on the UI thread. The MCP server receives requests asynchronously.

### 3.1 Thread Flow for a `tools/call` Request

```
MCP Client                 Sidecar (async)              Named Pipe              Add-In (STA)
   |                           |                            |                       |
   |-- tools/call ----------->|                            |                       |
   |                           |-- pipe: tool/execute ----->|                       |
   |                           |   (async await)            |-- receive on         |
   |                           |                            |   pipe server thread  |
   |                           |                            |                       |
   |                           |                            |-- IUiThreadInvoker -->|
   |                           |                            |   .Invoke(() => {     |
   |                           |                            |     dispatcher.Execute|
   |                           |                            |   })                  |
   |                           |                            |                       |
   |                           |                            |<-- result -----------|
   |                           |<-- pipe: result -----------|                       |
   |<-- tools/call result ----|                            |                       |
```

### 3.2 Key Threading Rules

1. **The sidecar is fully async.** It runs on .NET 8 with async/await throughout. No COM concerns.
2. **The named pipe server in the add-in runs on a background `ThreadPool` thread** (or a dedicated long-running thread). It reads requests from the pipe in a loop.
3. **Every tool execution request is marshaled to the UI thread** via `IUiThreadInvoker.Invoke()`, which uses `Control.Invoke()` under the hood. This is the same pattern already used by `HostState.ApplyPendingWrite`.
4. **The pipe server thread blocks while waiting for the UI thread** to complete. This is correct -- the existing `WinFormsUiThreadInvoker.Invoke()` is synchronous (blocks the caller until the UI thread completes the action).
5. **Timeout enforcement** happens in the sidecar. If the add-in does not respond within the configured timeout, the sidecar returns a JSON-RPC error to the client.

### 3.3 Concurrency

- Only one tool execution runs at a time on the UI thread (serialized by `Control.Invoke`).
- The sidecar can accept multiple concurrent MCP requests but serializes them through the single named pipe.
- This is correct for SOLIDWORKS -- you cannot safely run two COM operations concurrently anyway.

---

## 4. Tool Mapping: Adze Tools to MCP Tool Definitions

### 4.1 Direct Mapping Strategy

Adze's existing `ToolDefinitionBuilder` already produces tool definitions with `name`, `description`, and `inputSchema` (as `Dictionary<string, object?>` that serializes to JSON Schema). The MCP specification requires exactly these three fields plus optional `annotations`, `outputSchema`, `title`, and `icons`.

The mapping is nearly 1:1. A new `McpToolDefinitionAdapter` converts `AgentToolDefinition` to MCP tool definitions, adding:
- `title` -- human-readable display name (e.g., "Get Active Document" for `get_active_document`)
- `annotations` -- mapped from `ToolCapabilityMetadata`
- `outputSchema` -- optional, can be added incrementally

### 4.2 Tool Annotations Mapping

The existing `ToolCapabilityMetadata` maps cleanly to MCP tool annotations:

| Adze `ToolCapabilityClass` | MCP `readOnlyHint` | MCP `destructiveHint` | MCP `idempotentHint` | MCP `openWorldHint` |
|---|---|---|---|---|
| `ReadSafe` | `true` | `false` | `true` | `false` |
| `SoftWrite` | `false` | `false` | `true` | `false` |
| `HardWriteFirstWave` | `false` | `false` | varies | `false` |
| `HardWriteAdvanced` | `false` | `true` | `false` | `false` |
| `DeferredHighRisk` | `false` | `true` | `false` | `false` |

All Adze tools are `openWorldHint: false` because they operate exclusively on the local SOLIDWORKS session.

### 4.3 Complete Tool List with MCP Mapping

**Read Tools (Class 0 -- ReadSafe):**

| # | Adze Name | MCP Name | annotations |
|---|-----------|----------|-------------|
| 1 | `get_active_document` | `solidworks.get_active_document` | readOnly, idempotent |
| 2 | `get_document_summary` | `solidworks.get_document_summary` | readOnly, idempotent |
| 3 | `get_selection_context` | `solidworks.get_selection_context` | readOnly |
| 4 | `get_feature_tree_slice` | `solidworks.get_feature_tree_slice` | readOnly, idempotent |
| 5 | `get_dimensions` | `solidworks.get_dimensions` | readOnly, idempotent |
| 6 | `get_configurations` | `solidworks.get_configurations` | readOnly, idempotent |
| 7 | `get_custom_properties` | `solidworks.get_custom_properties` | readOnly, idempotent |
| 8 | `get_mates` | `solidworks.get_mates` | readOnly, idempotent |
| 9 | `get_rebuild_diagnostics` | `solidworks.get_rebuild_diagnostics` | readOnly, idempotent |
| 10 | `get_reference_graph` | `solidworks.get_reference_graph` | readOnly, idempotent |

**Write Tools (Class 2/3 -- HardWrite):**

| # | Adze Name | MCP Name | annotations |
|---|-----------|----------|-------------|
| 11 | `set_custom_property` | `solidworks.set_custom_property` | not readOnly, not destructive, idempotent |
| 12 | `set_dimension_value` | `solidworks.set_dimension_value` | not readOnly, not destructive |
| 13 | `suppress_feature` | `solidworks.suppress_feature` | not readOnly, destructive |
| 14 | `unsuppress_feature` | `solidworks.unsuppress_feature` | not readOnly, not destructive |
| 15 | `rename_object` | `solidworks.rename_object` | not readOnly, not destructive, idempotent |
| 16 | `insert_component` | `solidworks.insert_component` | not readOnly, not destructive |
| 17 | `create_drawing_view` | `solidworks.create_drawing_view` | not readOnly, not destructive |

**Retrieval Tool:**

| # | Adze Name | MCP Name | annotations |
|---|-----------|----------|-------------|
| 18 | `search_project_files` | `solidworks.search_project_files` | readOnly |

**Utility (new for MCP only):**

| # | MCP Name | Purpose |
|---|----------|---------|
| 19 | `solidworks.get_session_context` | Returns the full SessionContext snapshot as JSON |

### 4.4 Namespace Convention

MCP tool names use `solidworks.` prefix to avoid collisions when the server is composed with other MCP servers in a client's tool roster. This follows the MCP convention of using dot-separated namespaces (e.g., `admin.tools.list`).

### 4.5 Write Tool Behavior in MCP

Write tools via MCP follow the same `plan -> preview -> apply -> verify -> log` lifecycle as internal Adze writes. The MCP server exposes two modes, controlled by configuration:

**Mode 1 -- Preview-only (default, safe):**
Write tool calls return a preview (before/after, warnings, cascade risk) but do NOT apply. The response includes `"requires_confirmation": true` and instructions for the user to confirm in the SOLIDWORKS Task Pane. This is the safe default for untrusted external callers.

**Mode 2 -- Auto-apply (opt-in, gated):**
With `SOLIDWORKS_AI_MCP_AUTO_APPLY=true`, write tool calls execute the full preview/apply/verify lifecycle. This is for trusted local clients that are already providing their own confirmation UI. Requires a valid auth token.

---

## 5. MCP Resources

Beyond tools, the MCP server should expose SOLIDWORKS session data as MCP **resources** that clients can read and subscribe to.

### 5.1 Resource Definitions

| URI | Name | Description | MIME Type |
|-----|------|-------------|-----------|
| `solidworks://session/context` | Session Context | Full SessionContext JSON | `application/json` |
| `solidworks://document/info` | Document Info | Active document metadata | `application/json` |
| `solidworks://document/feature-tree` | Feature Tree | Full feature tree as JSON | `application/json` |
| `solidworks://document/properties` | Custom Properties | All custom properties | `application/json` |
| `solidworks://document/diagnostics` | Diagnostics | Rebuild diagnostics | `application/json` |

### 5.2 Resource Subscriptions

When the active document changes in SOLIDWORKS (detected by the add-in's existing document change events), the sidecar sends `notifications/resources/updated` for all document-scoped resources. The add-in notifies the sidecar of document changes via the named pipe.

---

## 6. MCP Prompts

The server exposes canned prompt templates for common SOLIDWORKS workflows.

| Name | Description | Arguments |
|------|-------------|-----------|
| `solidworks.diagnose` | Diagnose rebuild issues in the current model | none |
| `solidworks.summarize` | Summarize the active document | `include_properties: boolean` |
| `solidworks.compare_configs` | Compare configurations in the active document | `config_a: string, config_b: string` |
| `solidworks.review_mates` | Review assembly mates for issues | none |

These map to Adze's existing diagnostic intent routing and tool sequencing patterns.

---

## 7. Security Model

### 7.1 Threat Model

The MCP server runs locally and exposes write access to SOLIDWORKS. Key threats:
1. **DNS rebinding** -- remote website tricks browser into calling localhost MCP endpoint
2. **Unauthorized local process** -- malware or untrusted script calls the MCP server
3. **Token theft** -- session token intercepted by another local process

### 7.2 Mitigations

| Threat | Mitigation |
|--------|-----------|
| DNS rebinding | Bind HTTP to `127.0.0.1` only. Validate `Origin` header. Reject non-localhost origins. |
| Unauthorized access | **Auth token required.** On startup, the add-in generates a cryptographically random session token, writes it to `%LOCALAPPDATA%\Adze\mcp-auth-token`. The sidecar reads it. All named pipe messages must include the token. The MCP HTTP endpoint requires `Authorization: Bearer {token}` header. |
| Token theft | Token file has restricted ACL (current user only). Token rotates each SOLIDWORKS session. stdio transport inherits process security (only the parent process can write to stdin). |
| Write safety | Write tools default to preview-only mode for MCP callers. Auto-apply requires explicit opt-in. All writes follow existing `IWriteTool<T>` lifecycle with preview/verify/trace. |
| Rate limiting | Reuse existing `RateLimitHelper` infrastructure. Cap at configurable requests/minute for MCP callers. |

### 7.3 Feature Gating

The MCP server is gated behind `SOLIDWORKS_AI_MCP_SERVER=true`. Write tools in MCP additionally require `SOLIDWORKS_AI_FIRST_WAVE_WRITES=true` (same as internal path). The retrieval tool requires `SOLIDWORKS_AI_RETRIEVAL=true`.

---

## 8. Implementation Phases

### Phase M1: Minimum Viable MCP Server (read-only tools, stdio)

**Scope:** 10 read tools + 1 retrieval tool exposed over stdio transport. No write tools.

**New project:** `Adze.Mcp.Server` (.NET 8 console app)
**Modified project:** `Adze.Host` (add named pipe server)
**New shared project:** `Adze.Mcp.Contracts` (.NET Standard 2.0, shared message types for pipe protocol)

**Deliverables:**
1. Named pipe server in `Adze.Host` that accepts tool execution requests and marshals to UI thread
2. `Adze.Mcp.Contracts` project with pipe message types (request/response DTOs)
3. `Adze.Mcp.Server` console app using official MCP C# SDK
4. `McpToolDefinitionAdapter` -- converts `AgentToolDefinition` to MCP tool definitions
5. `McpToolHandler` -- receives MCP `tools/call`, sends pipe request to add-in, returns MCP result
6. Pipe discovery via `%LOCALAPPDATA%\Adze\mcp-pipe-name` file
7. Auth token generation and validation
8. Feature gate: `SOLIDWORKS_AI_MCP_SERVER=true`
9. Integration test: desktop MCP client config pointing to sidecar, read tools callable

**Estimated scope:** ~800 lines new code + ~200 lines modifications

### Phase M2: Write Tools + Resources

**Scope:** 7 write tools in preview-only mode. MCP resources for session context.

**Deliverables:**
1. Write tool MCP definitions with proper annotations
2. Preview-only mode for write tools via MCP
3. MCP resources (`solidworks://session/context`, etc.)
4. Document change notifications over pipe -> MCP resource update notifications
5. Optional auto-apply mode with additional auth gate

### Phase M3: Streamable HTTP Transport

**Scope:** HTTP transport for multi-client scenarios and web-based clients.

**Deliverables:**
1. Streamable HTTP endpoint in sidecar at `http://127.0.0.1:{port}/mcp`
2. Origin validation, session management, `Mcp-Session-Id` support
3. Configuration: `SOLIDWORKS_AI_MCP_HTTP_PORT=8742` (default)

### Phase M4: Prompts + Polish

**Scope:** MCP prompt templates, output schemas, icons, completions.

**Deliverables:**
1. Prompt templates for common workflows
2. Output schemas for all tools
3. Argument auto-completion for tool parameters (feature names, dimension names, config names)
4. Server metadata and icons

---

## 9. C# Contracts and Interfaces

### 9.1 New Project: `Adze.Mcp.Contracts` (.NET Standard 2.0)

This is the shared boundary between the .NET 4.8 add-in and the .NET 8 sidecar.

```csharp
namespace Adze.Mcp.Contracts;

// --- Pipe protocol messages ---

public sealed class PipeRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;     // "tool/execute", "resource/read", "context/session", "ping"
    public string AuthToken { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
    public string ResourceUri { get; set; } = string.Empty;
}

public sealed class PipeResponse
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string ResultJson { get; set; } = string.Empty;  // Serialized tool result or resource content
    public bool IsError { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class PipeNotification
{
    public string Type { get; set; } = string.Empty;        // "document_changed", "tools_changed", "shutdown"
    public string Payload { get; set; } = string.Empty;
}

// --- Pipe framing ---

public static class PipeFraming
{
    // 4-byte LE length prefix + UTF-8 JSON body
    public static byte[] Frame(string json);
    public static string Unframe(Stream stream);
}

// --- Discovery ---

public static class McpDiscovery
{
    public static string PipeNameFilePath => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Adze", "mcp-pipe-name");
    
    public static string AuthTokenFilePath => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Adze", "mcp-auth-token");
}
```

### 9.2 New Interfaces in `Adze.Host`

```csharp
namespace Adze.Host.Mcp;

/// <summary>
/// Runs a named pipe server inside the SOLIDWORKS process that accepts
/// tool execution requests from the MCP sidecar and marshals them to 
/// the UI thread.
/// </summary>
internal interface IMcpPipeServer : IDisposable
{
    void Start(string pipeName, string authToken, IUiThreadInvoker uiThread);
    void Stop();
    
    /// <summary>
    /// Sends an unsolicited notification to the sidecar (e.g., document changed).
    /// </summary>
    void SendNotification(PipeNotification notification);
}

/// <summary>
/// Executes MCP tool requests by delegating to the existing AgentToolDispatcher
/// with a fresh SessionContext captured on the UI thread.
/// </summary>
internal interface IMcpToolBridge
{
    PipeResponse ExecuteTool(PipeRequest request);
    PipeResponse ReadResource(PipeRequest request);
    PipeResponse GetSessionContext();
}
```

### 9.3 Key Classes in `Adze.Mcp.Server`

```csharp
namespace Adze.Mcp.Server;

/// <summary>
/// MCP server entry point. Connects to the add-in's named pipe and
/// exposes Adze tools to MCP clients.
/// </summary>
public sealed class AdzeMcpServer
{
    // Uses official ModelContextProtocol NuGet SDK
    // Registers tools, resources, prompts
    // Connects to add-in pipe on startup
}

/// <summary>
/// Converts Adze AgentToolDefinition to MCP Tool objects with proper
/// annotations, inputSchema, and optional outputSchema.
/// </summary>
public sealed class McpToolDefinitionAdapter
{
    public McpTool Convert(AgentToolDefinition definition, ToolCapabilityMetadata? capability);
    public McpToolAnnotations MapAnnotations(ToolCapabilityMetadata capability);
}

/// <summary>
/// Named pipe client that communicates with the in-process add-in.
/// Handles connection, reconnection, timeout, and auth.
/// </summary>
public sealed class AdzeHostPipeClient : IDisposable
{
    public Task<PipeResponse> SendRequestAsync(PipeRequest request, CancellationToken ct);
    public Task ConnectAsync(CancellationToken ct);
    public event Action<PipeNotification>? OnNotification;
}

/// <summary>
/// MCP tool handler that bridges tools/call requests to the add-in via pipe.
/// </summary>
public sealed class AdzeMcpToolHandler
{
    public async Task<McpToolResult> HandleCallAsync(string toolName, 
        Dictionary<string, object?> arguments, CancellationToken ct);
}
```

---

## 10. Configuration

| Environment Variable | Default | Description |
|---------------------|---------|-------------|
| `SOLIDWORKS_AI_MCP_SERVER` | `false` | Enable the MCP named pipe server in the add-in |
| `SOLIDWORKS_AI_MCP_AUTO_APPLY` | `false` | Allow write tools to auto-apply (not just preview) |
| `SOLIDWORKS_AI_MCP_HTTP_PORT` | `8742` | Port for Streamable HTTP transport (Phase M3) |
| `SOLIDWORKS_AI_MCP_HTTP_ENABLED` | `false` | Enable HTTP transport in sidecar |
| `SOLIDWORKS_AI_MCP_WRITE_TOOLS` | `false` | Expose write tools via MCP (independent of internal write gate) |

### Example Desktop MCP Client Configuration (stdio)

```json
{
  "mcpServers": {
    "solidworks": {
      "command": "C:\\adze-cad\\tools\\adze-mcp-server.exe",
      "args": ["--transport", "stdio"],
      "env": {}
    }
  }
}
```

---

## 11. Solution Structure Changes

```
Adze.sln (updated)
  src/
    Adze.Host/           (existing, modified -- add pipe server)
    Adze.Contracts/      (existing, no changes)
    Adze.Tools/          (existing, no changes)
    Adze.Trace/          (existing, no changes)
    Adze.Broker/         (existing, minor reuse)
    Adze.Index/          (existing, no changes)
    Adze.Mcp.Contracts/  (NEW -- .NET Standard 2.0, pipe protocol DTOs)
    Adze.Mcp.Server/     (NEW -- .NET 8 console app, MCP SDK)
  tests/
    Adze.Tests/          (existing, extended)
    Adze.Mcp.Tests/      (NEW -- .NET 8 test project for MCP layer)
```

The `Adze.Mcp.Contracts` project targets .NET Standard 2.0 so it can be referenced by both the .NET 4.8 add-in and the .NET 8 sidecar. It contains only simple DTOs and the pipe framing logic -- no SDK dependencies.

---

## 12. Key Design Decisions and Trade-offs

### Decision 1: Sidecar vs. In-Process

**Chosen: Sidecar.** The .NET Framework 4.8 constraint makes using the official MCP C# SDK in-process impractical. The sidecar also provides process isolation (MCP server crash does not take down SOLIDWORKS) and allows the MCP server to use modern .NET async patterns without fighting the STA COM threading model.

**Trade-off:** Higher complexity (two processes, IPC), but this is the architecturally clean solution.

### Decision 2: Named Pipes vs. TCP/HTTP for Internal Bridge

**Chosen: Named pipes.** They are Windows-native, local-only by default, faster than TCP loopback, and support Windows security descriptors for access control. They are available on both .NET 4.8 and .NET 8.

**Trade-off:** Windows-only, but Adze is Windows-only by nature (SOLIDWORKS is Windows-only).

### Decision 3: Tool Name Namespacing

**Chosen: `solidworks.` prefix** (e.g., `solidworks.get_dimensions`). This prevents name collisions when the MCP server is used alongside other MCP servers. The internal Adze tool names (e.g., `get_dimensions`) remain unchanged.

**Trade-off:** Slightly longer names, but follows MCP naming best practices.

### Decision 4: Write Tools Default to Preview-Only

**Chosen: Preview-only by default for MCP callers.** External agents should not silently mutate the SOLIDWORKS model. The MCP response includes the preview data and instructions to confirm in the Task Pane or to enable auto-apply mode.

**Trade-off:** Less autonomous for external agents, but preserves the "host-governed" safety model that is core to Adze's architecture.

### Decision 5: Reuse Existing Dispatcher vs. New MCP-Specific Dispatcher

**Chosen: Reuse `AgentToolDispatcher`.** The MCP pipe bridge captures a fresh `SessionContext` on the UI thread and passes it through the existing `ToolExecutionContext`. This reuses all existing tool logic, parameter deserialization, write preview generation, and error handling.

**Trade-off:** The `AgentToolDispatcher` is designed for in-process use and uses synchronous execution. This is actually fine because the UI thread marshal is inherently synchronous. The sidecar awaits the pipe response asynchronously.

---

## 13. Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|-----------|
| Named pipe connection drops | Medium | Reconnect logic in sidecar with exponential backoff. MCP clients see JSON-RPC error on timeout. |
| UI thread blocked during long tool execution | Medium | Timeout in sidecar (default 30s). Cancel pipe request on MCP cancellation notification. |
| SOLIDWORKS not running when sidecar starts | High | Sidecar retries pipe connection with backoff. Returns MCP error "SOLIDWORKS not connected" until pipe connects. |
| Multiple SOLIDWORKS instances | Low | Pipe name includes PID. Discovery file lists all active pipes. Client specifies which instance via env var or argument. |
| .NET Standard 2.0 compatibility friction | Low | Keep `Adze.Mcp.Contracts` dependency-free (no NuGet packages, only BCL types). |
| MCP SDK breaking changes | Medium | Pin SDK version. The v1.0 release is marked stable. |

---

## 14. Testing Strategy

### Unit Tests (in `Adze.Mcp.Tests`)
- Pipe framing serialization/deserialization round-trip
- `McpToolDefinitionAdapter` converts all 19 tools correctly
- Tool annotations mapping from `ToolCapabilityMetadata`
- Auth token validation (accept valid, reject invalid, reject missing)
- Pipe request/response serialization

### Integration Tests
- Sidecar connects to a mock pipe server, sends `tools/list`, receives all 19 tools
- Sidecar sends `tools/call` for each read tool, receives valid results
- Write tool returns preview-only result via MCP
- Resource read returns valid JSON
- Auth token rejection test
- Pipe reconnection after disconnect

### Live Smoke Tests (Category: LiveMcp)
- Start sidecar, connect to running SOLIDWORKS + add-in
- Call `solidworks.get_active_document` and verify result
- Call `solidworks.get_dimensions` and verify paginated result

---

## 15. Dependencies on Existing Code

The MCP server reuses these existing components without modification:

| Component | Location | Purpose |
|-----------|----------|---------|
| `ToolNames` | `Adze.Contracts/Tooling/ToolNames.cs` | Canonical tool name constants |
| `ToolDefinitionBuilder` | `Adze.Broker/Formatting/ToolDefinitionBuilder.cs` | Tool schemas and descriptions |
| `AgentToolDispatcher` | `Adze.Broker/Orchestration/AgentToolDispatcher.cs` | Tool execution and dispatch |
| `ToolCapabilityMetadata` | `Adze.Tools/Abstractions/ToolCapabilityContracts.cs` | Capability classification for annotations |
| `IUiThreadInvoker` | `Adze.Contracts/Abstractions/IUiThreadInvoker.cs` | COM thread marshaling |
| `WinFormsUiThreadInvoker` | `Adze.Host/Infrastructure/WinFormsUiThreadInvoker.cs` | Concrete UI thread invoker |
| `SessionContextBuilder` | `Adze.Host/Services/SessionContextBuilder.cs` | Fresh session context capture |
| `FeatureGateRegistry` | `Adze.Broker/Configuration/FeatureGateRegistry.cs` | Feature gate checking |
| `ErrorClassifier` | `Adze.Broker/Orchestration/ErrorClassifier.cs` | Clean error messages |

---

## 16. Summary

The MCP server for Adze is a **sidecar bridge** that translates between the MCP protocol and Adze's existing tool surface. The sidecar is a .NET 8 console app using the official MCP C# SDK. The add-in hosts a named pipe server that marshals tool execution to the COM/STA thread. This design:

- Preserves the "host-governed safety" principle (writes require confirmation by default)
- Reuses all existing tool logic, schemas, and error handling
- Provides clean process isolation between MCP server and SOLIDWORKS
- Supports both stdio and HTTP transports for broad client compatibility
- Maps Adze's capability classification system directly to MCP tool annotations
- Follows existing Adze patterns for feature gating, auth, and threading

The minimum viable implementation (Phase M1) requires approximately 1000 lines of new code across 3 new projects, with about 200 lines of modification to the existing add-in. It exposes 11 read-only tools via stdio transport with auth token security.

---

Sources:
- [MCP Specification - Tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP Specification - Transports](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports)
- [MCP Specification - Lifecycle](https://modelcontextprotocol.io/specification/2025-03-26/basic/lifecycle)
- [MCP Specification - Resources](https://modelcontextprotocol.io/specification/2025-03-26/server/resources)
- [Official C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP C# SDK v1.0 Announcement](https://www.infoq.com/news/2026/03/mcp-csharp-v1/)
- [MCP Tool Annotations Blog Post](https://blog.modelcontextprotocol.io/posts/2026-03-16-tool-annotations/)
- [MCP C# SDK NuGet](https://www.nuget.org/packages/ModelContextProtocol/)
- [Build MCP Server in C# (.NET Blog)](https://devblogs.microsoft.com/dotnet/build-a-model-context-protocol-mcp-server-in-csharp/)

### Critical Files for Implementation
- `C:\adze-cad\src\Adze.Broker\Formatting\ToolDefinitionBuilder.cs` - Contains all 19 tool definitions with schemas; the MCP adapter will transform these to MCP format
- `C:\adze-cad\src\Adze.Broker\Orchestration\AgentToolDispatcher.cs` - Core tool dispatch logic that the MCP pipe bridge will delegate to for tool execution
- `C:\adze-cad\src\Adze.Contracts\Abstractions\IUiThreadInvoker.cs` - Threading abstraction the pipe server must use to marshal COM calls to the STA thread
- `C:\adze-cad\src\Adze.Host\Infrastructure\HostState.cs` - Central host state class where the named pipe server must be initialized and where SessionContext capture happens
- `C:\adze-cad\src\Adze.Tools\Abstractions\ToolCapabilityContracts.cs` - Capability metadata classes that map directly to MCP tool annotations
