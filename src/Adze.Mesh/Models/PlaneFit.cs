using System.Collections.Generic;

namespace Adze.Mesh.Models;

/// <summary>
/// One detected planar region in a mesh. The plane is described in the
/// implicit form <c>n · x = d</c> where <c>n</c> is the unit normal and
/// <c>d</c> is the signed offset from the origin. Inliers are the indices
/// of mesh triangles whose centroid lies within the fitter's distance
/// threshold of the plane and whose own normal is within the angle
/// threshold of <see cref="Normal"/>.
/// </summary>
public sealed class PlaneFit
{
    public Vector3d Normal { get; set; }

    public double Offset { get; set; }

    public List<int> InlierTriangleIndices { get; set; } = new List<int>();

    public double InlierArea { get; set; }

    public double InlierAreaFraction { get; set; }

    /// <summary>Centroid of the inlier triangles, useful as a sketch anchor.</summary>
    public Vector3d Centroid { get; set; }
}
