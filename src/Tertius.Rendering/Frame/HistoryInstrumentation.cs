namespace Tertius.Rendering;

/// <summary>
/// The opt-in instrumentation of the slow specular history, which only the capture tool and the tests read. It renders
/// exactly as <see cref="None"/>. A precise specular history always keeps the light-change age (ticket tiunr-7haj,
/// <see cref="ScreenTraceImages.LightAge(int)"/>), so every value renders with it.
/// </summary>
internal enum HistoryInstrumentation
{
    None,

    /// <summary>
    /// The diagnostic of uncovered surfaces (<see cref="TemporalSignal.UncoveredDiagnostic(int)"/>), which the light-change
    /// age's records feed.
    /// </summary>
    LightAgeDiagnostic,

    /// <summary>
    /// The trace of the specular accumulation (ticket tiunr-4b2g, <see cref="TemporalSignal.AccumulationTrace(int)"/>).
    /// Its value keeps the name control-trace: the trace observes the renderer's own behavior, the control.
    /// </summary>
    ControlTrace,
}

/// <summary>The selection of <see cref="HistoryInstrumentation"/>, once per renderer.</summary>
internal static class HistoryInstrumentationSelection
{
    extension(HistoryInstrumentation)
    {
        /// <summary>
        /// The instrumentation that TERTIUS_REFLECTION_HISTORY_AGREEMENT selects: unset and empty select none,
        /// "light-age-diagnostic" the diagnostic of uncovered surfaces, and "control-trace" the accumulation trace. The
        /// removed values "off" and "light-age" fail, because the light-change age is always on.
        /// </summary>
        /// <exception cref="ArgumentException">The variable holds another value.</exception>
        public static HistoryInstrumentation Selected() =>
            Environment.GetEnvironmentVariable("TERTIUS_REFLECTION_HISTORY_AGREEMENT") switch
            {
                null or "" => HistoryInstrumentation.None,
                "light-age-diagnostic" => HistoryInstrumentation.LightAgeDiagnostic,
                "control-trace" => HistoryInstrumentation.ControlTrace,
                var value when value is "off" or "light-age" => throw new ArgumentException(
                    $"The TERTIUS_REFLECTION_HISTORY_AGREEMENT value '{value}' was removed: the light-change age is always on with a precise specular history. Leave the variable unset, or use light-age-diagnostic or control-trace."
                ),
                var value => throw new ArgumentException(
                    $"Unknown TERTIUS_REFLECTION_HISTORY_AGREEMENT value '{value}': leave it unset, or use light-age-diagnostic or control-trace."
                ),
            };
    }
}
