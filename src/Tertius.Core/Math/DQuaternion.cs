using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>A double-precision rotation quaternion. Multiplication is the Hamilton product: (a * b) rotates by b, then by a.</summary>
public record struct DQuaternion(double X, double Y, double Z, double W)
{
    /// <summary>
    /// The most that the square of the length of a unit quaternion may differ from 1. A rotation that was built in double
    /// precision from axes and angles differs by about 1E-16. A float rotation from Jolt, widened to double, differs by up to
    /// 2E-7, so it is not unit length until <see cref="Normalized"/> has made it so.
    /// </summary>
    public const double UnitTolerance = 1e-12;

    public static DQuaternion Identity => new(X: 0, Y: 0, Z: 0, W: 1);

    public readonly double LengthSquared => X * X + Y * Y + Z * Z + W * W;

    /// <summary>
    /// True when the square of the length is within <see cref="UnitTolerance"/> of 1, so that <see cref="Rotate(Double3)"/>
    /// and
    /// <see cref="Conjugate"/> are exact. False for the zero quaternion and for one with a component that is not finite.
    /// </summary>
    public readonly bool IsUnit => Math.Abs(LengthSquared - 1) <= UnitTolerance;

    public static DQuaternion operator *(DQuaternion a, DQuaternion b) =>
        new(
            X: a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            Y: a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            Z: a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            W: a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z
        );

    /// <summary>The inverse of a unit quaternion.</summary>
    public readonly DQuaternion Conjugate() => new(X: -X, Y: -Y, Z: -Z, W: W);

    public readonly DQuaternion Normalized()
    {
        double length = Math.Sqrt(LengthSquared);
        return length > 0 ? new(X: X / length, Y: Y / length, Z: Z / length, W: W / length) : Identity;
    }

    /// <summary>
    /// Rotates a vector by this unit quaternion. For a quaternion that is not unit length, the result is not a rotation of the
    /// vector: the error is about the vector's length times the excess of the square of this quaternion's length over 1.
    /// </summary>
    public readonly Double3 Rotate(Double3 v)
    {
        var u = new Double3(X, Y, Z);
        Double3 t = 2.0 * Double3.Cross(u, v);
        return v + W * t + Double3.Cross(u, t);
    }

    /// <summary>
    /// Rotates a vector by this quaternion as if it were normalized first, for a quaternion that is not unit length. The
    /// result equals that of <c>Normalized().Rotate(v)</c>, but it takes a division and no square root, so
    /// <see cref="DPose.Compose"/> stays cheap. Even that division shows on the hot loop of a render snapshot, which uses
    /// <see cref="DPose.ComposeUnitParent"/> for parents that are unit length already. The zero quaternion, which has no
    /// direction, and a quaternion whose length is not a number leave the vector as it is, as <see cref="Normalized"/> turns
    /// them into the identity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly Double3 RotateNormalizing(Double3 v)
    {
        double lengthSquared = LengthSquared;
        if (!(lengthSquared > 0))
            return v;
        // Rotate computes v + W t + u × t, with t = 2 u × v and u the vector part of q. Both terms after v grow with the square
        // of the length of q, so dividing their sum by the square of the length is the rotation by q / |q|.
        var u = new Double3(X, Y, Z);
        Double3 t = 2.0 * Double3.Cross(u, v);
        return v + (W * t + Double3.Cross(u, t)) * (1.0 / lengthSquared);
    }

    public readonly Vector3 Rotate(Vector3 v) => Rotate((Double3)v).ToVector3();

    public static DQuaternion FromAxisAngle(Double3 axis, double angle)
    {
        Double3 n = axis.Normalized();
        double half = angle * 0.5;
        double s = Math.Sin(half);
        return new(X: n.X * s, Y: n.Y * s, Z: n.Z * s, W: Math.Cos(half));
    }

    public static DQuaternion FromQuaternion(Quaternion q) => new(X: q.X, Y: q.Y, Z: q.Z, W: q.W);

    public readonly Quaternion ToQuaternion() =>
        Quaternion.Normalize(new(x: (float)X, y: (float)Y, z: (float)Z, w: (float)W));

    /// <summary>Normalized linear interpolation along the shorter arc.</summary>
    public static DQuaternion Nlerp(DQuaternion a, DQuaternion b, double t)
    {
        double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        double sign = dot < 0 ? -1 : 1;
        return new DQuaternion(
            X: a.X + (sign * b.X - a.X) * t,
            Y: a.Y + (sign * b.Y - a.Y) * t,
            Z: a.Z + (sign * b.Z - a.Z) * t,
            W: a.W + (sign * b.W - a.W) * t
        ).Normalized();
    }

    /// <summary>
    /// The constant angular velocity, in the parent's axes, in rad/s, that turns this rotation into <paramref name="to"/> in
    /// <paramref name="time"/> seconds, the short way. Zero when the two are the same rotation, whatever their signs. Neither
    /// rotation needs to be unit length.
    /// </summary>
    public readonly Double3 AngularVelocityTo(DQuaternion to, double time)
    {
        DQuaternion delta = (to * Conjugate()).Normalized();
        if (delta.W < 0)
            delta = new(X: -delta.X, Y: -delta.Y, Z: -delta.Z, W: -delta.W);
        var axis = new Double3(delta.X, delta.Y, delta.Z);
        double sinHalf = axis.Length;
        if (sinHalf < 1e-15)
            return Double3.Zero;
        double angle = 2 * Math.Atan2(sinHalf, delta.W);
        return axis * (angle / (sinHalf * time));
    }

    public override readonly string ToString() => $"({X:G9}, {Y:G9}, {Z:G9}, {W:G9})";
}
