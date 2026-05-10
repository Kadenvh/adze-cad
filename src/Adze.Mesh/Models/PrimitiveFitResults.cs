using System.Collections.Generic;

namespace Adze.Mesh.Models;

/// <summary>
/// Aggregated primitive-fit output. Currently exposes detected planes; the
/// type is shaped to grow with cylinders, spheres, and tori as fitters land.
/// </summary>
public sealed class PrimitiveFitResults
{
    public List<PlaneFit> Planes { get; set; } = new List<PlaneFit>();

    /// <summary>
    /// Fraction of total surface area that landed on a detected primitive.
    /// High values (≥0.95) indicate a mesh that's likely a primitive
    /// composition and is a strong candidate for parametric reconstruction.
    /// Low values indicate organic / freeform geometry.
    /// </summary>
    public double TotalCoveredAreaFraction { get; set; }
}
