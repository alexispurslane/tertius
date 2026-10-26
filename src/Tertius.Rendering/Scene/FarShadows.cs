namespace Tertius.Rendering;

/// <summary>The slots of the far lights (docs/plans/local-light-shadows.md, "Shadows of each light").</summary>
internal static class FarShadows
{
    /// <summary>The least share of the brightest far light's irradiance at the eye for which a far light gets far shadows.</summary>
    public const float Share = 1f / 256;

    /// <summary>
    /// Writes each far light's slot with empty occluder ranges. The far lights that cast shadows and have at least Share
    /// of the brightest far light's irradiance at the eye, shadowed or not, get a slot: CascadeSet 0 to cascadedLights − 1
    /// for the brightest, ties in list order, and FarShadowsOnly for the rest. Leaves near lights as they are. Allocates nothing.
    /// </summary>
    public static void Choose(Span<PreparedLight> lights, int cascadedLights)
    {
        float brightest = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            ref PreparedLight light = ref lights[i];
            if (!LightUnits.IsFar(light.Distance))
                continue;
            light.Shadow = default;
            brightest = MathF.Max(brightest, LightUnits.EyeIrradiance(light));
        }

        float least = Share * brightest;
        for (int set = 0; set < cascadedLights; set++)
        {
            if (BrightestWaiting(lights, least) is not { } index)
                break;
            lights[index].Shadow = new(new CascadeSet(Set: set, Occluders: default, Mapped: 0));
        }

        for (int i = 0; i < lights.Length; i++)
            if (Waits(lights[i], least))
                lights[i].Shadow = new(new FarShadowsOnly(default));
    }

    /// <summary>
    /// True when a light is far, casts shadows, has at least <paramref name="least"/> W/m² at the eye, and has no slot
    /// yet in this choice.
    /// </summary>
    private static bool Waits(in PreparedLight light, float least) =>
        LightUnits.IsFar(light.Distance)
        && light.CastsShadow
        && !light.Shadow.HasValue
        && LightUnits.EyeIrradiance(light) >= least;

    /// <summary>The index of the waiting light with the highest irradiance at the eye, the first of equal ones, or null.</summary>
    private static int? BrightestWaiting(ReadOnlySpan<PreparedLight> lights, float least)
    {
        int? brightest = null;
        float most = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            if (!Waits(lights[i], least))
                continue;
            float irradiance = LightUnits.EyeIrradiance(lights[i]);
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
