namespace Tertius.Rendering;

/// <summary>The slots of the far lights (docs/plans/local-light-shadows.md, "Shadows of each light").</summary>
internal static class FarShadows
{
    /// <summary>The least share of the brightest far light's irradiance at the eye for which a far light gets far shadows.</summary>
    public const float Share = 1f / 256;

    /// <summary>
    /// Writes each far light's slot with empty occluder ranges: CascadeSet 0 to cascadedLights − 1 for the far lights that
    /// cast shadows with the highest irradiance at the eye, ties in list order; FarShadowsOnly for each other one of at
    /// least Share of the brightest; none for the rest. Leaves near lights as they are. Allocates nothing.
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
            if (light.CastsShadow)
                brightest = MathF.Max(brightest, LightUnits.EyeIrradiance(light));
        }

        for (int set = 0; set < cascadedLights; set++)
        {
            if (BrightestWithoutSlot(lights) is not { } index)
                break;
            lights[index].Shadow = new(new CascadeSet(Set: set, Occluders: default, Mapped: 0));
        }

        for (int i = 0; i < lights.Length; i++)
        {
            ref PreparedLight light = ref lights[i];
            if (Waits(light) && LightUnits.EyeIrradiance(light) >= Share * brightest)
                light.Shadow = new(new FarShadowsOnly(default));
        }
    }

    /// <summary>True when a light is far, casts shadows and has no slot yet in this choice.</summary>
    private static bool Waits(in PreparedLight light) =>
        LightUnits.IsFar(light.Distance) && light.CastsShadow && !light.Shadow.HasValue;

    /// <summary>The index of the waiting light with the highest irradiance at the eye, the first of equal ones, or null.</summary>
    private static int? BrightestWithoutSlot(ReadOnlySpan<PreparedLight> lights)
    {
        int? brightest = null;
        float most = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            if (!Waits(lights[i]))
                continue;
            float irradiance = LightUnits.EyeIrradiance(lights[i]);
            // Strictly brighter only, so that of equal lights the first in the list wins.
            if (brightest is null || irradiance > most)
            {
                brightest = i;
                most = irradiance;
            }
        }

        return brightest;
    }
}
