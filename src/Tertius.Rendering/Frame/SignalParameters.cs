namespace Tertius.Rendering;

/// <summary>
/// The parameter set of one signal's accumulator (docs/plans/temporal-renderer.md, section 8, "The parameter sets"). The
/// cap is the one store of how long a signal's history can grow, which the still rule also reads (decision 38).
/// </summary>
/// <param name="Cap">The largest history length, in frames: the slow history's weight never falls below 1 / Cap.</param>
/// <param name="FastCap">The fast history's largest length, in frames.</param>
/// <param name="HistoryFixFrames">Below this length, the fix replaces the slow history with its neighbors' mean.</param>
/// <param name="ClampSigmas">The half width of the clamp's box, in standard deviations of the fast history.</param>
/// <param name="AntiLagShare">The clamp's move, as a share of the mean, at which the length falls to the fast cap.</param>
internal readonly record struct SignalParameters(
    int Cap,
    int FastCap,
    int HistoryFixFrames,
    float ClampSigmas,
    float AntiLagShare
)
{
    /// <summary>
    /// The direct signal's set under <paramref name="options"/>: a cap of 16 frames, or of 1 under
    /// <see cref="EveryLight"/>, whose sample has no noise without upscaling, so that its history is its exact sample.
    /// </summary>
    public static SignalParameters Direct(RendererOptions options) =>
        new(
            Cap: options.DirectLight is EveryLight ? 1 : 16,
            FastCap: 4,
            HistoryFixFrames: 4,
            ClampSigmas: 2,
            AntiLagShare: 0.5f
        );
}
