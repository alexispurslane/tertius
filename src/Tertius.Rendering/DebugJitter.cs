namespace Tertius.Rendering;

/// <summary>
/// Which draws jitter their projection: a debug switch of ticket tiunr-4b2g, which tells the jitter's effect on the
/// accumulator apart from the rest of the FSR path.
/// </summary>
internal enum DebugJitter
{
    /// <summary>The renderer's rule: a draw jitters exactly when FSR upscales it.</summary>
    Upscaler,

    /// <summary>No draw jitters, also under FSR, which then gets a zero jitter.</summary>
    None,

    /// <summary>Every draw jitters by FSR's sequence, also without an upscale.</summary>
    Always,

    /// <summary>
    /// The renderer's rule, but the reprojection takes this draw's jitter for the last draw's, so that a still camera
    /// reprojects each pixel onto its own texel. The surface test then rebuilds the last frame's points with this draw's
    /// jitter.
    /// </summary>
    Held,
}

/// <summary>The selection of <see cref="DebugJitter"/>, once per renderer.</summary>
internal static class DebugJitterSelection
{
    extension(DebugJitter)
    {
        /// <summary>
        /// The jitter rule that TERTIUS_DEBUG_JITTER selects: unset, empty and "upscaler" select the renderer's rule,
        /// "none" no jitter, "always" jitter in every draw and "held" the renderer's rule with
        /// a reprojection that takes this draw's jitter for the last draw's.
        /// </summary>
        /// <exception cref="ArgumentException">The variable holds another value.</exception>
        public static DebugJitter Selected() =>
            Environment.GetEnvironmentVariable("TERTIUS_DEBUG_JITTER") switch
            {
                null or "" or "upscaler" => DebugJitter.Upscaler,
                "none" => DebugJitter.None,
                "always" => DebugJitter.Always,
                "held" => DebugJitter.Held,
                var value => throw new ArgumentException(
                    $"Unknown TERTIUS_DEBUG_JITTER value '{value}': use upscaler, none, always or held."
                ),
            };
    }
}
