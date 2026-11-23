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

/// <summary>
/// A shader variant that Tertius.Rendering.csproj compiles: the suffix of its file names, and the shaders that it compiles
/// in it. <c>ShaderVariantTests</c> checks each variant against the shaders that the build embeds.
/// </summary>
internal readonly record struct ShaderVariant(string Suffix, string[] Shaders)
{
    /// <summary>The precise variant's suffix, which every shader of a precise history takes without an instrumented one.</summary>
    public const string PreciseSuffix = "_precise";

    /// <summary>
    /// The LightAgeShader items of Tertius.Rendering.csproj (ticket ter-wkvw): the spatial dispatch of a half-precision
    /// history whose samples have a light age, the diffuse signal's beside a precise specular history. It acts on its
    /// block's evidence, as the precise variants do (docs/plans/bounce-light-confidence.md, section 8.4). The temporal
    /// dispatch reads no light age: no cut runs before the blend (section 4).
    /// </summary>
    public static readonly ShaderVariant LightAge = new(Suffix: "_lightage", Shaders: ["accumulate_spatial.comp"]);

    /// <summary>The file name of <paramref name="shader"/>'s variant, such as composite_precise.frag.</summary>
    public static string Of(string shader, string suffix) =>
        $"{Path.GetFileNameWithoutExtension(shader)}{suffix}{Path.GetExtension(shader)}";
}

/// <summary>
/// The selection of <see cref="HistoryInstrumentation"/>, once per renderer, and the shader variant of each.
/// </summary>
internal static class HistoryInstrumentationSelection
{
    /// <summary>The LightAgeDiagnosticShader items of Tertius.Rendering.csproj.</summary>
    private static readonly ShaderVariant LightAgeDiagnosticVariant = new(
        Suffix: "_lightagediag_precise",
        Shaders:
        [
            "accumulate_temporal.comp",
            "screen_trace_block_rays.comp",
            "screen_trace_own_rays.comp",
            "screen_trace_plan.comp",
            "screen_trace_resolve.comp",
        ]
    );

    /// <summary>The AccumulationTraceShader items of Tertius.Rendering.csproj.</summary>
    private static readonly ShaderVariant AccumulationTraceVariant = new(
        Suffix: "_trace_precise",
        Shaders: ["accumulate_temporal.comp", "accumulate_spatial.comp"]
    );

    extension(HistoryInstrumentation instrumentation)
    {
        /// <summary>
        /// The variant of the shaders that write this instrumentation's image, or null for none. A shader that the
        /// variant does not hold takes its precise variant under the instrumentation too.
        /// </summary>
        public ShaderVariant? Variant =>
            instrumentation switch
            {
                HistoryInstrumentation.None => null,
                HistoryInstrumentation.LightAgeDiagnostic => LightAgeDiagnosticVariant,
                HistoryInstrumentation.ControlTrace => AccumulationTraceVariant,
            };
    }

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
