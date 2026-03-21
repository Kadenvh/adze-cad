using System.Collections.Generic;
using System.Threading;
using Adze.Broker.Abstractions;
using Adze.Broker.Configuration;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class MultiTurnContextTests
{
    [Test]
    public void Run_WithPriorConversation_SeedsHistoryBeforeCurrentRequest()
    {
        var capturedHistory = new List<object>();
        var mockClient = new CapturingAgentClient(capturedHistory);
        var mockExecutor = new NoOpToolExecutor();
        var runner = new AgentLoopRunner();

        var prior = new List<object>
        {
            new Dictionary<string, object?> { ["role"] = "user", ["content"] = "What dimensions does this part have?" },
            new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = "The part has 5 dimensions." }
        };

        var settings = new AgentModelSettings { MaxIterations = 1, MaxTotalTokens = 10000 };

        runner.Run(
            mockClient,
            mockExecutor,
            "system prompt",
            "What about the sketch dimensions?",
            new List<AgentToolDefinition>(),
            settings,
            CancellationToken.None,
            null,
            prior);

        // The captured history should have 3 messages: 2 prior + 1 current
        Assert.That(capturedHistory.Count, Is.EqualTo(3));
    }

    [Test]
    public void Run_WithNullPriorConversation_WorksNormally()
    {
        var capturedHistory = new List<object>();
        var mockClient = new CapturingAgentClient(capturedHistory);
        var mockExecutor = new NoOpToolExecutor();
        var runner = new AgentLoopRunner();

        var settings = new AgentModelSettings { MaxIterations = 1, MaxTotalTokens = 10000 };

        AgentLoopResult result = runner.Run(
            mockClient,
            mockExecutor,
            "system prompt",
            "Hello",
            new List<AgentToolDefinition>(),
            settings,
            CancellationToken.None,
            null,
            null);

        Assert.That(capturedHistory.Count, Is.EqualTo(1)); // Just the current user message
        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
    }

    [Test]
    public void Run_WithEmptyPriorConversation_WorksNormally()
    {
        var capturedHistory = new List<object>();
        var mockClient = new CapturingAgentClient(capturedHistory);
        var runner = new AgentLoopRunner();

        var settings = new AgentModelSettings { MaxIterations = 1, MaxTotalTokens = 10000 };

        runner.Run(
            mockClient,
            new NoOpToolExecutor(),
            "system",
            "Hello",
            new List<AgentToolDefinition>(),
            settings,
            CancellationToken.None,
            null,
            new List<object>());

        Assert.That(capturedHistory.Count, Is.EqualTo(1));
    }

    /// <summary>
    /// Mock client that captures the conversation history passed to SendTurn.
    /// Returns EndTurn on first call.
    /// </summary>
    private sealed class CapturingAgentClient : IAgentModelClient
    {
        private readonly List<object> _captured;

        public CapturingAgentClient(List<object> captured)
        {
            _captured = captured;
        }

        public AgentTurnResponse SendTurn(string systemPrompt, List<object> conversationHistory,
            List<AgentToolDefinition> toolDefinitions, AgentModelSettings settings)
        {
            _captured.Clear();
            _captured.AddRange(conversationHistory);
            return new AgentTurnResponse
            {
                Success = true,
                StopReason = AgentStopReason.EndTurn,
                TextContent = "Done."
            };
        }

        public object BuildUserMessage(string content)
        {
            return new Dictionary<string, object?> { ["role"] = "user", ["content"] = content };
        }

        public List<object> BuildToolResultMessages(List<AgentToolResult> results)
        {
            return new List<object>();
        }
    }

    private sealed class NoOpToolExecutor : IToolExecutor
    {
        public AgentToolResult Execute(string toolName, Dictionary<string, object?> arguments, ToolExecutionContext context)
        {
            return new AgentToolResult { ToolName = toolName, OutputJson = "{}" };
        }
    }
}
