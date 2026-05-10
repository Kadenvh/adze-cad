using System;
using System.Globalization;
using System.IO;
using System.Text;
using Adze.Mesh.Models;
using AdzeMesh = Adze.Mesh.Models.Mesh;

namespace Adze.Mesh.Io;

/// <summary>
/// Reads STL files in either binary or ASCII form into a <see cref="Mesh"/>.
///
/// Format detection rules (in order):
///   1. Files smaller than 84 bytes (header + count) are treated as ASCII —
///      a valid binary STL cannot be smaller than that.
///   2. If the first non-whitespace token is "solid" AND the declared binary
///      size (header.tricount * 50 + 84) does not match the file length,
///      fall back to ASCII parse.
///   3. Otherwise, treat as binary.
///
/// We deliberately do NOT trust the literal "solid" prefix as the sole
/// indicator: many binary STLs in the wild start their 80-byte header with
/// the word "solid" because exporters set a name there. The size-mismatch
/// heuristic is the standard robust check.
/// </summary>
public static class StlReader
{
    private const int BinaryHeaderSize = 80;
    private const int BinaryTriangleSize = 50;
    private const int BinaryHeaderAndCountSize = BinaryHeaderSize + 4;

    public static AdzeMesh ReadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("STL path is required.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("STL file not found.", path);

        byte[] bytes = File.ReadAllBytes(path);
        AdzeMesh mesh = ReadFromBytes(bytes);
        mesh.SourcePath = Path.GetFullPath(path);
        return mesh;
    }

    public static AdzeMesh ReadFromBytes(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        StlFormat detected = Detect(bytes);
        return detected == StlFormat.Ascii ? ReadAscii(bytes) : ReadBinary(bytes);
    }

    public static StlFormat Detect(byte[] bytes)
    {
        if (bytes.Length < BinaryHeaderAndCountSize) return StlFormat.Ascii;

        // Peek at first 5 bytes for "solid"
        bool startsWithSolid = bytes.Length >= 5
            && bytes[0] == (byte)'s'
            && bytes[1] == (byte)'o'
            && bytes[2] == (byte)'l'
            && bytes[3] == (byte)'i'
            && bytes[4] == (byte)'d';

        if (!startsWithSolid) return StlFormat.Binary;

        // The file claims to be ASCII via "solid" prefix. Verify against the
        // declared binary size; if it matches the file length exactly, the
        // file is actually binary with a header that happens to start with
        // "solid".
        uint declaredCount = BitConverter.ToUInt32(bytes, BinaryHeaderSize);
        long declaredBinarySize = (long)BinaryHeaderAndCountSize + (long)declaredCount * BinaryTriangleSize;
        if (declaredBinarySize == bytes.Length)
        {
            return StlFormat.Binary;
        }

        return StlFormat.Ascii;
    }

    private static AdzeMesh ReadBinary(byte[] bytes)
    {
        if (bytes.Length < BinaryHeaderAndCountSize)
        {
            throw new InvalidDataException("STL file is too small to be a valid binary STL.");
        }

        string header = Encoding.ASCII.GetString(bytes, 0, BinaryHeaderSize).TrimEnd('\0', ' ');
        uint declaredCount = BitConverter.ToUInt32(bytes, BinaryHeaderSize);

        long expectedSize = (long)BinaryHeaderAndCountSize + (long)declaredCount * BinaryTriangleSize;
        if (expectedSize != bytes.Length)
        {
            // Not strictly fatal — some exporters lie about the count or pad the file.
            // We cap at the smaller of declared count and what fits.
            long usableBytes = bytes.Length - BinaryHeaderAndCountSize;
            uint maxFromBytes = (uint)Math.Max(0, usableBytes / BinaryTriangleSize);
            declaredCount = Math.Min(declaredCount, maxFromBytes);
        }

        var mesh = new AdzeMesh { Header = header };
        mesh.Triangles.Capacity = (int)Math.Min(declaredCount, int.MaxValue);

        int offset = BinaryHeaderAndCountSize;
        for (uint i = 0; i < declaredCount; i++)
        {
            float nx = BitConverter.ToSingle(bytes, offset + 0);
            float ny = BitConverter.ToSingle(bytes, offset + 4);
            float nz = BitConverter.ToSingle(bytes, offset + 8);

            float v0x = BitConverter.ToSingle(bytes, offset + 12);
            float v0y = BitConverter.ToSingle(bytes, offset + 16);
            float v0z = BitConverter.ToSingle(bytes, offset + 20);

            float v1x = BitConverter.ToSingle(bytes, offset + 24);
            float v1y = BitConverter.ToSingle(bytes, offset + 28);
            float v1z = BitConverter.ToSingle(bytes, offset + 32);

            float v2x = BitConverter.ToSingle(bytes, offset + 36);
            float v2y = BitConverter.ToSingle(bytes, offset + 40);
            float v2z = BitConverter.ToSingle(bytes, offset + 44);

            // Last 2 bytes are "attribute byte count", ignored by spec.
            offset += BinaryTriangleSize;

            mesh.Triangles.Add(new Triangle(
                new Vector3d(v0x, v0y, v0z),
                new Vector3d(v1x, v1y, v1z),
                new Vector3d(v2x, v2y, v2z),
                new Vector3d(nx, ny, nz)));
        }

        return mesh;
    }

    private static AdzeMesh ReadAscii(byte[] bytes)
    {
        // ASCII STL grammar (whitespace-tolerant):
        //   solid <name>
        //     facet normal <nx> <ny> <nz>
        //       outer loop
        //         vertex <x> <y> <z>
        //         vertex <x> <y> <z>
        //         vertex <x> <y> <z>
        //       endloop
        //     endfacet
        //     ...
        //   endsolid <name>
        //
        // We parse with a simple token stream — robust to extra whitespace,
        // case variations, and trailing junk.

        var mesh = new AdzeMesh();
        string text = Encoding.ASCII.GetString(bytes);
        string[] tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        Vector3d normal = Vector3d.Zero;
        Vector3d v0 = Vector3d.Zero;
        Vector3d v1 = Vector3d.Zero;
        Vector3d v2 = Vector3d.Zero;
        int vertexInFacet = 0;

        for (int i = 0; i < tokens.Length; i++)
        {
            string tok = tokens[i];

            if (string.Equals(tok, "solid", StringComparison.OrdinalIgnoreCase))
            {
                if (mesh.Header == null && i + 1 < tokens.Length)
                {
                    mesh.Header = tokens[i + 1];
                }
                continue;
            }

            if (string.Equals(tok, "facet", StringComparison.OrdinalIgnoreCase))
            {
                vertexInFacet = 0;
                if (i + 4 < tokens.Length && string.Equals(tokens[i + 1], "normal", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseDouble(tokens[i + 2], out double nx)
                        && TryParseDouble(tokens[i + 3], out double ny)
                        && TryParseDouble(tokens[i + 4], out double nz))
                    {
                        normal = new Vector3d(nx, ny, nz);
                    }
                    i += 4;
                }
                continue;
            }

            if (string.Equals(tok, "vertex", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 3 >= tokens.Length) break;
                if (!TryParseDouble(tokens[i + 1], out double x)
                    || !TryParseDouble(tokens[i + 2], out double y)
                    || !TryParseDouble(tokens[i + 3], out double z))
                {
                    i += 3;
                    continue;
                }

                var vertex = new Vector3d(x, y, z);
                if (vertexInFacet == 0) v0 = vertex;
                else if (vertexInFacet == 1) v1 = vertex;
                else if (vertexInFacet == 2) v2 = vertex;
                vertexInFacet++;
                i += 3;
                continue;
            }

            if (string.Equals(tok, "endfacet", StringComparison.OrdinalIgnoreCase))
            {
                if (vertexInFacet >= 3)
                {
                    mesh.Triangles.Add(new Triangle(v0, v1, v2, normal));
                }
                vertexInFacet = 0;
                normal = Vector3d.Zero;
                continue;
            }
        }

        return mesh;
    }

    private static bool TryParseDouble(string s, out double value)
    {
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
