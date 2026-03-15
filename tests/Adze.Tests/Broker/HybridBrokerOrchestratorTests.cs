using System.Collections.Generic;
using System.Linq;
using Adze.Broker.Abstractions;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class HybridBrokerOrchestratorTests
{
    private KeywordBrokerOrchestrator _fallback = null!;

    [SetUp]
    public void SetUp()
    {
        _fallback = new KeywordBrokerOrchestrator();
    }

    [Test]
    public void CreateGroundingPlan_NoModelClient_ReturnsFallbackResponse()
    {
        var orchestrator = new HybridBrokerOrchestrator(_fallback, null);
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.TurnStatus, Is.EqualTo("ready"));
        Assert.That(response.RecommendedTools, Is.Not.Empty);
    }

    [Test]
    public void CreateGroundingPlan_HostUnavailable_SkipsModel()
    {
        var mockClient = new StubModelClient(new ModelTurnResult
        {
            Success = true,
            Response = new BrokerResponse { TurnStatus = "ready" }
        });
        var orchestrator = new HybridBrokerOrchestrator(_fallback, mockClient);
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "summarize", false);

        Assert.That(response.TurnStatus, Is.EqualTo("host_unavailable"));
        Assert.That(mockClient.CompleteCalled, Is.False);
    }

    [Test]
    public void CreateGroundingPlan_NeedsDocument_SkipsModel()
    {
        var mockClient = new StubModelClient(new ModelTurnResult
        {
            Success = true,
            Response = new BrokerResponse { TurnStatus = "ready" }
        });
        var orchestrator = new HybridBrokerOrchestrator(_fallback, mockClient);
        SessionContext context = SessionContextFactory.CreateMinimal();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.TurnStatus, Is.EqualTo("needs_document"));
        Assert.That(mockClient.CompleteCalled, Is.False);
    }

    [Test]
    public void CreateGroundingPlan_ModelFails_ReturnsFallback()
    {
        var mockClient = new StubModelClient(new ModelTurnResult
        {
            Success = false,
            FailureReason = "timeout"
        });
        var orchestrator = new HybridBrokerOrchestrator(_fallback, mockClient);
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.TurnStatus, Is.EqualTo("ready"));
        Assert.That(response.Source, Is.EqualTo("deterministic_fallback"));
    }

    [Test]
    public void CreateGroundingPlan_ModelSucceeds_MergesResponses()
    {
        var modelResponse = new BrokerResponse
        {
            TurnStatus = "ready",
            Intent = "model_intent",
            Confidence = 0.85,
            Summary = "Model summary",
            AssistantMessage = "Model message",
            Blockers = new List<string>(),
            RecoverySuggestions = new List<string> { "Model suggestion" },
            NextQuestions = new List<string> { "Model question" },
            RecommendedTools = new List<BrokerToolRecommendation>
            {
                new BrokerToolRecommendation { ToolName = ToolNames.GetActiveDocument, Priority = 1, Score = 10, Reason = "model reason" },
                new BrokerToolRecommendation { ToolName = ToolNames.GetDocumentSummary, Priority = 2, Score = 8, Reason = "model reason" }
            }
        };
        var mockClient = new StubModelClient(new ModelTurnResult
        {
            Success = true,
            Provider = "anthropic",
            Model = "claude-sonnet-4-20250514",
            Response = modelResponse
        });
        var orchestrator = new HybridBrokerOrchestrator(_fallback, mockClient);
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.Intent, Is.EqualTo("model_intent"));
        Assert.That(response.Confidence, Is.EqualTo(0.85));
        Assert.That(response.Summary, Is.EqualTo("Model summary"));
        Assert.That(response.Source, Is.EqualTo("model_anthropic"));
    }

    [Test]
    public void CreateGroundingPlan_ModelRecommendsDisabledTool_FallsBackToFallbackRecommendations()
    {
        var modelResponse = new BrokerResponse
        {
            TurnStatus = "ready",
            RecommendedTools = new List<BrokerToolRecommendation>
            {
                new BrokerToolRecommendation { ToolName = "nonexistent_tool", Priority = 1, Score = 10 }
            }
        };
        var mockClient = new StubModelClient(new ModelTurnResult
        {
            Success = true,
            Provider = "anthropic",
            Model = "test",
            Response = modelResponse
        });
        var orchestrator = new HybridBrokerOrchestrator(_fallback, mockClient);
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.RecommendedTools, Is.Not.Empty);
        Assert.That(response.RecommendedTools.All(t => context.Policy.EnabledTools.Contains(t.ToolName)), Is.True);
    }

    [Test]
    public void CreateGroundingPlan_ModelBlockersWithReadyStatus_CorrectsTurnStatus()
    {
        var modelResponse = new BrokerResponse
        {
            TurnStatus = "ready",
            Blockers = new List<string> { "Model detected a blocker" },
            RecommendedTools = new List<BrokerToolRecommendation>
            {
                new BrokerToolRecommendation { ToolName = ToolNames.GetActiveDocument, Priority = 1, Score = 10 }
            }
        };
        var mockClient = new StubModelClient(new ModelTurnResult
        {
            Success = true,
            Provider = "openai",
            Model = "gpt-4o",
            Response = modelResponse
        });
        var orchestrator = new HybridBrokerOrchestrator(_fallback, mockClient);
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.TurnStatus, Is.EqualTo("attention_needed"));
    }

    [Test]
    public void CreateGroundingPlan_ModelRecommendationsLimitedToFour()
    {
        var tools = new List<BrokerToolRecommendation>();
        foreach (string name in new[] { ToolNames.GetActiveDocument, ToolNames.GetDocumentSummary,
            ToolNames.GetSelectionContext, ToolNames.GetFeatureTreeSlice, ToolNames.GetDimensions, ToolNames.GetConfigurations })
        {
            tools.Add(new BrokerToolRecommendation { ToolName = name, Priority = tools.Count + 1, Score = 10 - tools.Count });
        }

        var modelResponse = new BrokerResponse
        {
            TurnStatus = "ready",
            RecommendedTools = tools
        };
        var mockClient = new StubModelClient(new ModelTurnResult
        {
            Success = true,
            Provider = "anthropic",
            Model = "test",
            Response = modelResponse
        });
        var orchestrator = new HybridBrokerOrchestrator(_fallback, mockClient);
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.RecommendedTools.Count, Is.LessThanOrEqualTo(4));
    }

    private sealed class StubModelClient : IModelClient
    {
        private readonly ModelTurnResult _result;

        public StubModelClient(ModelTurnResult result)
        {
            _result = result;
        }

        public bool CompleteCalled { get; private set; }

        public ModelTurnResult Complete(BrokerPrompt prompt)
        {
            CompleteCalled = true;
            return _result;
        }

        public AssistantSynthesisResult Synthesize(AssistantSynthesisPrompt prompt)
        {
            return new AssistantSynthesisResult { Success = false, FailureReason = "stub" };
        }
    }
}
