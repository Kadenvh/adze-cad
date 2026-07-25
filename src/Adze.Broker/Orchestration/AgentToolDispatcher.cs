using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Adze.Broker.Abstractions;
using Adze.Broker.Models;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tools;
using Adze.Tools.Grounding;
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
            ToolResult toolResult = DispatchTool(toolName, arguments ?? new Dictionary<string, object?>(), session, context?.Application);
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
        SessionContext session,
        object? application)
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

            case ToolNames.RenameObject:
                return DispatchWritePreview(new RenameObjectTool(), DeserializeRenameObjectParams(arguments), session);

            case ToolNames.InsertComponent:
                return DispatchWritePreview(new InsertComponentTool(), DeserializeInsertComponentParams(arguments), session);

            case ToolNames.CreateDrawingView:
                return DispatchWritePreview(new CreateDrawingViewTool(), DeserializeCreateDrawingViewParams(arguments), session);

            case ToolNames.SearchProjectFiles:
                return _catalog.SearchProjectFiles.Execute(session, DeserializeSearchProjectFilesParams(arguments));

            case ToolNames.AnalyzeMeshFile:
                return _catalog.AnalyzeMeshFile.Execute(session, DeserializeAnalyzeMeshFileParams(arguments));

            case ToolNames.ImportMesh:
                return DispatchWritePreview(new ImportMeshTool(), DeserializeImportMeshParams(arguments), session);

            case ToolNames.GetMassProperties:
                return _catalog.MassProperties.Execute(session, application, DeserializeMassPropertiesParams(arguments));

            case ToolNames.MeasureDistance:
                return _catalog.MeasureDistance.Execute(session, application, DeserializeMeasureDistanceParams(arguments));

            case ToolNames.CreateSketch:
                return DispatchWritePreview(new CreateSketchTool(), DeserializeCreateSketchParams(arguments), session);

            case ToolNames.AddSketchLine:
                return DispatchWritePreview(new AddSketchLineTool(), DeserializeAddSketchLineParams(arguments), session);

            case ToolNames.AddSketchCircle:
                return DispatchWritePreview(new AddSketchCircleTool(), DeserializeAddSketchCircleParams(arguments), session);

            case ToolNames.AddSketchDimension:
                return DispatchWritePreview(new AddSketchDimensionTool(), DeserializeAddSketchDimensionParams(arguments), session);

            case ToolNames.CreateDrawingDocument:
                return DispatchWritePreview(new CreateDrawingDocumentTool(), DeserializeCreateDrawingDocumentParams(arguments), session);

            case ToolNames.CreateExtrude:
                return DispatchWritePreview(new CreateExtrudeTool(), DeserializeCreateExtrudeParams(arguments), session);

            case ToolNames.CreateRevolve:
                return DispatchWritePreview(new CreateRevolveTool(), DeserializeCreateRevolveParams(arguments), session);

            case ToolNames.CreateFillet:
                return DispatchWritePreview(new CreateFilletTool(), DeserializeCreateFilletParams(arguments), session);

            case ToolNames.CreateChamfer:
                return DispatchWritePreview(new CreateChamferTool(), DeserializeCreateChamferParams(arguments), session);

            case ToolNames.CreatePatternLinear:
                return DispatchWritePreview(new CreatePatternLinearTool(), DeserializeCreatePatternLinearParams(arguments), session);

            case ToolNames.CreateReferencePlane:
                return DispatchWritePreview(new CreateReferencePlaneTool(), DeserializeCreateReferencePlaneParams(arguments), session);

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

    private static CreateDrawingViewParameters DeserializeCreateDrawingViewParams(Dictionary<string, object?> args)
    {
        var parameters = new CreateDrawingViewParameters();
        if (TryGetString(args, "view_type", out string? viewType))
            parameters.ViewType = viewType!;
        if (TryGetString(args, "model_path", out string? modelPath))
            parameters.ModelPath = modelPath;
        if (TryGetDouble(args, "x", out double x))
            parameters.X = x;
        if (TryGetDouble(args, "y", out double y))
            parameters.Y = y;
        if (TryGetDouble(args, "scale", out double scale))
            parameters.Scale = scale;
        return parameters;
    }

    private static InsertComponentParameters DeserializeInsertComponentParams(Dictionary<string, object?> args)
    {
        var parameters = new InsertComponentParameters();
        if (TryGetString(args, "component_path", out string? path))
            parameters.ComponentPath = path!;
        if (TryGetString(args, "configuration_name", out string? configName))
            parameters.ConfigurationName = configName;
        if (TryGetDouble(args, "x", out double x))
            parameters.X = x;
        if (TryGetDouble(args, "y", out double y))
            parameters.Y = y;
        if (TryGetDouble(args, "z", out double z))
            parameters.Z = z;
        return parameters;
    }

    private static RenameObjectParameters DeserializeRenameObjectParams(Dictionary<string, object?> args)
    {
        var parameters = new RenameObjectParameters();
        if (TryGetString(args, "object_type", out string? objectType))
            parameters.ObjectType = objectType!;
        if (TryGetString(args, "current_name", out string? currentName))
            parameters.CurrentName = currentName!;
        if (TryGetString(args, "new_name", out string? newName))
            parameters.NewName = newName!;
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
        if (TryGetInt(args, "offset", out int offset))
            parameters.Offset = offset;
        if (TryGetInt(args, "limit", out int limit))
            parameters.Limit = limit;
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
        if (TryGetInt(args, "offset", out int mateOffset))
            parameters.Offset = mateOffset;
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
        if (TryGetInt(args, "limit", out int limit))
            parameters.Limit = limit;
        return parameters;
    }

    private static SearchProjectFilesParameters DeserializeSearchProjectFilesParams(Dictionary<string, object?> args)
    {
        var parameters = new SearchProjectFilesParameters();
        if (TryGetString(args, "root_folder_path", out string? rootPath))
            parameters.RootFolderPath = rootPath!;
        if (TryGetString(args, "file_type", out string? fileType))
            parameters.FileType = fileType;
        if (TryGetString(args, "path_pattern", out string? pathPattern))
            parameters.PathPattern = pathPattern;
        if (TryGetString(args, "property_name", out string? propName))
            parameters.PropertyName = propName;
        if (TryGetString(args, "property_value", out string? propValue))
            parameters.PropertyValue = propValue;
        if (TryGetString(args, "keyword", out string? keyword))
            parameters.Keyword = keyword;
        if (TryGetInt(args, "max_results", out int maxResults))
            parameters.MaxResults = maxResults;
        return parameters;
    }

    private static AnalyzeMeshFileParameters DeserializeAnalyzeMeshFileParams(Dictionary<string, object?> args)
    {
        var parameters = new AnalyzeMeshFileParameters();
        if (TryGetString(args, "file_path", out string? path))
            parameters.FilePath = path!;
        if (TryGetInt(args, "max_advisories", out int max))
            parameters.MaxAdvisories = max;
        return parameters;
    }

    private static ImportMeshParameters DeserializeImportMeshParams(Dictionary<string, object?> args)
    {
        var parameters = new ImportMeshParameters();
        if (TryGetString(args, "file_path", out string? path))
            parameters.FilePath = path!;
        if (TryGetString(args, "strategy", out string? strategy))
            parameters.Strategy = strategy!;
        if (TryGetString(args, "source_units", out string? units))
            parameters.SourceUnits = units;
        if (TryGetBool(args, "include_primitive_analysis", out bool includePrimitives))
            parameters.IncludePrimitiveAnalysis = includePrimitives;
        return parameters;
    }

    private static GetMassPropertiesParameters DeserializeMassPropertiesParams(Dictionary<string, object?> args)
    {
        var parameters = new GetMassPropertiesParameters();
        if (TryGetString(args, "configuration_name", out string? configName))
            parameters.ConfigurationName = configName;
        if (TryGetBool(args, "include_principal_axes", out bool includeAxes))
            parameters.IncludePrincipalAxes = includeAxes;
        return parameters;
    }

    private static MeasureDistanceParameters DeserializeMeasureDistanceParams(Dictionary<string, object?> args)
    {
        var parameters = new MeasureDistanceParameters();
        if (TryGetString(args, "entity_a", out string? a))
            parameters.EntityA = a;
        if (TryGetString(args, "entity_a_type", out string? aType))
            parameters.EntityATypeHint = aType;
        if (TryGetString(args, "entity_b", out string? b))
            parameters.EntityB = b;
        if (TryGetString(args, "entity_b_type", out string? bType))
            parameters.EntityBTypeHint = bType;
        return parameters;
    }

    private static CreateSketchParameters DeserializeCreateSketchParams(Dictionary<string, object?> args)
    {
        var parameters = new CreateSketchParameters();
        if (TryGetString(args, "reference_name", out string? refName))
            parameters.ReferenceName = refName!;
        if (TryGetString(args, "sketch_name", out string? sketchName))
            parameters.SketchName = sketchName;
        return parameters;
    }

    private static AddSketchLineParameters DeserializeAddSketchLineParams(Dictionary<string, object?> args)
    {
        var parameters = new AddSketchLineParameters();
        if (TryGetString(args, "sketch_name", out string? sketchName))
            parameters.SketchName = sketchName;
        if (TryGetDouble(args, "start_x_mm", out double sx)) parameters.StartXMm = sx;
        if (TryGetDouble(args, "start_y_mm", out double sy)) parameters.StartYMm = sy;
        if (TryGetDouble(args, "end_x_mm", out double ex)) parameters.EndXMm = ex;
        if (TryGetDouble(args, "end_y_mm", out double ey)) parameters.EndYMm = ey;
        return parameters;
    }

    private static AddSketchCircleParameters DeserializeAddSketchCircleParams(Dictionary<string, object?> args)
    {
        var parameters = new AddSketchCircleParameters();
        if (TryGetString(args, "sketch_name", out string? sketchName))
            parameters.SketchName = sketchName;
        if (TryGetDouble(args, "center_x_mm", out double cx)) parameters.CenterXMm = cx;
        if (TryGetDouble(args, "center_y_mm", out double cy)) parameters.CenterYMm = cy;
        if (TryGetDouble(args, "radius_mm", out double r)) parameters.RadiusMm = r;
        return parameters;
    }

    private static AddSketchDimensionParameters DeserializeAddSketchDimensionParams(Dictionary<string, object?> args)
    {
        var parameters = new AddSketchDimensionParameters();
        if (TryGetString(args, "sketch_name", out string? sketchName))
            parameters.SketchName = sketchName;
        if (TryGetString(args, "entity_name", out string? entityName))
            parameters.EntityName = entityName!;
        if (TryGetString(args, "dimension_type", out string? dimType))
            parameters.DimensionType = dimType!;
        if (TryGetDouble(args, "target_value", out double v))
            parameters.TargetValue = v;
        return parameters;
    }

    private static CreateDrawingDocumentParameters DeserializeCreateDrawingDocumentParams(Dictionary<string, object?> args)
    {
        var parameters = new CreateDrawingDocumentParameters();
        if (TryGetString(args, "model_path", out string? modelPath))
            parameters.ModelPath = modelPath;
        if (TryGetString(args, "template_path", out string? templatePath))
            parameters.TemplatePath = templatePath;
        if (TryGetBool(args, "include_standard_views", out bool includeViews))
            parameters.IncludeStandardViews = includeViews;
        return parameters;
    }

    private static CreateExtrudeParameters DeserializeCreateExtrudeParams(Dictionary<string, object?> args)
    {
        var parameters = new CreateExtrudeParameters();
        if (TryGetString(args, "sketch_name", out string? sketchName))
            parameters.SketchName = sketchName;
        if (TryGetDouble(args, "depth_mm", out double depth))
            parameters.DepthMm = depth;
        if (TryGetString(args, "end_condition", out string? endCondition) && !string.IsNullOrWhiteSpace(endCondition))
            parameters.EndCondition = endCondition!;
        if (TryGetBool(args, "reverse", out bool reverse))
            parameters.Reverse = reverse;
        if (TryGetString(args, "feature_name", out string? featureName))
            parameters.FeatureName = featureName;
        return parameters;
    }

    private static CreateRevolveParameters DeserializeCreateRevolveParams(Dictionary<string, object?> args)
    {
        var parameters = new CreateRevolveParameters();
        if (TryGetString(args, "sketch_name", out string? sketchName))
            parameters.SketchName = sketchName;
        if (TryGetString(args, "axis_name", out string? axisName))
            parameters.AxisName = axisName!;
        if (TryGetDouble(args, "angle_degrees", out double angle))
            parameters.AngleDegrees = angle;
        if (TryGetString(args, "feature_name", out string? featureName))
            parameters.FeatureName = featureName;
        return parameters;
    }

    private static CreateFilletParameters DeserializeCreateFilletParams(Dictionary<string, object?> args)
    {
        var parameters = new CreateFilletParameters();
        if (TryGetStringList(args, "entity_names", out List<string> entities))
            parameters.EntityNames = entities;
        if (TryGetDouble(args, "radius_mm", out double radius))
            parameters.RadiusMm = radius;
        if (TryGetString(args, "feature_name", out string? featureName))
            parameters.FeatureName = featureName;
        return parameters;
    }

    private static CreateChamferParameters DeserializeCreateChamferParams(Dictionary<string, object?> args)
    {
        var parameters = new CreateChamferParameters();
        if (TryGetStringList(args, "entity_names", out List<string> entities))
            parameters.EntityNames = entities;
        if (TryGetDouble(args, "distance_mm", out double distance))
            parameters.DistanceMm = distance;
        if (TryGetDouble(args, "angle_degrees", out double angle))
            parameters.AngleDegrees = angle;
        if (TryGetString(args, "feature_name", out string? featureName))
            parameters.FeatureName = featureName;
        return parameters;
    }

    private static CreatePatternLinearParameters DeserializeCreatePatternLinearParams(Dictionary<string, object?> args)
    {
        var parameters = new CreatePatternLinearParameters();
        if (TryGetString(args, "feature_name", out string? featureName))
            parameters.FeatureName = featureName!;
        if (TryGetString(args, "direction_1_reference", out string? dir1))
            parameters.Direction1Reference = dir1!;
        if (TryGetInt(args, "count_1", out int count1))
            parameters.Count1 = count1;
        if (TryGetDouble(args, "spacing_1_mm", out double spacing1))
            parameters.Spacing1Mm = spacing1;
        if (TryGetBool(args, "flip_direction_1", out bool flip1))
            parameters.FlipDirection1 = flip1;
        if (TryGetString(args, "direction_2_reference", out string? dir2))
            parameters.Direction2Reference = dir2;
        if (TryGetInt(args, "count_2", out int count2))
            parameters.Count2 = count2;
        if (TryGetDouble(args, "spacing_2_mm", out double spacing2))
            parameters.Spacing2Mm = spacing2;
        if (TryGetBool(args, "flip_direction_2", out bool flip2))
            parameters.FlipDirection2 = flip2;
        if (TryGetString(args, "pattern_name", out string? patternName))
            parameters.PatternName = patternName;
        return parameters;
    }

    private static CreateReferencePlaneParameters DeserializeCreateReferencePlaneParams(Dictionary<string, object?> args)
    {
        var parameters = new CreateReferencePlaneParameters();
        if (TryGetString(args, "base_reference", out string? baseRef))
            parameters.BaseReference = baseRef!;
        if (TryGetDouble(args, "offset_mm", out double offset))
            parameters.OffsetMm = offset;
        if (TryGetBool(args, "flip", out bool flip))
            parameters.Flip = flip;
        if (TryGetString(args, "plane_name", out string? planeName))
            parameters.PlaneName = planeName;
        return parameters;
    }

    // --- Argument extraction helpers ---

    private static bool TryGetStringList(Dictionary<string, object?> args, string key, out List<string> value)
    {
        value = new List<string>();
        if (!args.TryGetValue(key, out object? raw) || raw == null)
            return false;

        // JavaScriptSerializer produces object[]; other JSON layers may hand
        // us ArrayList or List<object>. A bare string is treated as a
        // single-element list for agent-friendliness.
        if (raw is System.Collections.IEnumerable enumerable && raw is not string)
        {
            foreach (object? item in enumerable)
            {
                string? text = item?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    value.Add(text!);
            }
            return value.Count > 0;
        }

        if (raw is string single && !string.IsNullOrWhiteSpace(single))
        {
            value.Add(single);
            return true;
        }

        return false;
    }

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
