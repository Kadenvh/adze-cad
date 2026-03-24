using System;
using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetMatesTool : IReadOnlyTool<GetMatesParameters>
{
    public string ToolName => ToolNames.GetMates;

    public ToolResult Execute(SessionContext context, GetMatesParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document for mate inspection."
                : "Mate list generated."
        };

        if (context.Document == null)
        {
            return result;
        }

        if (!string.Equals(context.Document.Type, "assembly", StringComparison.OrdinalIgnoreCase))
        {
            result.Success = true;
            result.Summary = "Mate inspection is only available on assembly documents.";
            result.Data["scope"] = parameters.Scope;
            result.Data["count"] = 0;
            result.Data["returned_count"] = 0;
            result.Data["items"] = new List<Dictionary<string, object?>>();
            return result;
        }

        IEnumerable<MateNode> source = context.Mates.Items;
        if (string.Equals(parameters.Scope, "selection", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Selection.Items.Count == 0)
            {
                source = Array.Empty<MateNode>();
            }
            else
            {
                source = source.Where(item => item.Components.Any(component =>
                    context.Selection.Items.Any(selection =>
                        component.IndexOf(selection.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0)));
            }
        }

        int totalAvailable = source.Count();
        int offset = Math.Max(0, parameters.Offset);
        int limit = Math.Max(1, Math.Min(parameters.Limit, 200));
        List<MateNode> filtered = source.Skip(offset).Take(limit).ToList();
        var items = new List<Dictionary<string, object?>>();
        foreach (MateNode item in filtered)
        {
            items.Add(new Dictionary<string, object?>
            {
                ["name"] = item.Name,
                ["kind"] = item.Kind,
                ["entity_count"] = item.EntityCount,
                ["components"] = item.Components
            });
        }

        if (items.Count == 0)
        {
            result.Summary = "No mates found for the requested scope.";
        }

        bool hasMore = offset + items.Count < totalAvailable;
        result.Data["scope"] = string.IsNullOrWhiteSpace(parameters.Scope) ? "selection" : parameters.Scope.Trim();
        result.Data["total_count"] = totalAvailable;
        result.Data["offset"] = offset;
        result.Data["returned_count"] = items.Count;
        result.Data["has_more"] = hasMore;
        result.Data["items"] = items;
        return result;
    }
}
