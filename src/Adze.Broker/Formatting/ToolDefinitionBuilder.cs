using System;
using System.Collections.Generic;
using Adze.Broker.Models;
using Adze.Contracts.Tooling;

namespace Adze.Broker.Formatting;

public static class ToolDefinitionBuilder
{
    public static List<AgentToolDefinition> BuildReadToolDefinitions()
    {
        return new List<AgentToolDefinition>
        {
            Build(ToolNames.GetActiveDocument,
                "Returns the active document type, title, path, configuration, units, and state.",
                EmptySchema()),

            Build(ToolNames.GetDocumentSummary,
                "Returns a detailed summary of the active document including type, features, dimensions, diagnostics, and properties.",
                ObjectSchema(
                    Optional("include_diagnostics", "boolean", "Include rebuild diagnostics in the summary."),
                    Optional("include_properties", "boolean", "Include custom properties in the summary."))),

            Build(ToolNames.GetSelectionContext,
                "Returns the current selection in the active document, including selected entity types and owners.",
                ObjectSchema(
                    Optional("include_entity_details", "boolean", "Include detailed entity information."))),

            Build(ToolNames.GetFeatureTreeSlice,
                "Returns a slice of the feature tree centered on an anchor feature.",
                ObjectSchema(
                    Optional("anchor_name", "string", "Feature name to center the slice on."),
                    Optional("radius", "integer", "Number of features above and below the anchor to include."))),

            Build(ToolNames.GetDimensions,
                "Returns dimensions in the active document, optionally scoped to a feature.",
                ObjectSchema(
                    Optional("scope", "string", "Scope: 'document' for all, or a feature name to filter."),
                    Optional("include_driven", "boolean", "Include driven dimensions."))),

            Build(ToolNames.GetConfigurations,
                "Returns all configurations in the active document.",
                ObjectSchema(
                    Optional("include_suppression_state", "boolean", "Include feature suppression state per configuration."))),

            Build(ToolNames.GetCustomProperties,
                "Returns custom properties for the document and/or active configuration.",
                ObjectSchema(
                    Optional("scope", "string", "Scope: 'document', 'configuration', or 'both'."),
                    Optional("configuration_name", "string", "Configuration name to read properties from."))),

            Build(ToolNames.GetMates,
                "Returns mates in the active assembly document.",
                ObjectSchema(
                    Optional("scope", "string", "Scope: 'document' for all mates."),
                    Optional("limit", "integer", "Maximum number of mates to return."))),

            Build(ToolNames.GetRebuildDiagnostics,
                "Returns rebuild diagnostics, warnings, and missing references.",
                ObjectSchema(
                    Optional("include_missing_references", "boolean", "Include missing reference paths."),
                    Optional("include_warnings", "boolean", "Include rebuild warnings."))),

            Build(ToolNames.GetReferenceGraph,
                "Returns the reference graph showing file dependencies.",
                ObjectSchema(
                    Optional("depth", "integer", "Reference traversal depth."),
                    Optional("include_external_references", "boolean", "Include external file references.")))
        };
    }

    private static AgentToolDefinition Build(string name, string description, Dictionary<string, object?> parameterSchema)
    {
        return new AgentToolDefinition
        {
            Name = name,
            Description = description,
            ParameterSchema = parameterSchema
        };
    }

    private static Dictionary<string, object?> EmptySchema()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>(),
            ["required"] = new List<string>()
        };
    }

    private static Dictionary<string, object?> ObjectSchema(params Dictionary<string, object?>[] properties)
    {
        var props = new Dictionary<string, object?>();
        foreach (var prop in properties)
        {
            if (prop.TryGetValue("_name", out object? nameValue) && nameValue is string name)
            {
                var copy = new Dictionary<string, object?>(prop);
                copy.Remove("_name");
                props[name] = copy;
            }
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new List<string>()
        };
    }

    private static Dictionary<string, object?> Optional(string name, string type, string description)
    {
        return new Dictionary<string, object?>
        {
            ["_name"] = name,
            ["type"] = type,
            ["description"] = description
        };
    }
}
