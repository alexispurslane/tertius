using System.Numerics;

namespace Tertius;

/// <summary>
/// A gas of one density, everywhere in the frame or only inside a box, such as the air of a cabin in a ship's frame. It
/// matches <see cref="UniformGravity"/>, with the same optional box.
/// </summary>
/// <param name="Density">The medium's density, in kg/m³, 0 or more.</param>
/// <param name="Drag">The medium's drag, in 1/s, 0 or more: how fast it brings a puff at its temperature to rest in the frame.</param>
/// <param name="Region">
/// The box in frame coordinates that the medium fills, or null when it fills the whole frame. Outside the box is vacuum.
/// </param>
public readonly record struct UniformMedium(float Density, float Drag, BoundingBox? Region = null);

/// <summary>
/// The air of a planet at the frame's origin, at one temperature, so its density falls by the factor e per scale height
/// above the planet's radius. Below the radius the density stays at the surface density. It matches
/// <see cref="PointGravity"/>, centred on the frame's origin. It also holds the air's look, which the renderer paints for a
/// camera whose chain holds the frame, or for a camera whose chain holds no planet medium when this air is the densest at
/// its eye (docs/plans/atmosphere.md): the sky, the haze and the sun's tint. Each part of the
/// look defaults to none.
/// </summary>
/// <param name="Radius">The radius of the planet, in m, 0 or more.</param>
/// <param name="SurfaceDensity">The medium's density at the planet's surface and below it, in kg/m³, 0 or more.</param>
/// <param name="ScaleHeight">The height over which the density falls by the factor e, in m, above 0.</param>
/// <param name="Drag">The medium's drag, in 1/s, 0 or more.</param>
/// <param name="SkyRadiance">
/// The sky's radiance in full day at the surface, linear RGB in W/(m²·sr), each channel 0 or more. The renderer scales it by
/// the time of day and by the air's density at the eye over the surface density. Zero is a black sky.
/// </param>
/// <param name="Haze">The haze over what the eye sees through the air, or null for none.</param>
/// <param name="SunTint">
/// What the air lets through of its sun's light at the surface, a fraction from 0 to 1 per channel, or null for no tint.
/// Above the surface the tint fades with the density.
/// </param>
public readonly record struct PlanetMedium(
    double Radius,
    float SurfaceDensity,
    float ScaleHeight,
    float Drag,
    Vector3 SkyRadiance = default,
    PlanetHaze? Haze = null,
    Vector3? SunTint = null
)
{
    /// <summary>
    /// The height in m above the planet's surface of a place <paramref name="distance"/> m from its center, 0 at and below
    /// the radius, where the density stays at the surface density. The density there is the surface density times
    /// e^(−height / <see cref="ScaleHeight"/>). IMPORTANT: airHeight in atmosphere.glsl is the shaders' copy of this rule.
    /// </summary>
    public double HeightAt(double distance) => Math.Max(0, distance - Radius);
}

/// <summary>
/// The haze of a planet's air (docs/plans/atmosphere.md, "The haze"): a surface seen through the air fades toward
/// <paramref name="Radiance"/> with the air that lies between it and the eye.
/// </summary>
/// <param name="Radiance">
/// The haze's own light in full day, linear RGB in W/(m²·sr), each channel 0 or more: the color that a surface far away fades
/// to. The renderer scales it by the time of day.
/// </param>
/// <param name="Distance">
/// The distance, in m, above 0, over which the haze at the surface density lets through 1/e of the light behind it.
/// </param>
public readonly record struct PlanetHaze(Vector3 Radiance, float Distance);

/// <summary>
/// The medium of one frame: the gas that fills it, at rest in the frame's axes (docs/plans/particles.md, "The frame's
/// medium"). It is <see cref="UniformMedium"/>, <see cref="PlanetMedium"/> or neither, which is no medium: vacuum. The
/// frame tree holds one per frame beside its gravity field, and it matches the gravity field in who sets it, when, which
/// step it describes and where it is stored. Only puffs read a medium: physics reads none, and each body keeps its own
/// damping.
/// The union generator writes its union members (docs/plans/union-tooling.md), as it does for
/// <see cref="GravityField"/>. The empty union is the all-zero default, <see cref="None"/>, and its <c>HasValue</c> is false.
/// </summary>
[GeneratedUnion(typeof(UniformMedium), typeof(PlanetMedium))]
public readonly partial struct MediumField
{
    /// <summary>No medium: vacuum.</summary>
    public static MediumField None => default;

    /// <summary>
    /// True for a medium that the World accepts: every number is finite, the densities, the drags and the radius are 0 or
    /// more, the scale height is above 0, and a region has finite corners. A planet's look is valid too: each channel of
    /// its sky and of its haze's light is 0 or more, its haze distance is above 0, and each channel of its sun's tint lies
    /// from 0 to 1. No medium is valid.
    /// </summary>
    public bool IsValid =>
        this switch
        {
            UniformMedium { Density: var density, Drag: var drag, Region: var region } => NotNegative(density)
                && NotNegative(drag)
                && (region is not { } box || (IsFinite(box.Min) && IsFinite(box.Max))),
            PlanetMedium planet => IsValidPlanet(planet),
            null => true,
        };

    private static bool IsValidPlanet(in PlanetMedium planet) => IsValidAir(planet) && IsValidLook(planet);

    /// <summary>The air's shape: a finite radius of 0 or more, a surface density and a drag of 0 or more, and a scale height above 0.</summary>
    private static bool IsValidAir(in PlanetMedium planet) =>
        double.IsFinite(planet.Radius)
        && planet.Radius >= 0
        && NotNegative(planet.SurfaceDensity)
        && float.IsFinite(planet.ScaleHeight)
        && planet.ScaleHeight > 0
        && NotNegative(planet.Drag);

    /// <summary>The air's look: a sky of 0 or more, a haze of light of 0 or more and a distance above 0, and a tint from 0 to 1.</summary>
    private static bool IsValidLook(in PlanetMedium planet) =>
        NotNegative(planet.SkyRadiance)
        && (
            planet.Haze is not { } haze
            || (NotNegative(haze.Radiance) && haze.Distance is > 0 and < float.PositiveInfinity)
        )
        && (planet.SunTint is not { } tint || (NotNegative(tint) && Vector3.Max(tint, Vector3.One) == Vector3.One));

    /// <summary>The medium's drag, in 1/s, and 0 in vacuum.</summary>
    public float Drag =>
        this switch
        {
            UniformMedium { Drag: var drag } => drag,
            PlanetMedium { Drag: var drag } => drag,
            null => 0,
        };

    /// <summary>A uniform medium. Without a <paramref name="region"/>, it fills the whole frame.</summary>
    /// <param name="density">The density, in kg/m³.</param>
    /// <param name="drag">The drag, in 1/s.</param>
    /// <param name="region">The box in frame coordinates that the medium fills, or null for the whole frame.</param>
    public static MediumField Uniform(float density, float drag, BoundingBox? region = null) =>
        new(new UniformMedium(density, drag, region));

    /// <summary>The air of a planet at the frame's origin.</summary>
    /// <param name="radius">The planet's radius, in m.</param>
    /// <param name="surfaceDensity">The density at the surface and below it, in kg/m³.</param>
    /// <param name="scaleHeight">The height over which the density falls by the factor e, in m.</param>
    /// <param name="drag">The drag, in 1/s.</param>
    /// <param name="skyRadiance">The sky in full day at the surface, in W/(m²·sr), or zero for a black sky.</param>
    /// <param name="haze">The haze, or null for none.</param>
    /// <param name="sunTint">The tint of the sun's light at the surface, or null for none.</param>
    public static MediumField Planet(
        double radius,
        float surfaceDensity,
        float scaleHeight,
        float drag,
        Vector3 skyRadiance = default,
        PlanetHaze? haze = null,
        Vector3? sunTint = null
    ) =>
        new(
            new PlanetMedium(
                Radius: radius,
                SurfaceDensity: surfaceDensity,
                ScaleHeight: scaleHeight,
                Drag: drag,
                SkyRadiance: skyRadiance,
                Haze: haze,
                SunTint: sunTint
            )
        );

    private static bool NotNegative(float value) => float.IsFinite(value) && value >= 0;

    private static bool NotNegative(Vector3 v) => NotNegative(v.X) && NotNegative(v.Y) && NotNegative(v.Z);

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// Whether the medium has gas at <paramref name="place"/>, a place in the frame, in m, in the frame's axes. A uniform
    /// medium fills its region, or the whole frame when it has none, and a planet medium fills the whole frame. Each does
    /// so only when its density, or its surface density, is above 0. It never calls <see cref="Math.Exp"/>, so the step of a
    /// puff reads it at no cost beyond a box test.
    /// </summary>
    public bool Fills(Double3 place) =>
        this switch
        {
            UniformMedium { Density: var density, Region: var region } => density > 0
                && (region is not { } box || box.Contains(place.ToVector3())),
            PlanetMedium { SurfaceDensity: var surface } => surface > 0,
            null => false,
        };

    /// <summary>
    /// The medium's density at <paramref name="place"/>, a place in the frame, in m, in the frame's axes, in kg/m³, and 0 in
    /// vacuum. A planet medium has its surface density at and below its radius, and above it the surface density times
    /// e^(−height / scale height). It serves game code now, and bodies when a later ticket lets them read the medium. Puffs
    /// read only <see cref="Fills"/> and <see cref="Drag"/>.
    /// </summary>
    public double DensityAt(Double3 place) =>
        this switch
        {
            UniformMedium { Density: var density, Region: var region } => region is { } box
            && !box.Contains(place.ToVector3())
                ? 0
                : density,
            PlanetMedium planet => planet.SurfaceDensity * Math.Exp(-planet.HeightAt(place.Length) / planet.ScaleHeight),
            null => 0,
        };
}
