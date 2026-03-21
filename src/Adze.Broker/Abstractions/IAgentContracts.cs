using System;
using System.Collections.Generic;
using System.Threading;
using Adze.Broker.Configuration;
using Adze.Broker.Models;
using Adze.Contracts.Models;

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

/// <summary>
/// Optional extension for agent model clients that support SSE streaming.
/// When the model returns text content, chunks stream via onTextChunk as they arrive.
/// When the model returns tool calls, chunks are buffered internally — no text callback fires.
/// </summary>
public interface IStreamingAgentModelClient : IAgentModelClient
{
    AgentTurnResponse SendTurnStreaming(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        Action<string> onTextChunk);
}

public interface IToolExecutor
{
    AgentToolResult Execute(
        string toolName,
        Dictionary<string, object?> arguments,
        ToolExecutionContext context);
}

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

public sealed class ToolExecutionContext
{
    public string SessionId { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public int CurrentIteration { get; set; }

    public CancellationToken CancellationToken { get; set; }

    public SessionContext? SessionContext { get; set; }
}
