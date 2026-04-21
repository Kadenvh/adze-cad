using System;
using System.Collections.Generic;
using Adze.Broker.Abstractions;
using Adze.Broker.Clients;
using Adze.Broker.Configuration;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Models;
using Adze.Tests.Helpers;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
[Category("LiveProvider")]
public sealed class LiveProviderSmokeTests
{
    private BrokerModelSettings _settings = null!;
    private IModelClient _client = null!;

    // Preserve the user's ApiKeyStore across live smoke tests. These tests should
    // only run when API keys are explicitly present in the environment — not when
    // the Settings panel has a key saved for normal day-to-day usage.
    private (string? Provider, string? Key) _savedKeyStore;

    [SetUp]
    public void SetUp()
    {
        _savedKeyStore = ApiKeyStore.Load();
        ApiKeyStore.Clear();

        _settings = BrokerModelSettings.LoadFromEnvironment();

        if (!_settings.IsUsable())
        {
            Assert.Inconclusive(
                "No usable provider API key in environment. " +
                "Set SOLIDWORKS_AI_OPENAI_API_KEY, SOLIDWORKS_AI_ANTHROPIC_API_KEY, or SOLIDWORKS_AI_OPENROUTER_API_KEY to run live provider smoke tests.");
        }

        IModelClient? client = ModelClientFactory.Create(_settings);
        if (client == null)
        {
            Assert.Inconclusive("ModelClientFactory returned null despite usable settings.");
        }

        _client = client;
    }

    [TearDown]
    public void TearDown()
    {
        if (!string.IsNullOrWhiteSpace(_savedKeyStore.Provider) && _savedKeyStore.Key != null)
        {
            ApiKeyStore.Save(_savedKeyStore.Provider!, _savedKeyStore.Key);
        }
    }

    [Test]
    public void Settings_ResolvedCorrectly()
    {
        Assert.That(_settings.Enabled, Is.True, "Model should be enabled");
        Assert.That(_settings.ApiKey, Is.Not.Empty, "API key should be present");
        Assert.That(_settings.Provider, Is.AnyOf("openai", "anthropic", "openrouter"), "Provider should be openai, anthropic, or openrouter");
        Assert.That(_settings.Model, Is.Not.Empty, "Model name should be set");
        Assert.That(_settings.Endpoint, Is.Not.Empty, "Endpoint should be set");

        TestContext.WriteLine($"Provider: {_settings.Provider}");
        TestContext.WriteLine($"Model: {_settings.Model}");
        TestContext.WriteLine($"Endpoint: {_settings.Endpoint}");
    }

    [Test]
    public void BrokerPlanning_ReturnsStructuredResponse()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();
        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "What are the dimensions of this part?");

        ModelTurnResult result = _client.Complete(prompt);

        TestContext.WriteLine($"Success: {result.Success}");
        TestContext.WriteLine($"Provider: {result.Provider}");
        TestContext.WriteLine($"Model: {result.Model}");
        TestContext.WriteLine($"Summary: {result.Summary}");
        TestContext.WriteLine($"FailureReason: {result.FailureReason}");
        TestContext.WriteLine($"RawResponse length: {result.RawResponseText?.Length ?? 0}");
        TestContext.WriteLine($"Usage: prompt={result.Usage.PromptTokens} completion={result.Usage.CompletionTokens} total={result.Usage.TotalTokens}");

        Assert.That(result.Success, Is.True, $"Broker planning should succeed. Failure: {result.FailureReason}");
        Assert.That(result.Provider, Is.EqualTo(_settings.Provider));
        Assert.That(result.Model, Is.Not.Empty);
        Assert.That(result.Response, Is.Not.Null, "Parsed BrokerResponse should not be null");
        Assert.That(result.Response!.TurnStatus, Is.AnyOf("ready", "attention_needed"));
        Assert.That(result.Response.RecommendedTools, Is.Not.Empty, "Model should recommend at least one tool");
        Assert.That(result.Response.RecommendedTools.Count, Is.LessThanOrEqualTo(4));
        Assert.That(result.Usage.TotalTokens, Is.GreaterThan(0), "Usage should report token counts");
        Assert.That(result.Usage.PromptTokens, Is.GreaterThan(0), "Prompt tokens should be nonzero");
        Assert.That(result.Usage.CompletionTokens, Is.GreaterThan(0), "Completion tokens should be nonzero");
    }

    [Test]
    public void BrokerPlanning_RecommendsRelevantTools()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();
        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "What are the dimensions of this part?");

        ModelTurnResult result = _client.Complete(prompt);

        Assert.That(result.Success, Is.True, $"Planning should succeed. Failure: {result.FailureReason}");

        List<string> toolNames = result.RequestedTools;
        TestContext.WriteLine($"Recommended tools: {string.Join(", ", toolNames)}");

        Assert.That(toolNames, Does.Contain("get_dimensions"),
            "Model should recommend get_dimensions for a dimensions question");
    }

    [Test]
    public void Synthesis_ReturnsGroundedAnswer()
    {
        var report = new GroundingExecutionReport
        {
            Request = "What are the dimensions of this part?",
            Response = new BrokerResponse
            {
                TurnStatus = "ready",
                Intent = "dimension_review",
                Confidence = 0.9,
                Summary = "User wants to review part dimensions",
                AssistantMessage = "Retrieving dimensions for the active part.",
                RecoverySuggestions = new List<string>(),
                NextQuestions = new List<string>(),
                RecommendedTools = new List<BrokerToolRecommendation>
                {
                    new BrokerToolRecommendation
                    {
                        ToolName = "get_dimensions",
                        Reason = "User asked about dimensions",
                        Priority = 1,
                        Score = 10
                    }
                }
            },
            ToolResults = new List<ToolResult>
            {
                new ToolResult
                {
                    ToolName = "get_dimensions",
                    Success = true,
                    Summary = "3 dimensions found: D1@Sketch1 = 50.0 mm, D2@Sketch1 = 25.0 mm, D3@Boss-Extrude1 = 10.0 mm"
                }
            },
            SuccessfulToolCount = 1
        };

        string contextJson = "{\"document\":{\"type\":\"part\",\"title\":\"TestPart\",\"units\":\"mm\"}}";
        string toolResultsJson = "[{\"tool_name\":\"get_dimensions\",\"success\":true," +
            "\"summary\":\"3 dimensions found: D1@Sketch1 = 50.0 mm, D2@Sketch1 = 25.0 mm, D3@Boss-Extrude1 = 10.0 mm\"}]";

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(
            report, contextJson, toolResultsJson, _client);

        string expectedSource = ModelClientFactory.BuildModelSourceLabel(_settings.Provider);
        TestContext.WriteLine($"Source: {outcome.Source}");
        TestContext.WriteLine($"ModelId: {outcome.ModelId}");
        TestContext.WriteLine($"FailureReason: {outcome.FailureReason}");
        TestContext.WriteLine($"Answer length: {outcome.AnswerText?.Length ?? 0}");
        TestContext.WriteLine($"Answer: {outcome.AnswerText}");
        TestContext.WriteLine($"Usage: prompt={outcome.Usage.PromptTokens} completion={outcome.Usage.CompletionTokens} total={outcome.Usage.TotalTokens}");

        Assert.That(outcome.Source, Is.EqualTo(expectedSource),
            $"Answer should come from the model provider, not fallback. Failure: {outcome.FailureReason}");
        Assert.That(outcome.ModelId, Is.Not.Empty);
        Assert.That(outcome.AnswerText, Is.Not.Empty, "Synthesized answer should not be empty");
        Assert.That(outcome.FailureReason, Is.Empty, "No failure expected for successful synthesis");
        Assert.That(outcome.Usage.TotalTokens, Is.GreaterThan(0), "Synthesis should report token usage");
    }

    [Test]
    public void Synthesis_AnswerReferencesProvidedData()
    {
        var report = new GroundingExecutionReport
        {
            Request = "How many mates are in this assembly?",
            Response = new BrokerResponse
            {
                TurnStatus = "ready",
                Intent = "mate_review",
                Confidence = 0.9,
                Summary = "User wants to review assembly mates",
                AssistantMessage = "Retrieving mate information.",
                RecoverySuggestions = new List<string>(),
                NextQuestions = new List<string>(),
                RecommendedTools = new List<BrokerToolRecommendation>
                {
                    new BrokerToolRecommendation
                    {
                        ToolName = "get_mates",
                        Reason = "User asked about mates",
                        Priority = 1,
                        Score = 10
                    }
                }
            },
            ToolResults = new List<ToolResult>
            {
                new ToolResult
                {
                    ToolName = "get_mates",
                    Success = true,
                    Summary = "2 mates found: Coincident1 (Coincident) between Part1-1 and Part2-1, Concentric1 (Concentric) between Part1-1 and Part2-1"
                }
            },
            SuccessfulToolCount = 1
        };

        string contextJson = "{\"document\":{\"type\":\"assembly\",\"title\":\"TestAssembly\",\"units\":\"mm\"}}";
        string toolResultsJson = "[{\"tool_name\":\"get_mates\",\"success\":true," +
            "\"summary\":\"2 mates found: Coincident1 (Coincident) between Part1-1 and Part2-1, Concentric1 (Concentric) between Part1-1 and Part2-1\"}]";

        GroundingSynthesisOutcome outcome = GroundingSynthesisService.Build(
            report, contextJson, toolResultsJson, _client);

        string expectedSource = ModelClientFactory.BuildModelSourceLabel(_settings.Provider);
        TestContext.WriteLine($"Answer: {outcome.AnswerText}");

        Assert.That(outcome.Source, Is.EqualTo(expectedSource),
            $"Should use model path. Failure: {outcome.FailureReason}");
        Assert.That(outcome.AnswerText, Does.Contain("2").Or.Contain("two"),
            "Answer should reference the mate count from tool results");
    }

    [Test]
    public void FullOrchestration_EndToEnd()
    {
        var fallback = new KeywordBrokerOrchestrator();
        var orchestrator = new HybridBrokerOrchestrator(fallback, _client);
        SessionContext context = SessionContextFactory.CreateWithDimensions();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "Summarize the dimensions");

        string expectedSource = ModelClientFactory.BuildModelSourceLabel(_settings.Provider);
        TestContext.WriteLine($"Source: {response.Source}");
        TestContext.WriteLine($"TurnStatus: {response.TurnStatus}");
        TestContext.WriteLine($"Intent: {response.Intent}");
        TestContext.WriteLine($"Confidence: {response.Confidence}");
        TestContext.WriteLine($"Summary: {response.Summary}");
        TestContext.WriteLine($"ModelId: {response.ModelId}");
        TestContext.WriteLine($"Tools: {string.Join(", ", response.RecommendedTools.ConvertAll(t => t.ToolName))}");

        Assert.That(response.Source, Is.EqualTo(expectedSource),
            $"Orchestrator should use model path, not fallback");
        Assert.That(response.TurnStatus, Is.AnyOf("ready", "attention_needed"));
        Assert.That(response.RecommendedTools, Is.Not.Empty);
        Assert.That(response.Confidence, Is.GreaterThan(0));
    }
}
