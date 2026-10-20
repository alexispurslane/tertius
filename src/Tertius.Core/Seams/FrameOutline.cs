using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>
/// The outline of a semantic frame that has a parent: its bubble, the ellipsoid of <see cref="FrameBounds"/>. The bubble
/// decides which objects belong to the frame.
/// </summary>
/// <param name="Center">The frame's origin, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="Rotation">The frame's axes in the axes of the camera's frame. The radii lie along these axes.</param>
/// <param name="Radii">The radii of the bubble along the frame's X, Y and Z axes, in meters.</param>
public readonly record struct SemanticOutline(Vector3 Center, Quaternion Rotation, Vector3 Radii);

/// <summary>The outline of a falling numeric frame: its contents sphere (see <see cref="FrameOutline"/>).</summary>
/// <param name="Center">The frame's origin, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="Radius">The radius of the contents sphere, in meters. 0 for a frame that holds nothing.</param>
public readonly record struct FallingOutline(Vector3 Center, float Radius);

/// <summary>The outline of a resting numeric frame: its contents sphere (see <see cref="FrameOutline"/>).</summary>
/// <param name="Center">As for <see cref="FallingOutline"/>.</param>
/// <param name="Radius">As for <see cref="FallingOutline"/>.</param>
public readonly record struct RestingOutline(Vector3 Center, float Radius);

/// <summary>
/// The outline of a nesting numeric frame: its contents sphere (see <see cref="FrameOutline"/>). A nesting frame holds only
/// numeric frames, so the sphere holds their outline spheres.
/// </summary>
/// <param name="Center">As for <see cref="FallingOutline"/>.</param>
/// <param name="Radius">As for <see cref="FallingOutline"/>.</param>
public readonly record struct NestingOutline(Vector3 Center, float Radius);

/// <summary>
/// The outline of a following frame: its contents sphere (see <see cref="FrameOutline"/>). A following frame holds its anchor
/// at its origin: a plain kinematic body, or the frame of a hull, whose bubble the sphere then holds.
/// </summary>
/// <param name="Center">As for <see cref="FallingOutline"/>.</param>
/// <param name="Radius">As for <see cref="FallingOutline"/>.</param>
public readonly record struct FollowingOutline(Vector3 Center, float Radius);

/// <summary>
/// The outline of one frame in a <see cref="RenderSnapshot"/>: the region that the rule of its frame covers. A semantic
/// frame with a parent has a <see cref="SemanticOutline"/>, its bubble. A numeric frame has no bubble, because its members
/// are a group of bodies, not the bodies in a region, so its outline shows what it holds: its contents sphere, the smallest
/// sphere centred on its origin that holds the bounding sphere of each body whose physics home it is and the outline sphere
/// of each frame that hangs in it. Each kind of numeric frame has a case of its own, so that the renderer can give each kind
/// its color. A root frame has no outline. The default value holds no outline.
/// </summary>
/// <remarks>
/// The outline sphere of a frame is the smallest sphere centred on its origin that holds its outline: the largest radius of
/// the bubble for a semantic frame, and the contents sphere for a numeric frame.
/// <para>
/// This is a custom C# 15 union, as <see cref="RenderLight"/> is. <see cref="HasValue"/> and the <c>TryGetValue</c> methods
/// let the compiler pattern-match it without boxing. <see cref="Value"/> boxes, so use it only for debugging. An outline
/// reads only the fields of its own case, so the fields of the cases that it does not hold may hold anything and never
/// matter. Every case keeps its center in the same place, and the four sphere cases keep their radius in the same place. It
/// takes 48 bytes. Equality needs the same case and the same fields, and counts a NaN as equal to a NaN, as
/// <see cref="Vector3.Equals(Vector3)"/> does.
/// </para>
/// </remarks>
[Union]
public readonly struct FrameOutline : IEquatable<FrameOutline>
{
    private const byte KindNone = 0, KindSemantic = 1, KindFalling = 2, KindResting = 3, KindNesting = 4, KindFollowing = 5;

    private readonly byte kind;
    /// <summary>The center of every case.</summary>
    private readonly Vector3 center;
    /// <summary>The rotation of <see cref="SemanticOutline"/>.</summary>
    private readonly Quaternion rotation;
    /// <summary>The radii of <see cref="SemanticOutline"/>.</summary>
    private readonly Vector3 radii;
    /// <summary>The radius of the four sphere cases.</summary>
    private readonly float radius;

    public FrameOutline(SemanticOutline outline)
    {
        kind = KindSemantic;
        center = outline.Center;
        rotation = outline.Rotation;
        radii = outline.Radii;
    }

    public FrameOutline(FallingOutline outline) : this(KindFalling, outline.Center, outline.Radius) { }

    public FrameOutline(RestingOutline outline) : this(KindResting, outline.Center, outline.Radius) { }

    public FrameOutline(NestingOutline outline) : this(KindNesting, outline.Center, outline.Radius) { }

    public FrameOutline(FollowingOutline outline) : this(KindFollowing, outline.Center, outline.Radius) { }

    /// <summary>A sphere case.</summary>
    private FrameOutline(byte kind, Vector3 center, float radius)
    {
        this.kind = kind;
        this.center = center;
        this.radius = radius;
    }

    public static implicit operator FrameOutline(SemanticOutline outline) => new(outline);
    public static implicit operator FrameOutline(FallingOutline outline) => new(outline);
    public static implicit operator FrameOutline(RestingOutline outline) => new(outline);
    public static implicit operator FrameOutline(NestingOutline outline) => new(outline);
    public static implicit operator FrameOutline(FollowingOutline outline) => new(outline);

    /// <summary>False for the default value, which holds no outline.</summary>
    public bool HasValue => kind != KindNone;

    public bool TryGetValue(out SemanticOutline value)
    {
        value = kind == KindSemantic ? new SemanticOutline(center, rotation, radii) : default;
        return kind == KindSemantic;
    }

    public bool TryGetValue(out FallingOutline value)
    {
        value = kind == KindFalling ? new FallingOutline(center, radius) : default;
        return kind == KindFalling;
    }

    public bool TryGetValue(out RestingOutline value)
    {
        value = kind == KindResting ? new RestingOutline(center, radius) : default;
        return kind == KindResting;
    }

    public bool TryGetValue(out NestingOutline value)
    {
        value = kind == KindNesting ? new NestingOutline(center, radius) : default;
        return kind == KindNesting;
    }

    public bool TryGetValue(out FollowingOutline value)
    {
        value = kind == KindFollowing ? new FollowingOutline(center, radius) : default;
        return kind == KindFollowing;
    }

    public object? Value => kind switch
    {
        KindSemantic => new SemanticOutline(center, rotation, radii),
        KindFalling => new FallingOutline(center, radius),
        KindResting => new RestingOutline(center, radius),
        KindNesting => new NestingOutline(center, radius),
        KindFollowing => new FollowingOutline(center, radius),
        _ => null,
    };

    // Equals on the fields, not the operator ==. The operator follows IEEE 754, so it calls a NaN unequal to itself, and an
    // outline that holds a NaN would not equal itself. Equals treats a NaN as equal to a NaN, which agrees with GetHashCode.
    // The held case alone decides equality: the fields of the other cases are not compared.
    public bool Equals(FrameOutline other) => kind == other.kind && kind switch
    {
        KindNone => true,
        KindSemantic => center.Equals(other.center) && rotation.Equals(other.rotation) && radii.Equals(other.radii),
        _ => center.Equals(other.center) && radius.Equals(other.radius),
    };

    public override bool Equals(object? obj) => obj is FrameOutline other && Equals(other);

    public override int GetHashCode() => kind switch
    {
        KindNone => 0,
        KindSemantic => HashCode.Combine(kind, center, rotation, radii),
        _ => HashCode.Combine(kind, center, radius),
    };

    public static bool operator ==(FrameOutline a, FrameOutline b) => a.Equals(b);
    public static bool operator !=(FrameOutline a, FrameOutline b) => !a.Equals(b);

    // A null arm and no discard arm: the compiler checks that every case has an arm, so a new case without one fails the build.
    public override string ToString() => this switch
    {
        SemanticOutline outline => $"FrameOutline({outline})",
        FallingOutline outline => $"FrameOutline({outline})",
        RestingOutline outline => $"FrameOutline({outline})",
        NestingOutline outline => $"FrameOutline({outline})",
        FollowingOutline outline => $"FrameOutline({outline})",
        null => "FrameOutline(none)",
    };
}
