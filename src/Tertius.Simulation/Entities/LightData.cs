using System.Numerics;

namespace Tertius.Simulation;

/// <summary>
/// A point light that an entity carries: a sphere that sends its power the same in every direction, in SI units
/// (docs/plans/local-light-shadows.md, "Light data"). It sits at the entity's pose composed with <see cref="Offset"/>.
/// </summary>
/// <param name="Offset">The light's pose relative to the entity. A zeroed rotation, as in a defaulted pose, counts as the identity.</param>
/// <param name="Color">Linear RGB that scales the power per channel.</param>
/// <param name="Power">The radiant power that leaves the light, in W. A light of 0 or less adds no light.</param>
/// <param name="Radius">The radius of the emitting sphere, in m, 0 or more.</param>
/// <param name="CastsShadow">Whether the light casts shadows, within the renderer's budgets.</param>
public readonly record struct PointLightData(Pose Offset, Vector3 Color, float Power, float Radius, bool CastsShadow)
{
    /// <summary>
    /// The radiance that a sphere of <see cref="Radius"/> sends from its surface when it emits <see cref="Power"/> evenly:
    /// Color · Power / (4π² Radius²), in W/(m²·sr). A scene gives a star's sphere this emission.
    /// </summary>
    public Vector3 SurfaceRadiance => Color * (float)(Power / (4 * Math.PI * Math.PI * (double)Radius * Radius));
}

/// <summary>
/// A spot light that an entity carries: a sphere that sends its power through a cone along the local -Z axis of its pose,
/// which is the entity's pose composed with <see cref="Offset"/>, in SI units (docs/plans/local-light-shadows.md, "Light
/// data").
/// </summary>
/// <param name="Offset">The light's pose relative to the entity. A zeroed rotation, as in a defaulted pose, counts as the identity.</param>
/// <param name="Color">Linear RGB that scales the power per channel.</param>
/// <param name="Power">The radiant power that leaves the light through its outer cone, in W. A light of 0 or less adds no light.</param>
/// <param name="Radius">The radius of the emitting sphere, in m, 0 or more.</param>
/// <param name="CastsShadow">Whether the light casts shadows, within the renderer's budgets.</param>
/// <param name="InnerConeAngle">Half-angle of the full-intensity cone, in radians.</param>
/// <param name="OuterConeAngle">Half-angle of the outer cone, in radians. The light fades to zero between the two angles.</param>
public readonly record struct SpotLightData(
    Pose Offset,
    Vector3 Color,
    float Power,
    float Radius,
    bool CastsShadow,
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
