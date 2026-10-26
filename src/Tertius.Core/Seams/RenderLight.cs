using System.Numerics;

namespace Tertius;

/// <summary>
/// A point light of the snapshot: it shines the same in every direction. Its position follows the same rules as the
/// position of a <see cref="RenderInstance"/>.
/// </summary>
/// <param name="Position">The place of the light, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="Color">Linear RGB.</param>
/// <param name="Intensity">
/// Luminous intensity: irradiance at distance d is <paramref name="Color"/> · <paramref name="Intensity"/> / d², with a
/// smooth window to zero at <paramref name="Range"/>. The renderer skips a light of zero or less.
/// </param>
/// <param name="Range">The distance at which the light falls to zero, in meters. The renderer skips a light of zero or less.</param>
/// <param name="ChainDepth">The chain depth of the entity that carries the light, as <see cref="RenderInstance.ChainDepth"/>.</param>
public readonly record struct PointRenderLight(
    Vector3 Position,
    Vector3 Color,
    float Intensity,
    float Range,
    ushort ChainDepth
);

/// <summary>
/// A spot light of the snapshot: it shines in a cone around one direction. Its position and direction follow the same
/// rules as the position of a <see cref="RenderInstance"/>.
/// </summary>
/// <param name="Position">The place of the light, relative to the render origin, in the axes of the camera's frame, in meters.</param>
/// <param name="Direction">
/// The axis of the cone, in the axes of the camera's frame. It need not be normalized. The renderer shines straight down
/// (-Y) when it is zero.
/// </param>
/// <param name="Color">Linear RGB.</param>
/// <param name="Intensity">As for <see cref="PointRenderLight"/>.</param>
/// <param name="Range">As for <see cref="PointRenderLight"/>.</param>
/// <param name="InnerConeAngle">Half-angle of the full-intensity cone, in radians.</param>
/// <param name="OuterConeAngle">Half-angle of the outer edge of the cone, in radians. The light fades to zero between the two angles.</param>
/// <param name="ChainDepth">As for <see cref="PointRenderLight"/>.</param>
public readonly record struct SpotRenderLight(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Intensity,
    float Range,
    float InnerConeAngle,
    float OuterConeAngle,
    ushort ChainDepth
);

/// <summary>
/// One dynamic light of a <see cref="RenderSnapshot"/>: a <see cref="PointRenderLight"/> or a
/// <see cref="SpotRenderLight"/>.
/// The default value holds no light. The renderer skips it.
/// </summary>
/// <remarks>
/// The union generator writes its union members (docs/plans/union-tooling.md). A light reads only the values of its own
/// case. The cases share their slots by type: the two positions, intensities, ranges and chain depths share slots, and
/// the point light's color shares a slot with the spot light's direction. It takes 56 bytes, one spot light and its kind
/// byte.
/// Equality needs the same case and the same values, and counts a NaN as equal to a NaN, as
/// <see cref="Vector3.Equals(Vector3)"/> does.
/// </remarks>
[GeneratedUnion(typeof(PointRenderLight), typeof(SpotRenderLight))]
public readonly partial struct RenderLight;
