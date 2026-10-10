using System.Numerics;

namespace Tertius;

/// <summary>A double-precision rotation quaternion. Multiplication is the Hamilton product: (a * b) rotates by b, then by a.</summary>
public record struct DQuaternion(double X, double Y, double Z, double W)
{
    public static DQuaternion Identity => new(0, 0, 0, 1);

    public static DQuaternion operator *(DQuaternion a, DQuaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public readonly double LengthSquared => X * X + Y * Y + Z * Z + W * W;

    /// <summary>The inverse of a unit quaternion.</summary>
    public readonly DQuaternion Conjugate() => new(-X, -Y, -Z, W);

    public readonly DQuaternion Normalized()
    {
        double length = Math.Sqrt(LengthSquared);
        return length > 0 ? new(X / length, Y / length, Z / length, W / length) : Identity;
    }

    /// <summary>Rotates a vector by this unit quaternion.</summary>
    public readonly Double3 Rotate(Double3 v)
    {
        var u = new Double3(X, Y, Z);
        Double3 t = 2.0 * Double3.Cross(u, v);
        return v + W * t + Double3.Cross(u, t);
    }

    public readonly Vector3 Rotate(Vector3 v) => Rotate((Double3)v).ToVector3();

    public static DQuaternion FromAxisAngle(Double3 axis, double angle)
    {
        Double3 n = axis.Normalized();
        double half = angle * 0.5;
        double s = Math.Sin(half);
        return new(n.X * s, n.Y * s, n.Z * s, Math.Cos(half));
    }

    public static DQuaternion FromQuaternion(Quaternion q) => new(q.X, q.Y, q.Z, q.W);

    public readonly Quaternion ToQuaternion() => Quaternion.Normalize(new((float)X, (float)Y, (float)Z, (float)W));

    /// <summary>Normalized linear interpolation along the shorter arc.</summary>
    public static DQuaternion Nlerp(DQuaternion a, DQuaternion b, double t)
    {
        double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        double sign = dot < 0 ? -1 : 1;
        return new DQuaternion(
            a.X + (sign * b.X - a.X) * t,
            a.Y + (sign * b.Y - a.Y) * t,
            a.Z + (sign * b.Z - a.Z) * t,
            a.W + (sign * b.W - a.W) * t).Normalized();
    }

    public override readonly string ToString() => $"({X:G9}, {Y:G9}, {Z:G9}, {W:G9})";
}
