using System;
using System.Collections.Generic;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Extrudes a sketch into a solid boss. First of the Phase 11 Wave 2 feature
/// creation primitives — this is the tool that turns 2D sketch authoring
/// (create_sketch + add_sketch_circle) into actual 3D geometry ("make a
/// cylinder"). Class 3 (HardWriteAdvanced): creates a solid body, merges
/// with existing bodies, and triggers a rebuild.
/// </summary>
public sealed class CreateExtrudeTool : IWriteTool<CreateExtrudeParameters>
{
    private static readonly HashSet<string> ValidEndConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        "blind", "through_all", "mid_plane"
    };

    public WritePreview Preview(SessionContext context, CreateExtrudeParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.CreateExtrude };

        if (parameters == null)
        {
            preview.Summary = "Create extrude: parameters are required.";
            preview.Warnings.Add("Parameters are required.");
            return preview;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            preview.Summary = "Create extrude: no active document.";
            preview.Warnings.Add("No active document.");
            return preview;
        }

        if (!string.Equals(context.Document.Type, "Part", StringComparison.OrdinalIgnoreCase))
        {
            preview.Summary = "Create extrude: only part documents support extruded features.";
            preview.Warnings.Add("Document type '" + context.Document.Type + "' does not support extrusion.");
            return preview;
        }

        if (!ValidEndConditions.Contains(parameters.EndCondition))
        {
            preview.Summary = "Create extrude: unsupported end condition '" + parameters.EndCondition + "'.";
            preview.Warnings.Add("end_condition must be one of: blind, through_all, mid_plane.");
            return preview;
        }

        bool isBlindOrMid = !string.Equals(parameters.EndCondition, "through_all", StringComparison.OrdinalIgnoreCase);
        if (isBlindOrMid && parameters.DepthMm <= 0)
        {
            preview.Summary = "Create extrude: depth must be positive.";
            preview.Warnings.Add("depth_mm must be > 0 for blind / mid_plane extrusions.");
            return preview;
        }

        string sketchLabel = string.IsNullOrWhiteSpace(parameters.SketchName) ? "active sketch" : "'" + parameters.SketchName + "'";
        string depthLabel = isBlindOrMid ? parameters.DepthMm + " mm" : "through all";
        preview.Summary = "Extrude " + sketchLabel + " " + depthLabel
            + " (" + parameters.EndCondition.ToLowerInvariant() + (parameters.Reverse ? ", reversed" : "") + ")";

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "New feature",
            BeforeValue = "(none)",
            AfterValue = string.IsNullOrWhiteSpace(parameters.FeatureName) ? "Boss-Extrude (SW default name)" : parameters.FeatureName!
        });
        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "Profile",
            BeforeValue = sketchLabel,
            AfterValue = "consumed by extrusion"
        });

        if (context.Document.IsReadOnly)
        {
            preview.Warnings.Add("Document is read-only — extrusion will fail.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreateExtrudeParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "Parameters are required." };
            if (!ValidEndConditions.Contains(parameters.EndCondition))
                return new WriteApplyResult { Success = false, ErrorMessage = "Unsupported end condition '" + parameters.EndCondition + "'." };

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
            bool selected = false;
            try
            {
                selected = (bool)modelDoc.Extension.SelectByID2(sketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
            }
            catch
            {
            }
            if (!selected)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select sketch '" + sketchName + "'. Verify the sketch exists and is closed."
                };
            }

            // swEndConditions_e: Blind = 0, ThroughAll = 1, MidPlane = 6.
            int endCondition = string.Equals(parameters.EndCondition, "through_all", StringComparison.OrdinalIgnoreCase) ? 1
                : string.Equals(parameters.EndCondition, "mid_plane", StringComparison.OrdinalIgnoreCase) ? 6
                : 0;
            double depthMeters = parameters.DepthMm / 1000.0;

            dynamic? feature = modelDoc.FeatureManager.FeatureExtrusion3(
                true,                       // single-ended
                parameters.Reverse,         // flip direction
                false,                      // dir
                endCondition, 0,            // end condition dir1, dir2
                depthMeters, 0.0,           // depth dir1, dir2 (meters)
                false, false,               // draft on/off dir1, dir2
                false, false,               // draft direction dir1, dir2
                0.0, 0.0,                   // draft angle dir1, dir2
                false, false,               // offset reverse dir1, dir2
                false, false,               // translate surface dir1, dir2
                true,                       // merge result
                true,                       // use feature scope
                true,                       // auto-select
                0,                          // start condition: sketch plane
                0.0,                        // start offset
                false);                     // flip start offset

            if (feature == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "FeatureExtrusion3 returned null. The sketch profile may be open, self-intersecting, or empty."
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
                    ["end_condition"] = parameters.EndCondition.ToLowerInvariant(),
                    ["depth_mm"] = parameters.DepthMm.ToString("0.###")
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Create extrude failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        return FeatureToolHelpers.VerifyFeatureInTree(refreshedContext, applyResult, "Boss-Extrude");
    }

    public string BuildUndoLabel(CreateExtrudeParameters parameters)
    {
        string sketch = string.IsNullOrWhiteSpace(parameters?.SketchName) ? "active sketch" : parameters!.SketchName!;
        return "Adze: extrude " + sketch;
    }
}
