using System;
using System.Collections.Generic;
using System.Globalization;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Adds a 2D circle to a sketch. <c>RadiusMm</c> is the actual radius; SW's
/// <c>CreateCircleByRadius</c> takes meters internally. Coordinates are
/// sketch-plane local.
/// </summary>
public sealed class AddSketchCircleTool : IWriteTool<AddSketchCircleParameters>
{
    public WritePreview Preview(SessionContext context, AddSketchCircleParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.AddSketchCircle };

        if (parameters == null)
        {
            preview.Summary = "Add sketch circle: parameters missing.";
            preview.Warnings.Add("parameters required.");
            return preview;
        }

        if (parameters.RadiusMm <= 0)
        {
            preview.Summary = "Add sketch circle: radius must be positive.";
            preview.Warnings.Add("Radius must be > 0.");
            return preview;
        }

        string sketchLabel = string.IsNullOrWhiteSpace(parameters.SketchName)
            ? "(active sketch)"
            : parameters.SketchName!;

        preview.Summary = "Add circle in " + sketchLabel + " at ("
            + parameters.CenterXMm.ToString("0.###", CultureInfo.InvariantCulture) + ", "
            + parameters.CenterYMm.ToString("0.###", CultureInfo.InvariantCulture) + ") mm radius "
            + parameters.RadiusMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm";

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "Sketch circle in " + sketchLabel,
            BeforeValue = "(does not exist)",
            AfterValue = "radius " + parameters.RadiusMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm"
        });

        return preview;
    }

    public WriteApplyResult Apply(object application, AddSketchCircleParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "parameters required." };
            if (parameters.RadiusMm <= 0)
                return new WriteApplyResult { Success = false, ErrorMessage = "Radius must be > 0." };

            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "No active document." };

            SketchToolHelpers.EnsureSketchActive(modelDoc, parameters.SketchName);

            dynamic? circle = modelDoc.SketchManager.CreateCircleByRadius(
                parameters.CenterXMm / 1000.0,
                parameters.CenterYMm / 1000.0,
                0.0,
                parameters.RadiusMm / 1000.0);

            if (circle == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "CreateCircleByRadius returned null." };

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    ["radius_mm"] = parameters.RadiusMm.ToString("0.######", CultureInfo.InvariantCulture)
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Add sketch circle failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        return new WriteVerification
        {
            ChangeConfirmed = applyResult.Success,
            RebuildSucceeded = true
        };
    }

    public string BuildUndoLabel(AddSketchCircleParameters parameters)
    {
        return "Adze: add sketch circle";
    }
}
