using System;
using System.Collections.Generic;
using System.Threading;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;
using Adze.Broker.Models;

namespace Adze.Broker.Orchestration;

/// <summary>
/// Drives the iterative agent loop: send a turn to the model, execute any requested tools,
/// feed results back, and repeat until the model produces a final text answer or a stop
/// condition is reached.  All work runs on the calling thread (expected to be a background
/// thread in production; the host is responsible for threading).
/// </summary>
public sealed class AgentLoopRunner : IAgentLoopRunner
{
    public AgentLoopResult Run(
        IAgentModelClient modelClient,
        IToolExecutor toolExecutor,
        string systemPrompt,
        string userRequest,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        CancellationToken cancellationToken,
        Action<AgentProgressUpdate>? onProgress)
    {
        return Run(modelClient, toolExecutor, systemPrompt, userRequest, toolDefinitions, settings, cancellationToken, onProgress, null);
    }

    /// <summary>
    /// Runs the agent loop with optional prior conversation context for multi-turn awareness.
    /// Prior messages are prepended to the conversation history before the current user request.
    /// </summary>
    public AgentLoopResult Run(
        IAgentModelClient modelClient,
        IToolExecutor toolExecutor,
        string systemPrompt,
        string userRequest,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        CancellationToken cancellationToken,
        Action<AgentProgressUpdate>? onProgress,
        List<object>? priorConversation)
    {
        return Run(modelClient, toolExecutor, systemPrompt, userRequest, toolDefinitions, settings, cancellationToken, onProgress, priorConversation, null);
    }

    /// <summary>
    /// Runs the agent loop with optional streaming support. When onTextChunk is non-null and the
    /// model client implements IStreamingAgentModelClient, the final text turn streams tokens via
    /// the callback as they arrive. Tool-calling turns are always fully buffered.
    /// </summary>
    public AgentLoopResult Run(
        IAgentModelClient modelClient,
        IToolExecutor toolExecutor,
        string systemPrompt,
        string userRequest,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        CancellationToken cancellationToken,
        Action<AgentProgressUpdate>? onProgress,
        List<object>? priorConversation,
        Action<string>? onTextChunk)
    {
        if (modelClient == null) throw new ArgumentNullException(nameof(modelClient));
        if (toolExecutor == null) throw new ArgumentNullException(nameof(toolExecutor));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var executedTools = new List<AgentToolResult>();
        var aggregateUsage = new ModelUsage();
        var conversationHistory = new List<object>();
        int consecutiveErrors = 0;

        // Seed with prior conversation context (multi-turn history)
        if (priorConversation != null && priorConversation.Count > 0)
        {
            conversationHistory.AddRange(priorConversation);
        }

        // Seed the conversation with the user message.
        object userMessage = modelClient.BuildUserMessage(userRequest ?? string.Empty);
        conversationHistory.Add(userMessage);

        ReportProgress(onProgress, AgentProgressKind.Started, "Agent loop started.", iteration: 0);

        for (int iteration = 1; iteration <= settings.MaxIterations; iteration++)
        {
            // --- Cancellation check ---
            if (cancellationToken.IsCancellationRequested)
            {
                ReportProgress(onProgress, AgentProgressKind.Cancelled, "Cancelled by user.", iteration: iteration);
                return BuildResult(AgentRunOutcome.Cancelled, string.Empty, executedTools, "Cancelled by user.", aggregateUsage);
            }

            // --- Token budget check ---
            if (aggregateUsage.TotalTokens >= settings.MaxTotalTokens)
            {
                ReportProgress(onProgress, AgentProgressKind.FellBack, "Token budget exhausted.", iteration: iteration);
                return BuildResult(AgentRunOutcome.FellBack, string.Empty, executedTools, "Token budget exhausted.", aggregateUsage);
            }

            // --- Model turn ---
            ReportProgress(onProgress, AgentProgressKind.Thinking, $"Sending turn {iteration} to model.", iteration: iteration);

            AgentTurnResponse response;
            try
            {
                // Use streaming when the client supports it and a text callback is provided.
                // Tool-calling turns are buffered internally by the streaming client — only
                // the final text turn actually streams tokens to the callback.
                if (onTextChunk != null && modelClient is IStreamingAgentModelClient streamingClient)
                {
                    response = streamingClient.SendTurnStreaming(
                        systemPrompt ?? string.Empty,
                        conversationHistory,
                        toolDefinitions ?? new List<AgentToolDefinition>(),
                        settings,
                        onTextChunk);
                }
                else
                {
                    response = modelClient.SendTurn(
                        systemPrompt ?? string.Empty,
                        conversationHistory,
                        toolDefinitions ?? new List<AgentToolDefinition>(),
                        settings);
                }
            }
            catch (OperationCanceledException)
            {
                ReportProgress(onProgress, AgentProgressKind.Cancelled, "Cancelled during API call.", iteration: iteration);
                return BuildResult(AgentRunOutcome.Cancelled, string.Empty, executedTools, "Cancelled during API call.", aggregateUsage);
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                if (consecutiveErrors >= settings.MaxConsecutiveErrors)
                {
                    string failureReason = $"Max consecutive errors ({settings.MaxConsecutiveErrors}) reached. Last error: {ex.Message}";
                    ReportProgress(onProgress, AgentProgressKind.Failed, failureReason, iteration: iteration);
                    return BuildResult(AgentRunOutcome.Failed, string.Empty, executedTools, failureReason, aggregateUsage);
                }

                // Under budget: continue to next iteration to retry.
                continue;
            }

            // Accumulate usage from any response that includes it.
            aggregateUsage = aggregateUsage + (response.Usage ?? new ModelUsage());

            // Only reset the consecutive error counter for non-error responses.
            // SendTurn may catch its own exceptions and return StopReason.Error
            // without throwing — those must still count as consecutive errors.
            if (response.StopReason != AgentStopReason.Error)
            {
                consecutiveErrors = 0;
            }

            // --- Handle stop reasons ---
            if (response.StopReason == AgentStopReason.EndTurn || response.StopReason == AgentStopReason.MaxTokens)
            {
                // Model produced a final text answer (or hit its own max_tokens and returned text).
                AgentRunOutcome outcome = response.StopReason == AgentStopReason.EndTurn
                    ? AgentRunOutcome.Success
                    : AgentRunOutcome.FellBack;

                AgentProgressKind progressKind = outcome == AgentRunOutcome.Success
                    ? AgentProgressKind.Completed
                    : AgentProgressKind.FellBack;

                ReportProgress(onProgress, progressKind, "Agent loop finished.", iteration: iteration);
                return BuildResult(outcome, response.TextContent ?? string.Empty, executedTools, string.Empty, aggregateUsage);
            }

            if (response.StopReason == AgentStopReason.ToolUse)
            {
                ReportProgress(onProgress, AgentProgressKind.ToolRequested,
                    $"Model requested {response.ToolCalls.Count} tool(s).",
                    iteration: iteration);

                // Append the raw assistant message to conversation history so the model
                // sees its own tool_use block on the next turn.
                if (response.RawAssistantMessage != null)
                {
                    conversationHistory.Add(response.RawAssistantMessage);
                }

                // Execute each tool call sequentially.
                var turnToolResults = new List<AgentToolResult>();

                foreach (AgentToolCall toolCall in response.ToolCalls)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        ReportProgress(onProgress, AgentProgressKind.Cancelled, "Cancelled before tool execution.", iteration: iteration);
                        return BuildResult(AgentRunOutcome.Cancelled, string.Empty, executedTools, "Cancelled before tool execution.", aggregateUsage);
                    }

                    ReportProgress(onProgress, AgentProgressKind.ToolExecuting,
                        $"Executing tool: {toolCall.Name}",
                        toolName: toolCall.Name,
                        iteration: iteration);

                    AgentToolResult toolResult;
                    try
                    {
                        var executionContext = new ToolExecutionContext
                        {
                            SessionId = string.Empty,
                            DocumentKey = string.Empty,
                            CurrentIteration = iteration,
                            CancellationToken = cancellationToken
                        };

                        toolResult = toolExecutor.Execute(toolCall.Name, toolCall.Arguments, executionContext);
                    }
                    catch (Exception ex)
                    {
                        // Tool execution errors are sent back to the model so it can self-correct.
                        toolResult = new AgentToolResult
                        {
                            ToolCallId = toolCall.Id,
                            ToolName = toolCall.Name,
                            OutputJson = "{\"error\":\"" + EscapeJsonString(ex.Message) + "\"}",
                            IsError = true
                        };
                    }

                    // Ensure the tool call ID is propagated.
                    if (string.IsNullOrEmpty(toolResult.ToolCallId))
                    {
                        toolResult.ToolCallId = toolCall.Id;
                    }

                    if (string.IsNullOrEmpty(toolResult.ToolName))
                    {
                        toolResult.ToolName = toolCall.Name;
                    }

                    turnToolResults.Add(toolResult);
                    executedTools.Add(toolResult);
                }

                // Append tool result messages to conversation history.
                List<object> toolResultMessages = modelClient.BuildToolResultMessages(turnToolResults);
                conversationHistory.AddRange(toolResultMessages);

                // Continue the loop for the next model turn.
                continue;
            }

            if (response.StopReason == AgentStopReason.Error)
            {
                consecutiveErrors++;
                ReportProgress(onProgress, AgentProgressKind.Failed,
                    $"API error on iteration {iteration}: {response.FailureReason}",
                    iteration: iteration);

                if (consecutiveErrors >= settings.MaxConsecutiveErrors)
                {
                    string failureReason = $"Max consecutive errors ({settings.MaxConsecutiveErrors}) reached. Last reason: {response.FailureReason}";
                    ReportProgress(onProgress, AgentProgressKind.Failed, failureReason, iteration: iteration);
                    return BuildResult(AgentRunOutcome.Failed, string.Empty, executedTools, failureReason, aggregateUsage);
                }

                continue;
            }

            // Unrecognized stop reason: treat as a non-fatal anomaly and continue.
        }

        // Exhausted all iterations without a final answer.
        ReportProgress(onProgress, AgentProgressKind.FellBack,
            $"Max iterations ({settings.MaxIterations}) reached without a final answer.",
            iteration: settings.MaxIterations);

        return BuildResult(
            AgentRunOutcome.FellBack,
            string.Empty,
            executedTools,
            $"Max iterations ({settings.MaxIterations}) reached.",
            aggregateUsage);
    }

    // --- Helpers ---

    private static AgentLoopResult BuildResult(
        AgentRunOutcome outcome,
        string finalAnswer,
        List<AgentToolResult> executedTools,
        string failureReason,
        ModelUsage aggregateUsage)
    {
        return new AgentLoopResult
        {
            Outcome = outcome,
            FinalAnswer = finalAnswer,
            ExecutedTools = new List<AgentToolResult>(executedTools),
            FailureReason = failureReason,
            AggregateUsage = aggregateUsage
        };
    }

    private static void ReportProgress(
        Action<AgentProgressUpdate>? onProgress,
        AgentProgressKind kind,
        string message,
        string? toolName = null,
        int iteration = 0)
    {
        onProgress?.Invoke(new AgentProgressUpdate
        {
            Kind = kind,
            Message = message,
            ToolName = toolName,
            Iteration = iteration
        });
    }

    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
