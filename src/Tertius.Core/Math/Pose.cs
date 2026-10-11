using System.Numerics;

namespace Tertius;

/// <summary>A rigid transform that a <see cref="PoseTree{TPose}"/> can compose.</summary>
public interface IPose<TSelf> where TSelf : struct, IPose<TSelf>
{
    static abstract TSelf Identity { get; }

    /// <summary>The pose of a child in its grandparent's space: <paramref name="parent"/> applied after <paramref name="local"/>.</summary>
    static abstract TSelf Compose(in TSelf parent, in TSelf local);

    TSelf Inverse();
}

/// <summary>A single-precision rigid transform. Used inside one reference frame.</summary>
public record struct Pose(Vector3 Position, Quaternion Rotation) : IPose<Pose>
{
    public static Pose Identity => new(Vector3.Zero, Quaternion.Identity);

    public static Pose Compose(in Pose parent, in Pose local) => new(
        parent.Position + Vector3.Transform(local.Position, parent.Rotation),
        Quaternion.Normalize(parent.Rotation * local.Rotation));

    public readonly Pose Inverse()
    {
        Quaternion inverse = Quaternion.Conjugate(Rotation);
        return new(-Vector3.Transform(Position, inverse), inverse);
    }

    public readonly Vector3 TransformPoint(Vector3 point) => Position + Vector3.Transform(point, Rotation);
    public readonly Vector3 TransformDirection(Vector3 direction) => Vector3.Transform(direction, Rotation);
    public readonly Vector3 InverseTransformPoint(Vector3 point) => Vector3.Transform(point - Position, Quaternion.Conjugate(Rotation));
    public readonly Vector3 InverseTransformDirection(Vector3 direction) => Vector3.Transform(direction, Quaternion.Conjugate(Rotation));

    public static Pose Lerp(in Pose a, in Pose b, float t) =>
        new(Vector3.Lerp(a.Position, b.Position, t), Quaternion.Slerp(a.Rotation, b.Rotation, t));

    public readonly DPose ToDPose() => new(Position, DQuaternion.FromQuaternion(Rotation));
}

/// <summary>A double-precision rigid transform. Used for reference frames relative to their parents.</summary>
/// <remarks>
/// <c>new DPose()</c>, and so <c>new DPose { Position = p }</c>, has the identity rotation. The default value of the struct,
/// <c>default(DPose)</c>, skips every constructor, so its rotation is zero, which is not a rotation: spawn refuses it.
/// </remarks>
public record struct DPose(Double3 Position, DQuaternion Rotation) : IPose<DPose>
{
    /// <summary>A pose at the origin with the identity rotation.</summary>
    public DPose()
        : this(Double3.Zero, DQuaternion.Identity)
    {
    }

    public static DPose Identity => new(Double3.Zero, DQuaternion.Identity);

    /// <summary>
    /// True when the position is finite and the rotation can be normalized: the square of its length is finite and not zero.
    /// A rotation with a component that is not finite fails that test, and so does one too small or too large to square.
    /// </summary>
    public readonly bool IsValid =>
        double.IsFinite(Position.X) && double.IsFinite(Position.Y) && double.IsFinite(Position.Z) &&
        Rotation.LengthSquared is > 0 and < double.PositiveInfinity;

    public static DPose Compose(in DPose parent, in DPose local) => new(
        parent.Position + parent.Rotation.Rotate(local.Position),
        (parent.Rotation * local.Rotation).Normalized());

    public readonly DPose Inverse()
    {
        DQuaternion inverse = Rotation.Conjugate();
        return new(-inverse.Rotate(Position), inverse);
    }

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
    public readonly bool Contains(Vector3 point) =>
        point.X >= Min.X && point.Y >= Min.Y && point.Z >= Min.Z &&
        point.X <= Max.X && point.Y <= Max.Y && point.Z <= Max.Z;

    public readonly Vector3 Center => (Min + Max) * 0.5f;
    public readonly Vector3 Extents => (Max - Min) * 0.5f;
}
