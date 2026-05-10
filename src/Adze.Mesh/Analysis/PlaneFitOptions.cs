namespace Adze.Mesh.Analysis;

/// <summary>
/// Tuning parameters for <see cref="PrimitiveFitter"/>. Defaults are sized
/// for "moderately clean STLs from CAD exporters" — generous angle
/// tolerance, tight distance tolerance relative to model scale.
/// </summary>
public sealed class PlaneFitOptions
{
    /// <summary>Maximum number of planes to return. Stops early if coverage plateaus.</summary>
    public int MaxPlanes { get; set; } = 12;

    /// <summary>
    /// Triangles whose normal differs from a candidate plane's normal by
    /// more than this many degrees are rejected from the inlier set.
    /// </summary>
    public double NormalAngleThresholdDegrees { get; set; } = 8.0;

    /// <summary>
    /// Triangle centroids further than this fraction of the bounding-box
    /// diagonal from the candidate plane are rejected from the inlier set.
    /// </summary>
    public double DistanceThresholdFraction { get; set; } = 0.005;

    /// <summary>Stop emitting planes whose inlier-area fraction falls below this.</summary>
    public double MinInlierAreaFraction { get; set; } = 0.005;
}
