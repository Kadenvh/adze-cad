namespace Adze.Mesh.Models;

public sealed class MeshStatistics
{
    public int TriangleCount { get; set; }

    public AxisAlignedBoundingBox BoundingBox { get; set; }

    public double SurfaceArea { get; set; }

    /// <summary>
    /// Signed enclosed volume from divergence theorem. Only meaningful if
    /// the mesh is closed and consistently oriented (see <see cref="IsClosed"/>
    /// and <see cref="IsConsistentlyOriented"/>). Reported as |volume| in
    /// <see cref="EnclosedVolume"/>; a negative raw signed volume usually
    /// indicates inverted normals.
    /// </summary>
    public double SignedVolume { get; set; }

    public double EnclosedVolume { get; set; }

    public bool IsClosed { get; set; }

    public bool IsConsistentlyOriented { get; set; }

    /// <summary>
    /// Edges that appear in only one triangle. Closed manifolds have zero;
    /// non-zero indicates holes, cracks, or single-sided surfaces.
    /// </summary>
    public int OpenEdgeCount { get; set; }

    /// <summary>
    /// Edges that appear in three or more triangles. Manifold meshes have zero;
    /// non-zero indicates T-junctions or self-intersection.
    /// </summary>
    public int NonManifoldEdgeCount { get; set; }

    public int DegenerateTriangleCount { get; set; }

    public int DuplicateVertexEstimate { get; set; }
}
