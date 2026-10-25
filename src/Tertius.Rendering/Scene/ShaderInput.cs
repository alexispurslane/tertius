using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The ranges of the values that the shaders read and write, and the one place where the renderer reads a value from the
/// CPU into its range (ARCHITECTURE.md, "Portable Vulkan"). The Vulkan spec lets a driver assume that no shader operand
/// is a NaN or an infinity, so the renderer keeps both out at their sources instead of testing for them in a shader.
/// </summary>
internal static class ShaderInput
{
    /// <summary>
    /// The most light per channel that the surfaces write into one pixel of the HDR image, the emissive target or the
    /// impostor atlas: 32,256, `MaxRadiance` in Shaders/exposure.glsl. With twice <see cref="PuffBudget"/> it keeps each
    /// pixel at or below 64,512, below the largest half float (ARCHITECTURE.md, "Portable Vulkan").
    /// </summary>
    public const float MaxRadiance = 32256;

    /// <summary>
    /// The most pre-exposed light per channel that all the puffs over a pixel add, in exact sums: half of
    /// <see cref="MaxRadiance"/>, 16,128. A blend that rounds to nearest or toward zero at most doubles it, which keeps a
    /// pixel at or below 64,512 (ARCHITECTURE.md, "Portable Vulkan").
    /// </summary>
    public const float PuffBudget = MaxRadiance / 2;

    /// <summary>
    /// The brightest radiance that the renderer reads from the CPU, 2^64, per channel. At the lowest exposure, −64 stops,
    /// it pre-exposes to 1. Below it, no product of a light and the BRDF, the distance attenuation and the exposure
    /// leaves the single float's range before the cap.
    /// </summary>
    public const float RadianceLimit = 18446744073709551616f;

    /// <summary>The smallest normal single float, `SmallestNormal` in Shaders/vectors.glsl. A GPU may flush a smaller value to 0.</summary>
    public const float SmallestNormal = 1.17549435e-38f;

    /// <summary>
    /// The farthest from the eye, 2^62 m, that a point of a surface, a puff, a light, an outline or a far shadow may lie.
    /// The shaders square the distance between two such points, at most 2^63, which stays inside the single float's range.
    /// </summary>
    public const float PositionLimit = 4611686018427387904f;

    /// <summary>
    /// The largest range of a point or spot light that the shaders read, 2^63 m, which they square. No distance between
    /// two points within <see cref="PositionLimit"/> of the eye is longer.
    /// </summary>
    public const float RangeLimit = 9223372036854775808f;

    /// <summary>
    /// The most pre-exposed light per channel that one puff adds to a pixel, when the frame draws
    /// <paramref name="puffCount"/> puffs: an equal share of <see cref="PuffBudget"/>.
    /// </summary>
    public static float PuffRadianceCap(int puffCount) => PuffBudget / Math.Max(puffCount, 1);

    /// <summary>
    /// The C# copy of `exposed` in Shaders/exposure.glsl: <paramref name="radiance"/> times <paramref name="exposure"/>,
    /// at most <see cref="MaxRadiance"/> per channel. The cap applies before the product, which stays in the float range.
    /// </summary>
    public static Vector3 Exposed(Vector3 radiance, float exposure) =>
        Vector3.Min(radiance, new(MaxRadiance / exposure)) * exposure;

    /// <summary>
    /// The light that a surface pass writes: <paramref name="radiance"/> exposed, plus <paramref name="preExposedLight"/>,
    /// at most <see cref="MaxRadiance"/> per channel. The C# copy of the cap of `lobesRadiance` in Shaders/lighting.glsl,
    /// and of the direct and the ambient passes (docs/plans/screen-space-illumination.md, "The cap at MaxRadiance").
    /// </summary>
    /// <param name="preExposedLight">Finite light that the caller has already exposed and capped: emission, or the direct image.</param>
    public static Vector3 SurfaceLight(Vector3 radiance, Vector3 preExposedLight, float exposure) =>
        Vector3.Min(Exposed(radiance, exposure) + preExposedLight, new(MaxRadiance));

    /// <summary>Whether each channel of <paramref name="value"/> is finite.</summary>
    public static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    /// <summary>
    /// Whether every point within <paramref name="extent"/> of the camera-relative <paramref name="position"/> lies within
    /// <see cref="PositionLimit"/> of the eye. A number that is not finite fails too.
    /// </summary>
    public static bool InPositionRange(Vector3 position, float extent) =>
        // A NaN fails the comparison, and the length of a position with a huge part is infinite.
        position.Length() + extent
        <= PositionLimit;

    /// <summary>A finite light range above 0 as the shaders read it: at most <see cref="RangeLimit"/>.</summary>
    public static float Range(float range) => MathF.Min(range, RangeLimit);

    /// <summary>
    /// A radiance from the CPU as the shaders read it, per channel: its nearest value from 0 to
    /// <see cref="RadianceLimit"/>, and 0 for a NaN, which sends no light.
    /// </summary>
    public static Vector3 Radiance(Vector3 radiance) =>
        new(Radiance(radiance.X), Radiance(radiance.Y), Radiance(radiance.Z));

    /// <summary>A fraction from the CPU, such as an albedo or a metallic factor: its nearest value from 0 to 1, and 0 for a NaN.</summary>
    public static float Fraction(float value) => value >= 0 ? MathF.Min(value, 1) : 0;

    /// <summary><see cref="Fraction(float)"/> of each channel.</summary>
    public static Vector3 Fraction(Vector3 value) => new(Fraction(value.X), Fraction(value.Y), Fraction(value.Z));

    /// <summary><see cref="Fraction(float)"/> of each channel.</summary>
    public static Vector4 Fraction(Vector4 value) =>
        new(x: Fraction(value.X), y: Fraction(value.Y), z: Fraction(value.Z), w: Fraction(value.W));

    // A NaN fails the comparison, and MathF.Min takes +∞ to the limit.
    private static float Radiance(float value) => value >= 0 ? MathF.Min(value, RadianceLimit) : 0;
}
