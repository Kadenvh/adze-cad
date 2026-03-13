using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetDimensionsTool : IReadOnlyTool<GetDimensionsParameters>
{
    public string ToolName => ToolNames.GetDimensions;

    public ToolResult Execute(SessionContext context, GetDimensionsParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document for dimension inspection."
                : "Dimension list generated."
        };

        if (context.Document == null)
        {
            return result;
        }

        IEnumerable<DimensionNode> source = context.Dimensions.Items;
        if (string.Equals(parameters.Scope, "selection", StringComparison.OrdinalIgnoreCase) && context.Selection.Items.Count == 0)
        {
            source = Array.Empty<DimensionNode>();
        }

        List<Dictionary<string, object?>> items = source
            .Take(50)
            .Select(item => new Dictionary<string, object?>
            {
                ["name"] = item.Name,
                ["full_name"] = item.FullName,
                ["value"] = item.Value,
                ["unit_source"] = item.UnitSource
            })
            .ToList();

        if (items.Count == 0)
        {
            result.Summary = "No dimensions found for the requested scope.";
        }

        result.Data["scope"] = string.IsNullOrWhiteSpace(parameters.Scope) ? "selection" : parameters.Scope.Trim();
        result.Data["count"] = context.Dimensions.Count;
        result.Data["returned_count"] = items.Count;
        result.Data["include_driven"] = parameters.IncludeDriven;
        result.Data["items"] = items;
        return result;
    }
}
