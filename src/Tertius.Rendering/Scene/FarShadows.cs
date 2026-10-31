namespace Tertius.Rendering;

/// <summary>The slots of the far lights (docs/plans/local-light-shadows.md, "Shadows of each light").</summary>
internal static class FarShadows
{
    /// <summary>The least share of the brightest far light's rank irradiance for which a far light gets far shadows.</summary>
    public const float Share = 1f / 256;

    /// <summary>
    /// Writes each far light's slot with empty occluder ranges, in two choices. First the far lights that the main view
    /// keeps choose, as if no other light were in the list, then the far lights that only a face keeps choose from the
    /// cascade sets left, so the main view's lights get the slots that they get without the others
    /// (docs/plans/environment-map.md, decision 20). In each choice the far lights that cast shadows and have at least
    /// Share of the brightest far light's rank irradiance (<see cref="LightUnits.RankIrradiance"/>), shadowed or not, get
    /// a slot: the next cascade set for the brightest, ties in list order, while sets are left, and FarShadowsOnly for the
    /// rest. The brightest far light is the main view's brightest in the first choice and the brightest of all in the
    /// second. Leaves near lights as they are. Allocates nothing.
    /// </summary>
    public static void Choose(Span<PreparedLight> lights, int cascadedLights)
    {
        float brightestInMainView = 0;
        float brightest = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            ref PreparedLight light = ref lights[i];
            if (!LightUnits.IsFar(light.Distance))
                continue;
            light.Shadow = default;
            float irradiance = LightUnits.RankIrradiance(light);
            brightest = MathF.Max(brightest, irradiance);
            if (light.InMainView)
                brightestInMainView = MathF.Max(brightestInMainView, irradiance);
        }

        int sets = ChooseAmong(
            lights: lights,
            inMainView: true,
            least: Share * brightestInMainView,
            firstSet: 0,
            cascadedLights: cascadedLights
        );
        ChooseAmong(
            lights: lights,
            inMainView: false,
            least: Share * brightest,
            firstSet: sets,
            cascadedLights: cascadedLights
        );
    }

    /// <summary>
    /// Gives the cascade sets from <paramref name="firstSet"/> on to the brightest waiting lights whose InMainView is
    /// <paramref name="inMainView"/>, and FarShadowsOnly to the rest of them. Returns the first set left.
    /// </summary>
    private static int ChooseAmong(
        Span<PreparedLight> lights,
        bool inMainView,
        float least,
        int firstSet,
        int cascadedLights
    )
    {
        int set = firstSet;
        for (; set < cascadedLights; set++)
        {
            if (BrightestWaiting(lights: lights, inMainView: inMainView, least: least) is not { } index)
                break;
            ref PreparedLight chosen = ref lights[index];
            chosen.Shadow = new(new CascadeSet(Set: set, Occluders: default, Mapped: 0));
        }

        for (int i = 0; i < lights.Length; i++)
        {
            ref PreparedLight light = ref lights[i];
            if (Waits(light: light, inMainView: inMainView, least: least))
                light.Shadow = new(new FarShadowsOnly(default));
        }

        return set;
    }

    /// <summary>
    /// True when a light is far, is kept by the main view exactly when <paramref name="inMainView"/>, casts shadows, has
    /// a rank irradiance of at least <paramref name="least"/> W/m², and has no slot yet in this choice.
    /// </summary>
    private static bool Waits(in PreparedLight light, bool inMainView, float least) =>
        LightUnits.IsFar(light.Distance)
        && light.InMainView == inMainView
        && light.CastsShadow
        && !light.Shadow.HasValue
        && LightUnits.RankIrradiance(light) >= least;

    /// <summary>The index of the waiting light with the highest rank irradiance, the first of equal ones, or null.</summary>
    private static int? BrightestWaiting(ReadOnlySpan<PreparedLight> lights, bool inMainView, float least)
    {
        int? brightest = null;
        float most = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            if (!Waits(light: lights[i], inMainView: inMainView, least: least))
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
