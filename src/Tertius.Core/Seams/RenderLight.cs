using System.Numerics;

namespace Tertius;

/// <summary>
/// A point light of the snapshot: a sphere that sends its power the same in every direction. Its places follow the
/// rules of the position of a <see cref="RenderInstance"/> (docs/plans/local-light-shadows.md, "Light data").
/// </summary>
/// <param name="EntityIndex">
/// The slot of the entity that carries the light. It names the light within one snapshot. With
/// <paramref name="EntityGeneration"/>, it follows the light from frame to frame, because a new entity can take the slot
/// of a destroyed one.
/// </param>
/// <param name="EntityGeneration">
/// The generation of the entity that carries the light. With <paramref name="EntityIndex"/>, it identifies the light from
/// tick to tick, so a lamp that reuses a destroyed lamp's slot gets a new key (docs/plans/temporal-renderer.md, "The key of
/// an object").
/// </param>
/// <param name="Position">The place of the light, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="PreviousPosition">The place at the previous snapshot, relative to the same origin, as an instance's.</param>
/// <param name="Color">Linear RGB that scales the power per channel.</param>
/// <param name="Power">The radiant power that leaves the light, in W.</param>
/// <param name="Radius">The radius of the emitting sphere, in m.</param>
/// <param name="CastsShadow">Whether the light casts shadows, within the renderer's budgets.</param>
/// <param name="ChainDepth">The chain depth of the entity that carries the light, as <see cref="RenderInstance.ChainDepth"/>.</param>
public readonly record struct PointRenderLight(
    uint EntityIndex,
    uint EntityGeneration,
    Vector3 Position,
    Vector3 PreviousPosition,
    Vector3 Color,
    float Power,
    float Radius,
    bool CastsShadow,
    ushort ChainDepth
);

/// <summary>
/// A spot light of the snapshot: a sphere that sends its power through a cone around one axis. Its places and axes follow
/// the rules of the position of a <see cref="RenderInstance"/> (docs/plans/local-light-shadows.md, "Light data").
/// </summary>
/// <param name="EntityIndex">As for <see cref="PointRenderLight"/>: the slot names the light within one snapshot.</param>
/// <param name="EntityGeneration">As for <see cref="PointRenderLight"/>.</param>
/// <param name="Position">The place of the light, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="PreviousPosition">The place at the previous snapshot, relative to the same origin, as an instance's.</param>
/// <param name="Direction">
/// The axis of the cone, in the axes of the camera's frame. It need not be normalized. The renderer shines straight down
/// (-Y) when it is zero.
/// </param>
/// <param name="PreviousDirection">The axis at the previous snapshot.</param>
/// <param name="Color">Linear RGB that scales the power per channel.</param>
/// <param name="Power">The radiant power that leaves the light through its outer cone, in W.</param>
/// <param name="Radius">The radius of the emitting sphere, in m.</param>
/// <param name="CastsShadow">Whether the light casts shadows, within the renderer's budgets.</param>
/// <param name="InnerConeAngle">Half-angle of the full-intensity cone, in radians.</param>
/// <param name="OuterConeAngle">Half-angle of the outer edge of the cone, in radians. The light fades to zero between the two angles.</param>
/// <param name="ChainDepth">As for <see cref="PointRenderLight"/>.</param>
public readonly record struct SpotRenderLight(
    uint EntityIndex,
    uint EntityGeneration,
    Vector3 Position,
    Vector3 PreviousPosition,
    Vector3 Direction,
    Vector3 PreviousDirection,
    Vector3 Color,
    float Power,
    float Radius,
    bool CastsShadow,
    float InnerConeAngle,
    float OuterConeAngle,
    ushort ChainDepth
);

/// <summary>
/// One light of a <see cref="RenderSnapshot"/>: a <see cref="PointRenderLight"/> or a <see cref="SpotRenderLight"/>.
/// The default value holds no light. The renderer skips it.
/// </summary>
/// <remarks>
/// The union generator writes its union members (docs/plans/union-tooling.md). A light reads only the values of its own
/// case. The cases share their slots by type, so the union takes the fields of one spot light and its kind byte, 88
/// bytes.
/// Equality needs the same case and the same values, and counts a NaN as equal to a NaN, as
/// <see cref="Vector3.Equals(Vector3)"/> does.
/// </remarks>
[GeneratedUnion(typeof(PointRenderLight), typeof(SpotRenderLight))]
public readonly partial struct RenderLight;
