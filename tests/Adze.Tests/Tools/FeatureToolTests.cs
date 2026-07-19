using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tools.Write;
using NUnit.Framework;

namespace Adze.Tests.Tools;

/// <summary>
/// Unit tests for the Phase 11 Wave 2 feature creation tools (extrude,
/// revolve). Apply paths require a live <c>ISldWorks</c> COM application and
/// are exercised by host validation, not unit tests — these cover Preview
/// behavior, input validation, verification, and undo-label formatting.
/// </summary>
[TestFixture]
public sealed class FeatureToolTests
{
    // --- create_extrude ---

    [Test]
    public void CreateExtrude_NoActiveDoc_SurfacesWarning()
    {
        var tool = new CreateExtrudeTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateMinimal(),
            new CreateExtrudeParameters { DepthMm = 10 });
        Assert.That(preview.Warnings, Has.Some.Contains("active document"));
    }

    [Test]
    public void CreateExtrude_DrawingDoc_SurfacesWarning()
    {
        var tool = new CreateExtrudeTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithDrawing(),
            new CreateExtrudeParameters { DepthMm = 10 });
        Assert.That(preview.Warnings, Has.Some.Contains("does not support extrusion"));
    }

    [Test]
    public void CreateExtrude_InvalidEndCondition_SurfacesWarning()
    {
        var tool = new CreateExtrudeTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateExtrudeParameters { DepthMm = 10, EndCondition = "up_to_vertex" });
        Assert.That(preview.Warnings, Has.Some.Contains("end_condition"));
    }

    [Test]
    public void CreateExtrude_NonPositiveDepth_SurfacesWarning()
    {
        var tool = new CreateExtrudeTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateExtrudeParameters { DepthMm = 0 });
        Assert.That(preview.Warnings, Has.Some.Contains("depth_mm"));
    }

    [Test]
    public void CreateExtrude_ThroughAll_IgnoresDepth()
    {
        var tool = new CreateExtrudeTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateExtrudeParameters { DepthMm = 0, EndCondition = "through_all", SketchName = "Sketch1" });

        Assert.That(preview.Warnings, Is.Empty);
        Assert.That(preview.Summary, Does.Contain("through all"));
    }

    [Test]
    public void CreateExtrude_PartWithSketch_BuildsPreview()
    {
        var tool = new CreateExtrudeTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateExtrudeParameters { SketchName = "Sketch1", DepthMm = 25, FeatureName = "Base" });

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.CreateExtrude));
        Assert.That(preview.Summary, Does.Contain("Sketch1"));
        Assert.That(preview.Summary, Does.Contain("25"));
        Assert.That(preview.Changes, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void CreateExtrude_Verify_ConfirmsFeatureInTree()
    {
        var context = SessionContextFactory.CreateWithPart();
        context.FeatureTree.Features.Add(new FeatureNode { Name = "Boss-Extrude1", Kind = "Extrusion" });

        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = { ["feature_name"] = "Boss-Extrude1" }
        };

        WriteVerification verification = new CreateExtrudeTool().Verify(context, applyResult);
        Assert.IsTrue(verification.ChangeConfirmed);
    }

    [Test]
    public void CreateExtrude_Verify_MissingFeature_Unconfirmed()
    {
        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = { ["feature_name"] = "Boss-Extrude1" }
        };

        WriteVerification verification = new CreateExtrudeTool().Verify(
            SessionContextFactory.CreateWithPart(), applyResult);
        Assert.IsFalse(verification.ChangeConfirmed);
    }

    // --- create_revolve ---

    [Test]
    public void CreateRevolve_NoAxis_SurfacesWarning()
    {
        var tool = new CreateRevolveTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateRevolveParameters { AxisName = string.Empty });
        Assert.That(preview.Warnings, Has.Some.Contains("axis_name"));
    }

    [Test]
    public void CreateRevolve_NoActiveDoc_SurfacesWarning()
    {
        var tool = new CreateRevolveTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateMinimal(),
            new CreateRevolveParameters { AxisName = "Axis1" });
        Assert.That(preview.Warnings, Has.Some.Contains("active document"));
    }

    [Test]
    public void CreateRevolve_InvalidAngle_SurfacesWarning()
    {
        var tool = new CreateRevolveTool();

        WritePreview tooLarge = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateRevolveParameters { AxisName = "Axis1", AngleDegrees = 400 });
        Assert.That(tooLarge.Warnings, Has.Some.Contains("angle_degrees"));

        WritePreview zero = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateRevolveParameters { AxisName = "Axis1", AngleDegrees = 0 });
        Assert.That(zero.Warnings, Has.Some.Contains("angle_degrees"));
    }

    [Test]
    public void CreateRevolve_PartWithAxis_BuildsPreview()
    {
        var tool = new CreateRevolveTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateRevolveParameters { SketchName = "Sketch1", AxisName = "Line1@Sketch1", AngleDegrees = 180 });

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.CreateRevolve));
        Assert.That(preview.Summary, Does.Contain("Sketch1"));
        Assert.That(preview.Summary, Does.Contain("Line1@Sketch1"));
        Assert.That(preview.Summary, Does.Contain("180"));
    }

    [Test]
    public void CreateRevolve_Verify_ConfirmsFeatureInTree()
    {
        var context = SessionContextFactory.CreateWithPart();
        context.FeatureTree.Features.Add(new FeatureNode { Name = "Revolve1", Kind = "Revolution" });

        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = { ["feature_name"] = "Revolve1" }
        };

        WriteVerification verification = new CreateRevolveTool().Verify(context, applyResult);
        Assert.IsTrue(verification.ChangeConfirmed);
    }

    [Test]
    public void BuildUndoLabel_FeatureTools_AreInformative()
    {
        Assert.That(new CreateExtrudeTool().BuildUndoLabel(new CreateExtrudeParameters { SketchName = "Sketch1" }),
            Does.Contain("Sketch1"));
        Assert.That(new CreateExtrudeTool().BuildUndoLabel(new CreateExtrudeParameters()),
            Does.Contain("active sketch"));
        Assert.That(new CreateRevolveTool().BuildUndoLabel(new CreateRevolveParameters { AxisName = "Axis1" }),
            Does.Contain("Axis1"));
    }
}
