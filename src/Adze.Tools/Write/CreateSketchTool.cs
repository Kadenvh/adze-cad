using System;
using System.Collections.Generic;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Creates a new sketch on a reference plane or planar face. First of the
/// Phase 11 sketch authoring tools — without sketch creation, the agent
/// cannot generate any geometry from scratch. Class 3 (HardWriteAdvanced)
/// because it puts SW into sketch mode, which is a non-trivial state
/// transition the user should explicitly approve.
/// </summary>
public sealed class CreateSketchTool : IWriteTool<CreateSketchParameters>
{
    public WritePreview Preview(SessionContext context, CreateSketchParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.CreateSketch };

        if (parameters == null || string.IsNullOrWhiteSpace(parameters.ReferenceName))
        {
            preview.Summary = "Create sketch: reference_name is required.";
            preview.Warnings.Add("reference_name is required.");
            return preview;
        }

        if (context?.Document == null || context.Document.Type == "none")
        {
            preview.Summary = "Create sketch: no active document.";
            preview.Warnings.Add("No active document.");
            return preview;
        }

        if (!string.Equals(context.Document.Type, "Part", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.Document.Type, "Assembly", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.Document.Type, "part", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.Document.Type, "assembly", StringComparison.OrdinalIgnoreCase))
        {
            preview.Summary = "Create sketch: only parts and assemblies support sketches.";
            preview.Warnings.Add("Document type '" + context.Document.Type + "' does not support sketches.");
            return preview;
        }

        preview.Summary = "Create new sketch on '" + parameters.ReferenceName + "'"
            + (string.IsNullOrWhiteSpace(parameters.SketchName) ? "" : " named '" + parameters.SketchName + "'");

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "Reference",
            BeforeValue = "(no sketch)",
            AfterValue = parameters.ReferenceName
        });

        if (!string.IsNullOrWhiteSpace(parameters.SketchName))
        {
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = "Sketch name",
                BeforeValue = "(default)",
                AfterValue = parameters.SketchName!
            });
        }

        if (context.Document.IsReadOnly)
        {
            preview.Warnings.Add("Document is read-only — sketch creation will fail.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreateSketchParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null || string.IsNullOrWhiteSpace(parameters.ReferenceName))
                return new WriteApplyResult { Success = false, ErrorMessage = "reference_name is required." };

            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "No active document." };

            // Clear selection then select the reference plane/face. SelectByID2
            // accepts a "type" hint; we try common types in order. PLANE works
            // for default planes (Front/Top/Right). FACE works for planar faces.
            modelDoc.ClearSelection2(true);
            bool selected = TrySelectReference(modelDoc, parameters.ReferenceName);
            if (!selected)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Could not select reference '" + parameters.ReferenceName + "'. Verify the plane / face exists."
                };
            }

            // SketchManager.InsertSketch(true) enters sketch mode AND
            // creates a new sketch on the selected plane/face. Returns nothing
            // useful directly; the new sketch becomes the active sketch.
            modelDoc.SketchManager.InsertSketch(true);

            string actualName = "(unnamed)";
            try
            {
                dynamic? activeSketch = modelDoc.SketchManager.ActiveSketch;
                if (activeSketch != null)
                {
                    dynamic? sketchFeature = activeSketch.GetFeature();
                    if (sketchFeature != null)
                    {
                        actualName = (string)sketchFeature.Name;

                        if (!string.IsNullOrWhiteSpace(parameters.SketchName)
                            && !string.Equals(actualName, parameters.SketchName, StringComparison.Ordinal))
                        {
                            try
                            {
                                sketchFeature.Name = parameters.SketchName!;
                                actualName = parameters.SketchName!;
                            }
                            catch
                            {
                                // Rename failed; keep default name.
                            }
                        }
                    }
                }
            }
            catch
            {
                // Sketch was created but introspection failed; not fatal.
            }

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    ["sketch_name"] = actualName,
                    ["reference"] = parameters.ReferenceName
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Create sketch failed: " + ex.Message };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        var verification = new WriteVerification { RebuildSucceeded = true };
        if (!applyResult.Success)
        {
            verification.ChangeConfirmed = false;
            return verification;
        }

        // The fresh feature tree should now include a sketch with the
        // claimed name. Spot-check by name; full verification would require
        // querying SW for the active sketch, which we don't expose in
        // SessionContext yet.
        if (applyResult.AppliedValues.TryGetValue("sketch_name", out string? name))
        {
            foreach (FeatureNode feature in refreshedContext?.FeatureTree?.Features ?? new List<FeatureNode>())
            {
                if (string.Equals(feature.Name, name, StringComparison.Ordinal))
                {
                    verification.ChangeConfirmed = true;
                    verification.ObservedChanges.Add(new StateDiffItem
                    {
                        Path = "feature_tree." + name,
                        BeforeValue = "(missing)",
                        AfterValue = "Sketch"
                    });
                    return verification;
                }
            }
        }

        // Don't fail loudly — sketch creation may not yet be reflected in the
        // refreshed snapshot if the capture races the apply. Mark unconfirmed.
        verification.ChangeConfirmed = false;
        return verification;
    }

    public string BuildUndoLabel(CreateSketchParameters parameters)
    {
        return "Adze: create sketch on " + (parameters?.ReferenceName ?? "(reference)");
    }

    private static bool TrySelectReference(dynamic modelDoc, string referenceName)
    {
        // Try the obvious types in order. SelectByID2 returns false if the
        // type doesn't match; the empty string (auto-detect) sometimes works
        // but is less reliable, so we try explicit types first.
        string[] typesToTry = { "PLANE", "FACE", "BODYFEATURE", string.Empty };
        foreach (string swType in typesToTry)
        {
            try
            {
                bool ok = (bool)modelDoc.Extension.SelectByID2(
                    referenceName, swType, 0, 0, 0, false, 0, null, 0);
                if (ok) return true;
            }
            catch
            {
            }
        }
        return false;
    }
}
