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
/// A tube light that an entity carries: a capsule, a segment of <see cref="Length"/> along the local Y axis of its pose
/// with a sphere of <see cref="Radius"/> around every point, whose surface sends its power the same in every direction,
/// in SI units (ARCHITECTURE.md, "Tube lights"). Its pose is the entity's pose composed with <see cref="Offset"/>, and
/// the segment's middle stands at it.
/// </summary>
/// <param name="Offset">The light's pose relative to the entity. A zeroed rotation, as in a defaulted pose, counts as the identity.</param>
/// <param name="Color">Linear RGB that scales the power per channel.</param>
/// <param name="Power">The radiant power that leaves the light, in W. A light of 0 or less adds no light.</param>
/// <param name="Radius">The radius of the capsule, in m, 0 or more.</param>
/// <param name="Length">The length of the segment between the centers of the capsule's two ends, in m, 0 or more.</param>
/// <param name="CastsShadow">Whether the light casts shadows from the segment's middle, within the renderer's budgets.</param>
public readonly record struct TubeLightData(
    Pose Offset,
    Vector3 Color,
    float Power,
    float Radius,
    float Length,
    bool CastsShadow
)
{
    /// <summary>
    /// The radiance that the capsule sends from its surface of area 2π Radius (Length + 2 Radius) when it emits
    /// <see cref="Power"/> evenly: Color · Power / (π · area), in W/(m²·sr). A tube of length 0 is the sphere of
    /// <see cref="PointLightData.SurfaceRadiance"/>. A scene gives the glowing body that holds the light this emission.
    /// </summary>
    public Vector3 SurfaceRadiance =>
        Color * (float)(Power / (2 * Math.PI * Math.PI * (double)Radius * ((double)Length + 2.0 * Radius)));
}

/// <summary>
/// A rectangle light that an entity carries: a one-sided rectangle of <see cref="Width"/> along the local X axis of its
/// pose and <see cref="Height"/> along its local Y axis, centered on the pose, whose front face sends its power toward
/// local +Z with the same radiance in every direction, in SI units (ARCHITECTURE.md, "Rectangle lights"). Its pose is the
/// entity's pose composed with <see cref="Offset"/>.
/// </summary>
/// <param name="Offset">The light's pose relative to the entity. A zeroed rotation, as in a defaulted pose, counts as the identity.</param>
/// <param name="Color">Linear RGB that scales the power per channel.</param>
/// <param name="Power">The radiant power that leaves the front face, in W. A light of 0 or less adds no light.</param>
/// <param name="Width">The rectangle's side along local X, in m, above 0.</param>
/// <param name="Height">The rectangle's side along local Y, in m, above 0.</param>
/// <param name="CastsShadow">Whether the light casts shadows from the rectangle's center, within the renderer's budgets.</param>
public readonly record struct RectLightData(
    Pose Offset,
    Vector3 Color,
    float Power,
    float Width,
    float Height,
    bool CastsShadow
)
{
    /// <summary>
    /// The radiance that the front face of area Width · Height sends when it emits <see cref="Power"/> evenly:
    /// Color · Power / (π · Width · Height), in W/(m²·sr). A scene gives the glowing panel that holds the light this
    /// emission.
    /// </summary>
    public Vector3 SurfaceRadiance => Color * (float)(Power / (Math.PI * (double)Width * Height));
}

/// <summary>
/// The dynamic light that an entity carries: a <see cref="PointLightData"/>, a <see cref="SpotLightData"/>, a
/// <see cref="TubeLightData"/> or a <see cref="RectLightData"/>. The default
/// value holds no light, so an entity whose <see cref="RenderData.Light"/> is the default carries none and puts nothing in
/// the render snapshot. To switch a light off and on again, keep the light value and write the default over it.
/// </summary>
/// <remarks>
/// This is a generated union (docs/plans/union-tooling.md) of <see cref="PointLightData"/>,
/// <see cref="SpotLightData"/>, <see cref="TubeLightData"/> and <see cref="RectLightData"/>. Equality needs the same case and the same fields, and counts a NaN as equal to a NaN, as
/// <see cref="Vector3.Equals(Vector3)"/> does.
/// </remarks>
[GeneratedUnion(typeof(PointLightData), typeof(SpotLightData), typeof(TubeLightData), typeof(RectLightData))]
public readonly partial struct LightData;
