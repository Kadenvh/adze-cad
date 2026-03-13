using System;
using System.Collections.Generic;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools.Abstractions;

namespace Adze.Tools.Grounding;

public sealed class GetCustomPropertiesTool : IReadOnlyTool<GetCustomPropertiesParameters>
{
    private const string DocumentPrefix = "document_custom.";
    private const string ConfigurationPrefix = "configuration_custom.";

    public string ToolName => ToolNames.GetCustomProperties;

    public ToolResult Execute(SessionContext context, GetCustomPropertiesParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = context.Document != null,
            Summary = context.Document == null
                ? "No active document for custom property inspection."
                : "Custom property set generated."
        };

        if (context.Document == null)
        {
            return result;
        }

        string requestedScope = string.IsNullOrWhiteSpace(parameters.Scope) ? "both" : parameters.Scope.Trim();
        string configurationName = string.IsNullOrWhiteSpace(parameters.ConfigurationName)
            ? context.Configurations.ActiveName
            : parameters.ConfigurationName?.Trim() ?? string.Empty;

        var documentProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var configurationProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        string configurationPrefix = ConfigurationPrefix + configurationName + ".";

        foreach (KeyValuePair<string, object?> entry in context.Properties)
        {
            if (ShouldIncludeDocumentScope(requestedScope) && entry.Key.StartsWith(DocumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                documentProperties[entry.Key.Substring(DocumentPrefix.Length)] = entry.Value;
            }

            if (ShouldIncludeConfigurationScope(requestedScope) && entry.Key.StartsWith(configurationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                configurationProperties[entry.Key.Substring(configurationPrefix.Length)] = entry.Value;
            }
        }

        int totalCount = documentProperties.Count + configurationProperties.Count;
        if (totalCount == 0)
        {
            result.Summary = "No custom properties found for the requested scope.";
        }

        result.Data["scope"] = requestedScope;
        result.Data["document_count"] = documentProperties.Count;
        result.Data["configuration_name"] = configurationName;
        result.Data["configuration_count"] = configurationProperties.Count;
        result.Data["document_properties"] = documentProperties;
        result.Data["configuration_properties"] = configurationProperties;
        return result;
    }

    private static bool ShouldIncludeDocumentScope(string scope)
    {
        return string.Equals(scope, "both", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(scope, "document", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludeConfigurationScope(string scope)
    {
        return string.Equals(scope, "both", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(scope, "configuration", StringComparison.OrdinalIgnoreCase);
    }
}
