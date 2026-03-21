using System.Collections.Generic;
using System.Linq;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetReferenceGraphTool : IReadOnlyTool<GetReferenceGraphParameters>
{
    public string ToolName => ToolNames.GetReferenceGraph;

    public ToolResult Execute(SessionContext context, GetReferenceGraphParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document for reference-graph inspection."
                : "Reference graph generated."
        };

        if (context.Document == null)
        {
            return result;
        }

        bool includeTransitive = parameters.Depth > 1;
        List<ReferenceNode> sourceItems = includeTransitive
            ? context.ReferenceGraph.TransitiveItems
            : context.ReferenceGraph.DirectItems;

        int limit = parameters.Limit > 0 ? parameters.Limit : 100;
        var items = new List<Dictionary<string, object?>>();
        foreach (ReferenceNode item in sourceItems.Take(limit))
        {
            var serialized = new Dictionary<string, object?>
            {
                ["name"] = item.Name,
                ["path"] = item.Path,
                ["is_read_only"] = item.IsReadOnly,
                ["exists_on_disk"] = item.ExistsOnDisk,
                ["is_broken"] = item.IsBroken
            };

            if (parameters.IncludeExternalReferences)
            {
                serialized["imported_path"] = item.ImportedPath;
            }

            items.Add(serialized);
        }

        if (items.Count == 0)
        {
            result.Summary = "No dependency references found for the active document.";
        }

        result.Data["depth_requested"] = parameters.Depth;
        result.Data["scope"] = includeTransitive ? "transitive" : "direct";
        result.Data["direct_count"] = context.ReferenceGraph.DirectCount;
        result.Data["transitive_count"] = context.ReferenceGraph.TransitiveCount;
        result.Data["broken_reference_count"] = context.ReferenceGraph.BrokenReferenceCount;
        result.Data["returned_count"] = items.Count;
        result.Data["has_imported_paths"] = items.Any(item => item.ContainsKey("imported_path") && item["imported_path"] != null);
        result.Data["items"] = items;
        return result;
    }
}
