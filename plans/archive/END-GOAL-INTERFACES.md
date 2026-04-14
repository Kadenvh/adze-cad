# END-GOAL-INTERFACES: Runtime Contracts and C# Boundaries

**Date:** 2026-03-15  
**Status:** Final curated interface specification  
**Purpose:** Define the concrete contracts, namespaces, DTOs, and state machines needed to implement the final architecture in C# without blurring responsibilities.

---

## 1. Design principles

1. **Do not break the existing deterministic path.**
   - New agentic infrastructure should be additive.
   - Existing broker/synthesis flow remains available behind feature gates and fallback logic.

2. **Keep COM on the UI thread.**
   - No contract should encourage background-thread COM access.
   - Any interface that touches SOLIDWORKS directly should be assumed STA/UI-thread-only unless explicitly documented otherwise.

3. **Normalize the runtime early.**
   - Provider messages, tool calls, tool results, approval states, snapshots, and traces should all have normalized internal representations.

4. **Keep tool contracts narrow.**
   - Each tool should have one clear purpose, one parameter object, and one result shape.

5. **Separate “model chose” from “host allowed.”**
   - The model can request a tool.
   - The host decides whether the request is valid, permitted, confirmable, and executable.

---

## 2. Recommended assembly / namespace layout

Recommended logical layout:

- `Adze.Broker.Abstractions`
- `Adze.Broker.Configuration`
- `Adze.Broker.Clients`
- `Adze.Broker.Orchestration`
- `Adze.Broker.Formatting`
- `Adze.Tools.Abstractions`
- `Adze.Tools.Read`
- `Adze.Tools.Write`
- `Adze.Host.UI`
- `Adze.Host.Runtime`
- `Adze.Host.Policy`
- `Adze.Host.Services`
- `Adze.Trace`
- `Adze.Index`

This need not mean new projects immediately; it is first a contract boundary map.

---

## 3. Conversation and runtime core

### 3.1 Roles and stop reasons

```csharp
namespace Adze.Broker.Abstractions;

public enum ConversationRole
{
    System,
    User,
    Assistant,
    Tool
}

public enum AgentStopReason
{
    EndTurn,
    ToolUse,
    WaitingForApproval,
    Cancelled,
    MaxTokens,
    MaxIterations,
    Error,
    Fallback
}

public enum AgentRunOutcome
{
    Success,
    Cancelled,
    BlockedByPolicy,
    Failed,
    FellBack
}
```

### 3.2 Normalized conversation message

```csharp
namespace Adze.Broker.Abstractions;

public sealed class ConversationMessage
{
    public ConversationRole Role { get; set; }
    public string Text { get; set; } = string.Empty;

    // Provider-specific raw payload when needed for echoing the exact turn.
    public object? RawPayload { get; set; }

    // Optional metadata for tracing or UI.
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### 3.3 Session conversation state

```csharp
namespace Adze.Broker.Abstractions;

public sealed class AgentConversationState
{
    public string SessionId { get; set; } = string.Empty;
    public string DocumentKey { get; set; } = string.Empty;
    public List<ConversationMessage> Messages { get; } = new();

    public int EstimatedTotalTokens { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### 3.4 Truncation contract

```csharp
namespace Adze.Broker.Abstractions;

public interface IConversationTruncator
{
    AgentConversationState Truncate(
        AgentConversationState state,
        int maxTotalTokens,
        TruncationPolicy policy);
}

public sealed class TruncationPolicy
{
    public bool ProtectSystemMessage { get; set; } = true;
    public bool ProtectInitialUserIntent { get; set; } = true;
    public int ProtectedRecentTurns { get; set; } = 6;
}
```

---

## 4. Provider abstraction

### 4.1 Tool definition and normalized tool call/result

```csharp
namespace Adze.Broker.Abstractions;

public sealed class AgentToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object?> ParameterSchema { get; set; } = new();
    public ToolCapabilityMetadata Capability { get; set; } = new();
}

public sealed class AgentToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object?> Arguments { get; set; } = new();
    public string ArgumentsJson { get; set; } = string.Empty;
}

public sealed class AgentToolResult
{
    public string ToolCallId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string OutputJson { get; set; } = string.Empty;
    public bool IsError { get; set; }
}
```

### 4.2 Turn response

```csharp
namespace Adze.Broker.Abstractions;

public sealed class AgentTurnResponse
{
    public bool Success { get; set; }
    public AgentStopReason StopReason { get; set; }
    public string TextContent { get; set; } = string.Empty;
    public List<AgentToolCall> ToolCalls { get; set; } = new();
    public string FailureReason { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public ModelUsage Usage { get; set; } = new();
    public object? RawAssistantMessage { get; set; }
}
```

### 4.3 Provider client interface

```csharp
namespace Adze.Broker.Abstractions;

public interface IAgentModelClient
{
    AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings);

    object BuildUserMessage(string content);
    List<object> BuildToolResultMessages(List<AgentToolResult> results);
}
```

### 4.4 Provider settings

```csharp
namespace Adze.Broker.Configuration;

public sealed class AgentModelSettings
{
    public int MaxTokens { get; set; } = 4096;
    public int TimeoutMilliseconds { get; set; } = 30000;
    public double Temperature { get; set; } = 0.1;
    public int MaxIterations { get; set; } = 10;
    public int MaxConsecutiveErrors { get; set; } = 2;
    public int MaxTotalTokens { get; set; } = 100000;
    public int MaxToolResultChars { get; set; } = 8192;
    public bool DisableParallelToolCalls { get; set; } = true;
}
```

### 4.5 Provider client families

```csharp
namespace Adze.Broker.Clients;

public abstract class OpenAIFormatAgentClient : IAgentModelClient
{
    public abstract AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings);

    public abstract object BuildUserMessage(string content);
    public abstract List<object> BuildToolResultMessages(List<AgentToolResult> results);
}

public sealed class OpenAIAgentClient : OpenAIFormatAgentClient { }
public sealed class OpenRouterAgentClient : OpenAIFormatAgentClient { }
public sealed class OllamaAgentClient : OpenAIFormatAgentClient { }
public sealed class LmStudioAgentClient : OpenAIFormatAgentClient { }

public sealed class AnthropicAgentClient : IAgentModelClient
{
    public AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings) => throw new NotImplementedException();

    public object BuildUserMessage(string content) => throw new NotImplementedException();
    public List<object> BuildToolResultMessages(List<AgentToolResult> results) => throw new NotImplementedException();
}
```

### 4.6 Factory boundary

```csharp
namespace Adze.Broker.Abstractions;

public interface IAgentModelClientFactory
{
    IAgentModelClient Create(ProviderSelection provider);
}

public sealed class ProviderSelection
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
}
```

---

## 5. Tool registry and dispatch

### 5.1 Capability metadata

```csharp
namespace Adze.Tools.Abstractions;

public enum ToolCapabilityClass
{
    ReadSafe,
    SoftWrite,
    HardWriteFirstWave,
    HardWriteAdvanced,
    DeferredHighRisk
}

public enum ApprovalRequirement
{
    None,
    StandardConfirmation,
    ElevatedConfirmation,
    Disallowed
}

public sealed class ToolCapabilityMetadata
{
    public ToolCapabilityClass CapabilityClass { get; set; }
    public ApprovalRequirement ApprovalRequirement { get; set; }
    public bool RequiresUiThread { get; set; }
    public bool RequiresRebuild { get; set; }
    public bool SupportsUndoGrouping { get; set; }
    public bool MustCaptureSnapshot { get; set; }
    public bool AllowedInBatchPlan { get; set; }
}
```

### 5.2 Registry boundary

```csharp
namespace Adze.Tools.Abstractions;

public interface IToolRegistry
{
    IReadOnlyList<IToolDescriptor> GetEnabledTools(SessionContext context);
    IToolDescriptor? GetByName(string toolName);
}

public interface IToolDescriptor
{
    string Name { get; }
    string Description { get; }
    Type ParameterType { get; }
    Type ResultType { get; }
    ToolCapabilityMetadata Capability { get; }
    Dictionary<string, object?> BuildJsonSchema();
}
```

### 5.3 Runtime dispatch

```csharp
namespace Adze.Broker.Abstractions;

public interface IToolExecutor
{
    AgentToolResult Execute(
        string toolName,
        Dictionary<string, object?> arguments,
        ToolExecutionContext context);
}

public sealed class ToolExecutionContext
{
    public SessionContext SessionContext { get; set; } = new();
    public string SessionId { get; set; } = string.Empty;
    public CancellationToken CancellationToken { get; set; }
}
```

### 5.4 Parameter validation

```csharp
namespace Adze.Tools.Abstractions;

public interface IToolArgumentValidator
{
    ToolArgumentValidationResult Validate(
        IToolDescriptor descriptor,
        Dictionary<string, object?> rawArguments);
}

public sealed class ToolArgumentValidationResult
{
    public bool Success { get; set; }
    public object? TypedParameters { get; set; }
    public string FailureReason { get; set; } = string.Empty;
}
```

---

## 6. Agent loop orchestration

### 6.1 Loop runner

```csharp
namespace Adze.Broker.Orchestration;

public interface IAgentLoopRunner
{
    AgentLoopResult Run(
        IAgentModelClient modelClient,
        IToolExecutor toolExecutor,
        string systemPrompt,
        string userRequest,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        CancellationToken cancellationToken,
        Action<AgentProgressUpdate>? onProgress);
}

public sealed class AgentLoopResult
{
    public AgentRunOutcome Outcome { get; set; }
    public string FinalAnswer { get; set; } = string.Empty;
    public List<ExecutedToolRecord> ExecutedTools { get; set; } = new();
    public List<WriteTraceRecord> Writes { get; set; } = new();
    public string FailureReason { get; set; } = string.Empty;
    public ModelUsage AggregateUsage { get; set; } = new();
}
```

### 6.2 Progress updates

```csharp
namespace Adze.Broker.Orchestration;

public enum AgentProgressKind
{
    Started,
    Thinking,
    ToolRequested,
    ToolExecuting,
    WaitingForApproval,
    Approved,
    Cancelled,
    Verifying,
    Completed,
    Failed,
    FellBack
}

public sealed class AgentProgressUpdate
{
    public AgentProgressKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public int Iteration { get; set; }
}
```

### 6.3 Approval pause/resume boundary

```csharp
namespace Adze.Broker.Abstractions;

public interface IApprovalCoordinator
{
    ApprovalDecision RequestApproval(WritePreview preview, CancellationToken cancellationToken);
}

public sealed class ApprovalDecision
{
    public bool Approved { get; set; }
    public bool Cancelled { get; set; }
    public string? ModifiedValueJson { get; set; }
    public string Reason { get; set; } = string.Empty;
}
```

This boundary keeps the loop generic while letting the Task Pane own the user interaction.

---

## 7. Session context and COM access

### 7.1 Session capture service

```csharp
namespace Adze.Host.Services;

public interface ISessionContextBuilder
{
    SessionContext Build(ISldWorks application);
}
```

### 7.2 UI-thread invocation boundary

```csharp
namespace Adze.Host.Runtime;

public interface IUiThreadInvoker
{
    void Invoke(Action action);
    T Invoke<T>(Func<T> func);
    void BeginInvoke(Action action);
}
```

Everything that touches COM directly should flow through this boundary or equivalent existing UI-thread utilities.

### 7.3 Fresh context refresh

```csharp
namespace Adze.Host.Services;

public interface ISessionRefreshService
{
    SessionContext Refresh();
}
```

This should be used sparingly, because over-refreshing burns UI-thread time.

---

## 8. Write tool contract

### 8.1 Core write interface

```csharp
namespace Adze.Tools.Write;

public interface IWriteTool<TParams>
{
    WritePreview Preview(SessionContext context, TParams parameters);
    WriteApplyResult Apply(ISldWorks application, TParams parameters);
    WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult);
    string BuildUndoLabel(TParams parameters);
}
```

### 8.2 Supporting DTOs

```csharp
namespace Adze.Tools.Write;

public sealed class WritePreview
{
    public string ToolName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<WriteChangeItem> Changes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool ElevatedConfirmationRequired { get; set; }
}

public sealed class WriteChangeItem
{
    public string TargetLabel { get; set; } = string.Empty;
    public string BeforeValue { get; set; } = string.Empty;
    public string AfterValue { get; set; } = string.Empty;
}

public sealed class WriteApplyResult
{
    public bool Success { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public bool RebuildPerformed { get; set; }
    public string UndoLabel { get; set; } = string.Empty;
    public object? ProviderSpecificResult { get; set; }
}

public sealed class WriteVerification
{
    public bool Success { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public StateDiff Diff { get; set; } = new();
}
```

### 8.3 Write execution coordinator

```csharp
namespace Adze.Host.Runtime;

public interface IWriteExecutionCoordinator
{
    WriteExecutionOutcome Execute<TParams>(
        IWriteTool<TParams> tool,
        TParams parameters,
        ToolExecutionContext executionContext);
}

public sealed class WriteExecutionOutcome
{
    public bool Success { get; set; }
    public WritePreview Preview { get; set; } = new();
    public WriteApplyResult ApplyResult { get; set; } = new();
    public WriteVerification Verification { get; set; } = new();
    public WriteTraceRecord Trace { get; set; } = new();
}
```

This coordinator should own the common preview → approval → apply → verify → trace sequence.

---

## 9. Snapshots, diffs, and verification

### 9.1 Snapshot boundary

```csharp
namespace Adze.Host.Runtime;

public interface IStateSnapshotService
{
    StateSnapshot CaptureBefore(WriteTargetDescriptor target);
    StateSnapshot CaptureAfter(WriteTargetDescriptor target);
}

public sealed class StateSnapshot
{
    public string SnapshotType { get; set; } = string.Empty;
    public Dictionary<string, object?> Values { get; set; } = new();
    public DateTimeOffset CapturedUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### 9.2 Diff boundary

```csharp
namespace Adze.Host.Runtime;

public interface IStateDiffService
{
    StateDiff Compare(StateSnapshot before, StateSnapshot after);
}

public sealed class StateDiff
{
    public bool HasChanges { get; set; }
    public List<StateDiffItem> Items { get; set; } = new();
}

public sealed class StateDiffItem
{
    public string Path { get; set; } = string.Empty;
    public string BeforeValue { get; set; } = string.Empty;
    public string AfterValue { get; set; } = string.Empty;
}
```

### 9.3 Verification policy

```csharp
namespace Adze.Host.Policy;

public interface IVerificationPolicy
{
    VerificationDecision Evaluate(
        string toolName,
        WriteVerification verification,
        SessionContext refreshedContext);
}

public sealed class VerificationDecision
{
    public bool Accepted { get; set; }
    public bool SuggestRollback { get; set; }
    public string Reason { get; set; } = string.Empty;
}
```

---

## 10. Policy and trust boundaries

### 10.1 Policy engine

```csharp
namespace Adze.Host.Policy;

public interface IAgentPolicyEngine
{
    ToolExecutionPolicy EvaluateToolRequest(
        SessionContext context,
        AgentToolCall toolCall,
        IToolDescriptor descriptor);
}

public sealed class ToolExecutionPolicy
{
    public bool Allowed { get; set; }
    public ApprovalRequirement ApprovalRequirement { get; set; }
    public string FailureReason { get; set; } = string.Empty;
}
```

### 10.2 Trust model

```csharp
namespace Adze.Host.Policy;

public enum TrustTier
{
    Baseline,
    Assisted,
    Reviewed,
    TrustedBounded
}

public interface ITrustService
{
    TrustTier GetCurrentTier(UserContext userContext);
    bool CanPromoteRecipe(RecipeCandidate candidate);
}
```

---

## 11. UI state machine

### 11.1 Task Pane state

```csharp
namespace Adze.Host.UI;

public enum PaneState
{
    Idle,
    Running,
    WaitingForConfirmation,
    Completed,
    Failed,
    Cancelled
}

public interface ITaskPaneStateController
{
    PaneState CurrentState { get; }
    void TransitionTo(PaneState state);
}
```

### 11.2 Preview surface contract

```csharp
namespace Adze.Host.UI;

public interface IWritePreviewPresenter
{
    void Show(WritePreview preview);
    void Hide();
}

public interface IPlanReviewPresenter
{
    void Show(PlanReviewModel model);
    void Hide();
}

public sealed class PlanReviewModel
{
    public string Title { get; set; } = string.Empty;
    public List<PlanStepModel> Steps { get; set; } = new();
}

public sealed class PlanStepModel
{
    public string StepId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Approved { get; set; }
    public bool IsWrite { get; set; }
}
```

---

## 12. Trace and observability contracts

### 12.1 Trace writer

```csharp
namespace Adze.Trace;

public interface IAgentTraceWriter
{
    void WriteRun(AgentRunTrace trace);
}

public sealed class AgentRunTrace
{
    public string SessionId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public AgentRunOutcome Outcome { get; set; }
    public List<ExecutedToolRecord> ExecutedTools { get; set; } = new();
    public List<WriteTraceRecord> Writes { get; set; } = new();
    public ModelUsage Usage { get; set; } = new();
    public TimeSpan Duration { get; set; }
}
```

### 12.2 Tool and write trace records

```csharp
namespace Adze.Trace;

public sealed class ExecutedToolRecord
{
    public string ToolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string InputJson { get; set; } = string.Empty;
    public string OutputJson { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
}

public sealed class WriteTraceRecord
{
    public string ToolName { get; set; } = string.Empty;
    public string UndoLabel { get; set; } = string.Empty;
    public StateSnapshot Before { get; set; } = new();
    public StateSnapshot After { get; set; } = new();
    public StateDiff Diff { get; set; } = new();
    public bool VerificationSucceeded { get; set; }
    public bool RollbackSuggested { get; set; }
}
```

---

## 13. Memory and recipes

### 13.1 Memory store

```csharp
namespace Adze.Host.Runtime;

public interface IMemoryStore
{
    DocumentMemory? LoadDocumentMemory(string documentKey);
    void SaveDocumentMemory(DocumentMemory memory);

    UserPreferenceMemory? LoadUserPreferences(string userKey);
    void SaveUserPreferences(UserPreferenceMemory memory);
}

public sealed class DocumentMemory
{
    public string DocumentKey { get; set; } = string.Empty;
    public List<RecipeCandidate> RecipeCandidates { get; set; } = new();
    public List<string> KnownPatterns { get; set; } = new();
}

public sealed class UserPreferenceMemory
{
    public string UserKey { get; set; } = string.Empty;
    public string PreferredAnswerMode { get; set; } = string.Empty;
    public bool PreferPreviewFirst { get; set; }
}
```

### 13.2 Recipe promotion boundary

```csharp
namespace Adze.Host.Runtime;

public interface IRecipePromotionService
{
    RecipeCandidate? TryCreateCandidate(AgentRunTrace trace);
    bool Promote(RecipeCandidate candidate);
}

public sealed class RecipeCandidate
{
    public string RecipeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> ToolSequence { get; set; } = new();
    public int VerifiedRunCount { get; set; }
}
```

---

## 14. Closed-file retrieval contracts

```csharp
namespace Adze.Index;

public interface IClosedFileIndexer
{
    IndexRunResult BuildIndex(string rootFolderPath);
}

public interface IClosedFileSearchService
{
    IReadOnlyList<ClosedFileSearchResult> Search(ClosedFileSearchQuery query);
}

public sealed class ClosedFileSearchQuery
{
    public string QueryText { get; set; } = string.Empty;
    public string? FileExtensionFilter { get; set; }
    public Dictionary<string, string> PropertyFilters { get; set; } = new();
}

public sealed class ClosedFileSearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<string, string> MatchingProperties { get; set; } = new();
}
```

This layer should stay pure .NET and independent of live COM inspection.

---

## 15. Feature gates and configuration flags

Recommended initial flags:

```text
SOLIDWORKS_AI_AGENT_LOOP=true|false
SOLIDWORKS_AI_FIRST_WAVE_WRITES=true|false
SOLIDWORKS_AI_RETRIEVAL=true|false
SOLIDWORKS_AI_LOCAL_MODELS=true|false
SOLIDWORKS_AI_STREAM_FINAL_TEXT=true|false
```

Configuration rules:
- each major phase should be disable-able,
- fallback path must remain reachable,
- local models should not auto-enable simply because an endpoint exists.

---

## 16. Phase-to-interface mapping

### Phase 1
Implement first:
- `AgentConversationState`
- `IConversationTruncator`
- clarification UI contracts
- `PaneState`

### Phase 2
Implement next:
- `IAgentModelClient`
- `AgentToolDefinition`
- `IAgentLoopRunner`
- `IToolRegistry`
- `IToolExecutor`
- `IAgentPolicyEngine`

### Phase 3
Implement next:
- `IStateSnapshotService`
- `IStateDiffService`
- `IVerificationPolicy`
- `IAgentTraceWriter`

### Phase 4
Implement next:
- `IWriteTool<TParams>`
- `IApprovalCoordinator`
- `IWriteExecutionCoordinator`
- `IWritePreviewPresenter`

### Phase 5+
Implement next:
- `IMemoryStore`
- `IRecipePromotionService`
- `IClosedFileIndexer`
- `IClosedFileSearchService`

---

## 17. Final implementation note

The important boundary is not “agent vs non-agent.”
The important boundary is:

- **provider-specific wire format** vs **normalized runtime state**,
- **model request** vs **host permission**,
- **COM/UI-thread work** vs **background loop work**,
- **preview/apply** vs **verify/trace**,
- **observed success** vs **promoted trusted behavior**.

If those boundaries stay clean, the codebase can grow into the final architecture without becoming fragile.
