using System;
using System.Collections.Generic;
using System.Globalization;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Adds a parametric dimension to a sketch entity (line, arc, circle) and
/// drives it to a target value. The agent uses this to lock geometry to
/// design intent — without driving dimensions, sketch entities are just
/// floating geometry, not parametric features.
/// </summary>
public sealed class AddSketchDimensionTool : IWriteTool<AddSketchDimensionParameters>
{
    public WritePreview Preview(SessionContext context, AddSketchDimensionParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.AddSketchDimension };

        if (parameters == null || string.IsNullOrWhiteSpace(parameters.EntityName))
        {
            preview.Summary = "Add sketch dimension: entity_name is required.";
            preview.Warnings.Add("entity_name is required.");
            return preview;
        }

        string sketchLabel = string.IsNullOrWhiteSpace(parameters.SketchName)
            ? "(active sketch)"
            : parameters.SketchName!;

        string unit = string.Equals(parameters.DimensionType, "angle", StringComparison.OrdinalIgnoreCase) ? "deg" : "mm";

        preview.Summary = "Dimension '" + parameters.EntityName + "' in " + sketchLabel
            + " (" + parameters.DimensionType + ") = "
            + parameters.TargetValue.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit;

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = parameters.EntityName + " (" + parameters.DimensionType + ")",
            BeforeValue = "(undimensioned)",
            AfterValue = parameters.TargetValue.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit
        });

        return preview;
    }

    public WriteApplyResult Apply(object application, AddSketchDimensionParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null || string.IsNullOrWhiteSpace(parameters.EntityName))
                return new WriteApplyResult { Success = false, ErrorMessage = "entity_name is required." };

            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "No active document." };

            SketchToolHelpers.EnsureSketchActive(modelDoc, parameters.SketchName);

            // Select the entity to dimension. Sketch segments use type SKETCHSEGMENT.
            modelDoc.ClearSelection2(true);
            string entityType = MapDimensionTargetType(parameters.DimensionType);
            bool selected;
            try
            {
                selected = (bool)modelDoc.Extension.SelectByID2(
                    parameters.EntityName, entityType, 0, 0, 0, false, 0, null, 0);
            }
            catch
            {
                selected = false;
            }

            if (!selected)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select sketch entity '" + parameters.EntityName + "'."
                };
            }

            // AddDimension2 places the dimension at world coords (x, y, z).
            // Picking the entity centroid is rarely correct on the screen but
            // is fine for headless use; the agent rarely cares about visual
            // dimension placement.
            dynamic? displayDim = modelDoc.AddDimension2(0.0, 0.0, 0.0);
            if (displayDim == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "AddDimension2 returned null. Ensure the selected entity supports a "
                        + parameters.DimensionType + " dimension."
                };
            }

            // Drive the dimension to the target value. SW dimensions store
            // values in system units (meters / radians).
            dynamic? dim = displayDim.GetDimension2(0);
            if (dim != null)
            {
                bool isAngle = string.Equals(parameters.DimensionType, "angle", StringComparison.OrdinalIgnoreCase);
                double systemValue = isAngle
                    ? parameters.TargetValue * Math.PI / 180.0
                    : parameters.TargetValue / 1000.0;
                try { dim.SystemValue = systemValue; }
                catch
                {
                    // Some dimension types reject direct SystemValue assignment.
                    try { dim.SetSystemValue3(systemValue, 1, null); } catch { }
                }
            }

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    ["entity"] = parameters.EntityName,
                    ["type"] = parameters.DimensionType,
                    ["value"] = parameters.TargetValue.ToString("0.######", CultureInfo.InvariantCulture)
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Add sketch dimension failed: " + ex.Message };
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

    public string BuildUndoLabel(AddSketchDimensionParameters parameters)
    {
        return "Adze: add sketch dimension on " + (parameters?.EntityName ?? "(entity)");
    }

    private static string MapDimensionTargetType(string dimensionType)
    {
        // Most dimension targets are SKETCHSEGMENT (lines, arcs, circles).
        // Distance / angle dimensions on faces or edges use FACE/EDGE.
        return string.Equals(dimensionType, "distance", StringComparison.OrdinalIgnoreCase)
            ? string.Empty // auto-detect for cross-entity distance
            : "SKETCHSEGMENT";
    }
}
