using System.Collections.Generic;
using Adze.Tools.Abstractions;

namespace Adze.Broker.Models;

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

public sealed class AgentToolDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Dictionary<string, object?> ParameterSchema { get; set; } = new();

    public ToolCapabilityMetadata? Capability { get; set; }
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

public sealed class AgentProgressUpdate
{
    public AgentProgressKind Kind { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? ToolName { get; set; }

    public int Iteration { get; set; }
}

public sealed class AgentLoopResult
{
    public AgentRunOutcome Outcome { get; set; }

    public string FinalAnswer { get; set; } = string.Empty;

    public List<AgentToolResult> ExecutedTools { get; set; } = new();

    public string FailureReason { get; set; } = string.Empty;

    public ModelUsage AggregateUsage { get; set; } = new();
}
