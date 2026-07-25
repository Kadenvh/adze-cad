using System;
using System.Collections.Generic;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Creates a linear pattern of an existing feature along one or two
/// directions. Phase 11 Wave 2 feature creation primitive. Class 3
/// (HardWriteAdvanced): duplicates geometry and triggers a rebuild. The
/// direction reference must be a linear edge, reference axis, or sketch line.
/// </summary>
public sealed class CreatePatternLinearTool : IWriteTool<CreatePatternLinearParameters>
{
    private static readonly string[] DirectionTypesToTry = { "EDGE", "AXIS", "EXTSKETCHSEGMENT", "SKETCHSEGMENT" };

    public WritePreview Preview(SessionContext context, CreatePatternLinearParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.CreatePatternLinear };

        if (parameters == null || string.IsNullOrWhiteSpace(parameters.FeatureName))
        {
            preview.Summary = "Create linear pattern: feature_name is required.";
            preview.Warnings.Add("feature_name is required — the feature to pattern (e.g. 'Boss-Extrude1').");
            return preview;
        }

        if (string.IsNullOrWhiteSpace(parameters.Direction1Reference))
        {
            preview.Summary = "Create linear pattern: direction_1_reference is required.";
            preview.Warnings.Add("direction_1_reference is required — a linear edge, reference axis, or sketch line.");
            return preview;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            preview.Summary = "Create linear pattern: no active document.";
            preview.Warnings.Add("No active document.");
            return preview;
        }

        if (!string.Equals(context.Document.Type, "Part", StringComparison.OrdinalIgnoreCase))
        {
            preview.Summary = "Create linear pattern: only part documents are supported.";
            preview.Warnings.Add("Document type '" + context.Document.Type + "' is not supported (part documents only).");
            return preview;
        }

        if (parameters.Count1 < 2)
        {
            preview.Summary = "Create linear pattern: count_1 must be at least 2.";
            preview.Warnings.Add("count_1 must be ≥ 2 (includes the seed instance).");
            return preview;
        }

        if (parameters.Spacing1Mm <= 0)
        {
            preview.Summary = "Create linear pattern: spacing must be positive.";
            preview.Warnings.Add("spacing_1_mm must be > 0.");
            return preview;
        }

        bool twoDirectional = !string.IsNullOrWhiteSpace(parameters.Direction2Reference);
        if (twoDirectional && (parameters.Count2 < 2 || parameters.Spacing2Mm <= 0))
        {
            preview.Summary = "Create linear pattern: direction 2 requires count_2 ≥ 2 and positive spacing_2_mm.";
            preview.Warnings.Add("When direction_2_reference is set, count_2 must be ≥ 2 and spacing_2_mm > 0.");
            return preview;
        }

        preview.Summary = "Pattern '" + parameters.FeatureName + "' " + parameters.Count1 + "× along '"
            + parameters.Direction1Reference + "' at " + parameters.Spacing1Mm.ToString("0.###") + " mm"
            + (twoDirectional
                ? " and " + parameters.Count2 + "× along '" + parameters.Direction2Reference + "' at "
                    + parameters.Spacing2Mm.ToString("0.###") + " mm"
                : "");

        int totalInstances = parameters.Count1 * (twoDirectional ? parameters.Count2 : 1);
        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "New feature",
            BeforeValue = "(none)",
            AfterValue = string.IsNullOrWhiteSpace(parameters.PatternName) ? "LPattern (SW default name)" : parameters.PatternName!
        });
        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "Instances of " + parameters.FeatureName,
            BeforeValue = "1",
            AfterValue = totalInstances.ToString()
        });

        if (context.Document.IsReadOnly)
        {
            preview.Warnings.Add("Document is read-only — pattern will fail.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreatePatternLinearParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null || string.IsNullOrWhiteSpace(parameters.FeatureName))
                return new WriteApplyResult { Success = false, ErrorMessage = "feature_name is required." };
            if (string.IsNullOrWhiteSpace(parameters.Direction1Reference))
                return new WriteApplyResult { Success = false, ErrorMessage = "direction_1_reference is required." };
            if (parameters.Count1 < 2 || parameters.Spacing1Mm <= 0)
                return new WriteApplyResult { Success = false, ErrorMessage = "count_1 must be ≥ 2 and spacing_1_mm > 0." };

            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "No active document." };

            modelDoc.ClearSelection2(true);

            // Feature to pattern carries selection mark 4.
            bool featureSelected = false;
            try
            {
                featureSelected = (bool)modelDoc.Extension.SelectByID2(
                    parameters.FeatureName, "BODYFEATURE", 0, 0, 0, false, 4, null, 0);
            }
            catch
            {
            }
            if (!featureSelected)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select feature '" + parameters.FeatureName + "'. Verify the feature exists."
                };
            }

            // Direction 1 reference carries mark 1; direction 2 carries mark 2.
            if (FeatureToolHelpers.SelectEntities(modelDoc, new[] { parameters.Direction1Reference }, DirectionTypesToTry, 1) == 0)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select direction reference '" + parameters.Direction1Reference
                        + "'. Use a linear edge, reference axis, or sketch line."
                };
            }

            bool twoDirectional = !string.IsNullOrWhiteSpace(parameters.Direction2Reference);
            if (twoDirectional)
            {
                if (FeatureToolHelpers.SelectEntities(modelDoc, new[] { parameters.Direction2Reference! }, DirectionTypesToTry, 2) == 0)
                {
                    return new WriteApplyResult
                    {
                        Success = false,
                        ErrorMessage = "Could not select direction 2 reference '" + parameters.Direction2Reference + "'."
                    };
                }
            }

            double spacing1Meters = parameters.Spacing1Mm / 1000.0;
            double spacing2Meters = parameters.Spacing2Mm / 1000.0;
            int count2 = twoDirectional ? parameters.Count2 : 1;

            dynamic? feature = modelDoc.FeatureManager.FeatureLinearPattern4(
                parameters.Count1, spacing1Meters,
                count2, spacing2Meters,
                parameters.FlipDirection1, parameters.FlipDirection2,
                "NULL", "NULL",
                false, false, false, false, false, false);

            if (feature == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "FeatureLinearPattern4 returned null. Instances may collide or leave the body's bounds."
                };
            }

            string actualName = FeatureToolHelpers.FinalizeFeatureName(feature, parameters.PatternName);

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    ["feature_name"] = actualName,
                    ["seed_feature"] = parameters.FeatureName,
                    ["count_1"] = parameters.Count1.ToString(),
                    ["count_2"] = count2.ToString()
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Create linear pattern failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        return FeatureToolHelpers.VerifyFeatureInTree(refreshedContext, applyResult, "LinearPattern");
    }

    public string BuildUndoLabel(CreatePatternLinearParameters parameters)
    {
        return "Adze: linear pattern of " + (parameters?.FeatureName ?? "(feature)");
    }
}
