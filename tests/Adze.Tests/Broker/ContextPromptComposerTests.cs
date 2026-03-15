using System.Collections.Generic;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class ContextPromptComposerTests
{
    [Test]
    public void Compose_WithDocument_IncludesDocumentInfo()
    {
        SessionContext context = SessionContextFactory.CreateWithPart("MyPart");

        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "summarize");

        Assert.That(prompt.UserPrompt, Does.Contain("MyPart"));
        Assert.That(prompt.UserPrompt, Does.Contain("part"));
        Assert.That(prompt.UserPrompt, Does.Contain("summarize"));
    }

    [Test]
    public void Compose_NoDocument_IncludesNone()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "hello");

        Assert.That(prompt.UserPrompt, Does.Contain("none"));
    }

    [Test]
    public void Compose_IncludesAllowedTools()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "test");

        Assert.That(prompt.AllowedTools, Is.Not.Empty);
        Assert.That(prompt.AllowedTools, Does.Contain(ToolNames.GetActiveDocument));
    }

    [Test]
    public void Compose_SystemPrompt_ContainsInstructions()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "test");

        Assert.That(prompt.SystemPrompt, Does.Contain("SOLIDWORKS"));
        Assert.That(prompt.SystemPrompt, Does.Contain("JSON"));
    }

    [Test]
    public void Compose_WithSelection_IncludesSelectionPreview()
    {
        SessionContext context = SessionContextFactory.CreateWithSelection();

        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "test");

        Assert.That(prompt.UserPrompt, Does.Contain("Selection count: 2"));
        Assert.That(prompt.UserPrompt, Does.Contain("Face"));
    }

    [Test]
    public void Compose_WithWarnings_IncludesDiagnostics()
    {
        SessionContext context = SessionContextFactory.CreateWithDiagnosticIssues();

        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "test");

        Assert.That(prompt.UserPrompt, Does.Contain("warnings"));
        Assert.That(prompt.UserPrompt, Does.Contain("Missing references"));
    }

    [Test]
    public void ComposeSynthesisPrompt_IncludesAllFields()
    {
        var response = new BrokerResponse
        {
            TurnStatus = "ready",
            Intent = "general_grounding",
            Confidence = 0.8,
            Summary = "Test",
            AssistantMessage = "Test message",
            Blockers = new List<string>(),
            RecoverySuggestions = new List<string> { "try again" },
            NextQuestions = new List<string> { "what next?" },
            RecommendedTools = new List<BrokerToolRecommendation>()
        };

        AssistantSynthesisPrompt prompt = ContextPromptComposer.ComposeSynthesisPrompt(
            "summarize", response, "{}", "[]");

        Assert.That(prompt.SystemPrompt, Does.Contain("SOLIDWORKS"));
        Assert.That(prompt.UserPrompt, Does.Contain("summarize"));
        Assert.That(prompt.UserPrompt, Does.Contain("ready"));
        Assert.That(prompt.UserPrompt, Does.Contain("try again"));
    }

    [Test]
    public void Compose_UserPrompt_IncludesDocumentMetrics()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();
        context.Dimensions = new DimensionsInfo { Count = 5 };
        context.Mates = new MatesInfo { Count = 3 };

        BrokerPrompt prompt = ContextPromptComposer.Compose(context, "test");

        Assert.That(prompt.UserPrompt, Does.Contain("Feature preview count: 9"));
        Assert.That(prompt.UserPrompt, Does.Contain("Dimension count: 5"));
        Assert.That(prompt.UserPrompt, Does.Contain("Mate count: 3"));
    }
}
