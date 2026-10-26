using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>A double-precision 3D vector. Used for positions that can be far from an origin.</summary>
public record struct Double3(double X, double Y, double Z)
{
    public static Double3 Zero => default;
    public static Double3 UnitX => new(1, 0, 0);
    public static Double3 UnitY => new(0, 1, 0);
    public static Double3 UnitZ => new(0, 0, 1);

    public readonly double LengthSquared => X * X + Y * Y + Z * Z;
    public readonly double Length => Math.Sqrt(LengthSquared);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Double3 operator +(Double3 a, Double3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Double3 operator -(Double3 a, Double3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Double3 operator -(Double3 a) => new(-a.X, -a.Y, -a.Z);

    public static Double3 operator *(Double3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Double3 operator *(double s, Double3 a) => new(a.X * s, a.Y * s, a.Z * s);

    public static Double3 operator /(Double3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    /// <summary>Component-wise product.</summary>
    public static Double3 operator *(Double3 a, Double3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    /// <summary>Component-wise quotient.</summary>
    public static Double3 operator /(Double3 a, Double3 b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

    public static double Dot(Double3 a, Double3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Double3 Cross(Double3 a, Double3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static double Distance(Double3 a, Double3 b) => (a - b).Length;

    public static Double3 Lerp(Double3 a, Double3 b, double t) => a + (b - a) * t;

    public readonly Double3 Normalized()
    {
        double length = Length;
        return length > 0 ? this / length : Zero;
    }

    /// <summary>This vector at unit length, or <paramref name="fallback"/> when it has no direction.</summary>
    public readonly Double3 NormalizedOr(Double3 fallback)
    {
        double length = Length;
        return length > 0 ? this / length : fallback;
    }

    /// <summary>Widening conversion: every float vector is exactly representable.</summary>
    public static implicit operator Double3(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Narrowing conversion. Use it only for values that are small, such as offsets relative to a nearby origin.</summary>
    public readonly Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

    public override readonly string ToString() => $"<{X:G9}, {Y:G9}, {Z:G9}>";
}
