namespace Tertius.Rendering;

/// <summary>
/// The search of GTAO around one half-resolution texel: its view depth, its texel width, the empty near range below the
/// nearest step and the angles and the steps of the slices (docs/plans/ambient-occlusion.md, "The method"). gtao.glsl computes the same.
/// </summary>
internal static class HorizonSearch
{
    /// <summary>The factor that moves the center toward the eye, XeGTAO's value for 32-bit depths.</summary>
    public const double CenterBias = 0.99999;

    /// <summary>The nearest step from the center, in half-resolution texels (XeGTAO's pixelTooCloseThreshold).</summary>
    public const double NearestStepTexels = 1.3;

    /// <summary>The view depth of sky, in m: beyond every radius. No pass squares a sky texel's view position.</summary>
    public const double SkyDepth = 1e18;

    // The golden ratio's inverse, the R1 sequence that spreads the steps of one texel (XeGTAO.hlsli line 420).
    private const double StepSequence = 0.6180339887498948482;

    /// <summary>
    /// The view depth z of the point <paramref name="distance"/> m from the eye along the view <paramref name="ray"/>,
    /// whose z is 1. A distance of 0 is sky and gives <see cref="SkyDepth"/>.
    /// </summary>
    public static double ViewDepth(double distance, Double3 ray) => distance > 0 ? distance / ray.Length : SkyDepth;

    /// <summary>
    /// The view position of a sample at <paramref name="viewDepth"/> along its view <paramref name="ray"/>, or null for
    /// sky, which hides nothing. The depth decides first, because a sky position squares past the single float range at a
    /// wide field of view (ARCHITECTURE.md, "Portable Vulkan").
    /// </summary>
    public static Double3? SamplePosition(Double3 ray, double viewDepth) =>
        viewDepth >= SkyDepth ? null : ray * viewDepth;

    /// <summary>p(z) = 4 t_x z / W: the width in m of one half-resolution texel, two pixels, at the view depth z.</summary>
    public static double TexelWidth(double viewDepth, double tanHalfX, int fullWidth) =>
        4 * tanHalfX * viewDepth / fullWidth;

    /// <summary>
    /// Whether a radius of <paramref name="radiusInTexels"/> texels holds any near step: only beyond the nearest step.
    /// Below it the far rays give the whole estimate (docs/plans/screen-space-illumination.md, "Where the near range is
    /// empty").
    /// </summary>
    public static bool NearRangeHoldsSteps(double radiusInTexels) => radiusInTexels > NearestStepTexels;

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

    /// <summary>
    /// The offset in half-resolution texels of the step at <paramref name="stepFraction"/> of the radius along the slice
    /// at <paramref name="sliceAngle"/>, snapped to a texel. Screen rows run down, so y is −sin φ. A step beyond the
    /// image stays one texel past its longer side, where it still hides nothing, so that it fits an int.
    /// </summary>
    public static (int X, int Y) StepOffset(
        double stepFraction,
        double sliceAngle,
        double radiusInTexels,
        (int Width, int Height) halfSize
    )
    {
        double bound = Math.Max(halfSize.Width, halfSize.Height) + 1;
        double x = Math.Clamp(stepFraction * radiusInTexels * Math.Cos(sliceAngle), -bound, bound);
        double y = Math.Clamp(-stepFraction * radiusInTexels * Math.Sin(sliceAngle), -bound, bound);
        return (X: (int)Math.Round(x), Y: (int)Math.Round(y));
    }
}
