using System;
using System.Collections.Generic;
using System.Threading;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

// --- Stubs ---

/// <summary>
/// A configurable stub for <see cref="IAgentModelClient"/> that returns pre-configured
/// responses in sequence.  Each call to <see cref="SendTurn"/> pops the next response from
/// the queue.  If no responses remain, it throws <see cref="InvalidOperationException"/>.
/// </summary>
internal sealed class StubAgentModelClient : IAgentModelClient
{
    private readonly Queue<Func<AgentTurnResponse>> _responseQueue = new();

    public int SendTurnCallCount { get; private set; }

    public void EnqueueResponse(AgentTurnResponse response)
    {
        _responseQueue.Enqueue(() => response);
    }

    public void EnqueueResponseFactory(Func<AgentTurnResponse> factory)
    {
        _responseQueue.Enqueue(factory);
    }

    public AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings)
    {
        SendTurnCallCount++;

        if (_responseQueue.Count == 0)
        {
            throw new InvalidOperationException("StubAgentModelClient: no more responses configured.");
        }

        return _responseQueue.Dequeue()();
    }

    public object BuildUserMessage(string content)
    {
        return new Dictionary<string, string> { ["role"] = "user", ["content"] = content };
    }

    public List<object> BuildToolResultMessages(List<AgentToolResult> results)
    {
        var messages = new List<object>();
        foreach (AgentToolResult result in results)
        {
            messages.Add(new Dictionary<string, string>
            {
                ["role"] = "tool",
                ["tool_call_id"] = result.ToolCallId,
                ["content"] = result.OutputJson
            });
        }

        return messages;
    }
}

/// <summary>
/// A configurable stub for <see cref="IToolExecutor"/> that returns canned results
/// keyed by tool name, or throws if a tool is marked as failing.
/// </summary>
internal sealed class StubToolExecutor : IToolExecutor
{
    private readonly Dictionary<string, AgentToolResult> _results = new();
    private readonly HashSet<string> _throwingTools = new();

    public List<string> ExecutedToolNames { get; } = new();

    public void RegisterResult(string toolName, string outputJson)
    {
        _results[toolName] = new AgentToolResult
        {
            ToolName = toolName,
            OutputJson = outputJson,
            IsError = false
        };
    }

    public void RegisterThrowingTool(string toolName, string errorMessage)
    {
        _throwingTools.Add(toolName);
        _results[toolName] = new AgentToolResult
        {
            ToolName = toolName,
            OutputJson = errorMessage,
            IsError = true
        };
    }

    public AgentToolResult Execute(
        string toolName,
        Dictionary<string, object?> arguments,
        ToolExecutionContext context)
    {
        ExecutedToolNames.Add(toolName);

        if (_throwingTools.Contains(toolName))
        {
            throw new InvalidOperationException(_results[toolName].OutputJson);
        }

        if (_results.TryGetValue(toolName, out AgentToolResult? result))
        {
            return new AgentToolResult
            {
                ToolCallId = string.Empty,
                ToolName = result.ToolName,
                OutputJson = result.OutputJson,
                IsError = result.IsError
            };
        }

        return new AgentToolResult
        {
            ToolName = toolName,
            OutputJson = "{\"result\":\"default\"}",
            IsError = false
        };
    }
}

// --- Tests ---

[TestFixture]
public sealed class AgentLoopRunnerTests
{
    private AgentLoopRunner _runner = null!;
    private AgentModelSettings _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _runner = new AgentLoopRunner();
        _settings = new AgentModelSettings
        {
            MaxIterations = 10,
            MaxConsecutiveErrors = 2,
            MaxTotalTokens = 100000,
            MaxTokens = 4096
        };
    }

    // --- Single-turn: model returns text immediately ---

    [Test]
    public void Run_SingleTurnTextResponse_ReturnsSuccess()
    {
        var modelClient = new StubAgentModelClient();
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "The part has 3 features.",
            Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 20, TotalTokens = 120 }
        });

        var toolExecutor = new StubToolExecutor();

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "You are a SOLIDWORKS assistant.",
            "How many features?",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.FinalAnswer, Is.EqualTo("The part has 3 features."));
        Assert.That(result.ExecutedTools, Is.Empty);
        Assert.That(result.AggregateUsage.TotalTokens, Is.EqualTo(120));
        Assert.That(modelClient.SendTurnCallCount, Is.EqualTo(1));
    }

    // --- Two-turn: model calls one tool, then returns text ---

    [Test]
    public void Run_TwoTurnWithToolCall_ReturnsSuccessWithExecutedTool()
    {
        var modelClient = new StubAgentModelClient();

        // Turn 1: model requests a tool
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall
                {
                    Id = "call_001",
                    Name = "get_feature_tree_slice",
                    Arguments = new Dictionary<string, object?>()
                }
            },
            RawAssistantMessage = new Dictionary<string, string> { ["role"] = "assistant", ["content"] = "tool_use" },
            Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 30, TotalTokens = 130 }
        });

        // Turn 2: model returns text
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "The feature tree has 5 features.",
            Usage = new ModelUsage { PromptTokens = 200, CompletionTokens = 25, TotalTokens = 225 }
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_feature_tree_slice", "{\"features\":[\"Boss-Extrude1\",\"Cut-Extrude1\"]}");

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "You are a SOLIDWORKS assistant.",
            "What features are in this part?",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.FinalAnswer, Is.EqualTo("The feature tree has 5 features."));
        Assert.That(result.ExecutedTools.Count, Is.EqualTo(1));
        Assert.That(result.ExecutedTools[0].ToolName, Is.EqualTo("get_feature_tree_slice"));
        Assert.That(result.AggregateUsage.TotalTokens, Is.EqualTo(355));
        Assert.That(result.AggregateUsage.PromptTokens, Is.EqualTo(300));
        Assert.That(result.AggregateUsage.CompletionTokens, Is.EqualTo(55));
        Assert.That(modelClient.SendTurnCallCount, Is.EqualTo(2));
    }

    // --- Multi-tool in single turn ---

    [Test]
    public void Run_MultipleToolCallsInOneTurn_ExecutesAllSequentially()
    {
        var modelClient = new StubAgentModelClient();

        // Turn 1: model requests two tools
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_a", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() },
                new AgentToolCall { Id = "call_b", Name = "get_mates", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "raw_assistant",
            Usage = new ModelUsage { PromptTokens = 50, CompletionTokens = 10, TotalTokens = 60 }
        });

        // Turn 2: model returns text
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Found 3 dims and 2 mates.",
            Usage = new ModelUsage { PromptTokens = 80, CompletionTokens = 15, TotalTokens = 95 }
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dimensions", "{\"count\":3}");
        toolExecutor.RegisterResult("get_mates", "{\"count\":2}");

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Tell me about dims and mates.",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.ExecutedTools.Count, Is.EqualTo(2));
        Assert.That(toolExecutor.ExecutedToolNames.Count, Is.EqualTo(2));
        Assert.That(toolExecutor.ExecutedToolNames[0], Is.EqualTo("get_dimensions"));
        Assert.That(toolExecutor.ExecutedToolNames[1], Is.EqualTo("get_mates"));
    }

    // --- Max iterations enforced ---

    [Test]
    public void Run_MaxIterationsReached_ReturnsFellBack()
    {
        _settings.MaxIterations = 3;

        var modelClient = new StubAgentModelClient();

        // Each turn requests a tool, never producing a final answer.
        for (int i = 0; i < 3; i++)
        {
            modelClient.EnqueueResponse(new AgentTurnResponse
            {
                Success = true,
                StopReason = AgentStopReason.ToolUse,
                ToolCalls = new List<AgentToolCall>
                {
                    new AgentToolCall { Id = $"call_{i}", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
                },
                RawAssistantMessage = $"assistant_{i}",
                Usage = new ModelUsage { PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15 }
            });
        }

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dimensions", "{\"count\":3}");

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Loop forever",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.FellBack));
        Assert.That(result.FailureReason, Does.Contain("Max iterations"));
        Assert.That(result.ExecutedTools.Count, Is.EqualTo(3));
    }

    // --- Cancellation ---

    [Test]
    public void Run_CancellationBeforeFirstTurn_ReturnsCancelled()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var modelClient = new StubAgentModelClient();
        var toolExecutor = new StubToolExecutor();

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            cts.Token, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Cancelled));
        Assert.That(result.FailureReason, Does.Contain("Cancelled"));
        Assert.That(modelClient.SendTurnCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Run_CancellationDuringToolExecution_ReturnsCancelled()
    {
        var cts = new CancellationTokenSource();
        var modelClient = new StubAgentModelClient();

        // Turn 1: request two tools; we will cancel after the first executes.
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_1", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() },
                new AgentToolCall { Id = "call_2", Name = "get_mates", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "assistant",
            Usage = new ModelUsage { PromptTokens = 50, CompletionTokens = 10, TotalTokens = 60 }
        });

        // StubToolExecutor that cancels after first tool
        var toolExecutor = new CancellingToolExecutor(cts, cancelAfterToolIndex: 0);

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            cts.Token, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Cancelled));
        Assert.That(result.ExecutedTools.Count, Is.EqualTo(1));
    }

    // --- Consecutive error budget ---

    [Test]
    public void Run_ConsecutiveApiErrors_ReturnsFailed()
    {
        _settings.MaxConsecutiveErrors = 2;

        var modelClient = new StubAgentModelClient();

        // Enqueue two factories that throw.
        modelClient.EnqueueResponseFactory(() => throw new InvalidOperationException("API timeout"));
        modelClient.EnqueueResponseFactory(() => throw new InvalidOperationException("API timeout again"));

        var toolExecutor = new StubToolExecutor();

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Failed));
        Assert.That(result.FailureReason, Does.Contain("Max consecutive errors"));
        Assert.That(result.FailureReason, Does.Contain("API timeout"));
    }

    [Test]
    public void Run_ApiErrorFollowedBySuccess_ResetsErrorCounter()
    {
        _settings.MaxConsecutiveErrors = 2;
        _settings.MaxIterations = 5;

        var modelClient = new StubAgentModelClient();

        // Iteration 1: API error (consecutive = 1)
        modelClient.EnqueueResponseFactory(() => throw new InvalidOperationException("Transient error"));

        // Iteration 2: Success resets counter
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Recovered successfully.",
            Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 20, TotalTokens = 120 }
        });

        var toolExecutor = new StubToolExecutor();

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.FinalAnswer, Is.EqualTo("Recovered successfully."));
    }

    // --- Progress callbacks ---

    [Test]
    public void Run_SingleTurn_FiresProgressCallbacksInCorrectOrder()
    {
        var modelClient = new StubAgentModelClient();
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Done.",
            Usage = new ModelUsage()
        });

        var toolExecutor = new StubToolExecutor();
        var progressLog = new List<AgentProgressKind>();

        _runner.Run(
            modelClient, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None,
            update => progressLog.Add(update.Kind));

        Assert.That(progressLog.Count, Is.EqualTo(3));
        Assert.That(progressLog[0], Is.EqualTo(AgentProgressKind.Started));
        Assert.That(progressLog[1], Is.EqualTo(AgentProgressKind.Thinking));
        Assert.That(progressLog[2], Is.EqualTo(AgentProgressKind.Completed));
    }

    [Test]
    public void Run_TwoTurnWithTool_FiresProgressCallbacksInCorrectOrder()
    {
        var modelClient = new StubAgentModelClient();

        // Turn 1: tool use
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_1", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "assistant",
            Usage = new ModelUsage()
        });

        // Turn 2: final answer
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Answer.",
            Usage = new ModelUsage()
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dimensions", "{\"count\":3}");

        var progressLog = new List<AgentProgressKind>();

        _runner.Run(
            modelClient, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None,
            update => progressLog.Add(update.Kind));

        Assert.That(progressLog.Count, Is.EqualTo(6));
        Assert.That(progressLog[0], Is.EqualTo(AgentProgressKind.Started));
        Assert.That(progressLog[1], Is.EqualTo(AgentProgressKind.Thinking));       // before turn 1
        Assert.That(progressLog[2], Is.EqualTo(AgentProgressKind.ToolRequested));   // after turn 1
        Assert.That(progressLog[3], Is.EqualTo(AgentProgressKind.ToolExecuting));   // before tool execution
        Assert.That(progressLog[4], Is.EqualTo(AgentProgressKind.Thinking));        // before turn 2
        Assert.That(progressLog[5], Is.EqualTo(AgentProgressKind.Completed));       // after turn 2
    }

    // --- Usage accumulation ---

    [Test]
    public void Run_MultiTurn_AccumulatesUsageAcrossTurns()
    {
        var modelClient = new StubAgentModelClient();

        // Turn 1: tool use
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_1", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "assistant",
            Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 30, TotalTokens = 130 }
        });

        // Turn 2: tool use
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_2", Name = "get_mates", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "assistant2",
            Usage = new ModelUsage { PromptTokens = 200, CompletionTokens = 40, TotalTokens = 240 }
        });

        // Turn 3: final answer
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Summary.",
            Usage = new ModelUsage { PromptTokens = 300, CompletionTokens = 50, TotalTokens = 350 }
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dimensions", "{\"count\":3}");
        toolExecutor.RegisterResult("get_mates", "{\"count\":2}");

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Detailed analysis",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.AggregateUsage.PromptTokens, Is.EqualTo(600));
        Assert.That(result.AggregateUsage.CompletionTokens, Is.EqualTo(120));
        Assert.That(result.AggregateUsage.TotalTokens, Is.EqualTo(720));
        Assert.That(result.ExecutedTools.Count, Is.EqualTo(2));
    }

    // --- Token budget ---

    [Test]
    public void Run_TokenBudgetExhausted_ReturnsFellBack()
    {
        _settings.MaxTotalTokens = 200;

        var modelClient = new StubAgentModelClient();

        // Turn 1: uses 150 tokens, requests a tool
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_1", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "assistant",
            Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 50, TotalTokens = 150 }
        });

        // Turn 2: uses another 100, pushing past budget.
        // But the budget check happens at the start of the iteration, before this call.
        // After turn 1 we have 150 tokens. Turn 2 starts, checks 150 < 200, proceeds.
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_2", Name = "get_mates", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "assistant2",
            Usage = new ModelUsage { PromptTokens = 60, CompletionTokens = 40, TotalTokens = 100 }
        });

        // After turn 2 we have 250 tokens. Turn 3 starts, checks 250 >= 200, falls back.

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dimensions", "{\"count\":3}");
        toolExecutor.RegisterResult("get_mates", "{\"count\":2}");

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Detailed",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.FellBack));
        Assert.That(result.FailureReason, Does.Contain("Token budget"));
        Assert.That(result.AggregateUsage.TotalTokens, Is.EqualTo(250));
    }

    // --- Tool execution error: sent back to model ---

    [Test]
    public void Run_ToolThrows_ErrorSentBackToModel_ModelRecovers()
    {
        var modelClient = new StubAgentModelClient();

        // Turn 1: model requests a tool that will throw
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_err", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "assistant",
            Usage = new ModelUsage { PromptTokens = 50, CompletionTokens = 10, TotalTokens = 60 }
        });

        // Turn 2: model sees the error and produces a final answer
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "I could not retrieve dimensions because the document is not open.",
            Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 20, TotalTokens = 120 }
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterThrowingTool("get_dimensions", "Document not open");

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Get dims",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.ExecutedTools.Count, Is.EqualTo(1));
        Assert.That(result.ExecutedTools[0].IsError, Is.True);
        Assert.That(result.ExecutedTools[0].ToolName, Is.EqualTo("get_dimensions"));
    }

    // --- MaxTokens stop reason ---

    [Test]
    public void Run_MaxTokensStopReason_ReturnsFellBack()
    {
        var modelClient = new StubAgentModelClient();
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.MaxTokens,
            TextContent = "Partial answer...",
            Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 4096, TotalTokens = 4196 }
        });

        var toolExecutor = new StubToolExecutor();

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Explain everything",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.FellBack));
        Assert.That(result.FinalAnswer, Is.EqualTo("Partial answer..."));
    }

    // --- Null arguments safety ---

    [Test]
    public void Run_NullModelClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _runner.Run(null!, new StubToolExecutor(), "sys", "hi",
                new List<AgentToolDefinition>(), _settings, CancellationToken.None, null));
    }

    [Test]
    public void Run_NullToolExecutor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _runner.Run(new StubAgentModelClient(), null!, "sys", "hi",
                new List<AgentToolDefinition>(), _settings, CancellationToken.None, null));
    }

    [Test]
    public void Run_NullSettings_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _runner.Run(new StubAgentModelClient(), new StubToolExecutor(), "sys", "hi",
                new List<AgentToolDefinition>(), null!, CancellationToken.None, null));
    }

    // --- Progress iteration tracking ---

    [Test]
    public void Run_ProgressCallbacks_ContainCorrectIterationNumbers()
    {
        var modelClient = new StubAgentModelClient();

        // Turn 1: tool use
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "call_1", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
            },
            RawAssistantMessage = "asst",
            Usage = new ModelUsage()
        });

        // Turn 2: text
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Done.",
            Usage = new ModelUsage()
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dimensions", "{\"count\":1}");

        var progressUpdates = new List<AgentProgressUpdate>();

        _runner.Run(
            modelClient, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None,
            update => progressUpdates.Add(update));

        // Started at iteration 0
        Assert.That(progressUpdates[0].Iteration, Is.EqualTo(0));
        Assert.That(progressUpdates[0].Kind, Is.EqualTo(AgentProgressKind.Started));

        // Thinking at iteration 1
        Assert.That(progressUpdates[1].Iteration, Is.EqualTo(1));

        // ToolRequested at iteration 1
        Assert.That(progressUpdates[2].Iteration, Is.EqualTo(1));

        // ToolExecuting at iteration 1
        Assert.That(progressUpdates[3].Iteration, Is.EqualTo(1));
        Assert.That(progressUpdates[3].ToolName, Is.EqualTo("get_dimensions"));

        // Thinking at iteration 2
        Assert.That(progressUpdates[4].Iteration, Is.EqualTo(2));

        // Completed at iteration 2
        Assert.That(progressUpdates[5].Iteration, Is.EqualTo(2));
    }

    // --- Error stop reason from model response ---

    [Test]
    public void Run_ErrorStopReason_CountsAsConsecutiveError()
    {
        _settings.MaxConsecutiveErrors = 2;
        _settings.MaxIterations = 5;

        var modelClient = new StubAgentModelClient();

        // Two error responses in a row
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = false,
            StopReason = AgentStopReason.Error,
            FailureReason = "Rate limited",
            Usage = new ModelUsage { PromptTokens = 10, CompletionTokens = 0, TotalTokens = 10 }
        });

        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = false,
            StopReason = AgentStopReason.Error,
            FailureReason = "Rate limited again",
            Usage = new ModelUsage { PromptTokens = 10, CompletionTokens = 0, TotalTokens = 10 }
        });

        var toolExecutor = new StubToolExecutor();

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Failed));
        Assert.That(result.FailureReason, Does.Contain("Max consecutive errors"));
    }

    // --- Tool result truncation ---

    [Test]
    public void Run_LargeToolResult_IsTruncated()
    {
        _settings.MaxToolResultChars = 100;

        var modelClient = new StubAgentModelClient();
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "tc1", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
            },
            Usage = new ModelUsage { TotalTokens = 50 }
        });
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Done.",
            Usage = new ModelUsage { TotalTokens = 30 }
        });

        var toolExecutor = new StubToolExecutor();
        string largeOutput = new string('x', 500);
        toolExecutor.RegisterResult("get_dimensions", largeOutput);

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "system", "user",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.ExecutedTools.Count, Is.EqualTo(1));
        Assert.That(result.ExecutedTools[0].OutputJson.Length, Is.LessThan(500));
        Assert.That(result.ExecutedTools[0].OutputJson, Does.Contain("truncated"));
    }

    [Test]
    public void Run_SmallToolResult_IsNotTruncated()
    {
        _settings.MaxToolResultChars = 1000;

        var modelClient = new StubAgentModelClient();
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.ToolUse,
            ToolCalls = new List<AgentToolCall>
            {
                new AgentToolCall { Id = "tc1", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
            },
            Usage = new ModelUsage { TotalTokens = 50 }
        });
        modelClient.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Done.",
            Usage = new ModelUsage { TotalTokens = 30 }
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dimensions", "{\"count\":5}");

        AgentLoopResult result = _runner.Run(
            modelClient, toolExecutor,
            "system", "user",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null);

        Assert.That(result.ExecutedTools[0].OutputJson, Is.EqualTo("{\"count\":5}"));
        Assert.That(result.ExecutedTools[0].OutputJson, Does.Not.Contain("truncated"));
    }
}

/// <summary>
/// A tool executor that cancels the given <see cref="CancellationTokenSource"/> after
/// executing a specified number of tools, allowing tests to verify mid-loop cancellation.
/// </summary>
internal sealed class CancellingToolExecutor : IToolExecutor
{
    private readonly CancellationTokenSource _cts;
    private readonly int _cancelAfterToolIndex;
    private int _callCount;

    public CancellingToolExecutor(CancellationTokenSource cts, int cancelAfterToolIndex)
    {
        _cts = cts;
        _cancelAfterToolIndex = cancelAfterToolIndex;
    }

    public AgentToolResult Execute(
        string toolName,
        Dictionary<string, object?> arguments,
        ToolExecutionContext context)
    {
        int currentIndex = _callCount;
        _callCount++;

        if (currentIndex >= _cancelAfterToolIndex)
        {
            _cts.Cancel();
        }

        return new AgentToolResult
        {
            ToolCallId = string.Empty,
            ToolName = toolName,
            OutputJson = "{\"result\":\"ok\"}",
            IsError = false
        };
    }
}
