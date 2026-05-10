using System;
using System.IO;
using System.Text;
using Adze.Mesh.Models;

namespace Adze.Tests.Mesh;

/// <summary>
/// Procedural mesh fixtures used by Adze.Mesh test suites. Generated rather
/// than committed as binary assets to keep the repo lean and the tests
/// hermetic.
/// </summary>
internal static class MeshTestFixtures
{
    /// <summary>Builds a binary STL byte array containing a single triangle.</summary>
    public static byte[] BuildBinaryStl_SingleTriangle()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteBinaryHeader(bw, "single-tri");
        bw.Write((uint)1);
        WriteBinaryTriangle(bw, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a binary STL of an axis-aligned unit cube at the origin
    /// (0,0,0)–(1,1,1) with consistent outward-facing winding. 12 triangles,
    /// closed manifold. Each triangle is wound CCW when viewed from outside
    /// the cube; computed normals match the stored normals.
    /// </summary>
    public static byte[] BuildBinaryStl_UnitCube()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteBinaryHeader(bw, "unit-cube-binary");
        bw.Write((uint)12);

        foreach (var (nx, ny, nz, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z) in UnitCubeTriangles)
        {
            WriteBinaryTriangle(bw, nx, ny, nz, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z);
        }

        return ms.ToArray();
    }

    /// <summary>Builds an ASCII STL of the same unit cube as the binary fixture.</summary>
    public static string BuildAsciiStl_UnitCube()
    {
        var sb = new StringBuilder();
        sb.AppendLine("solid unit-cube-ascii");

        foreach (var (nx, ny, nz, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z) in UnitCubeTriangles)
        {
            WriteAsciiTriangle(sb, nx, ny, nz, v0x, v0y, v0z, v1x, v1y, v1z, v2x, v2y, v2z);
        }

        sb.AppendLine("endsolid unit-cube-ascii");
        return sb.ToString();
    }

    // Single source of truth for the unit cube. Each tuple is one triangle
    // with consistent outward CCW winding so the resulting mesh is a closed,
    // consistently-oriented manifold (Direct convertibility verdict).
    private static readonly (float nx, float ny, float nz,
                             float v0x, float v0y, float v0z,
                             float v1x, float v1y, float v1z,
                             float v2x, float v2y, float v2z)[] UnitCubeTriangles =
    {
        // -Z bottom: CCW from below produces normal (0,0,-1)
        (0, 0, -1, 0, 0, 0,  0, 1, 0,  1, 1, 0),
        (0, 0, -1, 0, 0, 0,  1, 1, 0,  1, 0, 0),
        // +Z top
        (0, 0, 1,  0, 0, 1,  1, 0, 1,  1, 1, 1),
        (0, 0, 1,  0, 0, 1,  1, 1, 1,  0, 1, 1),
        // -Y front
        (0, -1, 0, 0, 0, 0,  1, 0, 0,  1, 0, 1),
        (0, -1, 0, 0, 0, 0,  1, 0, 1,  0, 0, 1),
        // +Y back
        (0, 1, 0,  0, 1, 0,  0, 1, 1,  1, 1, 1),
        (0, 1, 0,  0, 1, 0,  1, 1, 1,  1, 1, 0),
        // -X left
        (-1, 0, 0, 0, 0, 0,  0, 0, 1,  0, 1, 1),
        (-1, 0, 0, 0, 0, 0,  0, 1, 1,  0, 1, 0),
        // +X right
        (1, 0, 0,  1, 0, 0,  1, 1, 0,  1, 1, 1),
        (1, 0, 0,  1, 0, 0,  1, 1, 1,  1, 0, 1),
    };

    /// <summary>
    /// Builds a binary STL containing only the bottom + top + 2 of the 4
    /// sides of a cube — i.e. an open box. Used to verify the open-edge
    /// detection path.
    /// </summary>
    public static byte[] BuildBinaryStl_OpenBox()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteBinaryHeader(bw, "open-box");
        bw.Write((uint)8);

        // Bottom + top
        WriteBinaryTriangle(bw, 0, 0, -1, 0, 0, 0, 1, 1, 0, 1, 0, 0);
        WriteBinaryTriangle(bw, 0, 0, -1, 0, 0, 0, 0, 1, 0, 1, 1, 0);
        WriteBinaryTriangle(bw, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1, 1);
        WriteBinaryTriangle(bw, 0, 0, 1, 0, 0, 1, 1, 1, 1, 0, 1, 1);
        // Only -Y and +X sides
        WriteBinaryTriangle(bw, 0, -1, 0, 0, 0, 0, 1, 0, 1, 1, 0, 0);
        WriteBinaryTriangle(bw, 0, -1, 0, 0, 0, 0, 0, 0, 1, 1, 0, 1);
        WriteBinaryTriangle(bw, 1, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 1);
        WriteBinaryTriangle(bw, 1, 0, 0, 1, 0, 0, 1, 1, 1, 1, 0, 1);

        return ms.ToArray();
    }

    public static string WriteToTemp(byte[] bytes, string suffix = ".stl")
    {
        string path = Path.Combine(Path.GetTempPath(), "adze-mesh-test-" + Guid.NewGuid().ToString("N") + suffix);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static string WriteToTemp(string text, string suffix = ".stl")
    {
        string path = Path.Combine(Path.GetTempPath(), "adze-mesh-test-" + Guid.NewGuid().ToString("N") + suffix);
        File.WriteAllText(path, text);
        return path;
    }

    private static void WriteBinaryHeader(BinaryWriter bw, string label)
    {
        byte[] header = new byte[80];
        byte[] labelBytes = Encoding.ASCII.GetBytes(label);
        Buffer.BlockCopy(labelBytes, 0, header, 0, Math.Min(labelBytes.Length, header.Length));
        bw.Write(header);
    }

    private static void WriteBinaryTriangle(
        BinaryWriter bw,
        float nx, float ny, float nz,
        float v0x, float v0y, float v0z,
        float v1x, float v1y, float v1z,
        float v2x, float v2y, float v2z)
    {
        bw.Write(nx); bw.Write(ny); bw.Write(nz);
        bw.Write(v0x); bw.Write(v0y); bw.Write(v0z);
        bw.Write(v1x); bw.Write(v1y); bw.Write(v1z);
        bw.Write(v2x); bw.Write(v2y); bw.Write(v2z);
        bw.Write((ushort)0);
    }

    private static void WriteAsciiTriangle(
        StringBuilder sb,
        double nx, double ny, double nz,
        double v0x, double v0y, double v0z,
        double v1x, double v1y, double v1z,
        double v2x, double v2y, double v2z)
    {
        sb.AppendLine("  facet normal " + Fmt(nx) + " " + Fmt(ny) + " " + Fmt(nz));
        sb.AppendLine("    outer loop");
        sb.AppendLine("      vertex " + Fmt(v0x) + " " + Fmt(v0y) + " " + Fmt(v0z));
        sb.AppendLine("      vertex " + Fmt(v1x) + " " + Fmt(v1y) + " " + Fmt(v1z));
        sb.AppendLine("      vertex " + Fmt(v2x) + " " + Fmt(v2y) + " " + Fmt(v2z));
        sb.AppendLine("    endloop");
        sb.AppendLine("  endfacet");
    }

    private static string Fmt(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
