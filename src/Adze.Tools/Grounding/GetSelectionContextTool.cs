using System.Collections.Generic;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetSelectionContextTool : IReadOnlyTool<GetSelectionContextParameters>
{
    public string ToolName => ToolNames.GetSelectionContext;

    public ToolResult Execute(SessionContext context, GetSelectionContextParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = true,
            Summary = context.Selection.Count == 0
                ? "No current selection."
                : "Selection context generated."
        };

        result.Data["count"] = context.Selection.Count;

        if (parameters.IncludeEntityDetails)
        {
            var details = new List<Dictionary<string, object?>>();
            foreach (SelectionItem item in context.Selection.Items)
            {
                details.Add(new Dictionary<string, object?>
                {
                    ["kind"] = item.Kind,
                    ["name"] = item.Name,
                    ["owner"] = item.Owner
                });
            }

            result.Data["items"] = details;
        }

        return result;
    }
}
