namespace Tertius.Rendering;

/// <summary>
/// The search of GTAO around one half-resolution texel: its view depth, its texel width, the fade below one texel and
/// the angles and the steps of the slices (docs/plans/ambient-occlusion.md, "The method"). gtao.glsl computes the same.
/// </summary>
internal static class HorizonSearch
{
    /// <summary>The factor that moves the center toward the eye, XeGTAO's value for 32-bit depths.</summary>
    public const double CenterBias = 0.99999;

    /// <summary>The nearest step from the center, in half-resolution texels (XeGTAO's pixelTooCloseThreshold).</summary>
    public const double NearestStepTexels = 1.3;

    /// <summary>The view depth of sky, in m: beyond every radius, and finite in three squared 32-bit coordinates.</summary>
    public const double SkyDepth = 1e18;

    // The golden ratio's inverse, the R1 sequence that spreads the steps of one texel (XeGTAO.hlsli line 420).
    private const double StepSequence = 0.6180339887498948482;

    /// <summary>
    /// The view depth z of the point <paramref name="distance"/> m from the eye along the view <paramref name="ray"/>,
    /// whose z is 1. A distance of 0 is sky and gives <see cref="SkyDepth"/>.
    /// </summary>
    public static double ViewDepth(double distance, Double3 ray) => distance > 0 ? distance / ray.Length : SkyDepth;

    /// <summary>p(z) = 4 t_x z / W: the width in m of one half-resolution texel, two pixels, at the view depth z.</summary>
    public static double TexelWidth(double viewDepth, double tanHalfX, int fullWidth) =>
        4 * tanHalfX * viewDepth / fullWidth;

    /// <summary>clamp(ρ − 1, 0, 1): how much of the estimate a radius of ρ texels keeps.</summary>
    public static double Fade(double radiusInTexels) => Math.Clamp(radiusInTexels - 1, 0, 1);

    /// <summary>1 − fade (1 − estimate): the estimate, faded toward full visibility.</summary>
    public static double Faded(double estimate, double fade) => 1 - fade * (1 - estimate);

    /// <summary>φ = (k + ξ_x) / S · π: the screen angle of slice k of S.</summary>
    public static double SliceAngle(int slice, int slices, float sliceNoise) => (slice + sliceNoise) / slices * Math.PI;

    /// <summary>
    /// The distance of a step from the center, as a share of the radius: an even place in its share of the radius,
    /// shifted by the R1 sequence, beyond the nearest step.
    /// </summary>
    public static double StepFraction(int step, int slice, int stepsPerSide, float stepNoise, double radiusInTexels)
    {
        double shift = stepNoise + (slice + step * stepsPerSide) * StepSequence;
        double withinShare = shift - Math.Floor(shift);
        return (step + withinShare) / stepsPerSide + NearestStepTexels / radiusInTexels;
    }
}
