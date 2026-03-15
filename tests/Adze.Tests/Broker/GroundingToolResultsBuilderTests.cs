using System.Collections.Generic;
using Adze.Broker.Formatting;
using Adze.Contracts.Models;
using NUnit.Framework;

namespace Adze.Tests.Broker;

[TestFixture]
public sealed class GroundingToolResultsBuilderTests
{
    [Test]
    public void Build_NullInput_ReturnsEmptyStateMessage()
    {
        string result = GroundingToolResultsBuilder.Build(null!);

        Assert.That(result, Does.Contain("Run the assistant"));
    }

    [Test]
    public void Build_EmptyList_ReturnsEmptyStateMessage()
    {
        string result = GroundingToolResultsBuilder.Build(new List<ToolResult>());

        Assert.That(result, Does.Contain("Run the assistant"));
    }

    [Test]
    public void Build_SingleSuccessfulTool_ShowsOkStatus()
    {
        var tools = new List<ToolResult>
        {
            CreateToolResult("get_active_document", true, "Document retrieved successfully")
        };

        string result = GroundingToolResultsBuilder.Build(tools);

        Assert.That(result, Does.Contain("get_active_document [ok]"));
        Assert.That(result, Does.Contain("summary: Document retrieved successfully"));
    }

    [Test]
    public void Build_SingleFailedTool_ShowsFailedStatus()
    {
        var tools = new List<ToolResult>
        {
            CreateToolResult("get_dimensions", false, "No document open")
        };

        string result = GroundingToolResultsBuilder.Build(tools);

        Assert.That(result, Does.Contain("get_dimensions [failed]"));
    }

    [Test]
    public void Build_ToolWithStringData_ShowsInlineValue()
    {
        var data = new Dictionary<string, object?>
        {
            { "fileName", "Part1.SLDPRT" }
        };
        var tools = new List<ToolResult>
        {
            CreateToolResult("get_active_document", true, "ok", data)
        };

        string result = GroundingToolResultsBuilder.Build(tools);

        Assert.That(result, Does.Contain("fileName: Part1.SLDPRT"));
    }

    [Test]
    public void Build_ToolWithNullData_ShowsNullMarker()
    {
        var data = new Dictionary<string, object?>
        {
            { "configName", null }
        };
        var tools = new List<ToolResult>
        {
            CreateToolResult("get_configurations", true, "ok", data)
        };

        string result = GroundingToolResultsBuilder.Build(tools);

        Assert.That(result, Does.Contain("configName: <null>"));
    }

    [Test]
    public void Build_ToolWithNestedDictionary_TruncatesAtFourItems()
    {
        var nested = new Dictionary<string, object>
        {
            { "a", "1" },
            { "b", "2" },
            { "c", "3" },
            { "d", "4" },
            { "e", "5" },
            { "f", "6" }
        };
        var data = new Dictionary<string, object?>
        {
            { "properties", nested }
        };
        var tools = new List<ToolResult>
        {
            CreateToolResult("get_custom_properties", true, "ok", data)
        };

        string result = GroundingToolResultsBuilder.Build(tools);

        Assert.That(result, Does.Contain("a: 1"));
        Assert.That(result, Does.Contain("b: 2"));
        Assert.That(result, Does.Contain("c: 3"));
        Assert.That(result, Does.Contain("d: 4"));
        Assert.That(result, Does.Not.Contain("e: 5"));
        Assert.That(result, Does.Not.Contain("f: 6"));
        Assert.That(result, Does.Contain("..."));
    }

    [Test]
    public void Build_ToolWithWarnings_ShowsWarningsSection()
    {
        var warnings = new List<string>
        {
            "Missing external reference",
            "Rebuild needed"
        };
        var tools = new List<ToolResult>
        {
            CreateToolResult("get_rebuild_diagnostics", true, "Issues found", warnings: warnings)
        };

        string result = GroundingToolResultsBuilder.Build(tools);

        Assert.That(result, Does.Contain("warnings:"));
        Assert.That(result, Does.Contain("Missing external reference"));
        Assert.That(result, Does.Contain("Rebuild needed"));
    }

    [Test]
    public void Build_MultipleTools_ShowsAllTools()
    {
        var tools = new List<ToolResult>
        {
            CreateToolResult("get_active_document", true, "Document found"),
            CreateToolResult("get_dimensions", true, "Dimensions retrieved")
        };

        string result = GroundingToolResultsBuilder.Build(tools);

        Assert.That(result, Does.Contain("get_active_document [ok]"));
        Assert.That(result, Does.Contain("get_dimensions [ok]"));
    }

    [Test]
    public void Build_DataLimitedToSixEntries()
    {
        var data = new Dictionary<string, object?>
        {
            { "key1", "val1" },
            { "key2", "val2" },
            { "key3", "val3" },
            { "key4", "val4" },
            { "key5", "val5" },
            { "key6", "val6" },
            { "key7", "val7" },
            { "key8", "val8" }
        };
        var tools = new List<ToolResult>
        {
            CreateToolResult("get_custom_properties", true, "ok", data)
        };

        string result = GroundingToolResultsBuilder.Build(tools);

        Assert.That(result, Does.Contain("key1: val1"));
        Assert.That(result, Does.Contain("key6: val6"));
        Assert.That(result, Does.Not.Contain("key7: val7"));
        Assert.That(result, Does.Not.Contain("key8: val8"));
    }

    private static ToolResult CreateToolResult(string name, bool success, string summary,
        Dictionary<string, object?>? data = null, List<string>? warnings = null)
    {
        return new ToolResult
        {
            ToolName = name,
            Success = success,
            Summary = summary,
            Data = data ?? new Dictionary<string, object?>(),
            Warnings = warnings ?? new List<string>()
        };
    }
}
