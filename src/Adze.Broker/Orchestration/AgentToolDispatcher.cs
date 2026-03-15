using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Adze.Broker.Abstractions;
using Adze.Broker.Models;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools;
using Adze.Tools.Write;

namespace Adze.Broker.Orchestration;

public sealed class AgentToolDispatcher : IToolExecutor
{
    private readonly GroundingToolCatalog _catalog;
    private readonly JavaScriptSerializer _serializer;

    public AgentToolDispatcher()
        : this(ToolCatalog.CreateGroundingCatalog())
    {
    }

    public AgentToolDispatcher(GroundingToolCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    }

    public AgentToolResult Execute(
        string toolName,
        Dictionary<string, object?> arguments,
        ToolExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return BuildErrorResult(string.Empty, string.Empty, "Tool name is null or empty.");
        }

        SessionContext? session = context?.SessionContext;
        if (session == null)
        {
            return BuildErrorResult(string.Empty, toolName, "No SessionContext available in ToolExecutionContext.");
        }

        try
        {
            ToolResult toolResult = DispatchTool(toolName, arguments ?? new Dictionary<string, object?>(), session);
            string outputJson = _serializer.Serialize(new Dictionary<string, object?>
            {
                ["tool_name"] = toolResult.ToolName,
                ["success"] = toolResult.Success,
                ["summary"] = toolResult.Summary,
                ["warnings"] = toolResult.Warnings,
                ["data"] = toolResult.Data
            });

            return new AgentToolResult
            {
                ToolName = toolName,
                OutputJson = outputJson,
                IsError = false
            };
        }
        catch (Exception ex)
        {
            return BuildErrorResult(string.Empty, toolName, ex.Message);
        }
    }

    private ToolResult DispatchTool(
        string toolName,
        Dictionary<string, object?> arguments,
        SessionContext session)
    {
        switch (toolName)
        {
            case ToolNames.GetActiveDocument:
                return _catalog.ActiveDocument.Execute(session, new EmptyParameters());

            case ToolNames.GetDocumentSummary:
                return _catalog.DocumentSummary.Execute(session, DeserializeDocumentSummaryParams(arguments));

            case ToolNames.GetSelectionContext:
                return _catalog.SelectionContext.Execute(session, DeserializeSelectionContextParams(arguments));

            case ToolNames.GetFeatureTreeSlice:
                return _catalog.FeatureTreeSlice.Execute(session, DeserializeFeatureTreeSliceParams(arguments));

            case ToolNames.GetDimensions:
                return _catalog.Dimensions.Execute(session, DeserializeDimensionsParams(arguments));

            case ToolNames.GetConfigurations:
                return _catalog.Configurations.Execute(session, DeserializeConfigurationsParams(arguments));

            case ToolNames.GetCustomProperties:
                return _catalog.CustomProperties.Execute(session, DeserializeCustomPropertiesParams(arguments));

            case ToolNames.GetMates:
                return _catalog.Mates.Execute(session, DeserializeMatesParams(arguments));

            case ToolNames.GetRebuildDiagnostics:
                return _catalog.RebuildDiagnostics.Execute(session, DeserializeRebuildDiagnosticsParams(arguments));

            case ToolNames.GetReferenceGraph:
                return _catalog.ReferenceGraph.Execute(session, DeserializeReferenceGraphParams(arguments));

            // Write tools — return preview result (actual apply requires host confirmation)
            case ToolNames.SetCustomProperty:
                return DispatchWritePreview(new SetCustomPropertyTool(), DeserializeSetCustomPropertyParams(arguments), session);

            case ToolNames.SetDimensionValue:
                return DispatchWritePreview(new SetDimensionValueTool(), DeserializeSetDimensionValueParams(arguments), session);

            case ToolNames.SuppressFeature:
                return DispatchWritePreview(new SuppressFeatureTool(), DeserializeSuppressFeatureParams(arguments), session);

            case ToolNames.UnsuppressFeature:
                return DispatchWritePreview(new UnsuppressFeatureTool(), DeserializeUnsuppressFeatureParams(arguments), session);

            default:
                throw new InvalidOperationException($"Unknown tool: {toolName}");
        }
    }

    private ToolResult DispatchWritePreview<TParams>(Contracts.Abstractions.IWriteTool<TParams> tool, TParams parameters, SessionContext session)
    {
        WritePreview preview = tool.Preview(session, parameters);
        var data = new Dictionary<string, object?>
        {
            ["preview"] = true,
            ["requires_confirmation"] = true,
            ["tool_name"] = preview.ToolName,
            ["summary"] = preview.Summary,
            ["undo_label"] = tool.BuildUndoLabel(parameters),
            ["changes"] = preview.Changes.ConvertAll(c => new Dictionary<string, object?>
            {
                ["target"] = c.TargetLabel,
                ["before"] = c.BeforeValue,
                ["after"] = c.AfterValue
            }),
            ["warnings"] = preview.Warnings
        };

        return new ToolResult
        {
            ToolName = preview.ToolName,
            Success = true,
            Summary = preview.Summary,
            Warnings = preview.Warnings,
            Data = data
        };
    }

    private static SetCustomPropertyParameters DeserializeSetCustomPropertyParams(Dictionary<string, object?> args)
    {
        var parameters = new SetCustomPropertyParameters();
        if (TryGetString(args, "property_name", out string? name))
            parameters.PropertyName = name!;
        if (TryGetString(args, "property_value", out string? value))
            parameters.PropertyValue = value!;
        if (TryGetString(args, "scope", out string? scope))
            parameters.Scope = scope!;
        if (TryGetString(args, "configuration_name", out string? configName))
            parameters.ConfigurationName = configName;
        return parameters;
    }

    private static SetDimensionValueParameters DeserializeSetDimensionValueParams(Dictionary<string, object?> args)
    {
        var parameters = new SetDimensionValueParameters();
        if (TryGetString(args, "dimension_full_name", out string? fullName))
            parameters.DimensionFullName = fullName!;
        if (TryGetDouble(args, "new_value", out double newValue))
            parameters.NewValue = newValue;
        if (TryGetString(args, "configuration_name", out string? configName))
            parameters.ConfigurationName = configName;
        return parameters;
    }

    private static SuppressFeatureParameters DeserializeSuppressFeatureParams(Dictionary<string, object?> args)
    {
        var parameters = new SuppressFeatureParameters();
        if (TryGetString(args, "feature_name", out string? name))
            parameters.FeatureName = name!;
        if (TryGetString(args, "configuration_name", out string? configName))
            parameters.ConfigurationName = configName;
        return parameters;
    }

    private static UnsuppressFeatureParameters DeserializeUnsuppressFeatureParams(Dictionary<string, object?> args)
    {
        var parameters = new UnsuppressFeatureParameters();
        if (TryGetString(args, "feature_name", out string? name))
            parameters.FeatureName = name!;
        if (TryGetString(args, "configuration_name", out string? configName))
            parameters.ConfigurationName = configName;
        return parameters;
    }

    private static GetDocumentSummaryParameters DeserializeDocumentSummaryParams(Dictionary<string, object?> args)
    {
        var parameters = new GetDocumentSummaryParameters();
        if (TryGetBool(args, "include_diagnostics", out bool includeDiag))
            parameters.IncludeDiagnostics = includeDiag;
        if (TryGetBool(args, "include_properties", out bool includeProps))
            parameters.IncludeProperties = includeProps;
        return parameters;
    }

    private static GetSelectionContextParameters DeserializeSelectionContextParams(Dictionary<string, object?> args)
    {
        var parameters = new GetSelectionContextParameters();
        if (TryGetBool(args, "include_entity_details", out bool includeDetails))
            parameters.IncludeEntityDetails = includeDetails;
        return parameters;
    }

    private static GetFeatureTreeSliceParameters DeserializeFeatureTreeSliceParams(Dictionary<string, object?> args)
    {
        var parameters = new GetFeatureTreeSliceParameters();
        if (TryGetString(args, "anchor_name", out string? anchorName))
            parameters.AnchorName = anchorName;
        if (TryGetInt(args, "radius", out int radius))
            parameters.Radius = radius;
        return parameters;
    }

    private static GetDimensionsParameters DeserializeDimensionsParams(Dictionary<string, object?> args)
    {
        var parameters = new GetDimensionsParameters();
        if (TryGetString(args, "scope", out string? scope))
            parameters.Scope = scope!;
        if (TryGetBool(args, "include_driven", out bool includeDriven))
            parameters.IncludeDriven = includeDriven;
        return parameters;
    }

    private static GetConfigurationsParameters DeserializeConfigurationsParams(Dictionary<string, object?> args)
    {
        var parameters = new GetConfigurationsParameters();
        if (TryGetBool(args, "include_suppression_state", out bool includeState))
            parameters.IncludeSuppressionState = includeState;
        return parameters;
    }

    private static GetCustomPropertiesParameters DeserializeCustomPropertiesParams(Dictionary<string, object?> args)
    {
        var parameters = new GetCustomPropertiesParameters();
        if (TryGetString(args, "scope", out string? scope))
            parameters.Scope = scope!;
        if (TryGetString(args, "configuration_name", out string? configName))
            parameters.ConfigurationName = configName;
        return parameters;
    }

    private static GetMatesParameters DeserializeMatesParams(Dictionary<string, object?> args)
    {
        var parameters = new GetMatesParameters();
        if (TryGetString(args, "scope", out string? scope))
            parameters.Scope = scope!;
        if (TryGetInt(args, "limit", out int limit))
            parameters.Limit = limit;
        return parameters;
    }

    private static GetRebuildDiagnosticsParameters DeserializeRebuildDiagnosticsParams(Dictionary<string, object?> args)
    {
        var parameters = new GetRebuildDiagnosticsParameters();
        if (TryGetBool(args, "include_warnings", out bool includeWarnings))
            parameters.IncludeWarnings = includeWarnings;
        if (TryGetBool(args, "include_missing_references", out bool includeMissing))
            parameters.IncludeMissingReferences = includeMissing;
        return parameters;
    }

    private static GetReferenceGraphParameters DeserializeReferenceGraphParams(Dictionary<string, object?> args)
    {
        var parameters = new GetReferenceGraphParameters();
        if (TryGetInt(args, "depth", out int depth))
            parameters.Depth = depth;
        if (TryGetBool(args, "include_external_references", out bool includeExternal))
            parameters.IncludeExternalReferences = includeExternal;
        return parameters;
    }

    // --- Argument extraction helpers ---

    private static bool TryGetBool(Dictionary<string, object?> args, string key, out bool value)
    {
        value = false;
        if (!args.TryGetValue(key, out object? raw) || raw == null)
            return false;

        if (raw is bool b)
        {
            value = b;
            return true;
        }

        if (bool.TryParse(raw.ToString(), out bool parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryGetString(Dictionary<string, object?> args, string key, out string? value)
    {
        value = null;
        if (!args.TryGetValue(key, out object? raw) || raw == null)
            return false;

        value = raw.ToString();
        return value != null;
    }

    private static bool TryGetInt(Dictionary<string, object?> args, string key, out int value)
    {
        value = 0;
        if (!args.TryGetValue(key, out object? raw) || raw == null)
            return false;

        if (raw is int i)
        {
            value = i;
            return true;
        }

        if (int.TryParse(raw.ToString(), out int parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryGetDouble(Dictionary<string, object?> args, string key, out double value)
    {
        value = 0.0;
        if (!args.TryGetValue(key, out object? raw) || raw == null)
            return false;

        if (raw is double d)
        {
            value = d;
            return true;
        }

        if (raw is int i)
        {
            value = i;
            return true;
        }

        if (raw is decimal dec)
        {
            value = (double)dec;
            return true;
        }

        if (double.TryParse(raw.ToString(), out double parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static AgentToolResult BuildErrorResult(string toolCallId, string toolName, string message)
    {
        return new AgentToolResult
        {
            ToolCallId = toolCallId,
            ToolName = toolName,
            OutputJson = "{\"error\":\"" + EscapeJsonString(message) + "\"}",
            IsError = true
        };
    }

    private static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
