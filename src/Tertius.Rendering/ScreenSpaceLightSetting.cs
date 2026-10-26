namespace Tertius.Rendering;

/// <summary>No screen-space light: the ambient light reaches every surface in full, with no crease shadowing and no bounce.</summary>
public readonly record struct NoScreenSpaceLight;

/// <summary>
/// One estimate of the light around each surface point, from what the screen shows
/// (docs/plans/screen-space-illumination.md): masks of sectors in GTAO's slices within the radius, and one ray per slice
/// beyond it through the depth pyramid. A blocked direction brings the direct light of its blocker, an open one the
/// ambient light. It runs at half resolution.
/// </summary>
/// <param name="RadiusMeters">Where the near range ends and the far rays start, in m: above 0, at most 10⁶.</param>
/// <param name="Slices">How many slices each half-resolution texel takes, 1 to 16.</param>
/// <param name="StepsPerSide">How many depth samples each slice takes on each side within the radius, 1 to 16.</param>
/// <param name="FarSteps">The most cells of the depth pyramid that one far ray examines, 1 to 1,024.</param>
/// <param name="FarRays">How many far rays each texel traces: one per slice, or one for the texel.</param>
public readonly record struct VisibilityBitmaskLight(
    float RadiusMeters,
    int Slices,
    int StepsPerSide,
    int FarSteps,
    FarRays FarRays
)
{
    /// <summary>
    /// The renderer's default: 1 m, 3 slices, 3 steps on each side, 64 far steps and one far ray per slice, until
    /// captures pick them.
    /// </summary>
    public static VisibilityBitmaskLight Default =>
        new(RadiusMeters: 1, Slices: 3, StepsPerSide: 3, FarSteps: 64, FarRays: FarRays.OnePerSlice);
}

/// <summary>
/// How many far rays a half-resolution texel traces beyond the radius (docs/plans/screen-space-illumination.md, fork 2).
/// </summary>
public enum FarRays
{
    /// <summary>Option A: one ray per slice, through an open sector of that slice, which decides the slice's open share.</summary>
    OnePerSlice,

    /// <summary>
    /// Option B: one ray per texel, through an open sector of any slice, picked by its slice's weight, which decides the
    /// texel's whole open share: a third of the rays at 3 slices, and more noise.
    /// </summary>
    OnePerTexel,
}

/// <summary>
/// Whether the renderer estimates the light around each point from the screen: <see cref="NoScreenSpaceLight"/> or
/// <see cref="VisibilityBitmaskLight"/>. The default value is NoScreenSpaceLight. It is a generated union (see
/// <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(NoScreenSpaceLight), typeof(VisibilityBitmaskLight), ZeroCase = typeof(NoScreenSpaceLight))]
public readonly partial struct ScreenSpaceLightSetting
{
    /// <summary>The largest radius, far below the depth at which the sky lies (docs/plans/ambient-occlusion.md).</summary>
    public const float MaxRadiusMeters = 1e6f;

    /// <summary>The most slices: the sector masks buffer takes 4 B per slice and texel, 33 MB at 16 slices at 1080p.</summary>
    public const int MaxSlices = 16;

    public const int MaxStepsPerSide = 16;

    /// <summary>The most far steps, as SSR's most steps (ConeTracedReflections.MaxStepsLimit).</summary>
    public const int MaxFarSteps = 1024;

    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            NoScreenSpaceLight => null,
            VisibilityBitmaskLight light => ProblemOf(light),
        };

    private static string? ProblemOf(VisibilityBitmaskLight light)
    {
        // The negated comparison also refuses NaN, which compares false with every number.
        bool radiusInRange = light.RadiusMeters is > 0 and <= MaxRadiusMeters;
        if (!radiusInRange)
            return $"VisibilityBitmaskLight.RadiusMeters must be a finite number above 0 and at most {MaxRadiusMeters} m, not {light.RadiusMeters}.";
        if (light.Slices is < 1 or > MaxSlices)
            return $"VisibilityBitmaskLight.Slices must be 1 to {MaxSlices}, not {light.Slices}.";
        if (light.StepsPerSide is < 1 or > MaxStepsPerSide)
            return $"VisibilityBitmaskLight.StepsPerSide must be 1 to {MaxStepsPerSide}, not {light.StepsPerSide}.";
        // No far step would leave the far range out, against the one estimate in two ranges (decision 36).
        if (light.FarSteps is < 1 or > MaxFarSteps)
            return $"VisibilityBitmaskLight.FarSteps must be 1 to {MaxFarSteps}, not {light.FarSteps}.";
        if (light.FarRays is not (FarRays.OnePerSlice or FarRays.OnePerTexel))
            return $"VisibilityBitmaskLight.FarRays must be OnePerSlice or OnePerTexel, not {(int)light.FarRays}.";
        return null;
    }
}
