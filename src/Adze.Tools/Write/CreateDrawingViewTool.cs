using System;
using System.Collections.Generic;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;

namespace Adze.Tools.Write;

/// <summary>
/// Class 3 (HardWriteAdvanced) write tool — creates a standard drawing view.
/// Requires elevated confirmation. Only available when the active document is a drawing.
/// Uses IDrawingDoc.CreateDrawViewFromModelView3() via COM.
/// </summary>
public sealed class CreateDrawingViewTool : IWriteTool<CreateDrawingViewParameters>
{
    private static readonly HashSet<string> ValidViewTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "front", "back", "top", "bottom", "left", "right", "isometric", "trimetric", "dimetric"
    };

    // SOLIDWORKS standard view name mappings for CreateDrawViewFromModelView3
    private static readonly Dictionary<string, string> ViewNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["front"] = "*Front",
        ["back"] = "*Back",
        ["top"] = "*Top",
        ["bottom"] = "*Bottom",
        ["left"] = "*Left",
        ["right"] = "*Right",
        ["isometric"] = "*Isometric",
        ["trimetric"] = "*Trimetric",
        ["dimetric"] = "*Dimetric"
    };

    public WritePreview Preview(SessionContext context, CreateDrawingViewParameters parameters)
    {
        var preview = new WritePreview
        {
            ToolName = Contracts.Tooling.ToolNames.CreateDrawingView,
            Summary = "Create " + parameters.ViewType + " view on drawing"
        };

        // Validate document type
        if (context.Document == null || !string.Equals(context.Document.Type, "drawing", StringComparison.OrdinalIgnoreCase))
        {
            preview.Warnings.Add("Drawing view creation requires an open drawing document. Current document type: " +
                (context.Document?.Type ?? "none") + ".");
        }

        // Validate view type
        string viewType = (parameters.ViewType ?? "front").ToLowerInvariant();
        if (!ValidViewTypes.Contains(viewType))
        {
            preview.Warnings.Add("Unknown view type: \"" + parameters.ViewType + "\". Valid types: " +
                string.Join(", ", ValidViewTypes) + ".");
        }

        // Validate scale
        if (parameters.Scale <= 0 || parameters.Scale > 100)
        {
            preview.Warnings.Add("Scale " + parameters.Scale + " is outside the expected range (0 < scale <= 100).");
        }

        string positionText = "(" + parameters.X.ToString("0.###") + ", " + parameters.Y.ToString("0.###") + ") m";
        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = viewType + " view",
            BeforeValue = "(does not exist)",
            AfterValue = "created at " + positionText + " scale " + parameters.Scale.ToString("0.##") + ":1"
        });

        if (!string.IsNullOrWhiteSpace(parameters.ModelPath))
        {
            preview.Changes.Add(new WriteChangeItem
            {
                TargetLabel = "Source model",
                BeforeValue = "",
                AfterValue = parameters.ModelPath
            });
        }

        // This is a Class 3 operation
        preview.Warnings.Add("This is an elevated operation. A new drawing view will be added to the active sheet.");

        if (context.Document?.IsReadOnly == true)
        {
            preview.Warnings.Add("Document is read-only. The view may not be saved.");
        }

        return preview;
    }

    public WriteApplyResult Apply(object application, CreateDrawingViewParameters parameters)
    {
        try
        {
            dynamic swApp = application;
            dynamic modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "No active document."
                };
            }

            // Verify it's a drawing
            int docType = (int)modelDoc.GetType();
            if (docType != 3) // swDocDRAWING = 3
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Active document is not a drawing."
                };
            }

            string viewType = (parameters.ViewType ?? "front").ToLowerInvariant();
            if (!ViewNameMap.TryGetValue(viewType, out string? swViewName))
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "Unsupported view type: " + parameters.ViewType
                };
            }

            // If a model path is specified, use it; otherwise use the first referenced model
            string modelPath = parameters.ModelPath ?? string.Empty;

            // CreateDrawViewFromModelView3(ModelPath, ViewName, X, Y) → View
            dynamic? view = modelDoc.CreateDrawViewFromModelView3(
                modelPath,
                swViewName,
                parameters.X,
                parameters.Y);

            if (view == null)
            {
                return new WriteApplyResult
                {
                    Success = false,
                    ErrorMessage = "SOLIDWORKS could not create the " + viewType + " view. " +
                        "Ensure a model is referenced by the drawing."
                };
            }

            // Set scale if non-default
            if (Math.Abs(parameters.Scale - 1.0) > 0.001)
            {
                try
                {
                    view.ScaleRatio = new double[] { parameters.Scale, 1.0 };
                }
                catch
                {
                    // Scale setting is best-effort
                }
            }

            string viewName = "(created)";
            try { viewName = view.Name; } catch { }

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    [viewType] = viewName
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult
            {
                Success = false,
                ErrorMessage = "Failed to create drawing view: " + ex.Message
            };
        }
    }

    public WriteVerification Verify(SessionContext refreshedContext, WriteApplyResult applyResult)
    {
        var verification = new WriteVerification();

        if (!applyResult.Success)
        {
            verification.ChangeConfirmed = false;
            verification.RebuildSucceeded = false;
            return verification;
        }

        verification.RebuildSucceeded = refreshedContext.Diagnostics.RebuildState != "failed";

        // For drawing views, we can't easily verify via the standard SessionContext
        // (no drawing-specific view list yet). Accept the COM return as confirmation.
        foreach (var applied in applyResult.AppliedValues)
        {
            verification.ChangeConfirmed = true;
            verification.ObservedChanges.Add(new StateDiffItem
            {
                Path = "drawing_view",
                BeforeValue = "(did not exist)",
                AfterValue = applied.Value
            });
        }

        return verification;
    }

    public string BuildUndoLabel(CreateDrawingViewParameters parameters)
    {
        return "Adze: create " + (parameters.ViewType ?? "front") + " view";
    }
}
