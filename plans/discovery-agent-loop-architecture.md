# Discovery: Multi-Turn Agentic Tool Loop Architecture

**Date:** 2026-03-15
**Status:** Discovery / architectural design
**Scope:** Evolve the current single-turn assistant run into a multi-turn agent loop where the model drives iterative tool execution

---

## 1. Current Architecture Summary

### Single-Turn Flow (today)

```
User clicks "Run assistant"
  |
  v
[UI thread] TaskPaneControl.RunAssistant()
  |-- HostState.PrepareAssistantRun()           (UI thread: COM capture -> SessionContext)
  |-- ThreadPool.QueueUserWorkItem()
        |
        v
      [Background thread] HostState.CompleteAssistantRun()
        |-- GroundingExecutionService.Execute()
        |     |-- ContextPromptComposer.Compose()
        |     |-- HybridBrokerOrchestrator.CreateGroundingPlan()
        |     |     |-- KeywordBrokerOrchestrator (deterministic fallback)
        |     |     |-- ModelClient.Complete() (single API call, returns BrokerResponse with tool recommendations)
        |     |-- ExecuteRecommendedTools()     (all tools run at once against cached SessionContext)
        |     \-- returns GroundingExecutionReport
        |
        |-- GroundingSynthesisService.Build()
        |     |-- ContextPromptComposer.ComposeSynthesisPrompt()
        |     |-- ModelClient.Synthesize()       (single API call, returns plain-text answer)
        |     \-- returns GroundingSynthesisOutcome
        |
        |-- TraceRecorder, SnapshotStore, ProgressionEngine
        \-- returns AssistantRunSnapshot
              |
              v
          [UI thread] PostToUi -> ApplyAssistantRunSnapshot()
```

### Key Observations

1. **Tools never touch COM.** Tools operate on the pre-captured `SessionContext` object. They are pure functions: `SessionContext -> ToolResult`. This is a deliberate design: COM stays on the host thread, tools reason over serialized contracts.

2. **Two API calls per run.** The broker planning call returns a `BrokerResponse` with up to 4 `recommended_tools`. The synthesis call produces the final answer from the executed tool results. Neither call includes tool-use protocol; the model returns structured JSON, not `tool_use` content blocks.

3. **Threading is simple.** COM capture on UI thread, then everything else on a single background thread. No concurrency within the background work.

4. **No conversation state.** Each run is independent. No message history is maintained between runs.

5. **No model-driven tool selection at execution time.** The model recommends tools, but the host executes all of them unconditionally. The model cannot observe results and decide what to do next.

---

## 2. Target Architecture: Multi-Turn Agent Loop

### Target Flow

```
User submits prompt
  |
  v
[UI thread] Capture SessionContext from COM
  |
  v
[Background thread] Agent loop starts
  |
  |-- Build conversation messages:
  |     system prompt + user prompt + [tool definitions]
  |
  |-- LOOP:
  |     |-- API call with messages + tool definitions
  |     |     (model returns either text or tool_use blocks)
  |     |
  |     |-- if response contains tool_use:
  |     |     |-- for each tool_use block:
  |     |     |     |-- if tool needs fresh COM data:
  |     |     |     |     marshal to UI thread, re-capture SessionContext slice
  |     |     |     |-- execute tool against SessionContext
  |     |     |     |-- append tool_result to conversation
  |     |     |-- continue LOOP
  |     |
  |     |-- if response is text (no tool_use):
  |     |     break LOOP, this is the final answer
  |     |
  |     |-- if max iterations reached:
  |     |     break LOOP with forced synthesis from accumulated results
  |
  \-- Return final answer + accumulated tool results + usage
        |
        v
    [UI thread] Update answer, plan, tools panels
```

### Why This Shape

- The model sees tool results and can decide whether it needs more information, ask a different tool, or refine its approach based on what it learned.
- The loop is bounded by a configurable iteration limit (safety valve).
- COM data can be refreshed mid-loop when a tool genuinely needs it (e.g., after the user changes selection), though most tools will operate on the initially captured context.
- The final text response is the model's own synthesis -- no separate synthesis API call needed because the model has seen all tool results in its conversation context.

---

## 3. Threading Model

### Constraints

| Constraint | Source |
|------------|--------|
| SOLIDWORKS COM calls must run on the STA UI thread | COM threading model; SOLIDWORKS is STA |
| `Control.Invoke`/`BeginInvoke` is the only safe way to marshal to the UI thread from a background thread | Windows Forms |
| API calls are blocking I/O (`HttpWebRequest` with `.GetResponse()`) and take 2-30 seconds | Network latency |
| API calls must not run on the UI thread | Would freeze SOLIDWORKS |
| .NET Framework 4.8: no `async/await` with `HttpClient` without extra packages | Toolchain constraint |
| `ThreadPool.QueueUserWorkItem` is the existing background execution pattern | Current codebase |

### Proposed Threading Strategy

```
UI Thread (STA)                    Background Thread
--------------                    -----------------
Click "Run"
  |
  |-- Disable UI controls
  |-- Capture initial SessionContext (COM)
  |-- Queue agent loop work item
  |                                 |
  |                                 |-- Build initial messages
  |                                 |
  |                                 |-- LOOP:
  |                                 |     |
  |                                 |     |-- API call (blocking HTTP, 2-30s)
  |                                 |     |     Check CancellationToken before/after
  |                                 |     |
  |                                 |     |-- Parse response
  |                                 |     |
  |                                 |     |-- if tool_use:
  |                                 |     |     |
  |  <-- Invoke for progress -------|-----|-----|-- PostToUi("Running get_dimensions...")
  |       update                    |     |     |
  |                                 |     |     |-- needs fresh COM data?
  |  <-- Invoke for COM capture ----|-----|-----|----- Control.Invoke(() => recapture slice)
  |       (synchronous, blocks      |     |     |     (blocks background thread until done)
  |        background thread)       |     |     |
  |                                 |     |     |-- execute tool(s)
  |                                 |     |     |-- append results to conversation
  |                                 |     |     |-- continue LOOP
  |                                 |     |
  |                                 |     |-- if text: break
  |                                 |     |-- if cancelled: break
  |                                 |     |-- if max iterations: break
  |                                 |
  |  <-- BeginInvoke final result --|-- return AgentLoopResult
  |
  |-- Apply result to UI
  |-- Re-enable controls
```

### Key Design Decisions

**Use `Control.Invoke` (synchronous) for mid-loop COM recapture, not `BeginInvoke` (async).**
The background thread needs the fresh `SessionContext` before it can execute the tool. `Invoke` blocks the background thread until the UI thread completes the COM capture, which is the correct behavior. The UI thread is not blocked for long -- COM context capture is fast (milliseconds). `BeginInvoke` would require a wait handle, adding complexity for no benefit.

**Use `BeginInvoke` (async) for progress updates and final result delivery.**
Progress text like "Running tool 2 of 3..." can be fire-and-forget. The background thread should not wait for the UI to repaint.

**Single background thread, no parallelism within the loop.**
API calls are sequential. Tool executions within a single turn are fast (they operate on in-memory data) and can run sequentially. Parallel tool execution is a future optimization that adds complexity without significant benefit at this stage.

**Use a simple `ManualResetEvent` or polling pattern for cancellation rather than `CancellationTokenSource`.**
.NET 4.8 supports `CancellationTokenSource` and `CancellationToken` (they are in `System.Threading` since .NET 4.0). Use them. The token is checked:
- Before each API call
- After each API call returns
- Before each tool execution
- `HttpWebRequest` does not natively support cancellation, but we can call `request.Abort()` from another thread. Alternatively, rely on the timeout and check the token after the call returns.

---

## 4. Conversation State Management

### Message History Data Structure

```csharp
namespace Adze.Broker.Models;

/// <summary>
/// Represents a single message in the agent conversation.
/// Maps directly to the messages array in OpenAI/Anthropic API format.
/// </summary>
public sealed class ConversationMessage
{
    /// <summary>"system", "user", "assistant", or "tool"</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Text content for user/assistant/system messages. Null for tool_use-only turns.</summary>
    public string? TextContent { get; set; }

    /// <summary>Tool use requests from the assistant. Null for non-tool-use turns.</summary>
    public List<ToolUseRequest>? ToolUses { get; set; }

    /// <summary>Tool result for a tool message. Null for non-tool messages.</summary>
    public ToolUseResult? ToolResult { get; set; }

    /// <summary>Unique ID for tool_use blocks, used to correlate tool_result responses.</summary>
    public string? ToolUseId { get; set; }
}

public sealed class ToolUseRequest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object?> Input { get; set; } = new();
}

public sealed class ToolUseResult
{
    public string ToolUseId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsError { get; set; }
}
```

### Conversation State Container

```csharp
namespace Adze.Broker.Models;

/// <summary>
/// Holds the full state of a multi-turn agent conversation within a single user interaction.
/// Not persisted across "Run assistant" clicks (each click starts fresh).
/// </summary>
public sealed class AgentConversationState
{
    public string ConversationId { get; set; } = Guid.NewGuid().ToString("N");
    public string SystemPrompt { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; } = new();
    public List<AgentToolDefinition> ToolDefinitions { get; } = new();
    public ModelUsage AccumulatedUsage { get; set; } = new();
    public int IterationCount { get; set; }
    public int MaxIterations { get; set; } = 10;
    public List<ToolResult> AccumulatedToolResults { get; } = new();

    public void AddUserMessage(string text) { ... }
    public void AddAssistantTextMessage(string text) { ... }
    public void AddAssistantToolUseMessage(List<ToolUseRequest> toolUses) { ... }
    public void AddToolResultMessage(string toolUseId, string content, bool isError) { ... }
}
```

### Tool Definition Contract

```csharp
public sealed class AgentToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object?> InputSchema { get; set; } = new();
}
```

Each of the 10 existing grounding tools gets a tool definition that describes its name, purpose, and parameter schema in the format expected by the API provider. The tool definitions are built from the existing `ToolContracts.cs` parameter classes and `ToolNames.cs` constants.

### Token Limit Management

The conversation grows with each iteration. Without truncation, it will eventually exceed the model's context window.

**Strategy: sliding window with protected anchors.**

```
[system prompt]                       <-- always present, never truncated
[user message]                        <-- always present (original user request)
[... older assistant/tool turns ...]  <-- candidates for truncation
[recent assistant/tool turns]         <-- protected (last N iterations)
```

Implementation:

```csharp
public sealed class ConversationTruncator
{
    private const int EstimatedTokensPerChar = 4;  // conservative estimate
    private const int ProtectedRecentTurns = 3;     // keep last 3 tool-use/result pairs

    public static List<ConversationMessage> Truncate(
        AgentConversationState state,
        int maxContextTokens)
    {
        // 1. Always include system prompt and first user message
        // 2. Estimate token count for all messages
        // 3. If under limit, return all messages
        // 4. If over limit, remove oldest assistant+tool pairs
        //    (keeping at least ProtectedRecentTurns recent pairs)
        // 5. If still over, summarize dropped turns into a single
        //    "conversation summary" user message after the first user message
    }
}
```

The token estimate is conservative (chars/4). A future improvement could use a tokenizer library, but the simple heuristic is sufficient for .NET 4.8 where adding dependencies is expensive.

### Within-Session vs. Cross-Session State

For the initial implementation, conversation state is **per-click** -- each "Run assistant" click starts a fresh agent loop. The conversation within that loop can span multiple model turns, but clicking "Run assistant" again starts from scratch.

**Future consideration:** maintaining conversation history across clicks (true multi-turn chat). This would require:
- A persistent `AgentConversationState` on the `TaskPaneControl`
- A "New conversation" button
- More aggressive truncation strategy
- Cross-click context refresh (SessionContext changes as the user works in SOLIDWORKS)

This is explicitly deferred. The agent loop within a single click is the current scope.

---

## 5. Agent Loop Flow: Detailed Design

### Core Loop Implementation

```csharp
namespace Adze.Broker.Orchestration;

public sealed class AgentLoopResult
{
    public string FinalAnswer { get; set; } = string.Empty;
    public string AnswerSource { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public List<ToolResult> ExecutedTools { get; } = new();
    public ModelUsage TotalUsage { get; set; } = new();
    public int IterationsUsed { get; set; }
    public bool WasCancelled { get; set; }
    public string? ErrorMessage { get; set; }
    public AgentConversationState ConversationState { get; set; }

    // For backward-compat with existing UI
    public string TurnStatus { get; set; } = "ready";
}

public sealed class AgentLoopRunner
{
    private readonly IAgentModelClient _modelClient;
    private readonly GroundingToolCatalog _toolCatalog;
    private readonly int _maxIterations;

    /// <summary>
    /// Runs the agent loop. Called on a background thread.
    /// </summary>
    /// <param name="context">Initial SessionContext captured on UI thread.</param>
    /// <param name="userRequest">User's question.</param>
    /// <param name="cancellationToken">Checked between iterations.</param>
    /// <param name="onProgress">Called to report progress (marshaled to UI by caller).</param>
    /// <param name="refreshContext">
    ///   Delegate that marshals to UI thread to re-capture SessionContext.
    ///   Signature: Func&lt;SessionContext&gt;, called via Control.Invoke.
    /// </param>
    public AgentLoopResult Run(
        SessionContext context,
        string userRequest,
        CancellationToken cancellationToken,
        Action<AgentProgressUpdate> onProgress,
        Func<SessionContext> refreshContext)
    {
        var state = new AgentConversationState
        {
            SystemPrompt = BuildAgentSystemPrompt(),
            MaxIterations = _maxIterations
        };

        RegisterToolDefinitions(state, context.Policy.EnabledTools);
        state.AddUserMessage(userRequest);

        while (state.IterationCount < state.MaxIterations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            state.IterationCount++;
            onProgress(new AgentProgressUpdate
            {
                Phase = "model_call",
                Iteration = state.IterationCount,
                Message = "Thinking..."
            });

            // 1. Call model with current conversation + tool definitions
            AgentModelResponse response = _modelClient.CompleteWithTools(
                state.SystemPrompt,
                ConversationTruncator.Truncate(state, maxContextTokens: 100_000),
                state.ToolDefinitions);

            state.AccumulatedUsage = state.AccumulatedUsage + response.Usage;

            if (!response.Success)
            {
                return CreateErrorResult(state, response.FailureReason);
            }

            // 2. Check stop reason
            if (response.StopReason == "end_turn" || response.StopReason == "stop")
            {
                // Model produced a final text answer
                state.AddAssistantTextMessage(response.TextContent);
                return CreateSuccessResult(state, response);
            }

            if (response.StopReason == "tool_use")
            {
                // Model wants to call tools
                state.AddAssistantToolUseMessage(response.ToolUses);

                foreach (ToolUseRequest toolUse in response.ToolUses)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    onProgress(new AgentProgressUpdate
                    {
                        Phase = "tool_execution",
                        Iteration = state.IterationCount,
                        Message = $"Running {toolUse.Name}..."
                    });

                    // 3. Execute the tool
                    ToolResult result = ExecuteTool(context, toolUse);
                    state.AccumulatedToolResults.Add(result);

                    // 4. Add tool result to conversation
                    string resultJson = SerializeToolResult(result);
                    state.AddToolResultMessage(toolUse.Id, resultJson, !result.Success);
                }

                // Continue loop -- model will see tool results on next iteration
                continue;
            }

            // Unexpected stop reason
            return CreateErrorResult(state, "Unexpected stop reason: " + response.StopReason);
        }

        // Max iterations reached -- force a final answer from whatever we have
        onProgress(new AgentProgressUpdate
        {
            Phase = "max_iterations",
            Message = "Reached maximum iterations. Producing answer from available results."
        });

        return CreateMaxIterationsResult(state);
    }
}
```

### Model Client Interface Extension

The existing `IModelClient` supports `Complete(BrokerPrompt)` (returns structured JSON) and `Synthesize(AssistantSynthesisPrompt)` (returns plain text). Neither supports tool-use protocol.

**Proposed new interface** (does not replace existing one):

```csharp
namespace Adze.Broker.Abstractions;

/// <summary>
/// Model client that supports the tool-use conversation protocol.
/// Separate from IModelClient to avoid breaking existing broker/synthesis paths.
/// </summary>
public interface IAgentModelClient
{
    AgentModelResponse CompleteWithTools(
        string systemPrompt,
        List<ConversationMessage> messages,
        List<AgentToolDefinition> toolDefinitions);
}
```

```csharp
public sealed class AgentModelResponse
{
    public bool Success { get; set; }
    public string StopReason { get; set; } = string.Empty;  // "end_turn", "tool_use", "stop", "max_tokens"
    public string? TextContent { get; set; }
    public List<ToolUseRequest>? ToolUses { get; set; }
    public ModelUsage Usage { get; set; } = new();
    public string FailureReason { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
```

### Provider-Specific Implementation Notes

**Anthropic Messages API** tool-use format:

```json
{
  "model": "claude-sonnet-4-20250514",
  "max_tokens": 4096,
  "system": "...",
  "tools": [
    {
      "name": "get_dimensions",
      "description": "Returns dimensions for the active document or current selection.",
      "input_schema": {
        "type": "object",
        "properties": {
          "scope": { "type": "string", "enum": ["selection", "document"] },
          "include_driven": { "type": "boolean" }
        }
      }
    }
  ],
  "messages": [
    { "role": "user", "content": "What are the dimensions of this part?" },
    {
      "role": "assistant",
      "content": [
        { "type": "tool_use", "id": "toolu_01A", "name": "get_dimensions", "input": { "scope": "document" } }
      ]
    },
    {
      "role": "user",
      "content": [
        { "type": "tool_result", "tool_use_id": "toolu_01A", "content": "{...json...}" }
      ]
    }
  ]
}
```

**OpenAI Chat Completions API** tool-use format:

```json
{
  "model": "gpt-4o",
  "messages": [
    { "role": "system", "content": "..." },
    { "role": "user", "content": "What are the dimensions?" },
    {
      "role": "assistant",
      "tool_calls": [
        { "id": "call_abc123", "type": "function", "function": { "name": "get_dimensions", "arguments": "{\"scope\":\"document\"}" } }
      ]
    },
    { "role": "tool", "tool_call_id": "call_abc123", "content": "{...json...}" }
  ],
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "get_dimensions",
        "description": "...",
        "parameters": { "type": "object", "properties": { ... } }
      }
    }
  ]
}
```

The `IAgentModelClient` implementations for each provider will translate the internal `ConversationMessage` format into the provider-specific wire format. The `AnthropicAgentClient` and `OpenAIAgentClient` classes handle this translation.

---

## 6. Cancellation

### Design

```csharp
// In TaskPaneControl:
private CancellationTokenSource? _agentCancellation;

private void RunAssistant()
{
    // ... existing guard ...
    _agentCancellation = new CancellationTokenSource();
    CancellationToken token = _agentCancellation.Token;

    // Change button text to "Cancel"
    _runButton.Text = "Cancel";
    _runButton.Click -= RunAssistantHandler;
    _runButton.Click += CancelAssistantHandler;
    _runButton.Enabled = true;  // re-enable as cancel button

    // ... capture context on UI thread ...

    ThreadPool.QueueUserWorkItem(_ =>
    {
        try
        {
            AgentLoopResult result = _agentRunner.Run(
                context, request, token, OnProgress, RefreshContextOnUiThread);
            PostToUi(() => ApplyAgentResult(result));
        }
        catch (OperationCanceledException)
        {
            PostToUi(() => ShowCancelled());
        }
        catch (Exception ex)
        {
            PostToUi(() => ShowRunFailure(ex));
        }
        finally
        {
            PostToUi(FinishAssistantRunUi);
        }
    });
}

private void CancelAssistant()
{
    _agentCancellation?.Cancel();
    _runStateLabel.Text = "Cancelling...";
    _runButton.Enabled = false;
}
```

### Cancellation Points

The `CancellationToken` is checked at these points in the agent loop:

1. **Before each API call.** `cancellationToken.ThrowIfCancellationRequested()` at the top of the loop.
2. **After each API call returns.** If the user cancels during a long API call, the call will complete (HTTP requests are not cancellable with `HttpWebRequest` without `Abort()`), but the loop will exit on the next check.
3. **Before each tool execution.** Between processing individual `tool_use` blocks.
4. **Optionally: HTTP request abort.** Store a reference to the active `HttpWebRequest` and call `.Abort()` when cancellation is requested. This throws a `WebException` with `Status == WebExceptionStatus.RequestCanceled`, which the API client catches and converts to a cancellation-aware result.

### Graceful vs. Immediate Cancellation

When cancelled, the agent loop should:
- Stop making new API calls
- Stop executing new tools
- Return partial results (tools already executed, partial conversation state)
- Display "Run was cancelled. Partial results from N tool(s) are shown below."
- NOT corrupt conversation state or leave the UI in a broken state

---

## 7. Streaming and Progress

### Progress Update Contract

```csharp
public sealed class AgentProgressUpdate
{
    public string Phase { get; set; } = string.Empty;     // "model_call", "tool_execution", "max_iterations"
    public int Iteration { get; set; }
    public int MaxIterations { get; set; }
    public string Message { get; set; } = string.Empty;   // Human-readable status
    public string? ToolName { get; set; }                  // Which tool is running, if applicable
    public int ToolsCompleted { get; set; }                // Tools done in this iteration
    public int ToolsTotal { get; set; }                    // Tools requested in this iteration
}
```

### UI Updates During Agent Loop

The `_runStateLabel` and potentially the answer/plan panels update as the agent works:

| Phase | Label Text | Other UI Updates |
|-------|------------|------------------|
| `model_call` (iteration 1) | "Thinking..." | -- |
| `model_call` (iteration N) | "Thinking... (turn N of max)" | -- |
| `tool_execution` | "Running get_dimensions... (tool 1 of 3, turn 2)" | Tools tab shows partial results |
| `max_iterations` | "Producing final answer..." | -- |
| `cancelling` | "Cancelling..." | Button disabled |

### Streaming API Responses (Future)

Both OpenAI and Anthropic support streaming (`stream: true`). For the initial implementation, use non-streaming requests. Streaming adds significant parsing complexity (SSE line-by-line parsing with `HttpWebRequest` on .NET 4.8) and the primary latency bottleneck is the model thinking, not network transfer.

**When to add streaming:**
- When the model's "thinking" output is long enough that the user perceives a dead wait
- When we want to show the answer as it is being generated
- Estimated effort: medium (SSE parser, incremental content assembly, thread-safe UI update)

### Intermediate Tool Results Display

As each tool completes within the loop, append its result to the Tools tab:

```csharp
private void OnProgress(AgentProgressUpdate update)
{
    PostToUi(() =>
    {
        _runStateLabel.Text = update.Message;
        if (update.Phase == "tool_execution" && update.ToolName != null)
        {
            // Append to tools display as they complete
            AppendToolResult(update);
        }
    });
}
```

---

## 8. Error Handling

### Error Categories and Recovery

| Error | Where It Happens | Recovery |
|-------|-----------------|----------|
| API timeout | `HttpWebRequest.GetResponse()` | Log the timeout, check iteration count. If iterations remain, retry once. If not, fall back to deterministic answer from accumulated tool results. |
| API rate limit (429) | HTTP response | Wait and retry once with exponential backoff (1s, then 2s). If still failing, fall back to deterministic. |
| API error (400, 401, 500) | HTTP response | Stop the loop. Return error message. Do not retry auth errors. |
| JSON parse failure | Response parsing | Log raw response. If the model returned partial text, try to use it as the answer. Otherwise fall back to deterministic. |
| Tool execution throws | Tool `Execute()` method | Catch the exception. Create a `ToolResult` with `Success = false` and the exception message. Send this as the `tool_result` to the model -- the model can reason about the failure and try a different approach. |
| COM exception during context refresh | `Control.Invoke` callback | Catch the `COMException`. Use the stale `SessionContext`. Log a warning. Continue the loop. |
| `InvalidOperationException` from `Invoke` | Control disposed during loop | Treat as cancellation. Return partial results. |
| Tool returns unexpected data | Tool result parsing | Always serialize tool results as JSON strings. Truncate if over a size limit (e.g., 8KB per tool result) to avoid blowing the context window. |
| Model returns unknown tool name | Response parsing | Send a `tool_result` with `is_error: true` and content "Unknown tool: {name}". The model will typically self-correct. |
| Model returns malformed tool input | Tool parameter parsing | Send a `tool_result` with `is_error: true` and content "Invalid parameters: {details}". |

### Per-Iteration Error Budget

```csharp
public sealed class AgentLoopConfig
{
    public int MaxIterations { get; set; } = 10;
    public int MaxConsecutiveErrors { get; set; } = 2;
    public int ApiTimeoutMs { get; set; } = 30_000;
    public int MaxToolResultBytes { get; set; } = 8192;
    public int MaxTotalTokens { get; set; } = 100_000;
}
```

If `MaxConsecutiveErrors` consecutive API calls fail (not tool errors -- those are sent back to the model), the loop stops and falls back to the deterministic answer builder using whatever tool results have been accumulated.

### Deterministic Fallback Integration

The existing `GroundingAnswerBuilder.Build()` and `GroundingSynthesisService.Build()` remain available as fallback paths. If the agent loop fails entirely (API unreachable, auth error, etc.), the system can fall back to the current single-turn behavior:

```csharp
if (agentLoopResult.ErrorMessage != null && agentLoopResult.ExecutedTools.Count == 0)
{
    // Complete fallback to current single-turn deterministic path
    return FallbackToSingleTurn(context, userRequest);
}
```

---

## 9. Integration with Existing Code

### What Changes

| Component | Change Required |
|-----------|----------------|
| `IAgentModelClient` | **New interface** in `Adze.Broker/Abstractions/` |
| `OpenAIAgentClient` | **New class** in `Adze.Broker/Clients/` -- implements tool-use protocol for OpenAI |
| `AnthropicAgentClient` | **New class** in `Adze.Broker/Clients/` -- implements tool-use protocol for Anthropic |
| `AgentLoopRunner` | **New class** in `Adze.Broker/Orchestration/` -- the core loop |
| `AgentConversationState` | **New class** in `Adze.Broker/Models/` -- message history |
| `ConversationMessage` etc. | **New classes** in `Adze.Broker/Models/` -- message contracts |
| `AgentToolDefinition` | **New class** in `Adze.Broker/Models/` -- tool schema for API |
| `AgentToolDefinitionBuilder` | **New class** in `Adze.Broker/Formatting/` -- builds tool defs from existing contracts |
| `ConversationTruncator` | **New class** in `Adze.Broker/Formatting/` -- token management |
| `AgentLoopConfig` | **New class** in `Adze.Broker/Configuration/` -- loop settings |
| `TaskPaneControl` | **Modified** -- add cancellation, progress, agent loop invocation |
| `HostState` | **Modified** -- add `RunAgentLoop()` alongside existing `RunAssistant()` |
| `GroundingExecutionService` | **Modified** -- extract tool dispatch into a reusable method |

### What Does NOT Change

| Component | Reason |
|-----------|--------|
| `IModelClient` | Existing interface stays for broker planning and synthesis (backward compat) |
| `OpenAIModelClient` | Stays for existing single-turn broker path |
| `AnthropicMessagesModelClient` | Stays for existing single-turn synthesis path |
| `HybridBrokerOrchestrator` | Stays as fallback/deterministic path |
| `KeywordBrokerOrchestrator` | Stays as deterministic fallback |
| `GroundingToolCatalog` | Stays unchanged -- tools are reused |
| `IReadOnlyTool<T>` | Stays unchanged -- tool interface is stable |
| All 10 grounding tools | Stay unchanged -- they operate on `SessionContext` |
| `SessionContext`, `ToolResult` | Stay unchanged -- they are the stable contracts |
| `BrokerModelSettings` | Extended with new settings, not replaced |
| Trace, progression, recipes | Stay unchanged -- agent runs produce trace events like single-turn runs |

### Component Diagram

```
+---------------------------+
|     TaskPaneControl        |
|  (Windows Forms UserControl)|
|  [UI thread / STA]         |
|                             |
|  "Run assistant" button     |
|  "Cancel" toggle            |
|  Progress label             |
|  Answer / Plan / Tools tabs |
+------+----------------+----+
       |                |
       | Click          | Invoke/BeginInvoke
       v                ^
+------+----------------+----+
|     HostState               |
|  (static orchestrator)      |
|                             |
|  PrepareAssistantRun()      |  <-- UI thread: COM capture
|  RunAgentLoop()             |  <-- background thread: agent loop
|  RunAssistant()             |  <-- existing single-turn (preserved)
+------+---------------------+
       |
       v
+------+---------------------+
|     AgentLoopRunner         |
|  (Adze.Broker)              |
|  [background thread]        |
|                             |
|  Run(context, request,      |
|      token, onProgress,     |
|      refreshContext)         |
+---+----+----+---+-----------+
    |    |    |   |
    v    |    |   v
+---+--+ | +--+--+---+    +---+----+
|IAgent| | |Conversa- |    |Conversa|
|Model | | |tionState |    |tion    |
|Client| | |          |    |Trunca- |
+---+--+ | +----------+    |tor     |
    |    |                  +--------+
    v    v
+---+--+---+---------+
| OpenAI | Anthropic  |
| Agent  | Agent      |
| Client | Client     |
+--------+------+-----+
                |
                v (tool_use response)
+---------------+-----+
|  Tool Dispatch       |
|  (reused from        |
|   GroundingExecution |
|   Service)           |
+---+------------------+
    |
    v
+---+-----------------+
| GroundingToolCatalog |
| (10 read-only tools) |
+---------------------+
    |
    v operates on
+---+-----------------+
| SessionContext       |
| (captured from COM   |
|  on UI thread)       |
+---------------------+
```

### Phased Rollout

**Phase 1: Agent loop with existing tools, no UI streaming**
- Add `IAgentModelClient`, `AgentLoopRunner`, conversation state classes
- Implement `AnthropicAgentClient` (primary provider)
- Wire into `HostState.RunAgentLoop()` and `TaskPaneControl`
- Add cancellation button
- Progress via `_runStateLabel` text only
- Feature-gated behind `SOLIDWORKS_AI_AGENT_LOOP=true`
- Existing single-turn path remains the default

**Phase 2: OpenAI agent client, config, hardening**
- Implement `OpenAIAgentClient`
- Add `AgentLoopConfig` with env-var overrides
- Add conversation truncation
- Add intermediate tool results in Tools tab
- Expand unit tests for agent loop, conversation state, truncation

**Phase 3: Streaming, cross-click conversation, richer progress**
- SSE streaming for answer generation
- Persist conversation state across clicks
- Richer progress UI (animated indicator, partial answer display)
- Token budget visualization

---

## 10. Tool Dispatch Refactoring

The existing `GroundingExecutionService.ExecuteRecommendedTools()` uses a switch expression over `ToolNames` constants to dispatch to the correct tool with hardcoded default parameters. The agent loop needs a different dispatch pattern because the model provides tool names AND parameters.

### Proposed Tool Dispatcher

```csharp
namespace Adze.Host.Services;

/// <summary>
/// Dispatches tool calls from the agent loop.
/// Accepts a tool name and a parameter dictionary (from the model's tool_use input),
/// maps them to the correct typed tool, and returns a ToolResult.
/// </summary>
internal static class AgentToolDispatcher
{
    private static readonly GroundingToolCatalog Tools = ToolCatalog.CreateGroundingCatalog();

    public static ToolResult Dispatch(
        SessionContext context,
        string toolName,
        Dictionary<string, object?> input)
    {
        try
        {
            return toolName switch
            {
                ToolNames.GetActiveDocument =>
                    Tools.ActiveDocument.Execute(context, new EmptyParameters()),

                ToolNames.GetDocumentSummary =>
                    Tools.DocumentSummary.Execute(context, new GetDocumentSummaryParameters
                    {
                        IncludeDiagnostics = ReadBool(input, "include_diagnostics", true),
                        IncludeProperties = ReadBool(input, "include_properties", true)
                    }),

                ToolNames.GetDimensions =>
                    Tools.Dimensions.Execute(context, new GetDimensionsParameters
                    {
                        Scope = ReadString(input, "scope", "selection"),
                        IncludeDriven = ReadBool(input, "include_driven", true)
                    }),

                // ... other tools with parameter mapping ...

                _ => new ToolResult
                {
                    ToolName = toolName,
                    Success = false,
                    Summary = "Unknown tool: " + toolName
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = toolName,
                Success = false,
                Summary = "Tool execution failed: " + ex.Message
            };
        }
    }
}
```

This keeps the existing `GroundingExecutionService.ExecuteRecommendedTools()` intact for the single-turn path while providing a model-parameter-aware dispatcher for the agent loop.

---

## 11. Configuration

### New Environment Variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `SOLIDWORKS_AI_AGENT_LOOP` | `false` | Feature gate for agent loop (vs. single-turn) |
| `SOLIDWORKS_AI_AGENT_MAX_ITERATIONS` | `10` | Maximum loop iterations before forced stop |
| `SOLIDWORKS_AI_AGENT_MAX_CONSECUTIVE_ERRORS` | `2` | Consecutive API errors before fallback |
| `SOLIDWORKS_AI_AGENT_TIMEOUT_MS` | `30000` | Per-API-call timeout in agent loop |
| `SOLIDWORKS_AI_AGENT_MAX_TOKENS` | `4096` | Max tokens per agent API call |
| `SOLIDWORKS_AI_AGENT_MAX_CONTEXT_TOKENS` | `100000` | Truncation threshold for conversation |
| `SOLIDWORKS_AI_AGENT_MAX_TOOL_RESULT_BYTES` | `8192` | Maximum serialized size per tool result |

These are read by `AgentLoopConfig.LoadFromEnvironment()`, following the same pattern as `BrokerModelSettings`.

---

## 12. Testing Strategy

### Unit Tests (Adze.Tests)

| Test Area | What to Test |
|-----------|-------------|
| `AgentConversationState` | Message addition, role validation, iteration counting |
| `ConversationTruncator` | Token estimation, sliding window, protected anchors |
| `AgentToolDispatcher` | All 10 tools dispatched correctly, unknown tool handling, exception handling |
| `AgentToolDefinitionBuilder` | Correct schema generation for all 10 tools |
| `AgentLoopRunner` | Mock `IAgentModelClient` returning scripted sequences: text-only, single tool use, multi-tool use, max iterations, consecutive errors |
| `AnthropicAgentClient` serialization | Correct wire format for tool definitions, messages, tool results |
| `OpenAIAgentClient` serialization | Correct wire format for function calling |
| Response parsing | `tool_use` blocks parsed correctly, `end_turn` detected, error responses handled |
| Cancellation | Token checked at correct points, partial results returned |

### Integration / Eval Tests

| Test | Method |
|------|--------|
| End-to-end agent loop with mock model | Scripted `IAgentModelClient` that returns a tool_use then a text response |
| Deterministic fallback when API unavailable | Verify agent loop degrades to single-turn |
| Thread safety | Agent loop on background thread, progress callbacks on UI thread (manual verification in host) |

---

## 13. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Runaway loop burns API credits | Cost | Hard iteration cap (default 10). Token budget tracking. Per-session usage display. |
| COM data goes stale during long agent runs | Incorrect tool results | Most tools use initially captured context. COM refresh available via `refreshContext` delegate for specific scenarios. |
| Model calls wrong tools repeatedly | Wasted iterations | Error results sent back to model. Consecutive-error cap stops the loop. Tool definitions include clear descriptions. |
| Large tool results blow context window | API errors or truncated context | Per-tool result size cap. Conversation truncator removes old turns. |
| .NET 4.8 limitations | No async/await, limited cancellation | Use `ThreadPool` + `CancellationTokenSource` (available since .NET 4.0). Blocking HTTP calls with timeout. |
| Breaking existing single-turn path | Regression | Agent loop is feature-gated. All existing classes preserved. Rollback is flipping the env var. |
| Two different tool-use wire formats (OpenAI vs Anthropic) | Maintenance | Abstract behind `IAgentModelClient`. Each provider handles its own serialization. Internal `ConversationMessage` format is provider-neutral. |

---

## 14. Open Questions

1. **Should the agent loop support cross-click conversation?** Current design says no (each click starts fresh). But users may want "tell me more about that dimension" as a follow-up. This could be Phase 3.

2. **Should tools be able to request a COM refresh?** Most tools operate on cached `SessionContext`. But `get_selection_context` might benefit from fresh data if the user changed their selection during a long run. The `refreshContext` delegate supports this, but which tools should trigger it?

3. **Should the agent loop coexist with the existing synthesis call?** Current design says no -- the agent loop's final text response replaces the separate synthesis step. The model has seen all tool results in context and produces its own synthesis. But the deterministic fallback (`GroundingAnswerBuilder`) should still be available when the agent loop fails entirely.

4. **Max token budget per agent run?** The loop can accumulate significant usage across iterations. Should there be a per-run token cap in addition to the per-call max_tokens? E.g., stop after 50,000 total tokens consumed.

5. **Should tool definitions include parameter schemas for all tools, or only the ones enabled by the current policy tier?** Current design: only enabled tools. This prevents the model from trying to call tools the user has not unlocked.

---

## 15. Recommended Implementation Order

1. `ConversationMessage`, `ToolUseRequest`, `ToolUseResult`, `AgentToolDefinition` (contracts in `Adze.Broker/Models/`)
2. `AgentConversationState` (state container in `Adze.Broker/Models/`)
3. `IAgentModelClient`, `AgentModelResponse` (interface in `Adze.Broker/Abstractions/`)
4. `AgentToolDefinitionBuilder` (tool schema builder in `Adze.Broker/Formatting/`)
5. `AgentToolDispatcher` (parameter-aware dispatch in `Adze.Host/Services/`)
6. `ConversationTruncator` (token management in `Adze.Broker/Formatting/`)
7. `AgentLoopConfig` (configuration in `Adze.Broker/Configuration/`)
8. `AgentLoopRunner` (core loop in `Adze.Broker/Orchestration/`)
9. `AnthropicAgentClient` (first provider in `Adze.Broker/Clients/`)
10. Unit tests for all of the above
11. `HostState.RunAgentLoop()` integration
12. `TaskPaneControl` modifications (cancellation button, progress, feature gate)
13. `OpenAIAgentClient` (second provider)
14. End-to-end host validation with agent loop enabled
