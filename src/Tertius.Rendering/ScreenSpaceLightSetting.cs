namespace Tertius.Rendering;

/// <summary>No screen-space light: the ambient light reaches every surface in full, with no crease shadowing and no bounce.</summary>
public readonly record struct NoScreenSpaceLight;

/// <summary>
/// One estimate of the light around each surface point, from what the screen shows
/// (docs/plans/screen-space-illumination.md): masks of sectors in GTAO's slices within the radius. A blocked direction
/// brings the direct light of its blocker, an open one the ambient light. It runs at half resolution.
/// </summary>
/// <param name="RadiusMeters">Where the near range ends, in m: above 0, at most 10⁶.</param>
/// <param name="Slices">How many slices each half-resolution texel takes, 1 to 16.</param>
/// <param name="StepsPerSide">How many depth samples each slice takes on each side within the radius, 1 to 16.</param>
public readonly record struct VisibilityBitmaskLight(float RadiusMeters, int Slices, int StepsPerSide)
{
    /// <summary>
    /// The renderer's default: 1 m, 3 slices and 3 steps on each side. With the denoise's 6 blurs, 3 slices leave noise
    /// within 10 % of a 16-slice reference, and no scallops on the cabin's ceiling panel, for less than 4 slices with 4
    /// blurs cost (docs/plans/screen-space-illumination.md, decision 43).
    /// </summary>
    public static VisibilityBitmaskLight Default => new(RadiusMeters: 1, Slices: 3, StepsPerSide: 3);
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

    public const int MaxSlices = 16;

    public const int MaxStepsPerSide = 16;

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
        return null;
    }
}
