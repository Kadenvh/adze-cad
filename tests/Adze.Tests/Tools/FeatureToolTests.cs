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

    // --- create_fillet ---

    [Test]
    public void CreateFillet_NoEntities_SurfacesWarning()
    {
        var tool = new CreateFilletTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateFilletParameters { RadiusMm = 2 });
        Assert.That(preview.Warnings, Has.Some.Contains("entity_names"));
    }

    [Test]
    public void CreateFillet_NonPositiveRadius_SurfacesWarning()
    {
        var tool = new CreateFilletTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateFilletParameters { EntityNames = { "Edge<1>" }, RadiusMm = 0 });
        Assert.That(preview.Warnings, Has.Some.Contains("radius_mm"));
    }

    [Test]
    public void CreateFillet_DrawingDoc_SurfacesWarning()
    {
        var tool = new CreateFilletTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithDrawing(),
            new CreateFilletParameters { EntityNames = { "Edge<1>" }, RadiusMm = 2 });
        Assert.That(preview.Warnings, Has.Some.Contains("does not support fillets"));
    }

    [Test]
    public void CreateFillet_PartWithEntities_BuildsPreview()
    {
        var tool = new CreateFilletTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateFilletParameters { EntityNames = { "Edge<1>", "Edge<2>" }, RadiusMm = 2.5 });

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.CreateFillet));
        Assert.That(preview.Summary, Does.Contain("2 entities"));
        Assert.That(preview.Summary, Does.Contain("2.5"));
        Assert.That(preview.Warnings, Is.Empty);
    }

    // --- create_chamfer ---

    [Test]
    public void CreateChamfer_NoEntities_SurfacesWarning()
    {
        var tool = new CreateChamferTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateChamferParameters { DistanceMm = 1 });
        Assert.That(preview.Warnings, Has.Some.Contains("entity_names"));
    }

    [Test]
    public void CreateChamfer_InvalidAngle_SurfacesWarning()
    {
        var tool = new CreateChamferTool();

        WritePreview tooLarge = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateChamferParameters { EntityNames = { "Edge<1>" }, DistanceMm = 1, AngleDegrees = 90 });
        Assert.That(tooLarge.Warnings, Has.Some.Contains("angle_degrees"));

        WritePreview zero = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateChamferParameters { EntityNames = { "Edge<1>" }, DistanceMm = 1, AngleDegrees = 0 });
        Assert.That(zero.Warnings, Has.Some.Contains("angle_degrees"));
    }

    [Test]
    public void CreateChamfer_PartWithEntities_BuildsPreview()
    {
        var tool = new CreateChamferTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateChamferParameters { EntityNames = { "Edge<1>" }, DistanceMm = 1.5, AngleDegrees = 30 });

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.CreateChamfer));
        Assert.That(preview.Summary, Does.Contain("1.5"));
        Assert.That(preview.Summary, Does.Contain("30"));
        Assert.That(preview.Warnings, Is.Empty);
    }

    // --- create_pattern_linear ---

    [Test]
    public void CreatePatternLinear_MissingInputs_SurfacesWarnings()
    {
        var tool = new CreatePatternLinearTool();

        WritePreview noFeature = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreatePatternLinearParameters { Direction1Reference = "Axis1" });
        Assert.That(noFeature.Warnings, Has.Some.Contains("feature_name"));

        WritePreview noDirection = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreatePatternLinearParameters { FeatureName = "Boss-Extrude1" });
        Assert.That(noDirection.Warnings, Has.Some.Contains("direction_1_reference"));
    }

    [Test]
    public void CreatePatternLinear_CountBelowTwo_SurfacesWarning()
    {
        var tool = new CreatePatternLinearTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreatePatternLinearParameters
            {
                FeatureName = "Boss-Extrude1",
                Direction1Reference = "Axis1",
                Count1 = 1
            });
        Assert.That(preview.Warnings, Has.Some.Contains("count_1"));
    }

    [Test]
    public void CreatePatternLinear_TwoDirectional_PreviewsTotalInstances()
    {
        var tool = new CreatePatternLinearTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreatePatternLinearParameters
            {
                FeatureName = "Boss-Extrude1",
                Direction1Reference = "Axis1",
                Count1 = 3,
                Spacing1Mm = 20,
                Direction2Reference = "Axis2",
                Count2 = 4,
                Spacing2Mm = 15
            });

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.CreatePatternLinear));
        Assert.That(preview.Warnings, Is.Empty);
        // 3 × 4 = 12 total instances shown in the changes list.
        Assert.That(preview.Changes, Has.Some.Matches<WriteChangeItem>(c => c.AfterValue == "12"));
    }

    // --- create_reference_plane ---

    [Test]
    public void CreateReferencePlane_MissingBase_SurfacesWarning()
    {
        var tool = new CreateReferencePlaneTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateReferencePlaneParameters { BaseReference = string.Empty });
        Assert.That(preview.Warnings, Has.Some.Contains("base_reference"));
    }

    [Test]
    public void CreateReferencePlane_NonPositiveOffset_SurfacesWarning()
    {
        var tool = new CreateReferencePlaneTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateReferencePlaneParameters { BaseReference = "Front Plane", OffsetMm = 0 });
        Assert.That(preview.Warnings, Has.Some.Contains("offset_mm"));
    }

    [Test]
    public void CreateReferencePlane_ValidOffset_BuildsPreview()
    {
        var tool = new CreateReferencePlaneTool();
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateWithPart(),
            new CreateReferencePlaneParameters { BaseReference = "Front Plane", OffsetMm = 25, Flip = true });

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.CreateReferencePlane));
        Assert.That(preview.Summary, Does.Contain("Front Plane"));
        Assert.That(preview.Summary, Does.Contain("25"));
        Assert.That(preview.Summary, Does.Contain("flipped"));
        Assert.That(preview.Warnings, Is.Empty);
    }

    [Test]
    public void CreateReferencePlane_Verify_ConfirmsFeatureInTree()
    {
        var context = SessionContextFactory.CreateWithPart();
        context.FeatureTree.Features.Add(new FeatureNode { Name = "Plane1", Kind = "RefPlane" });

        var applyResult = new WriteApplyResult
        {
            Success = true,
            AppliedValues = { ["feature_name"] = "Plane1" }
        };

        WriteVerification verification = new CreateReferencePlaneTool().Verify(context, applyResult);
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
        Assert.That(new CreateFilletTool().BuildUndoLabel(new CreateFilletParameters { RadiusMm = 3 }),
            Does.Contain("3"));
        Assert.That(new CreateChamferTool().BuildUndoLabel(new CreateChamferParameters { DistanceMm = 2, AngleDegrees = 45 }),
            Does.Contain("45"));
        Assert.That(new CreatePatternLinearTool().BuildUndoLabel(new CreatePatternLinearParameters { FeatureName = "Boss-Extrude1" }),
            Does.Contain("Boss-Extrude1"));
        Assert.That(new CreateReferencePlaneTool().BuildUndoLabel(new CreateReferencePlaneParameters { BaseReference = "Top Plane" }),
            Does.Contain("Top Plane"));
    }
}
