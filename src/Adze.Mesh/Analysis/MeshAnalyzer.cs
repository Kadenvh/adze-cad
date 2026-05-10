using System;
using System.Globalization;
using Adze.Mesh.Models;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Mesh.Analysis;

/// <summary>
/// Layers a higher-level <see cref="MeshAnalysis"/> on top of raw
/// <see cref="MeshStatistics"/> — convertibility verdict, recommended import
/// strategy, and human-readable advisories. This is what the
/// <c>analyze_mesh_file</c> tool returns to the agent and surfaces in
/// the import preview card.
/// </summary>
public static class MeshAnalyzer
{
    /// <summary>
    /// Triangle counts above this threshold produce an advisory. The
    /// heuristic is informational — SW can ingest much larger meshes, but
    /// performance falls off a cliff above this point on typical R2026x
    /// hardware.
    /// </summary>
    public const int HeavyMeshTriangleThreshold = 50_000;

    /// <summary>
    /// Mesh-to-solid conversion succeeds reliably only for closed manifolds.
    /// We tolerate up to this proportion of open edges relative to total
    /// triangle count before downgrading the convertibility verdict — covers
    /// typical exporter quirks (small precision gaps that knit/heal trivially).
    /// </summary>
    public const double TolerableOpenEdgeRatio = 0.005;

    public static MeshAnalysis Analyze(AdzeMesh mesh, bool includePrimitives = false)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));

        MeshStatistics stats = MeshStatsCalculator.Compute(mesh);
        MeshAnalysis analysis = AnalyzeStatsOnly(stats);

        if (includePrimitives && mesh.TriangleCount > 0)
        {
            analysis.Primitives = PrimitiveFitter.Fit(mesh);
            EnrichAdvisoriesWithPrimitives(analysis);
        }

        return analysis;
    }

    public static MeshAnalysis Analyze(MeshStatistics stats) => AnalyzeStatsOnly(stats);

    private static MeshAnalysis AnalyzeStatsOnly(MeshStatistics stats)
    {
        if (stats == null) throw new ArgumentNullException(nameof(stats));

        var analysis = new MeshAnalysis { Statistics = stats };

        if (stats.TriangleCount == 0)
        {
            analysis.Convertibility = MeshSolidConvertibility.NotConvertible;
            analysis.RecommendedStrategy = ImportStrategy.GraphicsBodyOnly;
            analysis.Advisories.Add("Mesh contains no triangles — nothing to import.");
            return analysis;
        }

        // Convertibility verdict
        double openRatio = stats.TriangleCount > 0
            ? (double)stats.OpenEdgeCount / stats.TriangleCount
            : 1.0;

        if (stats.IsClosed && stats.IsConsistentlyOriented && stats.NonManifoldEdgeCount == 0)
        {
            analysis.Convertibility = MeshSolidConvertibility.Direct;
            analysis.RecommendedStrategy = ImportStrategy.GraphicsThenConvert;
        }
        else if (openRatio <= TolerableOpenEdgeRatio && stats.NonManifoldEdgeCount == 0)
        {
            analysis.Convertibility = MeshSolidConvertibility.NeedsRepair;
            analysis.RecommendedStrategy = ImportStrategy.GraphicsThenConvert;
        }
        else if (stats.NonManifoldEdgeCount > 0 || openRatio > 0.05)
        {
            analysis.Convertibility = MeshSolidConvertibility.NeedsReverseEngineering;
            analysis.RecommendedStrategy = ImportStrategy.ScanTo3DAutoSurface;
        }
        else
        {
            analysis.Convertibility = MeshSolidConvertibility.NeedsRepair;
            analysis.RecommendedStrategy = ImportStrategy.GraphicsThenConvert;
        }

        // Advisories — the human-readable layer
        if (!stats.IsClosed)
        {
            analysis.Advisories.Add(
                "Mesh is open: " + stats.OpenEdgeCount.ToString(CultureInfo.InvariantCulture)
                + " boundary edge(s) detected. Knit/cap before solidifying.");
        }

        if (!stats.IsConsistentlyOriented)
        {
            analysis.Advisories.Add(
                "Triangle winding is inconsistent — at least one shared edge is wound the same direction by both adjacent triangles. SW may import with inverted normals.");
        }

        if (stats.NonManifoldEdgeCount > 0)
        {
            analysis.Advisories.Add(
                stats.NonManifoldEdgeCount.ToString(CultureInfo.InvariantCulture)
                + " non-manifold edge(s) detected. Direct mesh-to-solid conversion will fail; reverse engineering required.");
        }

        if (stats.DegenerateTriangleCount > 0)
        {
            analysis.Advisories.Add(
                stats.DegenerateTriangleCount.ToString(CultureInfo.InvariantCulture)
                + " degenerate (zero-area) triangle(s) skipped during stats. Source exporter likely emitted slivers.");
        }

        if (stats.TriangleCount > HeavyMeshTriangleThreshold)
        {
            analysis.Advisories.Add(
                "Heavy mesh (" + stats.TriangleCount.ToString("N0", CultureInfo.InvariantCulture)
                + " triangles). SW import will produce a heavy graphics body; expect rebuild and graphics latency.");
        }

        if (stats.SignedVolume < 0 && stats.IsClosed)
        {
            analysis.Advisories.Add(
                "Signed volume is negative on a closed mesh — vertex winding is likely inverted. Consider flipping normals after import.");
        }

        return analysis;
    }

    private static void EnrichAdvisoriesWithPrimitives(MeshAnalysis analysis)
    {
        int planeCount = analysis.Primitives.Planes.Count;
        if (planeCount == 0) return;

        double covered = analysis.Primitives.TotalCoveredAreaFraction;
        analysis.Advisories.Add(
            "Detected " + planeCount.ToString(CultureInfo.InvariantCulture)
            + " planar region(s) covering " + (covered * 100).ToString("F1", CultureInfo.InvariantCulture)
            + "% of surface area.");

        if (covered >= 0.98 && analysis.Convertibility == MeshSolidConvertibility.Direct)
        {
            analysis.Advisories.Add(
                "Mesh appears to be a primitive composition (≥98% planar). High-confidence candidate for parametric reconstruction.");
        }
    }
}
