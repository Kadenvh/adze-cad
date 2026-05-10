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

// ── Mesh tool parameters (Phase 11) ──

public sealed class AnalyzeMeshFileParameters
{
    /// <summary>Absolute path to the mesh file (currently STL — binary or ASCII).</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Cap the advisory list length to keep tool results compact in the agent loop.</summary>
    public int MaxAdvisories { get; set; } = 20;
}

public sealed class ImportMeshParameters
{
    /// <summary>Absolute path to the mesh file (.stl).</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Strategy for the import. <c>graphics_body</c> opens the mesh as a non-editable
    /// graphics body via SW's built-in import (fast, robust). Other strategies
    /// (mesh-BREP, ScanTo3D auto-surface, Adze reverse-engineer) are reserved
    /// for follow-on capability and currently fall back to graphics_body.
    /// </summary>
    public string Strategy { get; set; } = "graphics_body";

    /// <summary>
    /// Source units. STL itself is unitless; SW interprets values as document units.
    /// Common: <c>mm</c>, <c>cm</c>, <c>m</c>, <c>in</c>. When unspecified the SW
    /// import dialog default is honored.
    /// </summary>
    public string? SourceUnits { get; set; }

    /// <summary>
    /// Run primitive fitting (currently planes only) during analysis. The
    /// agent uses the result to decide whether reverse-engineering is
    /// feasible. Off by default because fitting is an O(T log T) extra pass
    /// that's overkill for the simple "open as graphics body" case.
    /// </summary>
    public bool IncludePrimitiveAnalysis { get; set; }
}

// ── Evaluate-tab parity read tools (Phase 11) ──

public sealed class GetMassPropertiesParameters
{
    /// <summary>Optional configuration name. If empty, uses the active configuration.</summary>
    public string? ConfigurationName { get; set; }

    /// <summary>Whether to compute principal axes / moments of inertia.</summary>
    public bool IncludePrincipalAxes { get; set; } = true;
}

public sealed class MeasureDistanceParameters
{
    /// <summary>
    /// SelectByID2-compatible name of the first entity, or empty to use the
    /// current selection. Format: <c>name@scope</c> (e.g. <c>Face1@Boss-Extrude1</c>).
    /// </summary>
    public string? EntityA { get; set; }

    /// <summary>Type of EntityA: face, edge, vertex, or sketchsegment.</summary>
    public string? EntityATypeHint { get; set; }

    /// <summary>SelectByID2-compatible name of the second entity, or empty.</summary>
    public string? EntityB { get; set; }

    public string? EntityBTypeHint { get; set; }
}

// ── Sketch authoring write tools (Phase 11) ──

public sealed class CreateSketchParameters
{
    /// <summary>
    /// SelectByID2-style reference name to anchor the sketch to. Examples:
    /// <c>Front Plane</c>, <c>Top Plane</c>, <c>Right Plane</c>, or a planar
    /// face name like <c>Face1@Boss-Extrude1</c>.
    /// </summary>
    public string ReferenceName { get; set; } = "Front Plane";

    /// <summary>
    /// Optional new name to assign to the sketch after creation. SW assigns
    /// a default name like <c>Sketch1</c>; this lets callers (especially the
    /// agent) give the sketch a semantically meaningful name up front.
    /// </summary>
    public string? SketchName { get; set; }
}

public sealed class AddSketchLineParameters
{
    /// <summary>Name of the sketch to add the line to. Empty = active sketch.</summary>
    public string? SketchName { get; set; }

    public double StartXMm { get; set; }
    public double StartYMm { get; set; }
    public double EndXMm { get; set; }
    public double EndYMm { get; set; }
}

public sealed class AddSketchCircleParameters
{
    /// <summary>Name of the sketch to add the circle to. Empty = active sketch.</summary>
    public string? SketchName { get; set; }

    public double CenterXMm { get; set; }
    public double CenterYMm { get; set; }
    public double RadiusMm { get; set; } = 1.0;
}

public sealed class AddSketchDimensionParameters
{
    /// <summary>Name of the sketch containing the entity. Empty = active sketch.</summary>
    public string? SketchName { get; set; }

    /// <summary>SelectByID2 name of the entity to dimension (e.g. <c>Line1</c>, <c>Arc1</c>).</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// Dimension type hint. SW-supported: <c>length</c>, <c>radius</c>,
    /// <c>diameter</c>, <c>angle</c>, <c>distance</c>. The actual placement
    /// uses the entity-appropriate dimension call.
    /// </summary>
    public string DimensionType { get; set; } = "length";

    /// <summary>Target value for the dimension in mm (or degrees for angle).</summary>
    public double TargetValue { get; set; }
}

// ── Drawing creation (Phase 11) ──

public sealed class CreateDrawingDocumentParameters
{
    /// <summary>
    /// Path to the part / assembly to base the drawing on. If empty, the
    /// currently active document is used. Required if no document is active.
    /// </summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Override drawing template path (.drwdot). If empty, SW's configured
    /// default drawing template is used.
    /// </summary>
    public string? TemplatePath { get; set; }

    /// <summary>
    /// When true, populates the new drawing with standard 3rd-angle views
    /// (front + top + right + isometric) via <c>Create3rdAngleViews2</c>.
    /// When false, creates a blank drawing for manual view placement.
    /// </summary>
    public bool IncludeStandardViews { get; set; } = true;
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
