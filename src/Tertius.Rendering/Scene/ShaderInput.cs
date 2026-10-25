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
    /// The most that a pass writes into the HDR image, the emissive target or the impostor atlas, per channel: 64,512,
    /// `MaxRadiance` in Shaders/exposure.glsl. It is the largest finite half float, 65,504, less 1/64 of it, the room that
    /// the rounding of the glass blend and of the summed bloom levels needs below the half float's largest value.
    /// </summary>
    public const float MaxRadiance = 64512;

    /// <summary>
    /// The brightest radiance that the renderer reads from the CPU, 2^64, per channel. At the lowest exposure, −64 stops,
    /// it pre-exposes to 1. Below it, no product of a light and the BRDF, the distance attenuation and the exposure
    /// leaves the single float's range before the cap.
    /// </summary>
    public const float RadianceLimit = 18446744073709551616f;

    /// <summary>Whether each channel of <paramref name="value"/> is finite.</summary>
    public static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

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
