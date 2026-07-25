using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Applies a constant-radius edge fillet to named edges or faces. Phase 11
/// Wave 2 feature creation primitive. Class 3 (HardWriteAdvanced): modifies
/// solid geometry and triggers a rebuild. Selecting a face fillets all of
/// that face's edges.
/// </summary>
public sealed class CreateFilletTool : IWriteTool<CreateFilletParameters>
{
    private static readonly string[] EntityTypesToTry = { "EDGE", "FACE" };

    public WritePreview Preview(SessionContext context, CreateFilletParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.CreateFillet };

        if (parameters == null || parameters.EntityNames.Count == 0
            || parameters.EntityNames.All(string.IsNullOrWhiteSpace))
        {
            preview.Summary = "Create fillet: entity_names is required.";
            preview.Warnings.Add("entity_names must contain at least one edge or face name.");
            return preview;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            preview.Summary = "Create fillet: no active document.";
            preview.Warnings.Add("No active document.");
            return preview;
        }

        if (!string.Equals(context.Document.Type, "Part", StringComparison.OrdinalIgnoreCase))
        {
            preview.Summary = "Create fillet: only part documents support fillet features.";
            preview.Warnings.Add("Document type '" + context.Document.Type + "' does not support fillets.");
            return preview;
        }

        if (parameters.RadiusMm <= 0)
        {
            preview.Summary = "Create fillet: radius must be positive.";
            preview.Warnings.Add("radius_mm must be > 0.");
            return preview;
        }

        int entityCount = parameters.EntityNames.Count(n => !string.IsNullOrWhiteSpace(n));
        preview.Summary = "Fillet " + entityCount + " entit" + (entityCount == 1 ? "y" : "ies")
            + " with radius " + parameters.RadiusMm.ToString("0.###") + " mm";

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "New feature",
            BeforeValue = "(none)",
            AfterValue = string.IsNullOrWhiteSpace(parameters.FeatureName) ? "Fillet (SW default name)" : parameters.FeatureName!
        });
        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "Entities",
            BeforeValue = "sharp",
            AfterValue = string.Join(", ", parameters.EntityNames.Where(n => !string.IsNullOrWhiteSpace(n)))
        });

        if (context.Document.IsReadOnly)
        {
            preview.Warnings.Add("Document is read-only — fillet will fail.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreateFilletParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null || parameters.EntityNames.Count == 0)
                return new WriteApplyResult { Success = false, ErrorMessage = "entity_names is required." };
            if (parameters.RadiusMm <= 0)
                return new WriteApplyResult { Success = false, ErrorMessage = "radius_mm must be > 0." };

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
                    ErrorMessage = "Could not select any of the named edges/faces. Verify the entity names (e.g. from get_selection_context)."
                };
            }

            double radiusMeters = parameters.RadiusMm / 1000.0;

            // Options bitmask 195 = uniform radius + propagate to tangent
            // faces + overflow default — the standard constant-radius fillet
            // invocation over a preselected (mark=1) edge/face set.
            dynamic? feature = modelDoc.FeatureManager.FeatureFillet3(
                195, radiusMeters, 0.0, 0.0, 0, 0, 0);

            if (feature == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "FeatureFillet3 returned null. The radius may be too large for the selected edges."
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
                    ["radius_mm"] = parameters.RadiusMm.ToString("0.###"),
                    ["entities_selected"] = selected.ToString()
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Create fillet failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        return FeatureToolHelpers.VerifyFeatureInTree(refreshedContext, applyResult, "Fillet");
    }

    public string BuildUndoLabel(CreateFilletParameters parameters)
    {
        return "Adze: fillet r=" + (parameters?.RadiusMm ?? 0).ToString("0.###") + " mm";
    }
}
