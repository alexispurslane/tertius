using System.Numerics;

namespace Tertius;

/// <summary>Up is one direction, as in a uniform gravity field.</summary>
/// <param name="Direction">The up direction: a unit vector in the axes of the camera's frame.</param>
public readonly record struct UpAlong(Vector3 Direction);

/// <summary>Up leads away from one point, as in the gravity field of a planet.</summary>
/// <param name="Center">
/// The point that up leads away from, relative to the instance's position, in the axes of the camera's frame. It moves
/// with the instance, so a planet's own center is exactly zero.
/// </param>
public readonly record struct UpAwayFrom(Vector3 Center);

/// <summary>
/// Which way is up for the hemisphere ambient light of one instance: the sky color lights a surface that faces up, the
/// ground color one that faces down, and a surface that faces sideways gets their mean. It is <see cref="UpAlong"/>,
/// <see cref="UpAwayFrom"/> or neither. The empty value is <see cref="Even"/>: no way is up, so every surface gets the
/// mean of the sky and the ground color, as in a frame without gravity.
/// The union generator writes its union members (docs/plans/union-tooling.md). The empty union is the all-zero default,
/// and its <c>HasValue</c> is false. It is 16 bytes: both cases keep their one vector in the same slot, because a value
/// holds only one case, and every render instance carries one.
/// </summary>
[GeneratedUnion(typeof(UpAlong), typeof(UpAwayFrom))]
public readonly partial struct HemisphereUp
{
    /// <summary>No way is up.</summary>
    public static HemisphereUp Even => default;

    /// <summary>Up is <paramref name="direction"/>, a unit vector in the axes of the camera's frame.</summary>
    public static HemisphereUp Along(Vector3 direction) => new(new UpAlong(direction));

    /// <summary>Up leads away from <paramref name="center"/>, relative to the instance's position.</summary>
    public static HemisphereUp AwayFrom(Vector3 center) => new(new UpAwayFrom(center));
}
