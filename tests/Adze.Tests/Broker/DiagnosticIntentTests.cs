using System.Linq;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class DiagnosticIntentTests
{
    [Test]
    public void ExtractClarificationIntent_DiagnosePrefix_ReturnsIntent()
    {
        string input = "[clarification] intent=diagnose, scope=Feature: Pocket, output=detailed [/clarification] What's wrong?";
        string? result = KeywordBrokerOrchestrator.ExtractClarificationIntent(input);
        Assert.That(result, Is.EqualTo("diagnose"));
    }

    [Test]
    public void ExtractClarificationIntent_InspectPrefix_ReturnsInspect()
    {
        string input = "[clarification] intent=inspect [/clarification] Summarize";
        string? result = KeywordBrokerOrchestrator.ExtractClarificationIntent(input);
        Assert.That(result, Is.EqualTo("inspect"));
    }

    [Test]
    public void ExtractClarificationIntent_NoPrefix_ReturnsNull()
    {
        string? result = KeywordBrokerOrchestrator.ExtractClarificationIntent("What are the dimensions?");
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExtractClarificationIntent_EmptyInput_ReturnsNull()
    {
        Assert.That(KeywordBrokerOrchestrator.ExtractClarificationIntent(""), Is.Null);
        Assert.That(KeywordBrokerOrchestrator.ExtractClarificationIntent(null!), Is.Null);
    }

    [Test]
    public void ExtractClarificationIntent_MalformedPrefix_ReturnsNull()
    {
        string input = "[clarification] scope=something [/clarification]";
        string? result = KeywordBrokerOrchestrator.ExtractClarificationIntent(input);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void DiagnoseIntent_BoostsDiagnosticTools()
    {
        var orchestrator = new KeywordBrokerOrchestrator();
        SessionContext context = SessionContextFactory.CreateWithPart();
        string request = "[clarification] intent=diagnose [/clarification] Check for issues";
        BrokerResponse response = orchestrator.CreateGroundingPlan(context, request);

        Assert.That(response.Intent, Is.EqualTo("diagnostics_review"));
        var topTool = response.RecommendedTools.FirstOrDefault();
        Assert.That(topTool, Is.Not.Null);
        Assert.That(topTool!.ToolName, Is.EqualTo(ToolNames.GetRebuildDiagnostics));
    }

    [Test]
    public void WhatsWrongKeyword_InfersDiagnosticIntent()
    {
        var orchestrator = new KeywordBrokerOrchestrator();
        SessionContext context = SessionContextFactory.CreateWithPart();
        BrokerResponse response = orchestrator.CreateGroundingPlan(context, "what's wrong with this part?");

        Assert.That(response.Intent, Is.EqualTo("diagnostics_review"));
    }

    [Test]
    public void BuildAgentSystemPrompt_DiagnosticIntent_IncludesDiagnosticGuidance()
    {
        string prompt = ContextPromptComposer.BuildAgentSystemPrompt("diagnostics_review");
        Assert.That(prompt, Does.Contain("diagnostic analysis"));
        Assert.That(prompt, Does.Contain("get_rebuild_diagnostics"));
    }

    [Test]
    public void BuildAgentSystemPrompt_NullIntent_NoExtraGuidance()
    {
        string prompt = ContextPromptComposer.BuildAgentSystemPrompt(null);
        Assert.That(prompt, Does.Not.Contain("diagnostic analysis"));
    }

    [Test]
    public void BuildSynthesisSystemPrompt_DiagnosticIntent_IncludesRootCauseGuidance()
    {
        string prompt = ContextPromptComposer.BuildSynthesisSystemPrompt("diagnostics_review");
        Assert.That(prompt, Does.Contain("root causes"));
        Assert.That(prompt, Does.Contain("severity"));
    }
}
