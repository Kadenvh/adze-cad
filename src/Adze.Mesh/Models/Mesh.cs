using System.Collections.Generic;

namespace Adze.Mesh.Models;

/// <summary>
/// Triangle-soup mesh. Adze.Mesh deliberately keeps this representation simple
/// (no shared-vertex indexing yet) because STL itself is a triangle soup —
/// adding indexed vertices is a separate analysis step that fits in
/// <see cref="Analysis.MeshAnalyzer"/> when callers need it.
/// </summary>
public sealed class Mesh
{
    public List<Triangle> Triangles { get; }

    public string? SourcePath { get; set; }

    public string? Header { get; set; }

    public Mesh()
    {
        Triangles = new List<Triangle>();
    }

    public Mesh(IEnumerable<Triangle> triangles)
    {
        Triangles = new List<Triangle>(triangles);
    }

    public int TriangleCount => Triangles.Count;
}
