using System;
using System.Collections.Generic;
using System.Globalization;
using Adze.Contracts.Abstractions;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;

namespace Adze.Tools.Write;

/// <summary>
/// Adds a 2D line segment to a sketch. Coordinates are sketch-plane local
/// (X,Y in mm; Z is implicit zero). When <see cref="AddSketchLineParameters.SketchName"/>
/// is empty, the currently active sketch is used.
/// </summary>
public sealed class AddSketchLineTool : IWriteTool<AddSketchLineParameters>
{
    public WritePreview Preview(SessionContext context, AddSketchLineParameters parameters)
    {
        var preview = new WritePreview { ToolName = ToolNames.AddSketchLine };

        if (parameters == null)
        {
            preview.Summary = "Add sketch line: parameters missing.";
            preview.Warnings.Add("parameters required.");
            return preview;
        }

        if (parameters.StartXMm == parameters.EndXMm && parameters.StartYMm == parameters.EndYMm)
        {
            preview.Summary = "Add sketch line: degenerate (start == end).";
            preview.Warnings.Add("Start and end points are identical; line would have zero length.");
            return preview;
        }

        double length = Math.Sqrt(
            (parameters.EndXMm - parameters.StartXMm) * (parameters.EndXMm - parameters.StartXMm) +
            (parameters.EndYMm - parameters.StartYMm) * (parameters.EndYMm - parameters.StartYMm));

        string sketchLabel = string.IsNullOrWhiteSpace(parameters.SketchName)
            ? "(active sketch)"
            : parameters.SketchName!;

        preview.Summary = "Add line in " + sketchLabel + ": ("
            + parameters.StartXMm.ToString("0.###", CultureInfo.InvariantCulture) + ", "
            + parameters.StartYMm.ToString("0.###", CultureInfo.InvariantCulture) + ") → ("
            + parameters.EndXMm.ToString("0.###", CultureInfo.InvariantCulture) + ", "
            + parameters.EndYMm.ToString("0.###", CultureInfo.InvariantCulture) + ") mm — length "
            + length.ToString("0.###", CultureInfo.InvariantCulture) + " mm";

        preview.Changes.Add(new WriteChangeItem
        {
            TargetLabel = "Sketch line in " + sketchLabel,
            BeforeValue = "(does not exist)",
            AfterValue = "length " + length.ToString("0.###", CultureInfo.InvariantCulture) + " mm"
        });

        return preview;
    }

    public WriteApplyResult Apply(object application, AddSketchLineParameters parameters)
    {
        try
        {
            if (application == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "ISldWorks reference is null." };
            if (parameters == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "parameters required." };

            dynamic swApp = application;
            dynamic? modelDoc = swApp.ActiveDoc;
            if (modelDoc == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "No active document." };

            SketchToolHelpers.EnsureSketchActive(modelDoc, parameters.SketchName);

            // CreateLine takes meters. Z=0 because we're in sketch plane local space.
            dynamic? line = modelDoc.SketchManager.CreateLine(
                parameters.StartXMm / 1000.0, parameters.StartYMm / 1000.0, 0.0,
                parameters.EndXMm / 1000.0, parameters.EndYMm / 1000.0, 0.0);

            if (line == null)
                return new WriteApplyResult { Success = false, ErrorMessage = "CreateLine returned null." };

            string lineName = "(line)";
            try { lineName = (string)line.GetName(); } catch { }

            return new WriteApplyResult
            {
                Success = true,
                UndoLabel = BuildUndoLabel(parameters),
                AppliedValues = new Dictionary<string, string>
                {
                    ["line_name"] = lineName
                }
            };
        }
        catch (Exception ex)
        {
            return new WriteApplyResult { Success = false, ErrorMessage = "Add sketch line failed: " + ex.Message };
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

    public string BuildUndoLabel(AddSketchLineParameters parameters)
    {
        return "Adze: add sketch line";
    }
}
