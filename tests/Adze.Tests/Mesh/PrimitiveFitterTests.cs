using Adze.Mesh.Analysis;
using Adze.Mesh.Io;
using Adze.Mesh.Models;
using NUnit.Framework;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Tests.Mesh;

[TestFixture]
public sealed class PrimitiveFitterTests
{
    [Test]
    public void Fit_EmptyMesh_ReturnsEmptyResults()
    {
        PrimitiveFitResults result = PrimitiveFitter.Fit(new AdzeMesh());

        Assert.That(result.Planes, Is.Empty);
        Assert.That(result.TotalCoveredAreaFraction, Is.Zero);
    }

    [Test]
    public void Fit_UnitCube_DetectsSixPlanes()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_UnitCube());
        PrimitiveFitResults result = PrimitiveFitter.Fit(mesh);

        // The unit cube has 6 axis-aligned faces. Triangle pairs should
        // collapse into 6 planes with full coverage.
        Assert.That(result.Planes, Has.Count.EqualTo(6));
        Assert.That(result.TotalCoveredAreaFraction, Is.EqualTo(1.0).Within(1e-6));
    }

    [Test]
    public void Fit_UnitCube_PlanesAreAxisAligned()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_UnitCube());
        PrimitiveFitResults result = PrimitiveFitter.Fit(mesh);

        // Each plane should have a normal aligned with ±X, ±Y, or ±Z.
        foreach (PlaneFit plane in result.Planes)
        {
            double maxComponent = System.Math.Max(
                System.Math.Abs(plane.Normal.X),
                System.Math.Max(System.Math.Abs(plane.Normal.Y), System.Math.Abs(plane.Normal.Z)));
            Assert.That(maxComponent, Is.EqualTo(1.0).Within(0.01),
                "Plane normal " + plane.Normal + " is not axis-aligned.");
        }
    }

    [Test]
    public void Fit_UnitCube_EachPlaneHasTwoTriangles()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_UnitCube());
        PrimitiveFitResults result = PrimitiveFitter.Fit(mesh);

        foreach (PlaneFit plane in result.Planes)
        {
            Assert.That(plane.InlierTriangleIndices, Has.Count.EqualTo(2),
                "Each cube face is two triangles; expected 2 inliers per plane.");
            Assert.That(plane.InlierAreaFraction, Is.EqualTo(1.0 / 6.0).Within(1e-6));
        }
    }

    [Test]
    public void Fit_OpenBox_DetectsFourPlanes()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_OpenBox());
        PrimitiveFitResults result = PrimitiveFitter.Fit(mesh);

        // Open box has bottom + top + 2 sides → 4 planes.
        Assert.That(result.Planes, Has.Count.EqualTo(4));
        Assert.That(result.TotalCoveredAreaFraction, Is.EqualTo(1.0).Within(1e-6));
    }
}
