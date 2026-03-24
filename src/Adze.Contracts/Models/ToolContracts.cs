using System.Collections.Generic;
using Adze.Contracts.Enums;
using Adze.Contracts.Tooling;

namespace Adze.Contracts.Models;

public sealed class ToolRequest<TParameters>
{
    public string ToolName { get; set; } = string.Empty;

    public ActionClass ActionClass { get; set; } = ActionClass.Green;

    public TParameters Parameters { get; set; } = default!;
}

public sealed class ToolResult
{
    public string ToolName { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Summary { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new();

    public Dictionary<string, object?> Data { get; set; } = new();
}

public sealed class EmptyParameters;

public sealed class GetDocumentSummaryParameters
{
    public bool IncludeDiagnostics { get; set; } = true;

    public bool IncludeProperties { get; set; } = true;
}

public sealed class GetSelectionContextParameters
{
    public bool IncludeEntityDetails { get; set; } = true;
}

public sealed class GetFeatureTreeSliceParameters
{
    public string? AnchorName { get; set; }

    public int Radius { get; set; } = 8;
}

public sealed class GetDimensionsParameters
{
    public string Scope { get; set; } = "selection";

    public bool IncludeDriven { get; set; } = true;

    public int Offset { get; set; }

    public int Limit { get; set; } = 50;
}

public sealed class GetConfigurationsParameters
{
    public bool IncludeSuppressionState { get; set; } = true;
}

public sealed class GetCustomPropertiesParameters
{
    public string Scope { get; set; } = "both";

    public string? ConfigurationName { get; set; }
}

public sealed class GetMatesParameters
{
    public string Scope { get; set; } = "selection";

    public int Offset { get; set; }

    public int Limit { get; set; } = 50;
}

public sealed class GetRebuildDiagnosticsParameters
{
    public bool IncludeWarnings { get; set; } = true;

    public bool IncludeMissingReferences { get; set; } = true;
}

public sealed class GetReferenceGraphParameters
{
    public int Depth { get; set; } = 1;

    public bool IncludeExternalReferences { get; set; } = true;

    public int Limit { get; set; } = 100;
}

// ── Write tool parameters (Phase 4) ──

public sealed class SetCustomPropertyParameters
{
    public string PropertyName { get; set; } = string.Empty;

    public string PropertyValue { get; set; } = string.Empty;

    public string Scope { get; set; } = "document";

    public string? ConfigurationName { get; set; }
}

public sealed class SetDimensionValueParameters
{
    public string DimensionFullName { get; set; } = string.Empty;

    public double NewValue { get; set; }

    public string? ConfigurationName { get; set; }
}

public sealed class SuppressFeatureParameters
{
    public string FeatureName { get; set; } = string.Empty;

    public string? ConfigurationName { get; set; }
}

public sealed class UnsuppressFeatureParameters
{
    public string FeatureName { get; set; } = string.Empty;

    public string? ConfigurationName { get; set; }
}

public sealed class RenameObjectParameters
{
    public string ObjectType { get; set; } = "feature";

    public string CurrentName { get; set; } = string.Empty;

    public string NewName { get; set; } = string.Empty;
}

public sealed class InsertComponentParameters
{
    public string ComponentPath { get; set; } = string.Empty;

    public string? ConfigurationName { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }
}

public sealed class CreateDrawingViewParameters
{
    public string ViewType { get; set; } = "front";

    public string? ModelPath { get; set; }

    public double X { get; set; } = 0.15;

    public double Y { get; set; } = 0.15;

    public double Scale { get; set; } = 1.0;
}

public static class ToolRequests
{
    public static ToolRequest<EmptyParameters> ActiveDocument() => new()
    {
        ToolName = ToolNames.GetActiveDocument,
        ActionClass = ActionClass.Green,
        Parameters = new EmptyParameters()
    };
}
