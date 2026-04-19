using System.Collections.Generic;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetConfigurationsTool : IReadOnlyTool<GetConfigurationsParameters>
{
    public string ToolName => ToolNames.GetConfigurations;

    public ToolResult Execute(SessionContext context, GetConfigurationsParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document for configuration inspection."
                : "Read configurations."
        };

        if (context.Document == null)
        {
            return result;
        }

        var items = new List<Dictionary<string, object?>>();
        foreach (ConfigurationItem configuration in context.Configurations.Items)
        {
            var item = new Dictionary<string, object?>
            {
                ["name"] = configuration.Name,
                ["is_active"] = configuration.IsActive
            };

            if (parameters.IncludeSuppressionState)
            {
                item["state"] = configuration.IsActive ? "active" : "available";
            }

            items.Add(item);
        }

        result.Data["active_name"] = context.Configurations.ActiveName;
        result.Data["count"] = context.Configurations.Count;
        result.Data["items"] = items;
        return result;
    }
}
