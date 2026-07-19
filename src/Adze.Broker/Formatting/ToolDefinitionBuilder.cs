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
                "Returns dimensions in the active document, optionally scoped to a feature. Supports pagination via offset/limit.",
                ObjectSchema(
                    Optional("scope", "string", "Scope: 'document' for all, or a feature name to filter."),
                    Optional("include_driven", "boolean", "Include driven dimensions."),
                    Optional("offset", "integer", "Number of dimensions to skip (default 0, for pagination)."),
                    Optional("limit", "integer", "Maximum dimensions to return (default 50, max 200)."))),

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
                "Returns mates in the active assembly document. Supports pagination via offset/limit.",
                ObjectSchema(
                    Optional("scope", "string", "Scope: 'document' for all mates."),
                    Optional("offset", "integer", "Number of mates to skip (default 0, for pagination)."),
                    Optional("limit", "integer", "Maximum number of mates to return (default 50, max 200)."))),

            Build(ToolNames.GetRebuildDiagnostics,
                "Returns rebuild diagnostics, warnings, and missing references.",
                ObjectSchema(
                    Optional("include_missing_references", "boolean", "Include missing reference paths."),
                    Optional("include_warnings", "boolean", "Include rebuild warnings."))),

            Build(ToolNames.GetReferenceGraph,
                "Returns the reference graph showing file dependencies.",
                ObjectSchema(
                    Optional("depth", "integer", "Reference traversal depth."),
                    Optional("include_external_references", "boolean", "Include external file references."))),

            Build(ToolNames.GetMassProperties,
                "Computes volume, surface area, mass, center of mass, and (optionally) principal axes / moments of inertia for the active part or assembly. Equivalent to the SOLIDWORKS Evaluate tab's Mass Properties command.",
                ObjectSchema(
                    Optional("configuration_name", "string", "Configuration to compute properties for. Defaults to the active configuration."),
                    Optional("include_principal_axes", "boolean", "Include principal axes and moments of inertia. Default true."))),

            Build(ToolNames.MeasureDistance,
                "Measures distance, projection components, angle, and area between two selected entities (faces, edges, vertices). Equivalent to the SOLIDWORKS Evaluate tab's Measure command. If entity_a and entity_b are empty, uses the current selection in SOLIDWORKS.",
                ObjectSchema(
                    Optional("entity_a", "string", "Name of the first entity (e.g. 'Face1@Boss-Extrude1'). Empty = use current selection."),
                    Optional("entity_a_type", "string", "Type hint: face, edge, vertex, sketchsegment, plane."),
                    Optional("entity_b", "string", "Name of the second entity."),
                    Optional("entity_b_type", "string", "Type hint for entity_b.")))
        };
    }

    public static List<AgentToolDefinition> BuildWriteToolDefinitions()
    {
        return new List<AgentToolDefinition>
        {
            Build(ToolNames.SetCustomProperty,
                "Sets a custom property value on the active document or a specific configuration. Does not require rebuild.",
                ObjectSchema(
                    Required("property_name", "string", "The name of the custom property to set."),
                    Required("property_value", "string", "The value to set the property to."),
                    Optional("scope", "string", "Scope: 'document' (default) or 'configuration'."),
                    Optional("configuration_name", "string", "Configuration name (required when scope is 'configuration')."))),

            Build(ToolNames.SetDimensionValue,
                "Sets a dimension value in the active document. Requires rebuild after change.",
                ObjectSchema(
                    Required("dimension_full_name", "string", "The full name of the dimension (e.g. 'D1@Sketch1')."),
                    Required("new_value", "number", "The new value to set (in document units)."),
                    Optional("configuration_name", "string", "Configuration to apply the change to."))),

            Build(ToolNames.SuppressFeature,
                "Suppresses a feature in the active document. Requires rebuild. May cascade to dependent features.",
                ObjectSchema(
                    Required("feature_name", "string", "The name of the feature to suppress."),
                    Optional("configuration_name", "string", "Configuration to apply the suppression to."))),

            Build(ToolNames.UnsuppressFeature,
                "Unsuppresses (resolves) a previously suppressed feature. Requires rebuild.",
                ObjectSchema(
                    Required("feature_name", "string", "The name of the feature to unsuppress."),
                    Optional("configuration_name", "string", "Configuration to apply the unsuppression to."))),

            Build(ToolNames.RenameObject,
                "Renames a feature in the active document. SOLIDWORKS automatically updates dimension references.",
                ObjectSchema(
                    Required("current_name", "string", "The current name of the object to rename."),
                    Required("new_name", "string", "The new name for the object."),
                    Optional("object_type", "string", "Object type: 'feature' (default)."))),

            Build(ToolNames.InsertComponent,
                "Inserts a component (part or sub-assembly) into the active assembly at specified coordinates. Elevated operation requiring confirmation. Only works with assembly documents.",
                ObjectSchema(
                    Required("component_path", "string", "Absolute file path to the .SLDPRT or .SLDASM file to insert."),
                    Optional("configuration_name", "string", "Configuration of the component to use."),
                    Optional("x", "number", "X coordinate for insertion point in mm (default 0)."),
                    Optional("y", "number", "Y coordinate for insertion point in mm (default 0)."),
                    Optional("z", "number", "Z coordinate for insertion point in mm (default 0)."))),

            Build(ToolNames.CreateDrawingView,
                "Creates a standard drawing view on the active sheet. Elevated operation requiring confirmation. Only works with drawing documents.",
                ObjectSchema(
                    Required("view_type", "string", "View type: front, back, top, bottom, left, right, isometric, trimetric, or dimetric."),
                    Optional("model_path", "string", "Path to the model file. If empty, uses the first referenced model."),
                    Optional("x", "number", "X position on the sheet in meters (default 0.15)."),
                    Optional("y", "number", "Y position on the sheet in meters (default 0.15)."),
                    Optional("scale", "number", "View scale ratio (default 1.0)."))),

            Build(ToolNames.CreateSketch,
                "Creates a new sketch on a reference plane (e.g. 'Front Plane') or planar face. Foundation for parametric geometry authoring — required before add_sketch_line / add_sketch_circle. Elevated operation.",
                ObjectSchema(
                    Required("reference_name", "string", "Reference plane or face to anchor the sketch. Examples: 'Front Plane', 'Top Plane', 'Face1@Boss-Extrude1'."),
                    Optional("sketch_name", "string", "Optional name for the new sketch. SW assigns a default like 'Sketch1' otherwise."))),

            Build(ToolNames.AddSketchLine,
                "Adds a 2D line segment to a sketch (the active sketch by default). Coordinates are sketch-plane local in mm.",
                ObjectSchema(
                    Required("start_x_mm", "number", "Start X in mm."),
                    Required("start_y_mm", "number", "Start Y in mm."),
                    Required("end_x_mm", "number", "End X in mm."),
                    Required("end_y_mm", "number", "End Y in mm."),
                    Optional("sketch_name", "string", "Name of the sketch to add to. Empty = active sketch."))),

            Build(ToolNames.AddSketchCircle,
                "Adds a 2D circle to a sketch. Coordinates and radius are in mm.",
                ObjectSchema(
                    Required("center_x_mm", "number", "Center X in mm."),
                    Required("center_y_mm", "number", "Center Y in mm."),
                    Required("radius_mm", "number", "Radius in mm (must be > 0)."),
                    Optional("sketch_name", "string", "Name of the sketch to add to. Empty = active sketch."))),

            Build(ToolNames.AddSketchDimension,
                "Adds a parametric dimension to a sketch entity and drives it to a target value. Use this to lock geometry to design intent. Length values in mm; angle values in degrees.",
                ObjectSchema(
                    Required("entity_name", "string", "Sketch entity to dimension (e.g. 'Line1', 'Arc1', 'Circle1')."),
                    Required("dimension_type", "string", "Dimension type: 'length', 'radius', 'diameter', 'angle', 'distance'."),
                    Required("target_value", "number", "Target value in mm (or degrees for angle)."),
                    Optional("sketch_name", "string", "Name of the sketch containing the entity. Empty = active sketch."))),

            Build(ToolNames.CreateDrawingDocument,
                "Creates a new SOLIDWORKS drawing document referencing a part or assembly, optionally populated with standard 3rd-angle views (front + top + right + isometric). Elevated operation requiring confirmation.",
                ObjectSchema(
                    Optional("model_path", "string", "Path to the .sldprt or .sldasm to draw. Empty = active document."),
                    Optional("template_path", "string", "Override drawing template path. Empty = SW default template."),
                    Optional("include_standard_views", "boolean", "Auto-add front/top/right/iso views. Default true."))),

            Build(ToolNames.CreateExtrude,
                "Extrudes a sketch into a solid boss. Turns 2D sketch geometry into 3D — e.g. sketch a circle then extrude it to make a cylinder. Part documents only. Elevated operation requiring confirmation.",
                ObjectSchema(
                    Required("depth_mm", "number", "Extrusion depth in mm. Ignored when end_condition is 'through_all'."),
                    Optional("sketch_name", "string", "Sketch to extrude (e.g. 'Sketch1'). Empty = active sketch."),
                    Optional("end_condition", "string", "End condition: 'blind' (default), 'through_all', or 'mid_plane'."),
                    Optional("reverse", "boolean", "Flip the extrusion direction. Default false."),
                    Optional("feature_name", "string", "Optional name for the new feature."))),

            Build(ToolNames.CreateRevolve,
                "Revolves a sketch profile around an axis into a solid. The axis must be a reference axis ('Axis1') or a sketch line ('Line1@Sketch1'). Part documents only. Elevated operation requiring confirmation.",
                ObjectSchema(
                    Required("axis_name", "string", "Axis of revolution: reference axis name or sketch line name (e.g. 'Line1@Sketch1')."),
                    Optional("sketch_name", "string", "Profile sketch to revolve. Empty = active sketch."),
                    Optional("angle_degrees", "number", "Revolve angle in degrees, in (0, 360]. Default 360."),
                    Optional("feature_name", "string", "Optional name for the new feature.")))
        };
    }

    public static List<AgentToolDefinition> BuildRetrievalToolDefinitions()
    {
        return new List<AgentToolDefinition>
        {
            Build(ToolNames.SearchProjectFiles,
                "Searches closed SOLIDWORKS files (.SLDPRT, .SLDASM, .SLDDRW) in a folder by metadata, custom properties, file type, or keyword. Indexes files via OLE Structured Storage without opening them in SOLIDWORKS.",
                ObjectSchema(
                    Required("root_folder_path", "string", "Absolute path to the folder to scan for SOLIDWORKS files."),
                    Optional("file_type", "string", "Filter by type: 'part', 'assembly', or 'drawing'."),
                    Optional("keyword", "string", "Search keyword matched against file name, title, author, keywords, comments, and custom property values."),
                    Optional("property_name", "string", "Filter by a specific custom property name."),
                    Optional("property_value", "string", "Filter by custom property value (substring match). Requires property_name."),
                    Optional("path_pattern", "string", "Filter by path substring."),
                    Optional("max_results", "integer", "Maximum results to return (default 20).")))
        };
    }

    public static List<AgentToolDefinition> BuildMeshToolDefinitions()
    {
        return new List<AgentToolDefinition>
        {
            Build(ToolNames.AnalyzeMeshFile,
                "Reads a mesh file (currently .stl, binary or ASCII) and returns triangle count, bounding box, surface area, enclosed volume, manifold and closure signals, plus advisories and a recommended import strategy. Read-only.",
                ObjectSchema(
                    Required("file_path", "string", "Absolute path to a .stl mesh file."),
                    Optional("max_advisories", "integer", "Cap on advisory list length (default 20)."))),

            Build(ToolNames.ImportMesh,
                "Imports a .stl mesh file into SOLIDWORKS as a part document. Elevated operation requiring user confirmation. The graphics_body strategy is the only fully implemented path; other strategies fall back to graphics_body and surface a warning. The Preview includes the mesh analysis so the agent and user can reason about feasibility before approving.",
                ObjectSchema(
                    Required("file_path", "string", "Absolute path to a .stl mesh file."),
                    Optional("strategy", "string", "Import strategy: 'graphics_body' (default), 'graphics_then_convert', 'scan_to_3d', or 'reverse_engineer'."),
                    Optional("source_units", "string", "Source units for the mesh: 'mm', 'cm', 'm', or 'in'.")))
        };
    }

    public static List<AgentToolDefinition> BuildAllToolDefinitions(bool includeRetrieval = false, bool includeMesh = false)
    {
        var all = new List<AgentToolDefinition>();
        all.AddRange(BuildReadToolDefinitions());
        all.AddRange(BuildWriteToolDefinitions());
        if (includeRetrieval)
            all.AddRange(BuildRetrievalToolDefinitions());
        if (includeMesh)
            all.AddRange(BuildMeshToolDefinitions());
        return all;
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
        var required = new List<string>();
        foreach (var prop in properties)
        {
            if (prop.TryGetValue("_name", out object? nameValue) && nameValue is string name)
            {
                var copy = new Dictionary<string, object?>(prop);
                copy.Remove("_name");
                bool isRequired = false;
                if (copy.TryGetValue("_required", out object? reqValue) && reqValue is bool reqBool)
                {
                    isRequired = reqBool;
                    copy.Remove("_required");
                }

                props[name] = copy;
                if (isRequired)
                {
                    required.Add(name);
                }
            }
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = required
        };
    }

    private static Dictionary<string, object?> Required(string name, string type, string description)
    {
        return new Dictionary<string, object?>
        {
            ["_name"] = name,
            ["_required"] = true,
            ["type"] = type,
            ["description"] = description
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
