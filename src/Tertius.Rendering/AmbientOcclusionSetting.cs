namespace Tertius.Rendering;

/// <summary>No ambient occlusion: the ambient light reaches every surface in full, as before this setting.</summary>
public readonly record struct NoAmbientOcclusion;

/// <summary>
/// Ground-truth ambient occlusion, GTAO (docs/plans/ambient-occlusion.md): the share of the cosine-weighted hemisphere
/// that nearby geometry on the screen hides scales the ambient light. It runs at half resolution.
/// </summary>
/// <param name="RadiusMeters">Geometry farther than this from a surface point hides nothing from it, in m: above 0, at most 10⁶.</param>
/// <param name="Slices">How many slices each half-resolution texel integrates, 1 to 16.</param>
/// <param name="StepsPerSide">How many depth samples each slice takes on each side to find its horizon, 1 to 16.</param>
public readonly record struct GroundTruthOcclusion(float RadiusMeters, int Slices, int StepsPerSide)
{
    /// <summary>The renderer's default: a radius of 1 m, 3 slices and 3 steps on each side.</summary>
    public static GroundTruthOcclusion Default => new(RadiusMeters: 1, Slices: 3, StepsPerSide: 3);
}

/// <summary>
/// Whether the renderer darkens the ambient light by ambient occlusion: <see cref="NoAmbientOcclusion"/> or
/// <see cref="GroundTruthOcclusion"/>. The default value is NoAmbientOcclusion. It is a generated union (see
/// <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(NoAmbientOcclusion), typeof(GroundTruthOcclusion), ZeroCase = typeof(NoAmbientOcclusion))]
public readonly partial struct AmbientOcclusionSetting
{
    /// <summary>The largest radius, far below the depth at which the sky lies (docs/plans/ambient-occlusion.md).</summary>
    public const float MaxRadiusMeters = 1e6f;

    public const int MaxSlices = 16;
    public const int MaxStepsPerSide = 16;

    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            NoAmbientOcclusion => null,
            GroundTruthOcclusion occlusion => ProblemOf(occlusion),
        };

    private static string? ProblemOf(GroundTruthOcclusion occlusion)
    {
        // The negated comparison also refuses NaN, which compares false with every number.
        bool radiusInRange = occlusion.RadiusMeters is > 0 and <= MaxRadiusMeters;
        if (!radiusInRange)
            return $"GroundTruthOcclusion.RadiusMeters must be a finite number above 0 and at most {MaxRadiusMeters} m, not {occlusion.RadiusMeters}.";
        if (occlusion.Slices is < 1 or > MaxSlices)
            return $"GroundTruthOcclusion.Slices must be 1 to {MaxSlices}, not {occlusion.Slices}.";
        if (occlusion.StepsPerSide is < 1 or > MaxStepsPerSide)
            return $"GroundTruthOcclusion.StepsPerSide must be 1 to {MaxStepsPerSide}, not {occlusion.StepsPerSide}.";
        return null;
    }
}
