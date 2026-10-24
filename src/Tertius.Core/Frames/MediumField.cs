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
/// <see cref="PointGravity"/>, centred on the frame's origin.
/// </summary>
/// <param name="Radius">The radius of the planet, in m, 0 or more.</param>
/// <param name="SurfaceDensity">The medium's density at the planet's surface and below it, in kg/m³, 0 or more.</param>
/// <param name="ScaleHeight">The height over which the density falls by the factor e, in m, above 0.</param>
/// <param name="Drag">The medium's drag, in 1/s, 0 or more.</param>
public readonly record struct PlanetMedium(double Radius, float SurfaceDensity, float ScaleHeight, float Drag);

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
    /// more, the scale height is above 0, and a region has finite corners. No medium is valid.
    /// </summary>
    public bool IsValid => this switch
    {
        UniformMedium { Density: var density, Drag: var drag, Region: var region } =>
            NotNegative(density) && NotNegative(drag) &&
            (region is not { } box || (IsFinite(box.Min) && IsFinite(box.Max))),
        PlanetMedium { Radius: var radius, SurfaceDensity: var surface, ScaleHeight: var scale, Drag: var drag } =>
            double.IsFinite(radius) && radius >= 0 && NotNegative(surface) && float.IsFinite(scale) && scale > 0 &&
            NotNegative(drag),
        null => true
    };

    /// <summary>The medium's drag, in 1/s, and 0 in vacuum.</summary>
    public float Drag => this switch
    {
        UniformMedium { Drag: var drag } => drag,
        PlanetMedium { Drag: var drag } => drag,
        null => 0
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
    public static MediumField Planet(double radius, float surfaceDensity, float scaleHeight, float drag) =>
        new(new PlanetMedium(radius, surfaceDensity, scaleHeight, drag));

    private static bool NotNegative(float value) => float.IsFinite(value) && value >= 0;

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>
    /// Whether the medium has gas at <paramref name="place"/>, a place in the frame, in m, in the frame's axes. A uniform
    /// medium fills its region, or the whole frame when it has none, and a planet medium fills the whole frame. Each does
    /// so only when its density, or its surface density, is above 0. It never calls <see cref="Math.Exp"/>, so the step of a
    /// puff reads it at no cost beyond a box test.
    /// </summary>
    public bool Fills(Double3 place) => this switch
    {
        UniformMedium { Density: var density, Region: var region } =>
            density > 0 && (region is not { } box || box.Contains(place.ToVector3())),
        PlanetMedium { SurfaceDensity: var surface } => surface > 0,
        null => false
    };

    /// <summary>
    /// The medium's density at <paramref name="place"/>, a place in the frame, in m, in the frame's axes, in kg/m³, and 0 in
    /// vacuum. A planet medium has its surface density at and below its radius, and above it the surface density times
    /// e^(−height / scale height). It serves game code now, and bodies when a later ticket lets them read the medium. Puffs
    /// read only <see cref="Fills"/> and <see cref="Drag"/>.
    /// </summary>
    public double DensityAt(Double3 place) => this switch
    {
        UniformMedium { Density: var density, Region: var region } =>
            region is { } box && !box.Contains(place.ToVector3()) ? 0 : density,
        PlanetMedium { Radius: var radius, SurfaceDensity: var surface, ScaleHeight: var scale } =>
            surface * Math.Exp(-Math.Max(0, place.Length - radius) / scale),
        null => 0
    };
}
