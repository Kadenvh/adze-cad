using System.IO;
using Adze.Contracts.Models;
using Adze.Contracts.Tooling;
using Adze.Tests.Helpers;
using Adze.Tests.Mesh;
using Adze.Tools.Mesh;
using NUnit.Framework;

namespace Adze.Tests.Tools;

[TestFixture]
public sealed class AnalyzeMeshFileToolTests
{
    [Test]
    public void Execute_NoPath_ReturnsFailure()
    {
        var tool = new AnalyzeMeshFileTool();
        SessionContext context = SessionContextFactory.CreateMinimal();

        ToolResult result = tool.Execute(context, new AnalyzeMeshFileParameters());

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("file_path"));
    }

    [Test]
    public void Execute_MissingFile_ReturnsFailure()
    {
        var tool = new AnalyzeMeshFileTool();
        SessionContext context = SessionContextFactory.CreateMinimal();
        string ghostPath = Path.Combine(Path.GetTempPath(), "adze-no-such-" + System.Guid.NewGuid().ToString("N") + ".stl");

        ToolResult result = tool.Execute(context, new AnalyzeMeshFileParameters { FilePath = ghostPath });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Summary, Does.Contain("not found"));
    }

    [Test]
    public void Execute_UnsupportedExtension_ReturnsFailure()
    {
        var tool = new AnalyzeMeshFileTool();
        SessionContext context = SessionContextFactory.CreateMinimal();
        string path = MeshTestFixtures.WriteToTemp(new byte[] { 0, 1, 2 }, ".obj");
        try
        {
            ToolResult result = tool.Execute(context, new AnalyzeMeshFileParameters { FilePath = path });
            Assert.That(result.Success, Is.False);
            Assert.That(result.Summary, Does.Contain("Unsupported"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Execute_UnitCubeBinary_ReturnsExpectedStats()
    {
        var tool = new AnalyzeMeshFileTool();
        SessionContext context = SessionContextFactory.CreateMinimal();
        string path = MeshTestFixtures.WriteToTemp(MeshTestFixtures.BuildBinaryStl_UnitCube());
        try
        {
            ToolResult result = tool.Execute(context, new AnalyzeMeshFileParameters { FilePath = path });

            Assert.That(result.Success, Is.True);
            Assert.That(result.ToolName, Is.EqualTo(ToolNames.AnalyzeMeshFile));
            Assert.That(result.Data["triangle_count"], Is.EqualTo(12));
            Assert.That(result.Data["is_closed"], Is.EqualTo(true));
            Assert.That(result.Data["convertibility"], Is.EqualTo("Direct"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Execute_UnitCubeAscii_ReturnsExpectedStats()
    {
        var tool = new AnalyzeMeshFileTool();
        SessionContext context = SessionContextFactory.CreateMinimal();
        string path = MeshTestFixtures.WriteToTemp(MeshTestFixtures.BuildAsciiStl_UnitCube());
        try
        {
            ToolResult result = tool.Execute(context, new AnalyzeMeshFileParameters { FilePath = path });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data["triangle_count"], Is.EqualTo(12));
            Assert.That(result.Data["is_closed"], Is.EqualTo(true));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Execute_OpenBox_AdvisoriesIncludeOpenEdges()
    {
        var tool = new AnalyzeMeshFileTool();
        SessionContext context = SessionContextFactory.CreateMinimal();
        string path = MeshTestFixtures.WriteToTemp(MeshTestFixtures.BuildBinaryStl_OpenBox());
        try
        {
            ToolResult result = tool.Execute(context, new AnalyzeMeshFileParameters { FilePath = path });

            Assert.That(result.Success, Is.True);
            Assert.That(result.Data["is_closed"], Is.EqualTo(false));
            object? advisoriesObj = result.Data["advisories"];
            Assert.That(advisoriesObj, Is.Not.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
