using System.Collections.Generic;
using System.Linq;
using Adze.Broker.Formatting;
using Adze.Broker.Models;
using Adze.Contracts.Models;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class GroundingAnswerBuilderTests
{
    [Test]
    public void Build_HostUnavailable_ShowsBlockersAndRecovery()
    {
        GroundingExecutionReport report = CreateReport(
            turnStatus: "host_unavailable",
            blockers: new List<string> { "SOLIDWORKS is not running" },
            recovery: new List<string> { "Launch SOLIDWORKS and try again" });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Current blockers"));
        Assert.That(result, Does.Contain("What to do next"));
        Assert.That(result, Does.Contain("SOLIDWORKS is not running"));
        Assert.That(result, Does.Contain("Launch SOLIDWORKS and try again"));
    }

    [Test]
    public void Build_NeedsDocument_ShowsBlockersAndRecovery()
    {
        GroundingExecutionReport report = CreateReport(
            turnStatus: "needs_document",
            blockers: new List<string> { "No document is open" },
            recovery: new List<string> { "Open a part or assembly" });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Current blockers"));
        Assert.That(result, Does.Contain("What to do next"));
        Assert.That(result, Does.Contain("No document is open"));
        Assert.That(result, Does.Contain("Open a part or assembly"));
    }

    [Test]
    public void Build_NoSuccessfulTools_ShowsCannotGroundMessage()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_active_document", false, "Failed to retrieve document")
            });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("could not ground"));
    }

    [Test]
    public void Build_NoSuccessfulTools_ShowsBlockedToolNames()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_active_document", false, "Failed"),
                CreateToolResult("get_feature_tree_slice", false, "Failed")
            });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Blocked tools:"));
        Assert.That(result, Does.Contain("get_active_document"));
        Assert.That(result, Does.Contain("get_feature_tree_slice"));
    }

    [Test]
    public void Build_NoSuccessfulTools_ShowsRecoverySuggestions()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_active_document", false, "Failed")
            },
            recovery: new List<string> { "Open a document first" });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Recovery suggestions"));
        Assert.That(result, Does.Contain("Open a document first"));
    }

    [Test]
    public void Build_SuccessfulTools_ShowsGroundedFindings()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_active_document", true, "Part1.SLDPRT is active"),
                CreateToolResult("get_feature_tree_slice", true, "9 features found")
            });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Grounded findings:"));
        Assert.That(result, Does.Contain("Part1.SLDPRT is active"));
        Assert.That(result, Does.Contain("9 features found"));
    }

    [Test]
    public void Build_SuccessfulTools_LimitsToFourSummaries()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("tool_1", true, "Summary 1"),
                CreateToolResult("tool_2", true, "Summary 2"),
                CreateToolResult("tool_3", true, "Summary 3"),
                CreateToolResult("tool_4", true, "Summary 4"),
                CreateToolResult("tool_5", true, "Summary 5"),
                CreateToolResult("tool_6", true, "Summary 6")
            });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Summary 1"));
        Assert.That(result, Does.Contain("Summary 4"));
        Assert.That(result, Does.Contain("Additional grounded results: 2"));
        Assert.That(result, Does.Not.Contain("- Summary 5"));
        Assert.That(result, Does.Not.Contain("- Summary 6"));
    }

    [Test]
    public void Build_MixedResults_ShowsUnavailableTools()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_active_document", true, "Part1 is active"),
                CreateToolResult("get_dimensions", true, "5 dimensions found"),
                CreateToolResult("get_mates", false, "Not an assembly")
            });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Unavailable during this run:"));
        Assert.That(result, Does.Contain("get_mates"));
    }

    [Test]
    public void Build_WithBlockers_ShowsCurrentBlockers()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_active_document", true, "Part1 is active")
            },
            blockers: new List<string> { "Rebuild errors detected" });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Current blockers"));
        Assert.That(result, Does.Contain("Rebuild errors detected"));
    }

    [Test]
    public void Build_WithWarnings_ShowsWarnings()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_rebuild_diagnostics", true, "Diagnostics collected",
                    warnings: new List<string> { "Missing reference: Part2.SLDPRT" })
            });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Warnings:"));
        Assert.That(result, Does.Contain("Missing reference: Part2.SLDPRT"));
    }

    [Test]
    public void Build_WithAssistantMessage_ShowsMessageBeforeFindings()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_active_document", true, "Part1 is active")
            },
            assistantMessage: "Here is what I found in your model.");

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Here is what I found in your model."));
        int messageIndex = result.IndexOf("Here is what I found in your model.");
        int findingsIndex = result.IndexOf("Grounded findings:");
        Assert.That(messageIndex, Is.LessThan(findingsIndex));
    }

    [Test]
    public void Build_ShowsRequestInHeader()
    {
        GroundingExecutionReport report = CreateReport(
            request: "How many features are in this part?",
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_feature_tree_slice", true, "9 features")
            });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.StartWith("Assistant response"));
        Assert.That(result, Does.Contain("How many features are in this part?"));
    }

    [Test]
    public void Build_WithRecoveryAndFollowUp_ShowsSuggestedSections()
    {
        GroundingExecutionReport report = CreateReport(
            toolResults: new List<ToolResult>
            {
                CreateToolResult("get_active_document", true, "Part1 is active")
            },
            recovery: new List<string> { "Check rebuild status" },
            nextQuestions: new List<string> { "What dimensions are applied?", "Are there any mates?" });

        string result = GroundingAnswerBuilder.Build(report);

        Assert.That(result, Does.Contain("Suggested next step"));
        Assert.That(result, Does.Contain("Check rebuild status"));
        Assert.That(result, Does.Contain("Suggested follow-up"));
        Assert.That(result, Does.Contain("What dimensions are applied?"));
    }

    private static GroundingExecutionReport CreateReport(
        string turnStatus = "ready",
        string request = "test request",
        List<ToolResult>? toolResults = null,
        List<string>? blockers = null,
        List<string>? recovery = null,
        List<string>? nextQuestions = null,
        string assistantMessage = "")
    {
        var response = new BrokerResponse
        {
            TurnStatus = turnStatus,
            AssistantMessage = assistantMessage,
            Blockers = blockers ?? new List<string>(),
            RecoverySuggestions = recovery ?? new List<string>(),
            NextQuestions = nextQuestions ?? new List<string>()
        };
        var results = toolResults ?? new List<ToolResult>();
        return new GroundingExecutionReport
        {
            Request = request,
            Response = response,
            ToolResults = results,
            SuccessfulToolCount = results.Count(r => r.Success),
            FailedToolCount = results.Count(r => !r.Success)
        };
    }

    private static ToolResult CreateToolResult(string name, bool success, string summary, List<string>? warnings = null)
    {
        return new ToolResult
        {
            ToolName = name,
            Success = success,
            Summary = summary,
            Warnings = warnings ?? new List<string>()
        };
    }
}
