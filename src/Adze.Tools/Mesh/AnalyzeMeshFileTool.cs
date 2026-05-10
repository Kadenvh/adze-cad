using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Mesh.Analysis;
using Adze.Mesh.Io;
using Adze.Mesh.Models;
using Adze.Tools.Abstractions;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Tools.Mesh;

/// <summary>
/// Reads an STL file and returns triangle-soup statistics, manifold/closure
/// signals, and a recommended import strategy. Read-only — does not touch
/// SOLIDWORKS state. Used both by the agent loop (to reason about whether to
/// propose a mesh import) and by the ribbon "Import Mesh" flow (to populate
/// the preview card before the user confirms).
/// </summary>
public sealed class AnalyzeMeshFileTool : IReadOnlyTool<AnalyzeMeshFileParameters>
{
    public string ToolName => ToolNames.AnalyzeMeshFile;

    public ToolResult Execute(SessionContext context, AnalyzeMeshFileParameters parameters)
    {
        var result = new ToolResult
        {
            ToolName = ToolName,
            Success = false,
            Summary = "Mesh analysis not executed."
        };

        if (parameters == null || string.IsNullOrWhiteSpace(parameters.FilePath))
        {
            result.Summary = "file_path is required.";
            return result;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(parameters.FilePath);
        }
        catch (Exception ex)
        {
            result.Summary = "Invalid file path: " + ex.Message;
            return result;
        }

        if (!File.Exists(fullPath))
        {
            result.Summary = "Mesh file not found: " + fullPath;
            return result;
        }

        if (!IsSupportedExtension(fullPath))
        {
            result.Summary = "Unsupported mesh format. Currently supported: .stl";
            return result;
        }

        try
        {
            AdzeMesh mesh = StlReader.ReadFromFile(fullPath);
            MeshAnalysis analysis = MeshAnalyzer.Analyze(mesh);

            result.Success = true;
            result.Summary = BuildSummary(fullPath, analysis);
            result.Data["file_path"] = fullPath;
            result.Data["file_size_bytes"] = new FileInfo(fullPath).Length;
            result.Data["format"] = "stl";
            result.Data["header"] = mesh.Header ?? string.Empty;
            result.Data["triangle_count"] = analysis.Statistics.TriangleCount;
            result.Data["bounding_box"] = SerializeBoundingBox(analysis.Statistics.BoundingBox);
            result.Data["surface_area"] = analysis.Statistics.SurfaceArea;
            result.Data["enclosed_volume"] = analysis.Statistics.EnclosedVolume;
            result.Data["signed_volume"] = analysis.Statistics.SignedVolume;
            result.Data["is_closed"] = analysis.Statistics.IsClosed;
            result.Data["is_consistently_oriented"] = analysis.Statistics.IsConsistentlyOriented;
            result.Data["open_edge_count"] = analysis.Statistics.OpenEdgeCount;
            result.Data["non_manifold_edge_count"] = analysis.Statistics.NonManifoldEdgeCount;
            result.Data["degenerate_triangle_count"] = analysis.Statistics.DegenerateTriangleCount;
            result.Data["convertibility"] = analysis.Convertibility.ToString();
            result.Data["recommended_strategy"] = analysis.RecommendedStrategy.ToString();

            int maxAdvisories = parameters.MaxAdvisories > 0 ? parameters.MaxAdvisories : 20;
            var advisories = analysis.Advisories;
            if (advisories.Count > maxAdvisories)
            {
                advisories = advisories.GetRange(0, maxAdvisories);
            }
            result.Data["advisories"] = advisories;

            if (analysis.Convertibility == MeshSolidConvertibility.NotConvertible)
            {
                result.Warnings.Add("Mesh is not convertible to a solid body in its current state.");
            }
        }
        catch (Exception ex)
        {
            result.Summary = "Mesh analysis failed: " + ex.Message;
        }

        return result;
    }

    private static bool IsSupportedExtension(string path)
    {
        string ext = Path.GetExtension(path);
        return string.Equals(ext, ".stl", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSummary(string path, MeshAnalysis analysis)
    {
        string name = Path.GetFileName(path);
        string closure = analysis.Statistics.IsClosed ? "closed" : "open";
        return name + ": "
            + analysis.Statistics.TriangleCount.ToString("N0", CultureInfo.InvariantCulture)
            + " triangles, " + closure
            + ", convertibility=" + analysis.Convertibility
            + ", strategy=" + analysis.RecommendedStrategy;
    }

    private static Dictionary<string, object?> SerializeBoundingBox(AxisAlignedBoundingBox bbox)
    {
        return new Dictionary<string, object?>
        {
            ["min_x"] = bbox.Min.X,
            ["min_y"] = bbox.Min.Y,
            ["min_z"] = bbox.Min.Z,
            ["max_x"] = bbox.Max.X,
            ["max_y"] = bbox.Max.Y,
            ["max_z"] = bbox.Max.Z,
            ["size_x"] = bbox.Size.X,
            ["size_y"] = bbox.Size.Y,
            ["size_z"] = bbox.Size.Z,
            ["diagonal_length"] = bbox.DiagonalLength
        };
    }
}
