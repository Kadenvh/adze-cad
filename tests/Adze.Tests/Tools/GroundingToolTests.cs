using System;
using System.Collections.Generic;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tools;
using Adze.Tools.Grounding;
using NUnit.Framework;

namespace Adze.Tests.Tools;

[TestFixture]
public sealed class GroundingToolTests
{
    private GroundingToolCatalog _catalog = null!;

    [SetUp]
    public void SetUp()
    {
        _catalog = ToolCatalog.CreateGroundingCatalog();
    }

    [Test]
    public void GetActiveDocument_WithDocument_ReturnsSuccess()
    {
        SessionContext context = SessionContextFactory.CreateWithPart("MyPart");

        ToolResult result = _catalog.ActiveDocument.Execute(context, new EmptyParameters());

        Assert.That(result.Success, Is.True);
        Assert.That(result.ToolName, Is.EqualTo(ToolNames.GetActiveDocument));
        Assert.That(result.Data["type"], Is.EqualTo("part"));
        Assert.That(result.Data["title"], Is.EqualTo("MyPart"));
    }

    [Test]
    public void GetActiveDocument_NoDocument_ReturnsFailure()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = _catalog.ActiveDocument.Execute(context, new EmptyParameters());

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("No active document"));
    }

    [Test]
    public void GetDocumentSummary_WithDocument_IncludesBasicFields()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.DocumentSummary.Execute(context, new GetDocumentSummaryParameters());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data.ContainsKey("title"), Is.True);
        Assert.That(result.Data.ContainsKey("type"), Is.True);
        Assert.That(result.Data.ContainsKey("units"), Is.True);
    }

    [Test]
    public void GetDocumentSummary_IncludeProperties_AddsProperties()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.DocumentSummary.Execute(context, new GetDocumentSummaryParameters { IncludeProperties = true });

        Assert.That(result.Data.ContainsKey("properties"), Is.True);
    }

    [Test]
    public void GetDocumentSummary_ExcludeProperties_OmitsProperties()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.DocumentSummary.Execute(context, new GetDocumentSummaryParameters { IncludeProperties = false });

        Assert.That(result.Data.ContainsKey("properties"), Is.False);
    }

    [Test]
    public void GetDocumentSummary_IncludeDiagnostics_AddsDiagnosticFields()
    {
        SessionContext context = SessionContextFactory.CreateWithDiagnosticIssues();

        ToolResult result = _catalog.DocumentSummary.Execute(context, new GetDocumentSummaryParameters { IncludeDiagnostics = true });

        Assert.That(result.Data.ContainsKey("rebuild_state"), Is.True);
        Assert.That(result.Data.ContainsKey("warnings"), Is.True);
        Assert.That(result.Data.ContainsKey("missing_references"), Is.True);
    }

    [Test]
    public void GetDocumentSummary_NoDocument_ReturnsFailure()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = _catalog.DocumentSummary.Execute(context, new GetDocumentSummaryParameters());

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void GetSelectionContext_WithSelection_ReturnsItems()
    {
        SessionContext context = SessionContextFactory.CreateWithSelection();

        ToolResult result = _catalog.SelectionContext.Execute(context, new GetSelectionContextParameters { IncludeEntityDetails = true });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["count"], Is.EqualTo(2));
        Assert.That(result.Data.ContainsKey("items"), Is.True);
    }

    [Test]
    public void GetSelectionContext_NoSelection_ReturnsZeroCount()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.SelectionContext.Execute(context, new GetSelectionContextParameters());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["count"], Is.EqualTo(0));
        Assert.That(result.Summary, Does.Contain("No current selection"));
    }

    [Test]
    public void GetSelectionContext_NoDocument_StillSucceeds()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = _catalog.SelectionContext.Execute(context, new GetSelectionContextParameters());

        Assert.That(result.Success, Is.True);
    }

    [Test]
    public void GetFeatureTreeSlice_WithFeatures_ReturnsSlice()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();

        ToolResult result = _catalog.FeatureTreeSlice.Execute(context, new GetFeatureTreeSliceParameters { Radius = 2 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["available_count"], Is.EqualTo(9));
        int returnedCount = (int)result.Data["returned_count"]!;
        Assert.That(returnedCount, Is.GreaterThan(0));
        Assert.That(returnedCount, Is.LessThanOrEqualTo(9));
    }

    [Test]
    public void GetFeatureTreeSlice_AnchorFound_CentersSlice()
    {
        SessionContext context = SessionContextFactory.CreateWithFeatures();

        ToolResult result = _catalog.FeatureTreeSlice.Execute(context, new GetFeatureTreeSliceParameters
        {
            AnchorName = "Boss-Extrude1",
            Radius = 1
        });

        Assert.That(result.Success, Is.True);
        var items = result.Data["items"] as List<Dictionary<string, object?>>;
        Assert.That(items, Is.Not.Null);
        Assert.That(items!.Exists(item => (string?)item["name"] == "Boss-Extrude1"), Is.True);
    }

    [Test]
    public void GetFeatureTreeSlice_NoDocument_ReturnsFailure()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = _catalog.FeatureTreeSlice.Execute(context, new GetFeatureTreeSliceParameters());

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void GetDimensions_WithDimensions_ReturnsList()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();

        ToolResult result = _catalog.Dimensions.Execute(context, new GetDimensionsParameters { Scope = "document" });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["total_count"], Is.EqualTo(3));
        Assert.That(result.Data["returned_count"], Is.EqualTo(3));
    }

    [Test]
    public void GetDimensions_SelectionScope_NoSelection_ReturnsEmpty()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();

        ToolResult result = _catalog.Dimensions.Execute(context, new GetDimensionsParameters { Scope = "selection" });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["returned_count"], Is.EqualTo(0));
    }

    [Test]
    public void GetDimensions_Pagination_OffsetAndLimit()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();

        ToolResult result = _catalog.Dimensions.Execute(context, new GetDimensionsParameters
        {
            Scope = "document",
            Offset = 1,
            Limit = 1
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["total_count"], Is.EqualTo(3));
        Assert.That(result.Data["returned_count"], Is.EqualTo(1));
        Assert.That(result.Data["offset"], Is.EqualTo(1));
        Assert.That(result.Data["has_more"], Is.True);
    }

    [Test]
    public void GetDimensions_Pagination_LastPage()
    {
        SessionContext context = SessionContextFactory.CreateWithDimensions();

        ToolResult result = _catalog.Dimensions.Execute(context, new GetDimensionsParameters
        {
            Scope = "document",
            Offset = 2,
            Limit = 50
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["returned_count"], Is.EqualTo(1));
        Assert.That(result.Data["has_more"], Is.False);
    }

    [Test]
    public void GetDimensions_NoDocument_ReturnsFailure()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = _catalog.Dimensions.Execute(context, new GetDimensionsParameters());

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void GetConfigurations_WithConfigurations_ReturnsList()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.Configurations.Execute(context, new GetConfigurationsParameters());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["active_name"], Is.EqualTo("Default"));
    }

    [Test]
    public void GetConfigurations_WithSuppressionState_IncludesState()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.Configurations.Execute(context, new GetConfigurationsParameters { IncludeSuppressionState = true });

        var items = result.Data["items"] as List<Dictionary<string, object?>>;
        Assert.That(items, Is.Not.Null);
        Assert.That(items![0].ContainsKey("state"), Is.True);
    }

    [Test]
    public void GetConfigurations_NoDocument_ReturnsFailure()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = _catalog.Configurations.Execute(context, new GetConfigurationsParameters());

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void GetCustomProperties_BothScope_ReturnsDocumentAndConfig()
    {
        SessionContext context = SessionContextFactory.CreateWithCustomProperties();

        ToolResult result = _catalog.CustomProperties.Execute(context, new GetCustomPropertiesParameters { Scope = "both" });

        Assert.That(result.Success, Is.True);
        Assert.That((int)result.Data["document_count"]!, Is.GreaterThan(0));
        Assert.That((int)result.Data["configuration_count"]!, Is.GreaterThan(0));
    }

    [Test]
    public void GetCustomProperties_DocumentScope_OnlyDocumentProperties()
    {
        SessionContext context = SessionContextFactory.CreateWithCustomProperties();

        ToolResult result = _catalog.CustomProperties.Execute(context, new GetCustomPropertiesParameters { Scope = "document" });

        Assert.That((int)result.Data["document_count"]!, Is.GreaterThan(0));
        Assert.That((int)result.Data["configuration_count"]!, Is.EqualTo(0));
    }

    [Test]
    public void GetCustomProperties_NoProperties_ReportsEmpty()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.CustomProperties.Execute(context, new GetCustomPropertiesParameters());

        Assert.That(result.Summary, Does.Contain("No custom properties"));
    }

    [Test]
    public void GetMates_Pagination_OffsetAndLimit()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();

        ToolResult result = _catalog.Mates.Execute(context, new GetMatesParameters
        {
            Scope = "document",
            Offset = 1,
            Limit = 1
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["total_count"], Is.EqualTo(2));
        Assert.That(result.Data["returned_count"], Is.EqualTo(1));
        Assert.That(result.Data["offset"], Is.EqualTo(1));
        Assert.That(result.Data["has_more"], Is.False);
    }

    [Test]
    public void GetMates_AssemblyWithMates_ReturnsList()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();

        ToolResult result = _catalog.Mates.Execute(context, new GetMatesParameters { Scope = "document", Limit = 50 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["total_count"], Is.EqualTo(2));
        Assert.That(result.Data["returned_count"], Is.EqualTo(2));
    }

    [Test]
    public void GetMates_PartDocument_ReturnsNotApplicable()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.Mates.Execute(context, new GetMatesParameters());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Summary, Does.Contain("only available on assembly"));
    }

    [Test]
    public void GetMates_NoDocument_ReturnsFailure()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = _catalog.Mates.Execute(context, new GetMatesParameters());

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void GetRebuildDiagnostics_WithWarnings_ReturnsWarnings()
    {
        SessionContext context = SessionContextFactory.CreateWithDiagnosticIssues();

        ToolResult result = _catalog.RebuildDiagnostics.Execute(context, new GetRebuildDiagnosticsParameters
        {
            IncludeWarnings = true,
            IncludeMissingReferences = true
        });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data.ContainsKey("warnings"), Is.True);
        Assert.That(result.Data.ContainsKey("missing_references"), Is.True);
    }

    [Test]
    public void GetRebuildDiagnostics_ExcludeWarnings_OmitsWarnings()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.RebuildDiagnostics.Execute(context, new GetRebuildDiagnosticsParameters
        {
            IncludeWarnings = false,
            IncludeMissingReferences = false
        });

        Assert.That(result.Data.ContainsKey("warnings"), Is.False);
        Assert.That(result.Data.ContainsKey("missing_references"), Is.False);
    }

    [Test]
    public void GetReferenceGraph_DirectDepth_ReturnsDirectItems()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();

        ToolResult result = _catalog.ReferenceGraph.Execute(context, new GetReferenceGraphParameters { Depth = 1 });

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["scope"], Is.EqualTo("direct"));
        Assert.That(result.Data["returned_count"], Is.EqualTo(2));
    }

    [Test]
    public void GetReferenceGraph_TransitiveDepth_ReturnsTransitiveItems()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        context.ReferenceGraph.TransitiveItems = new List<ReferenceNode>
        {
            new ReferenceNode { Name = "Sub1.SLDPRT", Path = @"C:\test\Sub1.SLDPRT", ExistsOnDisk = true },
            new ReferenceNode { Name = "Sub2.SLDPRT", Path = @"C:\test\Sub2.SLDPRT", ExistsOnDisk = true },
            new ReferenceNode { Name = "Sub3.SLDPRT", Path = @"C:\test\Sub3.SLDPRT", ExistsOnDisk = true }
        };

        ToolResult result = _catalog.ReferenceGraph.Execute(context, new GetReferenceGraphParameters { Depth = 2 });

        Assert.That(result.Data["scope"], Is.EqualTo("transitive"));
        Assert.That(result.Data["returned_count"], Is.EqualTo(3));
    }

    [Test]
    public void GetReferenceGraph_IncludeExternalReferences_AddsImportedPath()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        context.ReferenceGraph.DirectItems[0].ImportedPath = @"\\server\Part1.SLDPRT";

        ToolResult result = _catalog.ReferenceGraph.Execute(context, new GetReferenceGraphParameters
        {
            Depth = 1,
            IncludeExternalReferences = true
        });

        var items = result.Data["items"] as List<Dictionary<string, object?>>;
        Assert.That(items![0].ContainsKey("imported_path"), Is.True);
        Assert.That(result.Data["has_imported_paths"], Is.EqualTo(true));
    }

    [Test]
    public void GetReferenceGraph_NoDocument_ReturnsFailure()
    {
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = _catalog.ReferenceGraph.Execute(context, new GetReferenceGraphParameters());

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void AllTools_HaveCorrectToolNames()
    {
        Assert.That(_catalog.ActiveDocument.ToolName, Is.EqualTo(ToolNames.GetActiveDocument));
        Assert.That(_catalog.DocumentSummary.ToolName, Is.EqualTo(ToolNames.GetDocumentSummary));
        Assert.That(_catalog.SelectionContext.ToolName, Is.EqualTo(ToolNames.GetSelectionContext));
        Assert.That(_catalog.FeatureTreeSlice.ToolName, Is.EqualTo(ToolNames.GetFeatureTreeSlice));
        Assert.That(_catalog.Dimensions.ToolName, Is.EqualTo(ToolNames.GetDimensions));
        Assert.That(_catalog.Configurations.ToolName, Is.EqualTo(ToolNames.GetConfigurations));
        Assert.That(_catalog.CustomProperties.ToolName, Is.EqualTo(ToolNames.GetCustomProperties));
        Assert.That(_catalog.Mates.ToolName, Is.EqualTo(ToolNames.GetMates));
        Assert.That(_catalog.RebuildDiagnostics.ToolName, Is.EqualTo(ToolNames.GetRebuildDiagnostics));
        Assert.That(_catalog.ReferenceGraph.ToolName, Is.EqualTo(ToolNames.GetReferenceGraph));
    }

    [Test]
    public void GetMates_SelectionScope_WithMatchingSelection_FiltersCorrectly()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        context.Selection = new SelectionInfo
        {
            Count = 1,
            Items = new List<SelectionItem>
            {
                new SelectionItem { Kind = "Component", Name = "Part1-1", Owner = "TestAssembly" }
            }
        };

        ToolResult result = _catalog.Mates.Execute(context, new GetMatesParameters { Scope = "selection", Limit = 50 });

        Assert.That(result.Success, Is.True);
        Assert.That((int)result.Data["returned_count"]!, Is.GreaterThan(0));
    }

    [Test]
    public void GetFeatureTreeSlice_EmptyFeatureTree_ReturnsEmptyList()
    {
        SessionContext context = SessionContextFactory.CreateWithPart();

        ToolResult result = _catalog.FeatureTreeSlice.Execute(context, new GetFeatureTreeSliceParameters());

        Assert.That(result.Success, Is.True);
        Assert.That(result.Data["available_count"], Is.EqualTo(0));
    }

    [Test]
    public void GetReferenceGraph_LimitClipsResults()
    {
        SessionContext context = SessionContextFactory.CreateWithAssembly();
        context.ReferenceGraph.TransitiveItems = new List<ReferenceNode>();
        for (int i = 0; i < 200; i++)
            context.ReferenceGraph.TransitiveItems.Add(new ReferenceNode { Name = "Part" + i + ".SLDPRT", Path = @"C:\test\Part" + i + ".SLDPRT", ExistsOnDisk = true });

        ToolResult result = _catalog.ReferenceGraph.Execute(context, new GetReferenceGraphParameters { Depth = 2, Limit = 10 });

        Assert.That(result.Data["returned_count"], Is.EqualTo(10));
        var items = result.Data["items"] as List<Dictionary<string, object?>>;
        Assert.That(items, Is.Not.Null);
        Assert.That(items!.Count, Is.EqualTo(10));
    }

    [Test]
    public void GetReferenceGraph_DefaultLimit_Is100()
    {
        var parameters = new GetReferenceGraphParameters();
        Assert.That(parameters.Limit, Is.EqualTo(100));
    }
}
