using System.Linq;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class KeywordBrokerOrchestratorTests
{
    private KeywordBrokerOrchestrator _orchestrator = null!;

    [SetUp]
    public void SetUp()
    {
        _orchestrator = new KeywordBrokerOrchestrator();
    }

    [Test]
    public void CreateGroundingPlan_HostNotConnected_ReturnsHostUnavailable()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize", false);

        Assert.That(response.TurnStatus, Is.EqualTo("host_unavailable"));
        Assert.That(response.Confidence, Is.EqualTo(0.98));
        Assert.That(response.Blockers, Is.Not.Empty);
        Assert.That(response.RecoverySuggestions, Is.Not.Empty);
    }

    [Test]
    public void CreateGroundingPlan_NoDocument_ReturnsNeedsDocument()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.TurnStatus, Is.EqualTo("needs_document"));
        Assert.That(response.Confidence, Is.EqualTo(0.96));
        Assert.That(response.Blockers, Is.Not.Empty);
    }

    [Test]
    public void CreateGroundingPlan_NullRequest_DoesNotThrow()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, null!);

        Assert.That(response, Is.Not.Null);
        Assert.That(response.Mode, Is.EqualTo("grounding"));
    }

    [Test]
    public void CreateGroundingPlan_GeneralRequest_IncludesBaselineTools()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "help me understand this part");

        Assert.That(response.TurnStatus, Is.EqualTo("ready"));
        Assert.That(response.RecommendedTools, Is.Not.Empty);

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetActiveDocument));
        Assert.That(toolNames, Does.Contain(ToolNames.GetDocumentSummary));
    }

    [Test]
    public void CreateGroundingPlan_SelectionKeyword_IncludesSelectionTool()
    {
        SessionContext context = SessionContextFactory.CreateWithSelection();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "what is selected?");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetSelectionContext));
    }

    [Test]
    public void CreateGroundingPlan_DimensionKeyword_IncludesDimensionsTool()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "show me the dimensions");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetDimensions));
    }

    [Test]
    public void CreateGroundingPlan_FeatureKeyword_IncludesFeatureTreeTool()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "show me the feature tree");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetFeatureTreeSlice));
    }

    [Test]
    public void CreateGroundingPlan_ConfigurationKeyword_IncludesConfigurationsTool()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "list configurations");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetConfigurations));
    }

    [Test]
    public void CreateGroundingPlan_PropertyKeyword_IncludesCustomPropertiesTool()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "what are the custom properties?");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetCustomProperties));
    }

    [Test]
    public void CreateGroundingPlan_MateKeyword_IncludesMatesTool()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "show the mates");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetMates));
    }

    [Test]
    public void CreateGroundingPlan_DiagnosticsKeyword_IncludesDiagnosticsTool()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "are there any rebuild warnings?");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetRebuildDiagnostics));
    }

    [Test]
    public void CreateGroundingPlan_ReferenceKeyword_IncludesReferenceGraphTool()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "show dependencies and references");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetReferenceGraph));
    }

    [Test]
    public void CreateGroundingPlan_AssemblyContext_BoostsAssemblyTools()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "help me understand this");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetReferenceGraph));
    }

    [Test]
    public void CreateGroundingPlan_PartContext_BoostsPartTools()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "help me understand this");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetFeatureTreeSlice));
    }

    [Test]
    public void CreateGroundingPlan_MissingReferences_ReportsBlocker()
    {
        SessionContext context = SessionContextFactory.CreateWithDiagnosticIssues();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.TurnStatus, Is.EqualTo("attention_needed"));
        Assert.That(response.Blockers, Has.Some.Contains("missing references"));
    }

    [Test]
    public void CreateGroundingPlan_ReadOnlyDocument_ReportsBlocker()
    {
        SessionContext context = SessionContextFactory.CreateReadOnly();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.Blockers, Has.Some.Contains("read-only"));
    }

    [Test]
    public void CreateGroundingPlan_UnsavedDocument_ReportsBlocker()
    {
        SessionContext context = SessionContextFactory.CreateUnsaved();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.Blockers, Has.Some.Contains("saved path"));
    }

    [Test]
    public void CreateGroundingPlan_NoBlockers_StatusReady()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.TurnStatus, Is.EqualTo("ready"));
        Assert.That(response.Blockers, Is.Empty);
    }

    [Test]
    public void CreateGroundingPlan_LimitsFourRecommendations()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "show dimensions features mates references diagnostics properties configurations");

        Assert.That(response.RecommendedTools.Count, Is.LessThanOrEqualTo(4));
    }

    [Test]
    public void CreateGroundingPlan_OnlyAllowedToolsIncluded()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        context.Policy.EnabledTools = new System.Collections.Generic.List<string>
        {
            ToolNames.GetActiveDocument,
            ToolNames.GetDocumentSummary
        };

        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "show me everything");

        foreach (BrokerToolRecommendation rec in response.RecommendedTools)
        {
            Assert.That(context.Policy.EnabledTools, Does.Contain(rec.ToolName),
                $"Tool {rec.ToolName} was recommended but is not in enabled tools.");
        }
    }

    [Test]
    public void CreateGroundingPlan_Intent_SelectionRequest_InfersSelectionReview()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "what is selected?");

        Assert.That(response.Intent, Is.EqualTo("selection_review"));
    }

    [Test]
    public void CreateGroundingPlan_Intent_DimensionRequest_InfersDimensionReview()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "show dimensions");

        Assert.That(response.Intent, Is.EqualTo("dimension_review"));
    }

    [Test]
    public void CreateGroundingPlan_Intent_DiagnosticsRequest_InfersDiagnosticsReview()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "check rebuild warnings");

        Assert.That(response.Intent, Is.EqualTo("diagnostics_review"));
    }

    [Test]
    public void CreateGroundingPlan_Intent_GeneralRequest_InfersGeneralGrounding()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "hello");

        Assert.That(response.Intent, Is.EqualTo("general_grounding"));
    }

    [Test]
    public void CreateGroundingPlan_Confidence_NoRecommendations_Low()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        context.Policy.EnabledTools.Clear();

        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.Confidence, Is.EqualTo(0.25));
    }

    [Test]
    public void CreateGroundingPlan_Confidence_WithRecommendations_AboveBaseline()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.Confidence, Is.GreaterThan(0.35));
        Assert.That(response.Confidence, Is.LessThanOrEqualTo(0.95));
    }

    [Test]
    public void CreateGroundingPlan_DiagnosticsWarnings_BoostsDiagnosticsTool()
    {
        SessionContext context = SessionContextFactory.CreateWithDiagnosticIssues();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetRebuildDiagnostics));
    }

    [Test]
    public void CreateGroundingPlan_BrokenReferences_BoostsReferenceGraph()
    {
        SessionContext context = SessionContextFactory.CreateWithBrokenReferences();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetReferenceGraph));
    }

    [Test]
    public void CreateGroundingPlan_RecommendationsPrioritized()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "show me the dimensions");

        for (int i = 0; i < response.RecommendedTools.Count; i++)
        {
            Assert.That(response.RecommendedTools[i].Priority, Is.EqualTo(i + 1),
                $"Tool at index {i} should have priority {i + 1}.");
        }
    }

    [Test]
    public void CreateGroundingPlan_ModeAlwaysGrounding()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.Mode, Is.EqualTo("grounding"));
    }

    [Test]
    public void CreateGroundingPlan_NextQuestions_AssemblyContext_SuggestsAssemblyQuestions()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.NextQuestions, Has.Some.Contains("assembly"));
    }

    [Test]
    public void CreateGroundingPlan_NextQuestions_PartContext_SuggestsPartQuestions()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        Assert.That(response.NextQuestions, Has.Some.Contains("part"));
    }

    [Test]
    public void CreateGroundingPlan_LiveSelection_WithoutKeyword_StillBoostsSelection()
    {
        SessionContext context = SessionContextFactory.CreateWithSelection();
        BrokerResponse response = _orchestrator.CreateGroundingPlan(context, "summarize");

        var toolNames = response.RecommendedTools.Select(t => t.ToolName).ToList();
        Assert.That(toolNames, Does.Contain(ToolNames.GetSelectionContext));
    }
}
