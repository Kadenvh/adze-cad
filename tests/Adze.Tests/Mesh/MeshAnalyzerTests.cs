using Adze.Mesh.Analysis;
using Adze.Mesh.Io;
using Adze.Mesh.Models;
using NUnit.Framework;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Tests.Mesh;

[TestFixture]
public sealed class MeshAnalyzerTests
{
    [Test]
    public void Analyze_ClosedManifold_ReturnsDirectConvertibility()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_UnitCube());
        MeshAnalysis analysis = MeshAnalyzer.Analyze(mesh);

        Assert.That(analysis.Convertibility, Is.EqualTo(MeshSolidConvertibility.Direct));
        Assert.That(analysis.RecommendedStrategy, Is.EqualTo(ImportStrategy.GraphicsThenConvert));
    }

    [Test]
    public void Analyze_OpenMesh_ReturnsRepairOrReverseEngineer()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_OpenBox());
        MeshAnalysis analysis = MeshAnalyzer.Analyze(mesh);

        Assert.That(analysis.Convertibility,
            Is.EqualTo(MeshSolidConvertibility.NeedsRepair)
                .Or.EqualTo(MeshSolidConvertibility.NeedsReverseEngineering));
        Assert.That(analysis.Advisories, Is.Not.Empty);
    }

    [Test]
    public void Analyze_EmptyMesh_ReturnsNotConvertible()
    {
        var mesh = new AdzeMesh();
        MeshAnalysis analysis = MeshAnalyzer.Analyze(mesh);

        Assert.That(analysis.Convertibility, Is.EqualTo(MeshSolidConvertibility.NotConvertible));
    }

    [Test]
    public void Analyze_OpenMesh_HasOpenEdgeAdvisory()
    {
        AdzeMesh mesh = StlReader.ReadFromBytes(MeshTestFixtures.BuildBinaryStl_OpenBox());
        MeshAnalysis analysis = MeshAnalyzer.Analyze(mesh);

        bool hasOpenEdgeAdvisory = false;
        foreach (string advisory in analysis.Advisories)
        {
            if (advisory.Contains("open") || advisory.Contains("boundary edge"))
            {
                hasOpenEdgeAdvisory = true;
                break;
            }
        }
        Assert.That(hasOpenEdgeAdvisory, Is.True, "Expected an advisory mentioning open/boundary edges.");
    }
}
