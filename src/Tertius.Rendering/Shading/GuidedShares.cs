namespace Tertius.Rendering;

/// <summary>
/// The guided shares of the two ray kinds (ARCHITECTURE.md, "Guided rays"): the probability that a ray of a kind takes the
/// guided draw, which aims at a pixel of the light pyramid, in place of its lobe draw. Every ray of a guided kind counts
/// with the density of the one-sample mixture of the two draws (<see cref="MixtureDensity"/>), so its estimate stays
/// unbiased at any share that is known when the ray is weighed.
/// <para>
/// The diffuse kind's share is fixed. The reflection kind's share follows the draws: each tile of 8 × 8 blocks holds one
/// share, which each draw moves down the slope of the second moment of the tile's reflection samples, between 0 and
/// <see cref="ReflectionCeiling"/> (ticket ter-fe63). A tile whose guided draws bring little light, such as a floor whose
/// guide aims at its own plane, falls to 0; a tile whose lobe rarely finds a small bright sender rises to the ceiling. A
/// ramp over the roughness caps each pixel's share, so that a sharp lobe takes no guided draw.
/// </para>
/// The values are the one store of these constants, which reach the shaders through <see cref="ScreenTracePushConstants"/>.
/// The methods are the C# copies of mixtureDensity, guidedShareOf and shareEvidenceOf in screen_trace.glsl, and of
/// screen_trace_share.comp, for the tests.
/// </summary>
internal static class GuidedShares
{
    /// <summary>The share of the diffuse rays that take the guided draw.</summary>
    public const float Diffuse = 0.25f;

    /// <summary>
    /// The largest share of a tile's reflection rays, and each tile's share when the shares restart. Stage 1 of ter-fe63
    /// chose it over 0.5: it keeps 96 % of the sunlit nose's cut in specks, settles about twice as fast where the share
    /// falls to 0, and wanders less on a tile whose slope is flat.
    /// </summary>
    public const float ReflectionCeiling = 0.25f;

    /// <summary>
    /// The roughness, the square root of the floored alpha, up to which a pixel takes no guided reflection ray: the
    /// mirror plate's 0.05 stays below it, so its reflections are those of the lobe alone.
    /// </summary>
    public const float RampStart = 0.1f;

    /// <summary>
    /// The roughness from which a pixel's reflection share is its tile's share: the roughness of the bay's walls and of
    /// rect-lamps' glossy floor, on which stage 1 measured the mixture.
    /// </summary>
    public const float RampEnd = 0.38f;

    /// <summary>
    /// The step of a tile's share per draw at a ratio of 1 of its slope to its moment. 1/128 halved the wander of a
    /// settled tile on a flat slope against 1/64, and a share still crosses its whole range in 32 to 64 draws.
    /// </summary>
    public const float Step = 1f / 128;

    /// <summary>
    /// The least share at which a sample's evidence weighs it. At a share of 0 the slope needs the density of draws that no
    /// ray takes, so the evidence weighs by the mixture at this share: the secant of the second moment from the tile's
    /// share to the guard. Without it, a tile at 0 would never learn that a larger share helps.
    /// </summary>
    public const float Guard = 1f / 16;

    /// <summary>The largest ratio of a tile's summed slope to its summed moment that one draw acts on, either way.</summary>
    public const float MostRatio = 2;

    /// <summary>The largest weight of one reflection sample in its evidence: its square and a sum of 64 squares stay finite.</summary>
    public const float MostEvidenceWeight = 1e12f;

    /// <summary>The density of the one-sample mixture: the lobe draw's density and the guided draw's, by the guided share.</summary>
    public static float MixtureDensity(float share, float lobeDensity, float guidedDensity) =>
        (1 - share) * lobeDensity + share * guidedDensity;

    /// <summary>
    /// The share of the reflection rays from a pixel of <paramref name="roughness"/>, the square root of its floored alpha,
    /// in a tile whose share is <paramref name="tileShare"/>: the tile's share, capped by a ramp that is 0 up to
    /// <see cref="RampStart"/> and rises linearly to <see cref="ReflectionCeiling"/> at <see cref="RampEnd"/>.
    /// </summary>
    public static float ReflectionShareOf(float roughness, float tileShare) =>
        MathF.Min(ReflectionCeiling * Math.Clamp((roughness - RampStart) / (RampEnd - RampStart), 0, 1), tileShare);

    /// <summary>
    /// The evidence of one reflection sample for its tile's share: its squared weight, and the derivative of that squared
    /// weight's mean over the mixture's draws in the share, <c>w² (l − g) / q</c>.
    /// </summary>
    /// <param name="weight">The sample's luminance as the pixel's specular sample takes it, before the cap of a ray's weight.</param>
    /// <param name="share">The share at which the ray was drawn. Below <see cref="Guard"/>, the guard's mixture weighs it.</param>
    /// <param name="lobeDensity">The lobe draw's density along the ray, l.</param>
    /// <param name="guidedDensity">The guided draw's density along the ray, g: 0 on a miss.</param>
    public static (float Slope, float Moment) EvidenceOf(
        float weight,
        float share,
        float lobeDensity,
        float guidedDensity
    )
    {
        float capped = MathF.Min(weight, MostEvidenceWeight);
        float moment = capped * capped;
        float mixture = MixtureDensity(MathF.Max(share, Guard), lobeDensity, guidedDensity);
        return (moment * (lobeDensity - guidedDensity) / mixture, moment);
    }

    /// <summary>
    /// A tile's share after a draw whose samples summed to <paramref name="slopeSum"/> and <paramref name="momentSum"/>:
    /// one step down the slope, by the ratio of the two sums capped at <see cref="MostRatio"/>, kept between 0 and the
    /// ceiling. A tile without moment keeps its share.
    /// </summary>
    public static float NextShare(float share, float slopeSum, float momentSum)
    {
        if (momentSum <= 0)
            return share;
        float ratio = Math.Clamp(slopeSum / momentSum, -MostRatio, MostRatio);
        return Math.Clamp(share - Step * ratio, 0, ReflectionCeiling);
    }
}
