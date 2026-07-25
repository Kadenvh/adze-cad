using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Applies a distance-angle chamfer to named edges or faces. Phase 11
/// Wave 2 feature creation primitive. Class 3 (HardWriteAdvanced): modifies
/// solid geometry and triggers a rebuild.
/// </summary>
public sealed class CreateChamferTool : IWriteTool<CreateChamferParameters>
{
    private static readonly string[] EntityTypesToTry = { "EDGE", "FACE" };

    public WritePreview Preview(SessionContext context, CreateChamferParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.CreateChamfer };

        if (parameters == null || parameters.EntityNames.Count == 0
            || parameters.EntityNames.All(string.IsNullOrWhiteSpace))
        {
            preview.Summary = "Create chamfer: entity_names is required.";
            preview.Warnings.Add("entity_names must contain at least one edge or face name.");
            return preview;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            preview.Summary = "Create chamfer: no active document.";
            preview.Warnings.Add("No active document.");
            return preview;
        }

        if (!string.Equals(context.Document.Type, "Part", StringComparison.OrdinalIgnoreCase))
        {
            preview.Summary = "Create chamfer: only part documents support chamfer features.";
            preview.Warnings.Add("Document type '" + context.Document.Type + "' does not support chamfers.");
            return preview;
        }

        if (parameters.DistanceMm <= 0)
        {
            preview.Summary = "Create chamfer: distance must be positive.";
            preview.Warnings.Add("distance_mm must be > 0.");
            return preview;
        }

        if (parameters.AngleDegrees <= 0 || parameters.AngleDegrees >= 90)
        {
            preview.Summary = "Create chamfer: angle must be in (0, 90).";
            preview.Warnings.Add("angle_degrees must be greater than 0 and less than 90.");
            return preview;
        }

        int entityCount = parameters.EntityNames.Count(n => !string.IsNullOrWhiteSpace(n));
        preview.Summary = "Chamfer " + entityCount + " entit" + (entityCount == 1 ? "y" : "ies")
            + " at " + parameters.DistanceMm.ToString("0.###") + " mm × "
            + parameters.AngleDegrees.ToString("0.###") + "°";

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "New feature",
            BeforeValue = "(none)",
            AfterValue = string.IsNullOrWhiteSpace(parameters.FeatureName) ? "Chamfer (SW default name)" : parameters.FeatureName!
        });
        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "Entities",
            BeforeValue = "sharp",
            AfterValue = string.Join(", ", parameters.EntityNames.Where(n => !string.IsNullOrWhiteSpace(n)))
        });

        if (context.Document.IsReadOnly)
        {
            preview.Warnings.Add("Document is read-only — chamfer will fail.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreateChamferParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null || parameters.EntityNames.Count == 0)
                return new WriteApplyResult { Success = false, ErrorMessage = "entity_names is required." };
            if (parameters.DistanceMm <= 0)
                return new WriteApplyResult { Success = false, ErrorMessage = "distance_mm must be > 0." };
            if (parameters.AngleDegrees <= 0 || parameters.AngleDegrees >= 90)
                return new WriteApplyResult { Success = false, ErrorMessage = "angle_degrees must be in (0, 90)." };

            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "No active document." };

            modelDoc.ClearSelection2(true);
            int selected = FeatureToolHelpers.SelectEntities(modelDoc, parameters.EntityNames, EntityTypesToTry, 1);
            if (selected == 0)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select any of the named edges/faces. Verify the entity names."
                };
            }

            double distanceMeters = parameters.DistanceMm / 1000.0;
            double angleRadians = parameters.AngleDegrees * Math.PI / 180.0;

            // InsertFeatureChamfer(type=4 (angle-distance), propagate=1,
            // width, angle, otherDist, vertexA, vertexB, vertexC) over the
            // preselected edge/face set.
            dynamic? feature = modelDoc.FeatureManager.InsertFeatureChamfer(
                4, 1, distanceMeters, angleRadians, 0.0, 0.0, 0.0, 0.0);

            if (feature == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "InsertFeatureChamfer returned null. The distance may be too large for the selected edges."
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
                    ["distance_mm"] = parameters.DistanceMm.ToString("0.###"),
                    ["angle_degrees"] = parameters.AngleDegrees.ToString("0.###"),
                    ["entities_selected"] = selected.ToString()
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Create chamfer failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        return FeatureToolHelpers.VerifyFeatureInTree(refreshedContext, applyResult, "Chamfer");
    }

    public string BuildUndoLabel(CreateChamferParameters parameters)
    {
        return "Adze: chamfer " + (parameters?.DistanceMm ?? 0).ToString("0.###") + " mm × "
            + (parameters?.AngleDegrees ?? 0).ToString("0.###") + "°";
    }
}
