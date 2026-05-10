using System.IO;
using System.Text;
using Adze.Mesh.Io;
using Adze.Mesh.Models;
using NUnit.Framework;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Tests.Mesh;

[TestFixture]
public sealed class StlReaderTests
{
    [Test]
    public void Detect_BinaryStl_DetectsBinary()
    {
        byte[] bytes = MeshTestFixtures.BuildBinaryStl_UnitCube();
        Assert.That(StlReader.Detect(bytes), Is.EqualTo(StlFormat.Binary));
    }

    [Test]
    public void Detect_AsciiStl_DetectsAscii()
    {
        byte[] bytes = Encoding.ASCII.GetBytes(MeshTestFixtures.BuildAsciiStl_UnitCube());
        Assert.That(StlReader.Detect(bytes), Is.EqualTo(StlFormat.Ascii));
    }

    [Test]
    public void Detect_TooSmall_TreatsAsAscii()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("solid x\nendsolid\n");
        Assert.That(StlReader.Detect(bytes), Is.EqualTo(StlFormat.Ascii));
    }

    [Test]
    public void Detect_BinarySolidPrefix_StillDetectsBinaryWhenSizeMatches()
    {
        // Build a binary STL whose 80-byte header starts with "solid". Size
        // matches the declared count, so the reader should still classify it
        // as binary despite the deceptive prefix.
        byte[] bytes = MeshTestFixtures.BuildBinaryStl_SingleTriangle();
        // Overwrite the first 5 bytes of the header with "solid".
        Encoding.ASCII.GetBytes("solid", 0, 5, bytes, 0);
        Assert.That(StlReader.Detect(bytes), Is.EqualTo(StlFormat.Binary));
    }

    [Test]
    public void ReadFromBytes_BinaryUnitCube_Returns12Triangles()
    {
        byte[] bytes = MeshTestFixtures.BuildBinaryStl_UnitCube();
        AdzeMesh mesh = StlReader.ReadFromBytes(bytes);

        Assert.That(mesh.TriangleCount, Is.EqualTo(12));
        Assert.That(mesh.Header, Is.Not.Null);
        Assert.That(mesh.Header, Does.Contain("unit-cube-binary"));
    }

    [Test]
    public void ReadFromBytes_AsciiUnitCube_Returns12Triangles()
    {
        byte[] bytes = Encoding.ASCII.GetBytes(MeshTestFixtures.BuildAsciiStl_UnitCube());
        AdzeMesh mesh = StlReader.ReadFromBytes(bytes);

        Assert.That(mesh.TriangleCount, Is.EqualTo(12));
        Assert.That(mesh.Header, Is.EqualTo("unit-cube-ascii"));
    }

    [Test]
    public void ReadFromFile_PreservesFullPath()
    {
        string path = MeshTestFixtures.WriteToTemp(MeshTestFixtures.BuildBinaryStl_UnitCube());
        try
        {
            AdzeMesh mesh = StlReader.ReadFromFile(path);
            Assert.That(mesh.SourcePath, Is.EqualTo(Path.GetFullPath(path)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReadFromFile_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => StlReader.ReadFromFile(Path.Combine(Path.GetTempPath(), "does-not-exist-" + System.Guid.NewGuid().ToString("N") + ".stl")));
    }

    [Test]
    public void ReadFromBytes_BinarySingleTriangle_VertexCoordinatesPreserved()
    {
        byte[] bytes = MeshTestFixtures.BuildBinaryStl_SingleTriangle();
        AdzeMesh mesh = StlReader.ReadFromBytes(bytes);

        Assert.That(mesh.TriangleCount, Is.EqualTo(1));
        Triangle t = mesh.Triangles[0];
        Assert.That(t.V0, Is.EqualTo(new Vector3d(0, 0, 0)));
        Assert.That(t.V1, Is.EqualTo(new Vector3d(1, 0, 0)));
        Assert.That(t.V2, Is.EqualTo(new Vector3d(0, 1, 0)));
        Assert.That(t.StoredNormal, Is.EqualTo(new Vector3d(0, 0, 1)));
    }
}
