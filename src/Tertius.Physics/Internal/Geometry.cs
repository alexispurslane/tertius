using System.Numerics;
using System.Runtime.CompilerServices;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>Conversions between Tertius and Jolt math types.</summary>
internal static class Conversions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RVector3 ToJolt(this Double3 v) => new(v.X, v.Y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Double3 ToDouble3(this RVector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Jolt stores rotations as float quaternions. The result is normalized.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Quaternion ToJolt(this DQuaternion q) => q.ToQuaternion();

    /// <summary>
    /// Widens a rotation that Jolt holds as a float quaternion to double precision, and normalizes it there. The length of a
    /// float quaternion is not exactly 1: the square of it lies between -7.3E-8 and +2.1E-7 from 1, a new value every step.
    /// The hull's rotation becomes the local pose of its frame, and a local pose that is not unit length puts the parent frame
    /// about a meter off, seen from the frame, when the parent is millions of meters away. So every rotation that leaves
    /// Jolt goes through here.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion ToDouble(this Quaternion q) => DQuaternion.FromQuaternion(q).Normalized();
}

/// <summary>Rotation helpers for the bodies. The angular velocity that turns one rotation into another is <see cref="DQuaternion.AngularVelocityTo"/>.</summary>
internal static class Rotations
{
    /// <summary>The smallest turn of <paramref name="rotation"/> that puts its local +Y axis on <paramref name="up"/>.</summary>
    public static Quaternion AlignUp(Quaternion rotation, Vector3 up)
    {
        Vector3 current = Vector3.Transform(Vector3.UnitY, rotation);
        float dot = Vector3.Dot(current, up);
        if (dot > 0.9999995f)
            return rotation;
        Quaternion turn;
        if (dot < -0.9999995f)
        {
            Vector3 axis = Vector3.Cross(current, Vector3.UnitX);
            if (axis.LengthSquared() < 1e-6f)
                axis = Vector3.Cross(current, Vector3.UnitZ);
            turn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        else
        {
            turn = Quaternion.Normalize(new Quaternion(Vector3.Cross(current, up), 1 + dot));
        }
        return Quaternion.Normalize(turn * rotation);
    }
}
