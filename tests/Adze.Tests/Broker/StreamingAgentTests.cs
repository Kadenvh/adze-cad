using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Adze.Broker.Abstractions;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using NUnit.Framework;

namespace Adze.Tests.Broker;

// --- Stubs ---

/// <summary>
/// A stub IStreamingAgentModelClient that supports both buffered and streaming turns.
/// For streaming: pops from a separate streaming response queue to allow distinguishing
/// the two code paths. Falls back to the normal queue for non-streaming calls.
/// </summary>
internal sealed class StubStreamingAgentModelClient : IStreamingAgentModelClient
{
    private readonly Queue<AgentTurnResponse> _normalQueue = new();
    private readonly Queue<StreamingAgentResponse> _streamingQueue = new();

    public int SendTurnCallCount { get; private set; }
    public int SendTurnStreamingCallCount { get; private set; }

    public void EnqueueResponse(AgentTurnResponse response)
    {
        _normalQueue.Enqueue(response);
    }

    public void EnqueueStreamingResponse(StreamingAgentResponse response)
    {
        _streamingQueue.Enqueue(response);
    }

    public AgentTurnResponse SendTurn(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings)
    {
        SendTurnCallCount++;
        if (_normalQueue.Count == 0)
            throw new InvalidOperationException("StubStreamingAgentModelClient: no more normal responses.");
        return _normalQueue.Dequeue();
    }

    public AgentTurnResponse SendTurnStreaming(
        string systemPrompt,
        List<object> conversationHistory,
        List<AgentToolDefinition> toolDefinitions,
        AgentModelSettings settings,
        Action<string> onTextChunk)
    {
        SendTurnStreamingCallCount++;

        if (_streamingQueue.Count == 0)
            throw new InvalidOperationException("StubStreamingAgentModelClient: no more streaming responses.");

        StreamingAgentResponse streaming = _streamingQueue.Dequeue();

        // Simulate streaming: deliver text chunks if this is a text response
        if (streaming.TextChunks != null)
        {
            foreach (string chunk in streaming.TextChunks)
            {
                onTextChunk(chunk);
            }
        }

        return streaming.Response;
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
/// Wraps a response and optional text chunks for simulating streaming.
/// </summary>
internal sealed class StreamingAgentResponse
{
    public AgentTurnResponse Response { get; set; } = new();
    public List<string>? TextChunks { get; set; }
}

// --- OpenAIFormatAgentClient.ReadStreamingAgentResponse Tests ---

[TestFixture]
public sealed class StreamingAgentClientTests
{
    [Test]
    public void SendTurnStreaming_TextResponse_StreamsChunksAndReturnsEndTurn()
    {
        // Simulate an SSE stream with text content
        string sseData = string.Join("\r\n", new[]
        {
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\" world\"},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}",
            "",
            "data: {\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}",
            "",
            "data: [DONE]"
        });

        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Model = "gpt-4",
            ApiKey = "test-key",
            Endpoint = "http://localhost:1/v1/chat/completions"
        };

        var client = new OpenAIFormatAgentClient(settings);
        var streamedChunks = new List<string>();

        // Use reflection to call ReadStreamingAgentResponse directly since we can't
        // do a real HTTP call in unit tests. Instead, test via the SSE parsing logic
        // by constructing an in-memory stream and verifying the result.
        AgentTurnResponse response = InvokeReadStreamingAgentResponse(client, sseData, chunk => streamedChunks.Add(chunk));

        Assert.That(response.Success, Is.True);
        Assert.That(response.StopReason, Is.EqualTo(AgentStopReason.EndTurn));
        Assert.That(response.TextContent, Is.EqualTo("Hello world"));
        Assert.That(streamedChunks.Count, Is.EqualTo(2));
        Assert.That(streamedChunks[0], Is.EqualTo("Hello"));
        Assert.That(streamedChunks[1], Is.EqualTo(" world"));
        Assert.That(response.Usage.TotalTokens, Is.EqualTo(15));
    }

    [Test]
    public void SendTurnStreaming_ToolCallResponse_BuffersAndReturnsToolUse()
    {
        string sseData = string.Join("\r\n", new[]
        {
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"index\":0,\"id\":\"call_abc\",\"type\":\"function\",\"function\":{\"name\":\"get_dimensions\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"part\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"_name\\\"\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\": \\\"Boss\\\"\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"}\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
            "",
            "data: {\"usage\":{\"prompt_tokens\":50,\"completion_tokens\":20,\"total_tokens\":70}}",
            "",
            "data: [DONE]"
        });

        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Model = "gpt-4",
            ApiKey = "test-key",
            Endpoint = "http://localhost:1/v1/chat/completions"
        };

        var client = new OpenAIFormatAgentClient(settings);
        var streamedChunks = new List<string>();

        AgentTurnResponse response = InvokeReadStreamingAgentResponse(client, sseData, chunk => streamedChunks.Add(chunk));

        Assert.That(response.Success, Is.True);
        Assert.That(response.StopReason, Is.EqualTo(AgentStopReason.ToolUse));
        Assert.That(response.ToolCalls.Count, Is.EqualTo(1));
        Assert.That(response.ToolCalls[0].Id, Is.EqualTo("call_abc"));
        Assert.That(response.ToolCalls[0].Name, Is.EqualTo("get_dimensions"));
        Assert.That(response.ToolCalls[0].ArgumentsJson, Is.EqualTo("{\"part_name\": \"Boss\"}"));
        Assert.That(response.ToolCalls[0].Arguments["part_name"], Is.EqualTo("Boss"));
        Assert.That(streamedChunks, Is.Empty, "No text chunks should be streamed for tool call responses");
        Assert.That(response.Usage.TotalTokens, Is.EqualTo(70));
        Assert.That(response.RawAssistantMessage, Is.Not.Null);
    }

    [Test]
    public void SendTurnStreaming_MultipleToolCalls_AccumulatesAll()
    {
        string sseData = string.Join("\r\n", new[]
        {
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_dimensions\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{}\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":1,\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"get_mates\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"arguments\":\"{}\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
            "",
            "data: [DONE]"
        });

        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Model = "gpt-4",
            ApiKey = "test-key",
            Endpoint = "http://localhost:1/v1/chat/completions"
        };

        var client = new OpenAIFormatAgentClient(settings);

        AgentTurnResponse response = InvokeReadStreamingAgentResponse(client, sseData, _ => { });

        Assert.That(response.Success, Is.True);
        Assert.That(response.StopReason, Is.EqualTo(AgentStopReason.ToolUse));
        Assert.That(response.ToolCalls.Count, Is.EqualTo(2));
        Assert.That(response.ToolCalls[0].Name, Is.EqualTo("get_dimensions"));
        Assert.That(response.ToolCalls[1].Name, Is.EqualTo("get_mates"));
    }

    [Test]
    public void SendTurnStreaming_MaxTokensFinishReason_ReturnsMaxTokens()
    {
        string sseData = string.Join("\r\n", new[]
        {
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Partial\"},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"length\"}]}",
            "",
            "data: [DONE]"
        });

        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Model = "gpt-4",
            ApiKey = "test-key",
            Endpoint = "http://localhost:1/v1/chat/completions"
        };

        var client = new OpenAIFormatAgentClient(settings);

        AgentTurnResponse response = InvokeReadStreamingAgentResponse(client, sseData, _ => { });

        Assert.That(response.StopReason, Is.EqualTo(AgentStopReason.MaxTokens));
        Assert.That(response.TextContent, Is.EqualTo("Partial"));
    }

    [Test]
    public void SendTurnStreaming_EmptyStream_ReturnsEndTurnWithEmptyText()
    {
        string sseData = "data: [DONE]";

        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Model = "gpt-4",
            ApiKey = "test-key",
            Endpoint = "http://localhost:1/v1/chat/completions"
        };

        var client = new OpenAIFormatAgentClient(settings);

        AgentTurnResponse response = InvokeReadStreamingAgentResponse(client, sseData, _ => { });

        Assert.That(response.Success, Is.True);
        Assert.That(response.StopReason, Is.EqualTo(AgentStopReason.EndTurn));
        Assert.That(response.TextContent, Is.EqualTo(string.Empty));
    }

    [Test]
    public void SendTurnStreaming_MalformedJsonChunks_SkipsGracefully()
    {
        string sseData = string.Join("\r\n", new[]
        {
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"},\"finish_reason\":null}]}",
            "",
            "data: {malformed json!!!}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Good\"},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}",
            "",
            "data: [DONE]"
        });

        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Model = "gpt-4",
            ApiKey = "test-key",
            Endpoint = "http://localhost:1/v1/chat/completions"
        };

        var client = new OpenAIFormatAgentClient(settings);
        var chunks = new List<string>();

        AgentTurnResponse response = InvokeReadStreamingAgentResponse(client, sseData, c => chunks.Add(c));

        Assert.That(response.TextContent, Is.EqualTo("Good"));
        Assert.That(chunks.Count, Is.EqualTo(1));
    }

    [Test]
    public void SendTurnStreaming_ToolCallWithNoId_ReturnsError()
    {
        // Tool call delta with empty id
        string sseData = string.Join("\r\n", new[]
        {
            "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"index\":0,\"id\":\"\",\"type\":\"function\",\"function\":{\"name\":\"get_dims\",\"arguments\":\"{}\"}}]},\"finish_reason\":null}]}",
            "",
            "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"tool_calls\"}]}",
            "",
            "data: [DONE]"
        });

        var settings = new BrokerModelSettings
        {
            Provider = "openai",
            Model = "gpt-4",
            ApiKey = "test-key",
            Endpoint = "http://localhost:1/v1/chat/completions"
        };

        var client = new OpenAIFormatAgentClient(settings);

        AgentTurnResponse response = InvokeReadStreamingAgentResponse(client, sseData, _ => { });

        Assert.That(response.Success, Is.False);
        Assert.That(response.StopReason, Is.EqualTo(AgentStopReason.Error));
        Assert.That(response.FailureReason, Does.Contain("no valid tool calls"));
    }

    /// <summary>
    /// Invokes ReadStreamingAgentResponse via reflection since it's a private method.
    /// This tests the core SSE parsing logic without requiring HTTP calls.
    /// </summary>
    private static AgentTurnResponse InvokeReadStreamingAgentResponse(
        OpenAIFormatAgentClient client,
        string sseData,
        Action<string> onTextChunk)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(sseData);
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var method = typeof(OpenAIFormatAgentClient).GetMethod(
            "ReadStreamingAgentResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method == null)
            throw new InvalidOperationException("Could not find ReadStreamingAgentResponse method.");

        return (AgentTurnResponse)method.Invoke(client, new object[] { reader, onTextChunk })!;
    }
}

// --- AgentLoopRunner Streaming Tests ---

[TestFixture]
public sealed class AgentLoopRunnerStreamingTests
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

    [Test]
    public void Run_WithStreamingClient_UsesSendTurnStreaming()
    {
        var client = new StubStreamingAgentModelClient();
        client.EnqueueStreamingResponse(new StreamingAgentResponse
        {
            TextChunks = new List<string> { "Hello", " world" },
            Response = new AgentTurnResponse
            {
                Success = true,
                StopReason = AgentStopReason.EndTurn,
                TextContent = "Hello world",
                Usage = new ModelUsage { PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15 }
            }
        });

        var toolExecutor = new StubToolExecutor();
        var streamedChunks = new List<string>();

        AgentLoopResult result = _runner.Run(
            client, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null, null,
            chunk => streamedChunks.Add(chunk));

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.FinalAnswer, Is.EqualTo("Hello world"));
        Assert.That(streamedChunks.Count, Is.EqualTo(2));
        Assert.That(streamedChunks[0], Is.EqualTo("Hello"));
        Assert.That(streamedChunks[1], Is.EqualTo(" world"));
        Assert.That(client.SendTurnStreamingCallCount, Is.EqualTo(1));
        Assert.That(client.SendTurnCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Run_WithStreamingClient_ToolCallThenText_StreamsOnlyFinalTurn()
    {
        var client = new StubStreamingAgentModelClient();

        // Turn 1: tool call (no text chunks streamed)
        client.EnqueueStreamingResponse(new StreamingAgentResponse
        {
            TextChunks = null, // No text — this is a tool call
            Response = new AgentTurnResponse
            {
                Success = true,
                StopReason = AgentStopReason.ToolUse,
                ToolCalls = new List<AgentToolCall>
                {
                    new AgentToolCall { Id = "call_1", Name = "get_dimensions", Arguments = new Dictionary<string, object?>() }
                },
                RawAssistantMessage = new Dictionary<string, string> { ["role"] = "assistant" },
                Usage = new ModelUsage { PromptTokens = 50, CompletionTokens = 10, TotalTokens = 60 }
            }
        });

        // Turn 2: text response with streaming
        client.EnqueueStreamingResponse(new StreamingAgentResponse
        {
            TextChunks = new List<string> { "The ", "answer." },
            Response = new AgentTurnResponse
            {
                Success = true,
                StopReason = AgentStopReason.EndTurn,
                TextContent = "The answer.",
                Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 15, TotalTokens = 115 }
            }
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dimensions", "{\"count\":3}");
        var streamedChunks = new List<string>();

        AgentLoopResult result = _runner.Run(
            client, toolExecutor,
            "sys", "Analyze",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null, null,
            chunk => streamedChunks.Add(chunk));

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.FinalAnswer, Is.EqualTo("The answer."));
        Assert.That(streamedChunks.Count, Is.EqualTo(2));
        Assert.That(streamedChunks[0], Is.EqualTo("The "));
        Assert.That(streamedChunks[1], Is.EqualTo("answer."));
        Assert.That(client.SendTurnStreamingCallCount, Is.EqualTo(2));
    }

    [Test]
    public void Run_WithStreamingClient_NullCallback_UsesNonStreamingPath()
    {
        var client = new StubStreamingAgentModelClient();

        // Enqueue a normal (non-streaming) response
        client.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "No streaming.",
            Usage = new ModelUsage { PromptTokens = 10, CompletionTokens = 5, TotalTokens = 15 }
        });

        var toolExecutor = new StubToolExecutor();

        AgentLoopResult result = _runner.Run(
            client, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null, null,
            null); // Null callback — should use SendTurn, not SendTurnStreaming

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.FinalAnswer, Is.EqualTo("No streaming."));
        Assert.That(client.SendTurnCallCount, Is.EqualTo(1));
        Assert.That(client.SendTurnStreamingCallCount, Is.EqualTo(0));
    }

    [Test]
    public void Run_NonStreamingClient_WithCallback_UsesNonStreamingPath()
    {
        // Use the non-streaming StubAgentModelClient (does not implement IStreamingAgentModelClient)
        var client = new StubAgentModelClient();
        client.EnqueueResponse(new AgentTurnResponse
        {
            Success = true,
            StopReason = AgentStopReason.EndTurn,
            TextContent = "Buffered answer.",
            Usage = new ModelUsage()
        });

        var toolExecutor = new StubToolExecutor();
        var streamedChunks = new List<string>();

        AgentLoopResult result = _runner.Run(
            client, toolExecutor,
            "sys", "Hello",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null, null,
            chunk => streamedChunks.Add(chunk));

        Assert.That(result.Outcome, Is.EqualTo(AgentRunOutcome.Success));
        Assert.That(result.FinalAnswer, Is.EqualTo("Buffered answer."));
        Assert.That(streamedChunks, Is.Empty, "Non-streaming client should not produce text chunks");
        Assert.That(client.SendTurnCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Run_StreamingClient_UsageAccumulatesAcrossTurns()
    {
        var client = new StubStreamingAgentModelClient();

        // Turn 1: tool call
        client.EnqueueStreamingResponse(new StreamingAgentResponse
        {
            Response = new AgentTurnResponse
            {
                Success = true,
                StopReason = AgentStopReason.ToolUse,
                ToolCalls = new List<AgentToolCall>
                {
                    new AgentToolCall { Id = "call_1", Name = "get_dims", Arguments = new Dictionary<string, object?>() }
                },
                RawAssistantMessage = "asst",
                Usage = new ModelUsage { PromptTokens = 100, CompletionTokens = 30, TotalTokens = 130 }
            }
        });

        // Turn 2: text
        client.EnqueueStreamingResponse(new StreamingAgentResponse
        {
            TextChunks = new List<string> { "Done" },
            Response = new AgentTurnResponse
            {
                Success = true,
                StopReason = AgentStopReason.EndTurn,
                TextContent = "Done",
                Usage = new ModelUsage { PromptTokens = 200, CompletionTokens = 10, TotalTokens = 210 }
            }
        });

        var toolExecutor = new StubToolExecutor();
        toolExecutor.RegisterResult("get_dims", "{}");

        AgentLoopResult result = _runner.Run(
            client, toolExecutor,
            "sys", "test",
            new List<AgentToolDefinition>(), _settings,
            CancellationToken.None, null, null,
            _ => { });

        Assert.That(result.AggregateUsage.TotalTokens, Is.EqualTo(340));
        Assert.That(result.AggregateUsage.PromptTokens, Is.EqualTo(300));
        Assert.That(result.AggregateUsage.CompletionTokens, Is.EqualTo(40));
    }
}
