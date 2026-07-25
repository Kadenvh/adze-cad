using System;
using System.Collections.Generic;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Creates a reference plane offset from an existing plane or planar face.
/// Phase 11 Wave 2 feature creation primitive. Class 3 (HardWriteAdvanced)
/// for consistency with the other geometry creation tools, though the plane
/// itself is construction geometry. Offset-distance is the only creation
/// mode implemented; 3-point and normal-to-edge modes are deferred.
/// </summary>
public sealed class CreateReferencePlaneTool : IWriteTool<CreateReferencePlaneParameters>
{
    private static readonly string[] ReferenceTypesToTry = { "PLANE", "FACE" };

    public WritePreview Preview(SessionContext context, CreateReferencePlaneParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.CreateReferencePlane };

        if (parameters == null || string.IsNullOrWhiteSpace(parameters.BaseReference))
        {
            preview.Summary = "Create reference plane: base_reference is required.";
            preview.Warnings.Add("base_reference is required — a plane (e.g. 'Front Plane') or planar face.");
            return preview;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            preview.Summary = "Create reference plane: no active document.";
            preview.Warnings.Add("No active document.");
            return preview;
        }

        if (string.Equals(context.Document.Type, "Drawing", StringComparison.OrdinalIgnoreCase))
        {
            preview.Summary = "Create reference plane: drawings do not support reference planes.";
            preview.Warnings.Add("Document type 'Drawing' does not support reference planes.");
            return preview;
        }

        if (parameters.OffsetMm <= 0)
        {
            preview.Summary = "Create reference plane: offset must be positive.";
            preview.Warnings.Add("offset_mm must be > 0. Use flip=true to offset the other way.");
            return preview;
        }

        preview.Summary = "Create plane offset " + parameters.OffsetMm.ToString("0.###") + " mm from '"
            + parameters.BaseReference + "'" + (parameters.Flip ? " (flipped)" : "");

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "New plane",
            BeforeValue = "(none)",
            AfterValue = string.IsNullOrWhiteSpace(parameters.PlaneName) ? "Plane (SW default name)" : parameters.PlaneName!
        });

        if (context.Document.IsReadOnly)
        {
            preview.Warnings.Add("Document is read-only — plane creation will fail.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreateReferencePlaneParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null || string.IsNullOrWhiteSpace(parameters.BaseReference))
                return new WriteApplyResult { Success = false, ErrorMessage = "base_reference is required." };
            if (parameters.OffsetMm <= 0)
                return new WriteApplyResult { Success = false, ErrorMessage = "offset_mm must be > 0." };

            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "No active document." };

            modelDoc.ClearSelection2(true);
            if (FeatureToolHelpers.SelectEntities(modelDoc, new[] { parameters.BaseReference }, ReferenceTypesToTry, 0) == 0)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select base reference '" + parameters.BaseReference + "'. Verify the plane / face exists."
                };
            }

            double offsetMeters = parameters.OffsetMm / 1000.0;

            // swRefPlaneReferenceConstraint_Distance = 8;
            // swRefPlaneReferenceConstraint_OptionFlip = 16.
            int firstConstraint = 8 + (parameters.Flip ? 16 : 0);

            dynamic? feature = modelDoc.FeatureManager.InsertRefPlane(
                firstConstraint, offsetMeters, 0, 0.0, 0, 0.0);

            if (feature == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "InsertRefPlane returned null. The base reference may not be planar."
                };
            }

            string actualName = FeatureToolHelpers.FinalizeFeatureName(feature, parameters.PlaneName);

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    ["feature_name"] = actualName,
                    ["base_reference"] = parameters.BaseReference,
                    ["offset_mm"] = parameters.OffsetMm.ToString("0.###")
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Create reference plane failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        return FeatureToolHelpers.VerifyFeatureInTree(refreshedContext, applyResult, "RefPlane");
    }

    public string BuildUndoLabel(CreateReferencePlaneParameters parameters)
    {
        return "Adze: plane offset " + (parameters?.OffsetMm ?? 0).ToString("0.###") + " mm from "
            + (parameters?.BaseReference ?? "(reference)");
    }
}
