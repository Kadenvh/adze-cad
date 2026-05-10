using System.Collections.Generic;

namespace Adze.Mesh.Models;

/// <summary>
/// Higher-level analysis of a mesh that goes beyond raw stats — feasibility
/// signals, advisories, and a recommended import strategy. Designed as the
/// payload returned by the <c>analyze_mesh_file</c> tool so the agent (or a
/// human reviewing the preview card) can reason about whether and how to
/// convert a mesh into a SOLIDWORKS solid body.
/// </summary>
public sealed class MeshAnalysis
{
    public MeshStatistics Statistics { get; set; } = new MeshStatistics();

    public MeshSolidConvertibility Convertibility { get; set; } = MeshSolidConvertibility.Unknown;

    public ImportStrategy RecommendedStrategy { get; set; } = ImportStrategy.GraphicsBodyOnly;

    /// <summary>
    /// Detected primitive regions (currently planes). Populated when
    /// primitive fitting is requested; otherwise empty. High primitive
    /// coverage signals a strong candidate for parametric reconstruction.
    /// </summary>
    public PrimitiveFitResults Primitives { get; set; } = new PrimitiveFitResults();

    /// <summary>
    /// Human-readable advisories surfaced to preview cards and trace records.
    /// Examples: "Mesh is open (37 boundary edges) — knit/cap before solidifying",
    /// "Mesh contains 12,400 triangles; SW import will produce a heavy graphics body".
    /// </summary>
    public List<string> Advisories { get; set; } = new List<string>();
}

public enum MeshSolidConvertibility
{
    Unknown,

    /// <summary>Closed, manifold, consistently oriented; safe to attempt direct mesh-to-solid conversion.</summary>
    Direct,

    /// <summary>Manifold but has minor flaws (a few open edges or inverted normals); auto-repair likely succeeds.</summary>
    NeedsRepair,

    /// <summary>Significant topology issues; ScanTo3D-style surface fitting or manual cleanup required.</summary>
    NeedsReverseEngineering,

    /// <summary>Cannot be converted to a solid (e.g. point cloud disguised as mesh, severely non-manifold).</summary>
    NotConvertible
}

public enum ImportStrategy
{
    /// <summary>Open the file as a graphics body via SOLIDWORKS' built-in mesh import. Fast but non-editable.</summary>
    GraphicsBodyOnly,

    /// <summary>Import as graphics body, then run native "Convert to Solid Body" / Mesh BREP operation.</summary>
    GraphicsThenConvert,

    /// <summary>Use ScanTo3D's automatic surface-fitting wizard. Requires the ScanTo3D add-in license.</summary>
    ScanTo3DAutoSurface,

    /// <summary>Adze-driven primitive fit + sketch generation. Future capability — not yet implemented.</summary>
    AdzeReverseEngineer
}
