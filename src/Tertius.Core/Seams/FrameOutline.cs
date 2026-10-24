using System.Numerics;

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
/// The union generator writes its union members (docs/plans/union-tooling.md), as it does for <see cref="RenderLight"/>.
/// An outline reads only the values of its own case. The cases share their slots by type, so every case keeps its center
/// in the same slot, and the four sphere cases keep their radius in the same slot. It takes 48 bytes. Equality needs the
/// same case and the same values, and counts a NaN as equal to a NaN, as <see cref="Vector3.Equals(Vector3)"/> does.
/// </para>
/// </remarks>
[GeneratedUnion(typeof(SemanticOutline), typeof(FallingOutline), typeof(RestingOutline), typeof(NestingOutline),
    typeof(FollowingOutline))]
public readonly partial struct FrameOutline;
