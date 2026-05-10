using System;
using System.Collections.Generic;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Compute;

/// <summary>
/// Equivalent of the Evaluate tab's "Measure" command. Returns linear
/// distance, projection components, area, and other geometric measurements
/// between two selected entities (or the current selection).
///
/// SW exposes measurement via <c>ISldWorks.GetMeasure</c> →
/// <c>IMeasure</c>. We pre-select the requested entities (or use whatever
/// is currently selected), call <c>Calculate(null)</c>, and read back the
/// available scalar fields.
/// </summary>
public sealed class MeasureDistanceTool : IComputeTool<MeasureDistanceParameters>
{
    public string ToolName => ToolNames.MeasureDistance;

    public ToolResult Execute(SessionContext context, object? application, MeasureDistanceParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = false,
            Summary = "Measurement not executed."
        };

        if (application == null)
        {
            result.Summary = "SOLIDWORKS is not connected — measurement requires a live application reference.";
            return result;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            result.Summary = "No active document.";
            return result;
        }

        try
        {
            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
            {
                result.Summary = "No active document.";
                return result;
            }

            int requestedSelections = 0;
            if (!string.IsNullOrWhiteSpace(parameters?.EntityA))
            {
                if (TrySelect(modelDoc, parameters!.EntityA!, parameters.EntityATypeHint, append: false))
                {
                    requestedSelections++;
                }
            }
            if (!string.IsNullOrWhiteSpace(parameters?.EntityB))
            {
                if (TrySelect(modelDoc, parameters!.EntityB!, parameters.EntityBTypeHint, append: requestedSelections > 0))
                {
                    requestedSelections++;
                }
            }

            // If no entities requested, fall back to whatever the user has
            // currently selected. SelectionInfo from the snapshot is only a
            // hint; the actual current selection may differ.
            int activeSelections = 0;
            try { activeSelections = (int)modelDoc.SelectionManager.GetSelectedObjectCount2(-1); } catch { }
            if (requestedSelections == 0 && activeSelections == 0)
            {
                result.Summary = "Nothing selected. Provide entity_a / entity_b or pre-select two entities in SW.";
                return result;
            }

            dynamic? measure = swApp.GetMeasure();
            if (measure == null)
            {
                result.Summary = "GetMeasure returned null.";
                return result;
            }

            bool ok = false;
            try { ok = (bool)measure.Calculate(null); }
            catch (Exception ex)
            {
                result.Summary = "Measure.Calculate threw: " + ex.Message;
                return result;
            }

            if (!ok)
            {
                result.Summary = "Measurement failed — selection may not support measurement.";
                return result;
            }

            // Pull the populated scalar fields. Many will be 0 if not applicable
            // to the selection (e.g. Area is 0 for two vertices).
            double distance = SafeGet(() => (double)measure.Distance);
            double dx = SafeGet(() => (double)measure.DeltaX);
            double dy = SafeGet(() => (double)measure.DeltaY);
            double dz = SafeGet(() => (double)measure.DeltaZ);
            double angle = SafeGet(() => (double)measure.Angle);
            double area = SafeGet(() => (double)measure.Area);
            double length = SafeGet(() => (double)measure.Length);

            result.Success = true;
            result.Summary = "Distance: "
                + (distance * 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " mm";

            result.Data["distance_m"] = distance;
            result.Data["distance_mm"] = distance * 1000.0;
            result.Data["delta_x_mm"] = dx * 1000.0;
            result.Data["delta_y_mm"] = dy * 1000.0;
            result.Data["delta_z_mm"] = dz * 1000.0;
            result.Data["angle_radians"] = angle;
            result.Data["angle_degrees"] = angle * 180.0 / Math.PI;
            result.Data["area_mm2"] = area * 1e6;
            result.Data["length_mm"] = length * 1000.0;
        }
        catch (Exception ex)
        {
            result.Summary = "Measurement failed: " + ex.Message;
        }

        return result;
    }

    private static bool TrySelect(dynamic modelDoc, string entityName, string? typeHint, bool append)
    {
        // SelectByID2 signature:
        //   (string Name, string Type, double X, double Y, double Z,
        //    bool Append, int Mark, object Callout, int SelectOption) → bool
        string swType = MapTypeHint(typeHint);
        try
        {
            return (bool)modelDoc.Extension.SelectByID2(
                entityName, swType, 0, 0, 0, append, 0, null, 0);
        }
        catch
        {
            return false;
        }
    }

    private static string MapTypeHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return string.Empty;
        return hint!.Trim().ToLowerInvariant() switch
        {
            "face" => "FACE",
            "edge" => "EDGE",
            "vertex" => "VERTEX",
            "sketchsegment" or "sketch_segment" => "SKETCHSEGMENT",
            "feature" => "BODYFEATURE",
            "plane" => "PLANE",
            _ => hint!.ToUpperInvariant()
        };
    }

    private static double SafeGet(Func<double> reader)
    {
        try { return reader(); }
        catch { return 0.0; }
    }
}
