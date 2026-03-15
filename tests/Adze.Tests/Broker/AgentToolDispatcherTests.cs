using System.Collections.Generic;
using Adze.Broker.Abstractions;
using Adze.Broker.Models;
using Adze.Broker.Orchestration;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tools;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class AgentToolDispatcherTests
{
    private AgentToolDispatcher _dispatcher = null!;
    private GroundingToolCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = ToolCatalog.CreateGroundingCatalog();
        _dispatcher = new AgentToolDispatcher(_catalog);
    }

    // --- Known tool dispatch tests ---

    [Test]
    public void Execute_GetActiveDocument_WithDocument_ReturnsSuccessResult()
    {
        SessionContext session = SessionContextFactory.CreateWithPart("MyPart");
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetActiveDocument,
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.ToolName, Is.EqualTo(ToolNames.GetActiveDocument));
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
        Assert.That(result.OutputJson, Does.Contain("MyPart"));
    }

    [Test]
    public void Execute_GetActiveDocument_NoDocument_ReturnsToolFailureNotDispatcherError()
    {
        SessionContext session = SessionContextFactory.CreateMinimal();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetActiveDocument,
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("\"success\":false"));
        Assert.That(result.OutputJson, Does.Contain("No active document"));
    }

    [Test]
    public void Execute_GetDimensions_WithScopeArgument_PassesParameterCorrectly()
    {
        SessionContext session = SessionContextFactory.CreateWithDimensions();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetDimensions,
            new Dictionary<string, object?>
            {
                ["scope"] = "document",
                ["include_driven"] = true
            },
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.ToolName, Is.EqualTo(ToolNames.GetDimensions));
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
        Assert.That(result.OutputJson, Does.Contain("\"scope\":\"document\""));
    }

    [Test]
    public void Execute_GetFeatureTreeSlice_WithAnchorName_PassesParameterCorrectly()
    {
        SessionContext session = SessionContextFactory.CreateWithFeatures();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetFeatureTreeSlice,
            new Dictionary<string, object?>
            {
                ["anchor_name"] = "Boss-Extrude1",
                ["radius"] = 2
            },
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
        Assert.That(result.OutputJson, Does.Contain("Boss-Extrude1"));
    }

    [Test]
    public void Execute_GetDocumentSummary_DefaultParams_ReturnsSuccess()
    {
        SessionContext session = SessionContextFactory.CreateWithPart();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetDocumentSummary,
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
    }

    [Test]
    public void Execute_GetConfigurations_ReturnsSuccess()
    {
        SessionContext session = SessionContextFactory.CreateWithPart();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetConfigurations,
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
    }

    [Test]
    public void Execute_GetMates_OnAssembly_ReturnsSuccess()
    {
        SessionContext session = SessionContextFactory.CreateWithAssembly();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetMates,
            new Dictionary<string, object?>
            {
                ["scope"] = "document",
                ["limit"] = 50
            },
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
    }

    // --- Error handling tests ---

    [Test]
    public void Execute_UnknownToolName_ReturnsErrorResult()
    {
        SessionContext session = SessionContextFactory.CreateWithPart();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            "nonexistent_tool",
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.True);
        Assert.That(result.ToolName, Is.EqualTo("nonexistent_tool"));
        Assert.That(result.OutputJson, Does.Contain("Unknown tool"));
    }

    [Test]
    public void Execute_NullToolName_ReturnsErrorResult()
    {
        SessionContext session = SessionContextFactory.CreateWithPart();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            null!,
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.True);
        Assert.That(result.OutputJson, Does.Contain("null or empty"));
    }

    [Test]
    public void Execute_EmptyToolName_ReturnsErrorResult()
    {
        SessionContext session = SessionContextFactory.CreateWithPart();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            "",
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.True);
        Assert.That(result.OutputJson, Does.Contain("null or empty"));
    }

    [Test]
    public void Execute_WhitespaceToolName_ReturnsErrorResult()
    {
        SessionContext session = SessionContextFactory.CreateWithPart();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            "   ",
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.True);
        Assert.That(result.OutputJson, Does.Contain("null or empty"));
    }

    [Test]
    public void Execute_NullContext_ReturnsErrorResult()
    {
        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetActiveDocument,
            new Dictionary<string, object?>(),
            null!);

        Assert.That(result.IsError, Is.True);
        Assert.That(result.OutputJson, Does.Contain("No SessionContext"));
    }

    [Test]
    public void Execute_ContextWithoutSessionContext_ReturnsErrorResult()
    {
        var context = new ToolExecutionContext
        {
            SessionId = "test",
            SessionContext = null
        };

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetActiveDocument,
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.True);
        Assert.That(result.OutputJson, Does.Contain("No SessionContext"));
    }

    [Test]
    public void Execute_NullArguments_TreatedAsEmpty()
    {
        SessionContext session = SessionContextFactory.CreateWithPart();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetActiveDocument,
            null!,
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
    }

    // --- Argument deserialization edge cases ---

    [Test]
    public void Execute_GetDimensions_StringBoolArgument_ParsedCorrectly()
    {
        SessionContext session = SessionContextFactory.CreateWithDimensions();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetDimensions,
            new Dictionary<string, object?>
            {
                ["scope"] = "document",
                ["include_driven"] = "true"
            },
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
    }

    [Test]
    public void Execute_GetFeatureTreeSlice_StringIntArgument_ParsedCorrectly()
    {
        SessionContext session = SessionContextFactory.CreateWithFeatures();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            ToolNames.GetFeatureTreeSlice,
            new Dictionary<string, object?>
            {
                ["radius"] = "3"
            },
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.OutputJson, Does.Contain("\"success\":true"));
    }

    // --- All tool names dispatch without error ---

    [TestCase(ToolNames.GetActiveDocument)]
    [TestCase(ToolNames.GetDocumentSummary)]
    [TestCase(ToolNames.GetSelectionContext)]
    [TestCase(ToolNames.GetFeatureTreeSlice)]
    [TestCase(ToolNames.GetDimensions)]
    [TestCase(ToolNames.GetConfigurations)]
    [TestCase(ToolNames.GetCustomProperties)]
    [TestCase(ToolNames.GetMates)]
    [TestCase(ToolNames.GetRebuildDiagnostics)]
    [TestCase(ToolNames.GetReferenceGraph)]
    public void Execute_AllKnownTools_DoNotReturnDispatcherError(string toolName)
    {
        SessionContext session = SessionContextFactory.CreateWithPart();
        ToolExecutionContext context = BuildContext(session);

        AgentToolResult result = _dispatcher.Execute(
            toolName,
            new Dictionary<string, object?>(),
            context);

        Assert.That(result.IsError, Is.False);
        Assert.That(result.ToolName, Is.EqualTo(toolName));
        Assert.That(result.OutputJson, Is.Not.Null.And.Not.Empty);
    }

    // --- Helper ---

    private static ToolExecutionContext BuildContext(SessionContext session)
    {
        return new ToolExecutionContext
        {
            SessionId = session.Session.RequestId,
            DocumentKey = session.Document?.Path ?? string.Empty,
            SessionContext = session
        };
    }
}
