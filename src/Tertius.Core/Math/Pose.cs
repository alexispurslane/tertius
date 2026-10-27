using System.Numerics;

namespace Tertius;

/// <summary>A rigid transform that a <see cref="PoseTree{TPose}"/> can compose.</summary>
public interface IPose<TSelf>
    where TSelf : struct, IPose<TSelf>
{
    static abstract TSelf Identity { get; }

    /// <summary>The pose of a child in its grandparent's space: <paramref name="parent"/> applied after
    /// <paramref name="local"/>.</summary>
    static abstract TSelf Compose(in TSelf parent, in TSelf local);

    TSelf Inverse();
}

/// <summary>A single-precision rigid transform. Used inside one reference frame.</summary>
public record struct Pose(Vector3 Position, Quaternion Rotation) : IPose<Pose>
{
    public static Pose Identity => new(Vector3.Zero, Quaternion.Identity);

    public static Pose Compose(in Pose parent, in Pose local) =>
        new(
            parent.Position + Vector3.Transform(local.Position, parent.Rotation),
            Quaternion.Normalize(parent.Rotation * local.Rotation)
        );

    public readonly Pose Inverse()
    {
        var inverse = Quaternion.Conjugate(Rotation);
        return new(-Vector3.Transform(Position, inverse), inverse);
    }

    public readonly Vector3 TransformPoint(Vector3 point) => Position + Vector3.Transform(point, Rotation);

    public readonly Vector3 TransformDirection(Vector3 direction) => Vector3.Transform(direction, Rotation);

    public readonly Vector3 InverseTransformPoint(Vector3 point) =>
        Vector3.Transform(point - Position, Quaternion.Conjugate(Rotation));

    public readonly Vector3 InverseTransformDirection(Vector3 direction) =>
        Vector3.Transform(direction, Quaternion.Conjugate(Rotation));

    public static Pose Lerp(in Pose a, in Pose b, float t) =>
        new(Vector3.Lerp(a.Position, b.Position, t), Quaternion.Slerp(a.Rotation, b.Rotation, t));

    /// <summary>
    /// The same pose in double precision, with the rotation normalized in double. A float quaternion is not exactly unit
    /// length, and a double-precision pose composes and inverts as a rotation only when its quaternion is.
    /// </summary>
    public readonly DPose ToDPose() => new(Position, DQuaternion.FromQuaternion(Rotation).Normalized());
}

/// <summary>A double-precision rigid transform. Used for reference frames relative to their parents.</summary>
/// <remarks>
/// <see cref="Compose"/> and <see cref="Inverse"/> normalize the rotation that they use, so they stay exact for a rotation that
/// is a little off unit length, such as a float quaternion from Jolt. <see cref="TransformPoint"/>,
/// <see cref="TransformDirection"/>
/// and the two inverse transforms use the rotation as it is, for speed. They need a unit rotation, which the frame tree keeps
/// for the local pose of every frame (see <see cref="FrameTree.AssertInvariants(int)"/>), and which <see cref="Compose"/>
/// gives.
/// <see cref="ComposeUnitParent"/> is <see cref="Compose"/> without the normalization of the parent's rotation, for a hot
/// path where the parent is unit length already.
/// <para>
/// <c>new DPose()</c>, and so <c>new DPose { Position = p }</c>, has the identity rotation. The default value of the struct,
/// <c>default(DPose)</c>, skips every constructor, so its rotation is zero, which is not a rotation: spawn refuses it.
/// </para>
/// </remarks>
public record struct DPose(Double3 Position, DQuaternion Rotation) : IPose<DPose>
{
    public static DPose Identity => new(Double3.Zero, DQuaternion.Identity);

    /// <summary>A pose at the origin with the identity rotation.</summary>
    public DPose()
        : this(Double3.Zero, DQuaternion.Identity) { }

    /// <summary>
    /// True when the position is finite and the rotation can be normalized: the square of its length is finite and not zero.
    /// A rotation with a component that is not finite fails that test, and so does one too small or too large to square.
    /// </summary>
    public readonly bool IsValid =>
        double.IsFinite(Position.X)
        && double.IsFinite(Position.Y)
        && double.IsFinite(Position.Z)
        && Rotation.LengthSquared is > 0 and < double.PositiveInfinity;

    /// <summary>
    /// <paramref name="parent"/> applied after <paramref name="local"/>. A rotation that is not unit length acts as its
    /// normalized rotation does, in the position and in the rotation of the result. So the pose of a parent that Jolt gave,
    /// whose rotation is a float quaternion, moves the position of its child by the same rotation that it composes into the
    /// child's rotation.
    /// </summary>
    public static DPose Compose(in DPose parent, in DPose local) =>
        new(
            parent.Position + parent.Rotation.RotateNormalizing(local.Position),
            (parent.Rotation * local.Rotation).Normalized()
        );

    /// <summary>
    /// <see cref="Compose"/> for a <paramref name="parent"/> whose rotation is unit length (see
    /// <see cref="DQuaternion.IsUnit"/>).
    /// It rotates the position of the child by the rotation of the parent as it is, and so it skips the division that
    /// <see cref="Compose"/> needs to normalize that rotation. Use it on a hot path where the parent is unit length because of
    /// where it came from, such as a pose that <see cref="Compose"/> gave, or the local pose of a frame. The rotation of the
    /// result is normalized, as for <see cref="Compose"/>, so it is unit length whatever the rotation of
    /// <paramref name="local"/>.
    /// <para>
    /// A parent that is within <see cref="DQuaternion.UnitTolerance"/> of unit length counts as unit length. The position of the
    /// result then differs from that of <see cref="Compose"/> by at most the length of the child's position times that
    /// tolerance. A parent that is not unit length is a bug in the caller: a build with contracts refuses it, and another build
    /// moves the position of the child by a vector that is longer or shorter than it should be.
    /// </para>
    /// </summary>
    internal static DPose ComposeUnitParent(in DPose parent, in DPose local)
    {
        Contract.Requires(parent.Rotation.IsUnit);
        return new(
            parent.Position + parent.Rotation.Rotate(local.Position),
            (parent.Rotation * local.Rotation).Normalized()
        );
    }

    /// <summary>
    /// The pose that undoes this one. A rotation that is not unit length acts as its normalized rotation does, in the
    /// position and in the rotation of the result, so <see cref="Compose"/> of a pose and its inverse is the identity.
    /// </summary>
    public readonly DPose Inverse()
    {
        DQuaternion inverse = Rotation.Normalized().Conjugate();
        return new(-inverse.Rotate(Position), inverse);
    }

    /// <summary>
    /// This pose with its rotation normalized, unless the rotation is unit length already (see
    /// <see cref="DQuaternion.IsUnit"/>).
    /// A rotation that is unit length stays as it is, bit for bit, so a pose that needs no change does not change by rounding.
    /// A zero rotation becomes the identity.
    /// </summary>
    public readonly DPose WithUnitRotation() => Rotation.IsUnit ? this : this with { Rotation = Rotation.Normalized() };

    public readonly Double3 TransformPoint(Double3 point) => Position + Rotation.Rotate(point);

    public readonly Double3 TransformDirection(Double3 direction) => Rotation.Rotate(direction);

    public readonly Double3 InverseTransformPoint(Double3 point) => Rotation.Conjugate().Rotate(point - Position);

    public readonly Double3 InverseTransformDirection(Double3 direction) => Rotation.Conjugate().Rotate(direction);

    /// <summary>Narrowing conversion. Use it only when the position is small, such as a pose relative to a nearby origin.</summary>
    public readonly Pose ToPose() => new(Position.ToVector3(), Rotation.ToQuaternion());
}

/// <summary>An axis-aligned box.</summary>
public record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    public readonly Vector3 Center => (Min + Max) * 0.5f;
    public readonly Vector3 Extents => (Max - Min) * 0.5f;

    public readonly bool Contains(Vector3 point) =>
        point.X >= Min.X
        && point.Y >= Min.Y
        && point.Z >= Min.Z
        && point.X <= Max.X
        && point.Y <= Max.Y
        && point.Z <= Max.Z;
}
