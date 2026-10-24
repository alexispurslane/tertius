using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// A point light that an entity carries: it shines the same in every direction. It sits at the entity's pose composed with
/// <see cref="Offset"/>.
/// </summary>
/// <param name="Offset">The light's pose relative to the entity. A zeroed rotation, as in a defaulted pose, counts as the identity.</param>
/// <param name="Color">Linear color.</param>
/// <param name="Intensity">Brightness multiplier of <paramref name="Color"/>.</param>
/// <param name="Range">The distance at which the light falls to zero, in meters.</param>
public readonly record struct PointLightData(Pose Offset, Vector3 Color, float Intensity, float Range);

/// <summary>
/// A spot light that an entity carries: it shines in a cone along the local -Z axis of its pose, which is the entity's pose
/// composed with <see cref="Offset"/>.
/// </summary>
/// <param name="Offset">The light's pose relative to the entity. A zeroed rotation, as in a defaulted pose, counts as the identity.</param>
/// <param name="Color">Linear color.</param>
/// <param name="Intensity">Brightness multiplier of <paramref name="Color"/>.</param>
/// <param name="Range">The distance at which the light falls to zero, in meters.</param>
/// <param name="InnerConeAngle">Half-angle of the full-intensity cone, in radians.</param>
/// <param name="OuterConeAngle">Half-angle of the outer cone, in radians. The light fades to zero between the two angles.</param>
public readonly record struct SpotLightData(
    Pose Offset,
    Vector3 Color,
    float Intensity,
    float Range,
    float InnerConeAngle,
    float OuterConeAngle
);

/// <summary>
/// The dynamic light that an entity carries: a <see cref="PointLightData"/> or a <see cref="SpotLightData"/>. The default
/// value holds no light, so an entity whose <see cref="RenderData.Light"/> is the default carries none and puts nothing in
/// the render snapshot. To switch a light off and on again, keep the light value and write the default over it.
/// </summary>
/// <remarks>
/// This is a generated union (docs/plans/union-tooling.md) of <see cref="PointLightData"/> and
/// <see cref="SpotLightData"/>. Equality needs the same case and the same fields, and counts a NaN as equal to a NaN, as
/// <see cref="Vector3.Equals(Vector3)"/> does.
/// </remarks>
[GeneratedUnion(typeof(PointLightData), typeof(SpotLightData))]
public readonly partial struct LightData;
