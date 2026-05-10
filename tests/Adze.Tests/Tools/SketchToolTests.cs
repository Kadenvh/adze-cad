using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tools.Write;
using NUnit.Framework;

namespace Adze.Tests.Tools;

/// <summary>
/// Unit tests for the four Phase 11 sketch authoring tools. Apply paths
/// require a live <c>ISldWorks</c> COM application and are exercised by
/// host validation, not unit tests — these cover Preview behavior, input
/// validation, and undo-label formatting.
/// </summary>
[TestFixture]
public sealed class SketchToolTests
{
    [Test]
    public void CreateSketch_NoReference_SurfacesWarning()
    {
        var tool = new CreateSketchTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateSketchParameters { ReferenceName = string.Empty });
        Assert.That(preview.Warnings, Has.Some.Contains("reference_name"));
    }

    [Test]
    public void CreateSketch_NoActiveDoc_SurfacesWarning()
    {
        var tool = new CreateSketchTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateMinimal(),
            new CreateSketchParameters { ReferenceName = "Front Plane" });
        Assert.That(preview.Warnings, Has.Some.Contains("active document"));
    }

    [Test]
    public void CreateSketch_DrawingDoc_SurfacesWarning()
    {
        var tool = new CreateSketchTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithDrawing(),
            new CreateSketchParameters { ReferenceName = "Front Plane" });
        Assert.That(preview.Warnings, Has.Some.Contains("does not support sketches"));
    }

    [Test]
    public void CreateSketch_PartWithReference_BuildsPreview()
    {
        var tool = new CreateSketchTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateSketchParameters { ReferenceName = "Front Plane", SketchName = "BaseSketch" });

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.CreateSketch));
        Assert.That(preview.Summary, Does.Contain("Front Plane"));
        Assert.That(preview.Summary, Does.Contain("BaseSketch"));
        Assert.That(preview.Changes, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void AddSketchLine_ZeroLength_SurfacesWarning()
    {
        var tool = new AddSketchLineTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new AddSketchLineParameters { StartXMm = 5, StartYMm = 5, EndXMm = 5, EndYMm = 5 });
        Assert.That(preview.Warnings, Has.Some.Contains("zero length"));
    }

    [Test]
    public void AddSketchLine_BuildsPreviewWithLength()
    {
        var tool = new AddSketchLineTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new AddSketchLineParameters
            {
                StartXMm = 0, StartYMm = 0,
                EndXMm = 30, EndYMm = 40,
                SketchName = "Sketch1"
            });

        Assert.That(preview.Summary, Does.Contain("Sketch1"));
        Assert.That(preview.Summary, Does.Contain("50")); // hypotenuse 30-40-50
    }

    [Test]
    public void AddSketchCircle_NonPositiveRadius_SurfacesWarning()
    {
        var tool = new AddSketchCircleTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new AddSketchCircleParameters { CenterXMm = 0, CenterYMm = 0, RadiusMm = 0 });
        Assert.That(preview.Warnings, Has.Some.Contains("Radius"));
    }

    [Test]
    public void AddSketchCircle_BuildsPreview()
    {
        var tool = new AddSketchCircleTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new AddSketchCircleParameters
            {
                CenterXMm = 10, CenterYMm = 20, RadiusMm = 5
            });

        Assert.That(preview.Summary, Does.Contain("10"));
        Assert.That(preview.Summary, Does.Contain("20"));
        Assert.That(preview.Summary, Does.Contain("5"));
    }

    [Test]
    public void AddSketchDimension_NoEntity_SurfacesWarning()
    {
        var tool = new AddSketchDimensionTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new AddSketchDimensionParameters());
        Assert.That(preview.Warnings, Has.Some.Contains("entity_name"));
    }

    [Test]
    public void AddSketchDimension_BuildsPreviewWithUnits()
    {
        var tool = new AddSketchDimensionTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new AddSketchDimensionParameters
            {
                EntityName = "Line1",
                DimensionType = "length",
                TargetValue = 25.0
            });

        Assert.That(preview.Summary, Does.Contain("Line1"));
        Assert.That(preview.Summary, Does.Contain("25"));
        Assert.That(preview.Summary, Does.Contain("mm"));
    }

    [Test]
    public void AddSketchDimension_AnglePreviewUsesDegrees()
    {
        var tool = new AddSketchDimensionTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new AddSketchDimensionParameters
            {
                EntityName = "Line1",
                DimensionType = "angle",
                TargetValue = 45
            });

        Assert.That(preview.Summary, Does.Contain("deg"));
    }

    [Test]
    public void BuildUndoLabel_AllSketchTools_AreInformative()
    {
        Assert.That(new CreateSketchTool().BuildUndoLabel(new CreateSketchParameters { ReferenceName = "Top Plane" }),
            Does.Contain("Top Plane"));
        Assert.That(new AddSketchLineTool().BuildUndoLabel(new AddSketchLineParameters()),
            Does.Contain("line"));
        Assert.That(new AddSketchCircleTool().BuildUndoLabel(new AddSketchCircleParameters()),
            Does.Contain("circle"));
        Assert.That(new AddSketchDimensionTool().BuildUndoLabel(new AddSketchDimensionParameters { EntityName = "Arc1" }),
            Does.Contain("Arc1"));
    }
}
