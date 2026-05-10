using Adze.Mesh.Analysis;
using Adze.Mesh.Io;
using Adze.Mesh.Models;
using NUnit.Framework;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Tests.Mesh;

[TestFixture]
public sealed class MeshStatsCalculatorTests
{
    [Test]
    public void Compute_EmptyMesh_ReturnsZeros()
    {
        var mesh = new AdzeMesh();
        MeshStatistics stats = MeshStatsCalculator.Compute(mesh);

        Assert.That(stats.TriangleCount, Is.Zero);
        Assert.That(stats.SurfaceArea, Is.Zero);
        Assert.That(stats.IsClosed, Is.False);
    }

    [Test]
    public void Compute_UnitCube_HasExpectedSurfaceArea()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_UnitCube());
        MeshStatistics stats = MeshStatsCalculator.Compute(mesh);

        Assert.That(stats.TriangleCount, Is.EqualTo(12));
        // Unit cube has 6 unit faces.
        Assert.That(stats.SurfaceArea, Is.EqualTo(6.0).Within(1e-6));
    }

    [Test]
    public void Compute_UnitCube_HasExpectedVolume()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_UnitCube());
        MeshStatistics stats = MeshStatsCalculator.Compute(mesh);

        Assert.That(stats.EnclosedVolume, Is.EqualTo(1.0).Within(1e-6));
    }

    [Test]
    public void Compute_UnitCube_IsClosedManifold()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_UnitCube());
        MeshStatistics stats = MeshStatsCalculator.Compute(mesh);

        Assert.That(stats.IsClosed, Is.True);
        Assert.That(stats.OpenEdgeCount, Is.Zero);
        Assert.That(stats.NonManifoldEdgeCount, Is.Zero);
    }

    [Test]
    public void Compute_UnitCube_BoundingBoxIsZeroToOne()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_UnitCube());
        MeshStatistics stats = MeshStatsCalculator.Compute(mesh);

        Assert.That(stats.BoundingBox.Min, Is.EqualTo(new Vector3d(0, 0, 0)));
        Assert.That(stats.BoundingBox.Max, Is.EqualTo(new Vector3d(1, 1, 1)));
    }

    [Test]
    public void Compute_OpenBox_HasOpenEdges()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_OpenBox());
        MeshStatistics stats = MeshStatsCalculator.Compute(mesh);

        Assert.That(stats.IsClosed, Is.False);
        Assert.That(stats.OpenEdgeCount, Is.GreaterThan(0));
    }
}
