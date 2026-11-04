namespace Tertius.Rendering;

/// <summary>
/// The parameter set of one signal's accumulator (docs/plans/temporal-renderer.md, section 8, "The parameter sets"). The
/// cap is the one store of how long a signal's history can grow (decision 38). The renderer makes no signal until the
/// screen tracer's (ter-gayt), whose signals will also give the still rule its warm-up draws.
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
);
