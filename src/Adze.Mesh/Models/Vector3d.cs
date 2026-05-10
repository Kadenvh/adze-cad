using System;

namespace Adze.Mesh.Models;

/// <summary>
/// Double-precision 3D vector. Used for vertex positions and normals throughout
/// the mesh layer. Doubles (not floats) because STL coordinates can span large
/// CAD volumes and downstream primitive fitting cares about precision.
/// </summary>
public readonly struct Vector3d : IEquatable<Vector3d>
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vector3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3d Zero => new Vector3d(0, 0, 0);

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Length => Math.Sqrt(LengthSquared);

    public Vector3d Normalized()
    {
        double len = Length;
        if (len < 1e-15) return Zero;
        return new Vector3d(X / len, Y / len, Z / len);
    }

    public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.X * s, a.Y * s, a.Z * s);

    public static Vector3d operator /(Vector3d a, double s) => new Vector3d(a.X / s, a.Y / s, a.Z / s);

    public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);

    public bool Equals(Vector3d other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is Vector3d v && Equals(v);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + X.GetHashCode();
            hash = hash * 31 + Y.GetHashCode();
            hash = hash * 31 + Z.GetHashCode();
            return hash;
        }
    }

    public override string ToString() => "(" + X.ToString("R") + ", " + Y.ToString("R") + ", " + Z.ToString("R") + ")";
}
