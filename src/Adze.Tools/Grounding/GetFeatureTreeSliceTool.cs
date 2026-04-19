using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetFeatureTreeSliceTool : IReadOnlyTool<GetFeatureTreeSliceParameters>
{
    public string ToolName => ToolNames.GetFeatureTreeSlice;

    public ToolResult Execute(SessionContext context, GetFeatureTreeSliceParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document for feature-tree inspection."
                : "Read the feature tree."
        };

        if (context.Document == null)
        {
            return result;
        }

        List<FeatureNode> features = context.FeatureTree.Features;
        if (features.Count == 0)
        {
            result.Success = true;
            result.Summary = "No feature-tree preview available.";
            result.Data["anchor"] = context.FeatureTree.Anchor;
            result.Data["available_count"] = 0;
            result.Data["returned_count"] = 0;
            result.Data["items"] = new List<Dictionary<string, object?>>();
            return result;
        }

        string? anchorName = string.IsNullOrWhiteSpace(parameters.AnchorName)
            ? context.FeatureTree.Anchor
            : parameters.AnchorName?.Trim();
        int radius = Math.Max(1, parameters.Radius);
        int anchorIndex = string.IsNullOrWhiteSpace(anchorName)
            ? 0
            : features.FindIndex(feature => string.Equals(feature.Name, anchorName, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<FeatureNode> slice;
        if (anchorIndex >= 0)
        {
            int start = Math.Max(0, anchorIndex - radius);
            int length = Math.Min(features.Count - start, (radius * 2) + 1);
            slice = features.Skip(start).Take(length).ToList();
        }
        else
        {
            slice = features.Take(radius).ToList();
        }

        var items = new List<Dictionary<string, object?>>();
        foreach (FeatureNode feature in slice)
        {
            items.Add(new Dictionary<string, object?>
            {
                ["name"] = feature.Name,
                ["kind"] = feature.Kind,
                ["state"] = feature.State
            });
        }

        result.Data["anchor"] = anchorName;
        result.Data["available_count"] = features.Count;
        result.Data["returned_count"] = items.Count;
        result.Data["items"] = items;
        return result;
    }
}
