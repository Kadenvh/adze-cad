using System;
using System.Collections.Generic;
using Adze.Mesh.Models;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Mesh.Analysis;

/// <summary>
/// Detects planar regions in a triangle-soup mesh by clustering triangles
/// with similar unit normals and fitting a plane through each cluster.
/// Deterministic (no randomness, no RANSAC) — runs in O(T log T) and
/// produces stable results for unit tests. The clustering approach trades
/// some robustness on noisy organic meshes for predictability and speed
/// on CAD-exported geometry, which is the primary input we care about for
/// the v1.1 mesh-to-solid track.
///
/// Successive sessions can layer RANSAC-based detection on top for noisy
/// scan data without disturbing this deterministic baseline.
/// </summary>
public static class PrimitiveFitter
{
    public static PrimitiveFitResults Fit(AdzeMesh mesh, PlaneFitOptions? options = null)
    {
        if (mesh == null) throw new ArgumentNullException(nameof(mesh));
        options ??= new PlaneFitOptions();

        var results = new PrimitiveFitResults();
        if (mesh.TriangleCount == 0) return results;

        // Pre-compute per-triangle unit normal, area, centroid. Skip degenerate.
        var triData = new List<TriData>(mesh.TriangleCount);
        double totalArea = 0;
        AxisAlignedBoundingBox bbox = AxisAlignedBoundingBox.Empty;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            Triangle t = mesh.Triangles[i];
            bbox = bbox.Expand(t.V0).Expand(t.V1).Expand(t.V2);
            double area = t.Area;
            if (area < 1e-12) continue;
            Vector3d n = t.UnitNormal;
            Vector3d c = (t.V0 + t.V1 + t.V2) / 3.0;
            triData.Add(new TriData(i, n, area, c));
            totalArea += area;
        }

        if (totalArea < 1e-12 || triData.Count == 0) return results;

        double diag = bbox.DiagonalLength;
        if (diag < 1e-12) diag = 1.0;
        double distanceThreshold = options.DistanceThresholdFraction * diag;
        double cosAngleThreshold = Math.Cos(options.NormalAngleThresholdDegrees * Math.PI / 180.0);

        // Greedy clustering: pick the triangle with the largest area not yet
        // assigned, claim all triangles whose normal lies within angle
        // threshold AND whose centroid is within distance threshold of the
        // implied plane. Repeat until coverage plateaus or MaxPlanes hit.
        var assigned = new bool[triData.Count];
        // Indices into triData sorted by descending area for greedy seeding.
        int[] orderByArea = new int[triData.Count];
        for (int i = 0; i < triData.Count; i++) orderByArea[i] = i;
        Array.Sort(orderByArea, (a, b) => triData[b].Area.CompareTo(triData[a].Area));

        double coveredArea = 0;
        for (int seedIdx = 0; seedIdx < orderByArea.Length && results.Planes.Count < options.MaxPlanes; seedIdx++)
        {
            int seed = orderByArea[seedIdx];
            if (assigned[seed]) continue;

            Vector3d seedNormal = triData[seed].Normal;
            double seedOffset = Vector3d.Dot(seedNormal, triData[seed].Centroid);

            // First pass: gather inliers by normal + distance.
            var inlierIndices = new List<int>();
            double inlierArea = 0;
            Vector3d centroidAccum = Vector3d.Zero;
            for (int i = 0; i < triData.Count; i++)
            {
                if (assigned[i]) continue;
                TriData td = triData[i];
                double cosAngle = Vector3d.Dot(td.Normal, seedNormal);
                if (cosAngle < cosAngleThreshold) continue;
                double signedDist = Vector3d.Dot(td.Centroid, seedNormal) - seedOffset;
                if (Math.Abs(signedDist) > distanceThreshold) continue;
                inlierIndices.Add(i);
                inlierArea += td.Area;
                centroidAccum += td.Centroid * td.Area;
            }

            double inlierFraction = inlierArea / totalArea;
            if (inlierFraction < options.MinInlierAreaFraction)
            {
                // Too small — abandon this seed and continue (mark seed assigned
                // so we don't pick it again, but don't emit a plane).
                assigned[seed] = true;
                continue;
            }

            // Refit the plane via area-weighted least squares using the inliers.
            (Vector3d refinedNormal, double refinedOffset) = RefitPlane(triData, inlierIndices, seedNormal);

            // Mark inliers and record the plane.
            foreach (int idx in inlierIndices) assigned[idx] = true;

            Vector3d planeCentroid = inlierArea > 0
                ? centroidAccum / inlierArea
                : triData[seed].Centroid;

            results.Planes.Add(new PlaneFit
            {
                Normal = refinedNormal,
                Offset = refinedOffset,
                InlierTriangleIndices = MapToOriginalIndices(triData, inlierIndices),
                InlierArea = inlierArea,
                InlierAreaFraction = inlierFraction,
                Centroid = planeCentroid
            });

            coveredArea += inlierArea;
        }

        results.TotalCoveredAreaFraction = totalArea > 0 ? coveredArea / totalArea : 0;
        return results;
    }

    private static (Vector3d Normal, double Offset) RefitPlane(
        List<TriData> tris,
        List<int> inlierIdx,
        Vector3d seedNormal)
    {
        // Area-weighted average normal. Sign-aligned to the seed so that
        // inliers facing nearly opposite directions don't cancel out
        // (shouldn't happen given the angle filter, but the alignment is
        // essentially free).
        Vector3d normalSum = Vector3d.Zero;
        double weightSum = 0;
        Vector3d centroidAccum = Vector3d.Zero;
        foreach (int idx in inlierIdx)
        {
            TriData td = tris[idx];
            double dot = Vector3d.Dot(td.Normal, seedNormal);
            Vector3d aligned = dot >= 0 ? td.Normal : new Vector3d(-td.Normal.X, -td.Normal.Y, -td.Normal.Z);
            normalSum += aligned * td.Area;
            centroidAccum += td.Centroid * td.Area;
            weightSum += td.Area;
        }

        Vector3d refinedNormal = weightSum > 0 ? normalSum.Normalized() : seedNormal;
        Vector3d centroid = weightSum > 0 ? centroidAccum / weightSum : Vector3d.Zero;
        double refinedOffset = Vector3d.Dot(refinedNormal, centroid);
        return (refinedNormal, refinedOffset);
    }

    private static List<int> MapToOriginalIndices(List<TriData> tris, List<int> compactIdx)
    {
        var result = new List<int>(compactIdx.Count);
        foreach (int idx in compactIdx)
        {
            result.Add(tris[idx].MeshIndex);
        }
        return result;
    }

    private readonly struct TriData
    {
        public readonly int MeshIndex;
        public readonly Vector3d Normal;
        public readonly double Area;
        public readonly Vector3d Centroid;

        public TriData(int meshIndex, Vector3d normal, double area, Vector3d centroid)
        {
            MeshIndex = meshIndex;
            Normal = normal;
            Area = area;
            Centroid = centroid;
        }
    }
}
