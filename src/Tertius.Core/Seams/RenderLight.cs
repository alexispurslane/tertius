using System.Numerics;

namespace Tertius;

/// <summary>
/// A point light of the snapshot: a sphere that sends its power the same in every direction. Its places follow the
/// rules of the position of a <see cref="RenderInstance"/> (docs/plans/local-light-shadows.md, "Light data").
/// </summary>
/// <param name="EntityIndex">
/// The slot of the entity that carries the light. It names the light within one snapshot. A new entity can take the slot
/// of a destroyed one, so the renderer follows the light from frame to frame by <see cref="Entity"/>, the slot with
/// <paramref name="EntityGeneration"/>.
/// </param>
/// <param name="EntityGeneration">The generation of the entity in <paramref name="EntityIndex"/>. Live generations start at 1.</param>
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
)
{
    /// <summary>
    /// The entity that carries the light: <see cref="EntityIndex"/> with <see cref="EntityGeneration"/>. The renderer
    /// names the light by it, within one snapshot and from frame to frame.
    /// </summary>
    public Entity Entity => new(Index: EntityIndex, Generation: EntityGeneration);
}

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
)
{
    /// <summary>As for <see cref="PointRenderLight.Entity"/>.</summary>
    public Entity Entity => new(Index: EntityIndex, Generation: EntityGeneration);
}

/// <summary>
/// A tube light of the snapshot: a capsule around a segment through <see cref="Position"/> along <see cref="Axis"/>, whose
/// surface sends its power the same in every direction. Its places and axes follow the rules of the position of a
/// <see cref="RenderInstance"/> (ARCHITECTURE.md, "Tube lights").
/// </summary>
/// <param name="EntityIndex">As for <see cref="PointRenderLight"/>: the slot names the light within one snapshot.</param>
/// <param name="EntityGeneration">As for <see cref="PointRenderLight"/>.</param>
/// <param name="Position">The middle of the segment, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="PreviousPosition">The middle at the previous snapshot, relative to the same origin, as an instance's.</param>
/// <param name="Axis">
/// The direction of the segment, in the axes of the camera's frame. It need not be normalized. The renderer takes +Y
/// when it is zero.
/// </param>
/// <param name="PreviousAxis">The direction at the previous snapshot.</param>
/// <param name="Color">Linear RGB that scales the power per channel.</param>
/// <param name="Power">The radiant power that leaves the light, in W.</param>
/// <param name="Radius">The radius of the capsule, in m.</param>
/// <param name="Length">The length of the segment between the centers of the capsule's ends, in m.</param>
/// <param name="CastsShadow">Whether the light casts shadows from the segment's middle, within the renderer's budgets.</param>
/// <param name="ChainDepth">As for <see cref="PointRenderLight"/>.</param>
public readonly record struct TubeRenderLight(
    uint EntityIndex,
    uint EntityGeneration,
    Vector3 Position,
    Vector3 PreviousPosition,
    Vector3 Axis,
    Vector3 PreviousAxis,
    Vector3 Color,
    float Power,
    float Radius,
    float Length,
    bool CastsShadow,
    ushort ChainDepth
)
{
    /// <summary>As for <see cref="PointRenderLight.Entity"/>.</summary>
    public Entity Entity => new(Index: EntityIndex, Generation: EntityGeneration);
}

/// <summary>
/// One light of a <see cref="RenderSnapshot"/>: a <see cref="PointRenderLight"/>, a <see cref="SpotRenderLight"/> or a
/// <see cref="TubeRenderLight"/>.
/// The default value holds no light. The renderer skips it.
/// </summary>
/// <remarks>
/// The union generator writes its union members (docs/plans/union-tooling.md). A light reads only the values of its own
/// case. The cases share their slots by type, so the union takes the fields of one spot light and its kind byte, 88
/// bytes: a tube light's fields fit in a spot light's.
/// Equality needs the same case and the same values, and counts a NaN as equal to a NaN, as
/// <see cref="Vector3.Equals(Vector3)"/> does.
/// </remarks>
[GeneratedUnion(typeof(PointRenderLight), typeof(SpotRenderLight), typeof(TubeRenderLight))]
public readonly partial struct RenderLight;
