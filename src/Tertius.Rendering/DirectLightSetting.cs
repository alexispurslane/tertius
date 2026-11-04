namespace Tertius.Rendering;

/// <summary>
/// The exact loop: each pixel takes the light of every light of the main view, as the forward passes do. Its sample has no
/// noise, so the direct signal keeps one frame (docs/plans/temporal-renderer.md, decision 29).
/// </summary>
public readonly record struct EveryLight;

/// <summary>
/// ReSTIR DI (docs/plans/temporal-renderer.md, section 7): each pixel shades one light that it chooses from candidates and
/// from the choices of the last frame and of its neighbors, and the direct signal's history averages the noise. The lights
/// with a cascade set still take the exact loop (decision 28).
/// </summary>
/// <param name="Candidates">The candidates that each pixel draws from the light table in each frame, 1 to 32.</param>
/// <param name="SpatialNeighbors">The neighbors whose choices each pixel reuses in each frame, 0 to 8.</param>
public readonly record struct ResampledLights(int Candidates, int SpatialNeighbors)
{
    /// <summary>The renderer's default: 4 candidates and 2 neighbors (docs/plans/temporal-renderer.md, "The settings").</summary>
    public static ResampledLights Default => new(Candidates: 4, SpatialNeighbors: 2);
}

/// <summary>
/// How the main view estimates its direct light: <see cref="EveryLight"/> or <see cref="ResampledLights"/>. The default
/// value is EveryLight. It is a generated union (see <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(EveryLight), typeof(ResampledLights), ZeroCase = typeof(EveryLight))]
public readonly partial struct DirectLightSetting
{
    /// <summary>The most candidates: 20 times this, the cap of a reservoir's count, fits its 16 bits.</summary>
    public const int MaxCandidates = 32;

    public const int MaxSpatialNeighbors = 8;

    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            EveryLight => null,
            ResampledLights lights => ProblemOf(lights),
        };

    private static string? ProblemOf(ResampledLights lights)
    {
        if (lights.Candidates is < 1 or > MaxCandidates)
            return $"ResampledLights.Candidates must be 1 to {MaxCandidates}, not {lights.Candidates}.";
        if (lights.SpatialNeighbors is < 0 or > MaxSpatialNeighbors)
            return $"ResampledLights.SpatialNeighbors must be 0 to {MaxSpatialNeighbors}, not {lights.SpatialNeighbors}.";
        return null;
    }
}
