using System;
using System.Collections.Generic;
using Adze.Mesh.Models;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Mesh.Analysis;

/// <summary>
/// Computes <see cref="MeshStatistics"/> from a triangle-soup <see cref="Mesh"/>.
/// One pass over triangles for bbox / area / volume; a second pass builds the
/// edge dictionary for manifold + closure checks.
/// </summary>
public static class MeshStatsCalculator
{
    private const double DegenerateAreaEpsilon = 1e-12;

    public static MeshStatistics Compute(AdzeMesh mesh)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));

        var stats = new MeshStatistics
        {
            TriangleCount = mesh.TriangleCount
        };

        if (mesh.TriangleCount == 0)
        {
            stats.BoundingBox = AxisAlignedBoundingBox.Empty;
            stats.IsClosed = false;
            stats.IsConsistentlyOriented = true;
            return stats;
        }

        AxisAlignedBoundingBox bbox = AxisAlignedBoundingBox.Empty;
        double area = 0;
        double signedVolume = 0;
        int degenerate = 0;

        var edgeUseCounts = new Dictionary<EdgeKey, int>(mesh.TriangleCount * 3);
        var directedEdgeCounts = new Dictionary<DirectedEdge, int>(mesh.TriangleCount * 3);

        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            Triangle t = mesh.Triangles[i];

            bbox = bbox.Expand(t.V0).Expand(t.V1).Expand(t.V2);

            double triArea = t.Area;
            if (triArea < DegenerateAreaEpsilon)
            {
                degenerate++;
                continue;
            }

            area += triArea;
            signedVolume += t.SignedVolumeContribution;

            AccumulateEdge(edgeUseCounts, directedEdgeCounts, t.V0, t.V1);
            AccumulateEdge(edgeUseCounts, directedEdgeCounts, t.V1, t.V2);
            AccumulateEdge(edgeUseCounts, directedEdgeCounts, t.V2, t.V0);
        }

        int openEdges = 0;
        int nonManifoldEdges = 0;
        bool consistentOrientation = true;

        foreach (KeyValuePair<EdgeKey, int> edge in edgeUseCounts)
        {
            if (edge.Value == 1) openEdges++;
            else if (edge.Value > 2) nonManifoldEdges++;
        }

        // Consistent orientation check: every directed edge (a → b) should
        // appear at most once in a properly oriented manifold. A duplicate
        // means two adjacent triangles wind the shared edge the same way,
        // which inverts normals between them.
        foreach (KeyValuePair<DirectedEdge, int> directed in directedEdgeCounts)
        {
            if (directed.Value > 1)
            {
                consistentOrientation = false;
                break;
            }
        }

        stats.BoundingBox = bbox;
        stats.SurfaceArea = area;
        stats.SignedVolume = signedVolume;
        stats.EnclosedVolume = Math.Abs(signedVolume);
        stats.OpenEdgeCount = openEdges;
        stats.NonManifoldEdgeCount = nonManifoldEdges;
        stats.IsClosed = openEdges == 0 && nonManifoldEdges == 0;
        stats.IsConsistentlyOriented = consistentOrientation;
        stats.DegenerateTriangleCount = degenerate;

        // Triangle soup vs indexed mesh estimation: a watertight closed
        // manifold has roughly V = T/2 + 2 (Euler), so duplicate-vertex
        // count ≈ 3T − V ≈ 3T − (T/2 + 2). Useful order-of-magnitude signal
        // for "this came from an unindexed exporter", not a hard count.
        if (mesh.TriangleCount > 0)
        {
            double estimatedUniqueVertices = mesh.TriangleCount / 2.0 + 2.0;
            int duplicates = (int)Math.Max(0, mesh.TriangleCount * 3 - estimatedUniqueVertices);
            stats.DuplicateVertexEstimate = duplicates;
        }

        return stats;
    }

    private static void AccumulateEdge(
        Dictionary<EdgeKey, int> undirected,
        Dictionary<DirectedEdge, int> directed,
        Vector3d a,
        Vector3d b)
    {
        var key = EdgeKey.Make(a, b);
        undirected.TryGetValue(key, out int count);
        undirected[key] = count + 1;

        var dir = new DirectedEdge(a, b);
        directed.TryGetValue(dir, out int dCount);
        directed[dir] = dCount + 1;
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly Vector3d A;
        public readonly Vector3d B;

        private EdgeKey(Vector3d a, Vector3d b)
        {
            A = a;
            B = b;
        }

        public static EdgeKey Make(Vector3d a, Vector3d b)
        {
            // Order endpoints so the undirected key is stable regardless of
            // which triangle visited the edge first.
            if (Compare(a, b) <= 0) return new EdgeKey(a, b);
            return new EdgeKey(b, a);
        }

        private static int Compare(Vector3d a, Vector3d b)
        {
            int cx = a.X.CompareTo(b.X);
            if (cx != 0) return cx;
            int cy = a.Y.CompareTo(b.Y);
            if (cy != 0) return cy;
            return a.Z.CompareTo(b.Z);
        }

        public bool Equals(EdgeKey other) => A.Equals(other.A) && B.Equals(other.B);

        public override bool Equals(object? obj) => obj is EdgeKey e && Equals(e);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + A.GetHashCode();
                h = h * 31 + B.GetHashCode();
                return h;
            }
        }
    }

    private readonly struct DirectedEdge : IEquatable<DirectedEdge>
    {
        public readonly Vector3d From;
        public readonly Vector3d To;

        public DirectedEdge(Vector3d from, Vector3d to)
        {
            From = from;
            To = to;
        }

        public bool Equals(DirectedEdge other) => From.Equals(other.From) && To.Equals(other.To);

        public override bool Equals(object? obj) => obj is DirectedEdge e && Equals(e);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + From.GetHashCode();
                h = h * 31 + To.GetHashCode();
                return h;
            }
        }
    }
}
