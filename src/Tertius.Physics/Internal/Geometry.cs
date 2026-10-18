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

/// <summary>Rotation helpers in double precision.</summary>
internal static class Rotations
{
    /// <summary>The constant angular velocity (parent axes) that turns <paramref name="from"/> into <paramref name="to"/> in <paramref name="time"/> seconds.</summary>
    public static Double3 AngularVelocity(DQuaternion from, DQuaternion to, double time)
    {
        DQuaternion delta = (to * from.Conjugate()).Normalized();
        if (delta.W < 0)
            delta = new DQuaternion(-delta.X, -delta.Y, -delta.Z, -delta.W);
        var axis = new Double3(delta.X, delta.Y, delta.Z);
        double sinHalf = axis.Length;
        if (sinHalf < 1e-15)
            return Double3.Zero;
        double angle = 2 * Math.Atan2(sinHalf, delta.W);
        return axis * (angle / (sinHalf * time));
    }

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

/// <summary>
/// The motion of a frame relative to its parent, in parent axes: the pose, the velocity of the frame origin and the
/// angular velocity. The conversions follow ARCHITECTURE.md, hand-off rule 5: v' = R⁻¹(v − v_frame − ω × r).
/// </summary>
internal readonly record struct FrameMotion(DPose Pose, Double3 Velocity, Double3 AngularVelocity)
{
    public DPose PoseToParent(in DPose local) => DPose.Compose(Pose, local);

    public DPose PoseFromParent(in DPose parentPose) => DPose.Compose(Pose.Inverse(), parentPose);

    /// <summary>The velocity in the parent of a point that moves with <paramref name="velocity"/> in the frame.</summary>
    public Double3 VelocityToParent(Double3 pointInFrame, Double3 velocity) =>
        Pose.TransformDirection(velocity) + Velocity + Double3.Cross(AngularVelocity, Pose.TransformDirection(pointInFrame));

    /// <summary>The velocity in the frame of a point that moves with <paramref name="velocity"/> in the parent.</summary>
    public Double3 VelocityFromParent(Double3 pointInParent, Double3 velocity) =>
        Pose.InverseTransformDirection(velocity - Velocity - Double3.Cross(AngularVelocity, pointInParent - Pose.Position));

    public Double3 AngularToParent(Double3 omega) => Pose.TransformDirection(omega) + AngularVelocity;

    public Double3 AngularFromParent(Double3 omega) => Pose.InverseTransformDirection(omega - AngularVelocity);
}
