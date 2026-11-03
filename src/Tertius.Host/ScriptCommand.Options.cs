using Tertius.Host.Scripting;
using Tertius.Rendering;

namespace Tertius.Host;

public static partial class ScriptCommand
{
    /// <summary>The options of a command line: a <see cref="FreePlayRun"/> or a <see cref="ScriptRun"/>.</summary>
    [GeneratedUnion(typeof(FreePlayRun), typeof(ScriptRun))]
    internal readonly partial struct RunOptions;

    /// <summary>A scene that runs live in a window until the user closes it.</summary>
    /// <param name="Scene">The name of the scene.</param>
    /// <param name="Speed">The wall-clock speed of the window, 1 for real time.</param>
    /// <param name="Record">The file that the run writes its session to as a script, or null to record nothing.</param>
    internal readonly record struct FreePlayRun(string Scene, double Speed, string? Record);

    /// <summary>A script that runs in a window or headless.</summary>
    /// <param name="Script">The script file.</param>
    /// <param name="Out">The output folder of the dumps, and of the PNG files of the captures.</param>
    /// <param name="Size">The size that replaces the size of the script's window, or null to keep it.</param>
    /// <param name="Display">Where the run shows its frames: in a window or headless.</param>
    internal readonly record struct ScriptRun(string Script, string Out, ScriptWindow? Size, ScriptDisplay Display);

    /// <summary>Where a script run shows its frames: <see cref="InWindow"/> or <see cref="Headless"/>.</summary>
    [GeneratedUnion(typeof(InWindow), typeof(Headless))]
    internal readonly partial struct ScriptDisplay;

    /// <summary>A script run in a window, which paces its ticks by the wall clock.</summary>
    /// <param name="Speed">The wall-clock speed of the window, 1 for real time.</param>
    internal readonly record struct InWindow(double Speed);

    /// <summary>A script run with no window, which runs its ticks back to back.</summary>
    /// <param name="Video">The choice of <c>--video</c>, or null without the option.</param>
    /// <param name="Renderer">The settings that replace the headless renderer's defaults.</param>
    internal readonly record struct Headless(HeadlessVideo? Video, RendererOverrides Renderer)
    {
        /// <summary>
        /// Where the captures go: the video or the discard of <c>--video</c>, or without the option the PNG files of
        /// <paramref name="outFolder"/>, the output folder of the script run.
        /// </summary>
        public CaptureTarget TargetIn(string outFolder) =>
            Video switch
            {
                VideoFile file => file,
                Discard discard => discard,
                null => new PngFolder(outFolder),
            };
    }

    /// <summary>
    /// The settings of a headless run that replace the headless renderer's defaults. The default value replaces none.
    /// </summary>
    internal readonly record struct RendererOverrides
    {
        /// <summary>The curve that replaces the renderer's default, or null to keep it.</summary>
        public ToneCurve? ToneCurve { get; init; }

        /// <summary>The setting that replaces the renderer's default, or null to keep it.</summary>
        public ScreenSpaceLightSetting? ScreenSpaceLight { get; init; }

        /// <summary>True to turn the renderer's bloom off, false to keep its default.</summary>
        public bool BloomOff { get; init; }

        /// <summary>True to turn the renderer's reflections off, false to keep its default.</summary>
        public bool ReflectionsOff { get; init; }

        /// <summary>The anti-aliasing that replaces the renderer's default, or null to keep it.</summary>
        public AntiAliasing? AntiAliasing { get; init; }

        /// <summary>The local shadows that replace the renderer's default, or null to keep them.</summary>
        public LocalShadowSetting? LocalShadows { get; init; }

        /// <summary>The count of far lights with cascades that replaces the renderer's default, or null to keep it.</summary>
        public int? CascadedLights { get; init; }

        /// <summary>True to turn the renderer's environment map off, false to keep its default.</summary>
        public bool EnvironmentMapOff { get; init; }

        /// <summary>True to turn the renderer's skin scatter off, false to keep its default.</summary>
        public bool SubsurfaceOff { get; init; }

        /// <summary>True to take every light exactly (<see cref="EveryLight"/>), false to keep the default ReSTIR.</summary>
        public bool EveryLight { get; init; }

        /// <summary>Replaces the defaults of <paramref name="options"/> with these overrides.</summary>
        public void ApplyTo(RendererOptions options)
        {
            if (ToneCurve is { } curve)
                options.ToneCurve = curve;
            if (ScreenSpaceLight is { } screenSpaceLight)
                options.ScreenSpaceLight = screenSpaceLight;
            if (BloomOff)
                options.Bloom = new NoBloom();
            if (ReflectionsOff)
                options.Reflections = new NoReflections();
            if (AntiAliasing is { } antiAliasing)
                options.AntiAliasing = antiAliasing;
            if (LocalShadows is { } localShadows)
                options.LocalShadows = localShadows;
            if (CascadedLights is { } cascadedLights)
                options.CascadedLights = cascadedLights;
            if (EnvironmentMapOff)
                options.EnvironmentMap = new NoEnvironmentMap();
            if (SubsurfaceOff)
                options.Subsurface = new NoSubsurface();
            if (EveryLight)
                options.DirectLight = new EveryLight();
        }
    }
}
