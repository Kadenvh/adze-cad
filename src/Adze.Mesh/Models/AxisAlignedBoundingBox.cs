using System;

namespace Adze.Mesh.Models;

public readonly struct AxisAlignedBoundingBox
{
    public readonly Vector3d Min;
    public readonly Vector3d Max;

    public AxisAlignedBoundingBox(Vector3d min, Vector3d max)
    {
        Min = min;
        Max = max;
    }

    public static AxisAlignedBoundingBox Empty => new AxisAlignedBoundingBox(
        new Vector3d(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new Vector3d(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;

    public Vector3d Size => Max - Min;

    public Vector3d Center => (Min + Max) * 0.5;

    public double DiagonalLength => Size.Length;

    public double Volume
    {
        get
        {
            Vector3d s = Size;
            return s.X * s.Y * s.Z;
        }
    }

    public AxisAlignedBoundingBox Expand(Vector3d point) => new AxisAlignedBoundingBox(
        new Vector3d(Math.Min(Min.X, point.X), Math.Min(Min.Y, point.Y), Math.Min(Min.Z, point.Z)),
        new Vector3d(Math.Max(Max.X, point.X), Math.Max(Max.Y, point.Y), Math.Max(Max.Z, point.Z)));
}
