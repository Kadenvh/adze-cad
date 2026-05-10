using System.IO;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tests.Mesh;
using Adze.Tools.Write;
using NUnit.Framework;

namespace Adze.Tests.Tools;

/// <summary>
/// ImportMeshTool unit tests cover the Preview path only — the Apply path
/// requires a real <c>ISldWorks</c> COM application and is exercised by the
/// live host validation suite, not unit tests.
/// </summary>
[TestFixture]
public sealed class ImportMeshToolTests
{
    [Test]
    public void Preview_NoPath_SurfacesWarning()
    {
        var tool = new ImportMeshTool();
        WritePreview preview = tool.Preview(SessionContextFactory.CreateMinimal(), new ImportMeshParameters());

        Assert.That(preview.ToolName, Is.EqualTo(ToolNames.ImportMesh));
        Assert.That(preview.Warnings, Has.Some.Contains("file_path"));
    }

    [Test]
    public void Preview_MissingFile_SurfacesWarning()
    {
        var tool = new ImportMeshTool();
        string ghost = Path.Combine(Path.GetTempPath(), "adze-no-such-" + System.Guid.NewGuid().ToString("N") + ".stl");
        WritePreview preview = tool.Preview(
            SessionContextFactory.CreateMinimal(),
            new ImportMeshParameters { FilePath = ghost });

        Assert.That(preview.Warnings, Has.Some.Contains("not found"));
    }

    [Test]
    public void Preview_UnsupportedExtension_SurfacesWarning()
    {
        var tool = new ImportMeshTool();
        string path = MeshTestFixtures.WriteToTemp(new byte[] { 1, 2, 3 }, ".ply");
        try
        {
            WritePreview preview = tool.Preview(
                SessionContextFactory.CreateMinimal(),
                new ImportMeshParameters { FilePath = path });

            Assert.That(preview.Warnings, Has.Some.Contains("Unsupported"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Preview_UnitCube_BuildsRichSummary()
    {
        var tool = new ImportMeshTool();
        string path = MeshTestFixtures.WriteToTemp(MeshTestFixtures.BuildBinaryStl_UnitCube());
        try
        {
            WritePreview preview = tool.Preview(
                SessionContextFactory.CreateMinimal(),
                new ImportMeshParameters { FilePath = path });

            Assert.That(preview.Summary, Does.Contain("12"));
            Assert.That(preview.Summary, Does.Contain("closed"));
            Assert.That(preview.Changes, Has.Count.GreaterThanOrEqualTo(2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Preview_NonGraphicsBodyStrategy_WarnsAboutFallback()
    {
        var tool = new ImportMeshTool();
        string path = MeshTestFixtures.WriteToTemp(MeshTestFixtures.BuildBinaryStl_UnitCube());
        try
        {
            WritePreview preview = tool.Preview(
                SessionContextFactory.CreateMinimal(),
                new ImportMeshParameters { FilePath = path, Strategy = "scan_to_3d" });

            Assert.That(
                preview.Warnings,
                Has.Some.Contains("reserved").Or.Some.Contains("graphics body"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void BuildUndoLabel_IncludesFileName()
    {
        var tool = new ImportMeshTool();
        string label = tool.BuildUndoLabel(new ImportMeshParameters { FilePath = "C:\\models\\bracket.stl" });

        Assert.That(label, Does.Contain("import mesh"));
        Assert.That(label, Does.Contain("bracket.stl"));
    }
}
