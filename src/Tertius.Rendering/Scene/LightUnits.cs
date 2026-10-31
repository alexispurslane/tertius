using System.Numerics;

namespace Tertius.Rendering;

/// <summary>The light quantities that the renderer derives (docs/plans/local-light-shadows.md, "The cutoff and the reach").</summary>
internal static class LightUnits
{
    /// <summary>R_cut = 2^−13: a pre-exposed radiance that both tone curves show below half an 8-bit step.</summary>
    public const float CutoffRadiance = 1f / 8192;

    /// <summary>A light at least this far from the eye, in m, is far: 2 × 10^7 m.</summary>
    public const double FarDistance = 10_000.0 * ShadowCascades.Distance;

    /// <summary>The largest channel of color × intensity, in W/sr, that a light may have.</summary>
    public const double MaxIntensity = 1e34;

    /// <summary>The longest reach, in m, whose square is a finite single float.</summary>
    public const double MaxReach = 5e18;

    /// <summary>The smallest outer half-angle of a spot light's cone, in rad. A narrower cone counts as this one.</summary>
    public const float MinConeAngle = 1e-3f;

    /// <summary>The floor of the distance, in m, by which a light of radius 0 divides its irradiance.</summary>
    public const float NearestDistance = 0.01f;

    /// <summary>
    /// The length below which an interpolated axis counts as cancelled: unit axes carry float errors of about 10^−7, so the
    /// direction of a shorter sum is mostly error.
    /// </summary>
    private const float CancelledLength = 1e-4f;

    /// <summary>
    /// E_cut = π · R_cut / 2^<paramref name="highestStops"/>, in W/m²: below it a light alone changes no pixel of a frame
    /// whose exposure is at most <paramref name="highestStops"/>.
    /// </summary>
    public static double CutoffIrradiance(float highestStops) => Math.PI * CutoffRadiance / Math.Pow(2, highestStops);

    /// <summary>The intensity, in W/sr, of a point light of <paramref name="power"/> W: P / (4π).</summary>
    public static double PointIntensity(double power) => power / (4 * Math.PI);

    /// <summary>
    /// The intensity, in W/sr, of a spot light of <paramref name="power"/> W that sends it through its outer cone:
    /// P / (2π (1 − cos θ)), with θ clamped to [MinConeAngle, π].
    /// </summary>
    public static double SpotIntensity(double power, float outerConeAngle)
    {
        // 1 − cos θ = 2 sin²(θ / 2) keeps its precision for the narrowest cones.
        double half = Math.Clamp(outerConeAngle, MinConeAngle, Math.PI) / 2;
        double sine = Math.Sin(half);
        return power / (4 * Math.PI * sine * sine);
    }

    /// <summary>
    /// The radiant intensity per channel, in W/sr: <paramref name="color"/> × <paramref name="intensity"/>, multiplied in
    /// doubles, so that an intensity above the largest float still gives a finite channel within MaxIntensity.
    /// </summary>
    public static Vector3 RadiantIntensity(Vector3 color, double intensity) =>
        new(x: (float)(color.X * intensity), y: (float)(color.Y * intensity), z: (float)(color.Z * intensity));

    /// <summary>
    /// Where a light's brightest channel, of <paramref name="peakIntensity"/> W/sr, falls to
    /// <paramref name="cutoffIrradiance"/>: √(peak / cutoff), in m, at most MaxReach.
    /// </summary>
    public static double Reach(double peakIntensity, double cutoffIrradiance) =>
        Math.Min(Math.Sqrt(peakIntensity / cutoffIrradiance), MaxReach);

    /// <summary>True when a light at <paramref name="distance"/> m from the eye is far.</summary>
    public static bool IsFar(double distance) => distance >= FarDistance;

    /// <summary>The angle, in rad, of a sphere of <paramref name="radius"/> m whose center is <paramref name="distance"/> m away: a quarter turn from inside it.</summary>
    public static float AngularRadius(float radius, float distance) =>
        MathF.Asin(MathF.Min(radius / MathF.Max(distance, 1e-6f), 1));

    /// <summary>
    /// The irradiance by which every shadow budget ranks a light, in W/m²: the luminance of the radiant intensity over
    /// max(D², R², NearestDistance²), with D the light's <see cref="PreparedLight.RankDistance"/>, the eye's distance to its
    /// spot of light. For a point light it is the irradiance at the eye.
    /// </summary>
    public static float RankIrradiance(in PreparedLight light)
    {
        double radius = light.Radius;
        double distanceSquared = Math.Max(
            Math.Max(light.RankDistance * light.RankDistance, radius * radius),
            NearestDistance * NearestDistance
        );
        return (float)(ExposureMeter.Luminance(light.RadiantIntensity) / distanceSquared);
    }

    /// <summary>
    /// A spot light's axis at <paramref name="alpha"/> between its previous and current unit axes: the normalized linear
    /// interpolation, or the current axis when the two cancel.
    /// </summary>
    public static Vector3 InterpolateAxis(Vector3 previous, Vector3 current, float alpha)
    {
        var between = Vector3.Lerp(previous, current, alpha);
        float length = between.Length();
        return length > CancelledLength ? between / length : current;
    }
}
