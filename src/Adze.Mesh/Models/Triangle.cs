using System;

namespace Adze.Mesh.Models;

public readonly struct Triangle
{
    public readonly Vector3d V0;
    public readonly Vector3d V1;
    public readonly Vector3d V2;

    /// <summary>
    /// Stored normal as written in the source file (e.g. STL header). May be
    /// zero or unreliable for hand-edited or tool-generated meshes; consumers
    /// that care about correctness should use <see cref="ComputedNormal"/>.
    /// </summary>
    public readonly Vector3d StoredNormal;

    public Triangle(Vector3d v0, Vector3d v1, Vector3d v2, Vector3d storedNormal)
    {
        V0 = v0;
        V1 = v1;
        V2 = v2;
        StoredNormal = storedNormal;
    }

    public Triangle(Vector3d v0, Vector3d v1, Vector3d v2)
        : this(v0, v1, v2, Vector3d.Zero)
    {
    }

    /// <summary>
    /// Right-hand-rule normal computed from vertex order. Length unnormalized;
    /// see <see cref="UnitNormal"/> for the unit-length variant.
    /// </summary>
    public Vector3d ComputedNormal => Vector3d.Cross(V1 - V0, V2 - V0);

    public Vector3d UnitNormal => ComputedNormal.Normalized();

    /// <summary>
    /// Triangle area = ½ · ‖(V1 − V0) × (V2 − V0)‖.
    /// </summary>
    public double Area => 0.5 * ComputedNormal.Length;

    /// <summary>
    /// Signed contribution to volume of the closed mesh containing this triangle,
    /// computed as 1/6 · V0 · (V1 × V2). Summing this across a closed manifold
    /// yields the enclosed volume; sign tracks vertex orientation.
    /// </summary>
    public double SignedVolumeContribution => Vector3d.Dot(V0, Vector3d.Cross(V1, V2)) / 6.0;
}
