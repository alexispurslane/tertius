namespace Tertius.Rendering;

/// <summary>The slots of the far lights (docs/plans/local-light-shadows.md, "Shadows of each light").</summary>
internal static class FarShadows
{
    /// <summary>The least share of the brightest far light's rank irradiance for which a far light gets far shadows.</summary>
    public const float Share = 1f / 256;

    /// <summary>The count of <see cref="MainViewTier"/> values, 0 to InView.</summary>
    private const int TierCount = (int)MainViewTier.InView + 1;

    /// <summary>
    /// Writes each far light's slot with empty occluder ranges, in one choice per <see cref="MainViewTier"/>, the highest
    /// tier first, and each from the cascade sets that the tiers above leave. So the lights of a tier get the slots that
    /// they get without the lower tiers: a spot lamp whose axis misses the view cannot take a set from a light whose light
    /// falls in view, and a light that only a face keeps cannot take one from the main view's lights
    /// (docs/plans/environment-map.md, decision 20). In each choice the far lights of the tier that cast shadows and have
    /// at least Share of the brightest rank irradiance (<see cref="LightUnits.RankIrradiance"/>) of far lights of that tier
    /// and the tiers above, shadowed or not, get a slot: the next cascade set for the brightest, ties in list order, while
    /// sets are left, and FarShadowsOnly for the rest. The share counts from the tiers above too, because their lights
    /// light the same view, and beside a light 256 times brighter a lower tier's shadow cannot show. It counts from no
    /// lower tier, so that a lower tier cannot take a slot from a higher one. Leaves near lights as they are. Allocates
    /// nothing.
    /// </summary>
    public static void Choose(Span<PreparedLight> lights, int cascadedLights)
    {
        Span<float> brightest = stackalloc float[TierCount];
        for (int i = 0; i < lights.Length; i++)
        {
            ref PreparedLight light = ref lights[i];
            if (!LightUnits.IsFar(light.Distance))
                continue;
            light.Shadow = default;
            int tier = (int)light.Tier;
            brightest[tier] = MathF.Max(brightest[tier], LightUnits.RankIrradiance(light));
        }

        int set = 0;
        float brightestAbove = 0;
        for (int tier = TierCount - 1; tier >= 0; tier--)
        {
            brightestAbove = MathF.Max(brightestAbove, brightest[tier]);
            set = ChooseAmong(
                lights: lights,
                tier: (MainViewTier)tier,
                least: Share * brightestAbove,
                firstSet: set,
                cascadedLights: cascadedLights
            );
        }
    }

    /// <summary>
    /// Gives the cascade sets from <paramref name="firstSet"/> on to the brightest waiting lights of
    /// <paramref name="tier"/>, and FarShadowsOnly to the rest of them. Returns the first set left.
    /// </summary>
    private static int ChooseAmong(
        Span<PreparedLight> lights,
        MainViewTier tier,
        float least,
        int firstSet,
        int cascadedLights
    )
    {
        int set = firstSet;
        for (; set < cascadedLights; set++)
        {
            if (BrightestWaiting(lights: lights, tier: tier, least: least) is not { } index)
                break;
            ref PreparedLight chosen = ref lights[index];
            chosen.Shadow = new(new CascadeSet(Set: set, Occluders: default, Mapped: 0));
        }

        for (int i = 0; i < lights.Length; i++)
        {
            ref PreparedLight light = ref lights[i];
            if (Waits(light: light, tier: tier, least: least))
                light.Shadow = new(new FarShadowsOnly(default));
        }

        return set;
    }

    /// <summary>
    /// True when a light is far, is of <paramref name="tier"/>, casts shadows, has a rank irradiance of at least
    /// <paramref name="least"/> W/m², and has no slot yet in this choice.
    /// </summary>
    private static bool Waits(in PreparedLight light, MainViewTier tier, float least) =>
        LightUnits.IsFar(light.Distance)
        && light.Tier == tier
        && light.CastsShadow
        && !light.Shadow.HasValue
        && LightUnits.RankIrradiance(light) >= least;

    /// <summary>The index of the waiting light with the highest rank irradiance, the first of equal ones, or null.</summary>
    private static int? BrightestWaiting(ReadOnlySpan<PreparedLight> lights, MainViewTier tier, float least)
    {
        int? brightest = null;
        float most = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            if (!Waits(light: lights[i], tier: tier, least: least))
                continue;
            float irradiance = LightUnits.RankIrradiance(lights[i]);
            // Strictly brighter only, so that of equal lights the first in the list wins. The list is in the snapshot's
            // order, the slot order of the entity table, which every run shares, so the choice is deterministic
            // (docs/plans/local-light-shadows.md, decision 24).
            if (brightest is null || irradiance > most)
            {
                brightest = i;
                most = irradiance;
            }
        }

        return brightest;
    }
}
