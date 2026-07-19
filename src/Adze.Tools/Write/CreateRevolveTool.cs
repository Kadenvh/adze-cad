using System;
using System.Collections.Generic;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Revolves a sketch profile around an axis into a solid. Second Phase 11
/// Wave 2 feature creation primitive. Class 3 (HardWriteAdvanced): creates
/// a solid body and triggers a rebuild. The axis must be named explicitly —
/// a reference axis ('Axis1') or a sketch line ('Line1@Sketch1') — because
/// SW cannot infer the axis of revolution.
/// </summary>
public sealed class CreateRevolveTool : IWriteTool<CreateRevolveParameters>
{
    public WritePreview Preview(SessionContext context, CreateRevolveParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.CreateRevolve };

        if (parameters == null || string.IsNullOrWhiteSpace(parameters.AxisName))
        {
            preview.Summary = "Create revolve: axis_name is required.";
            preview.Warnings.Add("axis_name is required — a reference axis ('Axis1') or sketch line ('Line1@Sketch1').");
            return preview;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            preview.Summary = "Create revolve: no active document.";
            preview.Warnings.Add("No active document.");
            return preview;
        }

        if (!string.Equals(context.Document.Type, "Part", StringComparison.OrdinalIgnoreCase))
        {
            preview.Summary = "Create revolve: only part documents support revolved features.";
            preview.Warnings.Add("Document type '" + context.Document.Type + "' does not support revolution.");
            return preview;
        }

        if (parameters.AngleDegrees <= 0 || parameters.AngleDegrees > 360)
        {
            preview.Summary = "Create revolve: angle must be in (0, 360].";
            preview.Warnings.Add("angle_degrees must be greater than 0 and at most 360.");
            return preview;
        }

        string sketchLabel = string.IsNullOrWhiteSpace(parameters.SketchName) ? "active sketch" : "'" + parameters.SketchName + "'";
        preview.Summary = "Revolve " + sketchLabel + " around '" + parameters.AxisName + "' by "
            + parameters.AngleDegrees.ToString("0.###") + "°";

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "New feature",
            BeforeValue = "(none)",
            AfterValue = string.IsNullOrWhiteSpace(parameters.FeatureName) ? "Revolve (SW default name)" : parameters.FeatureName!
        });
        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "Profile",
            BeforeValue = sketchLabel,
            AfterValue = "consumed by revolution around " + parameters.AxisName
        });

        if (context.Document.IsReadOnly)
        {
            preview.Warnings.Add("Document is read-only — revolution will fail.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreateRevolveParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null || string.IsNullOrWhiteSpace(parameters.AxisName))
                return new WriteApplyResult { Success = false, ErrorMessage = "axis_name is required." };
            if (parameters.AngleDegrees <= 0 || parameters.AngleDegrees > 360)
                return new WriteApplyResult { Success = false, ErrorMessage = "angle_degrees must be in (0, 360]." };

            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "No active document." };

            string? sketchName = FeatureToolHelpers.ResolveProfileSketch(modelDoc, parameters.SketchName);
            if (string.IsNullOrWhiteSpace(sketchName))
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "No sketch specified and no active sketch found. Create a sketch first or pass sketch_name."
                };
            }

            modelDoc.ClearSelection2(true);
            bool profileSelected = false;
            try
            {
                profileSelected = (bool)modelDoc.Extension.SelectByID2(sketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
            }
            catch
            {
            }
            if (!profileSelected)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select sketch '" + sketchName + "'."
                };
            }

            // The axis of revolution carries selection mark 16. Try reference
            // axis, then sketch segment (centerline / line inside a sketch).
            bool axisSelected = TrySelectAxis(modelDoc, parameters.AxisName);
            if (!axisSelected)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select axis '" + parameters.AxisName
                        + "'. Use a reference axis name ('Axis1') or a sketch line ('Line1@Sketch1')."
                };
            }

            double angleRadians = parameters.AngleDegrees * Math.PI / 180.0;

            dynamic? feature = modelDoc.FeatureManager.FeatureRevolve2(
                true,                       // single direction
                true,                       // solid
                false,                      // thin
                false,                      // cut
                false,                      // reverse direction
                false,                      // both directions up to same entity
                0, 0,                       // end condition dir1, dir2 (blind)
                angleRadians, 0.0,          // angle dir1, dir2 (radians)
                false, false,               // offset reverse dir1, dir2
                0.0, 0.0,                   // offset distance dir1, dir2
                0,                          // thin type
                0.0, 0.0,                   // thin thickness dir1, dir2
                true,                       // merge result
                true,                       // use feature scope
                true);                      // auto-select

            if (feature == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "FeatureRevolve2 returned null. The profile may cross the axis, be open, or be empty."
                };
            }

            string actualName = FeatureToolHelpers.FinalizeFeatureName(feature, parameters.FeatureName);

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    ["feature_name"] = actualName,
                    ["sketch"] = sketchName!,
                    ["axis"] = parameters.AxisName,
                    ["angle_degrees"] = parameters.AngleDegrees.ToString("0.###")
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Create revolve failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        return FeatureToolHelpers.VerifyFeatureInTree(refreshedContext, applyResult, "Revolve");
    }

    public string BuildUndoLabel(CreateRevolveParameters parameters)
    {
        string sketch = string.IsNullOrWhiteSpace(parameters?.SketchName) ? "active sketch" : parameters!.SketchName!;
        return "Adze: revolve " + sketch + " around " + (parameters?.AxisName ?? "(axis)");
    }

    private static bool TrySelectAxis(dynamic modelDoc, string axisName)
    {
        string[] typesToTry = { "AXIS", "EXTSKETCHSEGMENT", "SKETCHSEGMENT" };
        foreach (string swType in typesToTry)
        {
            try
            {
                bool ok = (bool)modelDoc.Extension.SelectByID2(
                    axisName, swType, 0, 0, 0, true, 16, null, 0);
                if (ok) return true;
            }
            catch
            {
            }
        }
        return false;
    }
}
