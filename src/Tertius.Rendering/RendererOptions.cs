namespace Tertius.Rendering;

/// <summary>Settings for creating a <see cref="VulkanRenderer"/>.</summary>
public sealed class RendererOptions
{
    /// <summary>The one temporal anti-aliasing and upscale setting. Headless defaults to a fixed scale of 2/3.</summary>
    public UpscalingSetting Upscaling { get; set; } = Fsr3Upscaling.Default;

    /// <summary>The most far lights that get cascaded shadow maps.</summary>
    public const int MaxCascadedLights = 4;

    /// <summary>
    /// Enables VK_LAYER_KHRONOS_validation with synchronization validation, and records every warning and error in
    /// <see cref="VulkanRenderer.ValidationMessages"/>. Costs CPU time; use it in tests and debug builds.
    /// </summary>
    public bool Validation { get; set; }

    /// <summary>How many frames the CPU may record while the GPU still works on earlier ones. 1 to 4.</summary>
    public int FramesInFlight { get; set; } = 2;

    /// <summary>
    /// Whether a captured frame stays in flight. False: <see cref="VulkanRenderer.Render"/> waits for the captured frame
    /// and hands its pixels to the sink before it returns, so the GPU idles until the next frame. True: the renderer hands
    /// them over when it draws the frame's slot again or at <see cref="VulkanRenderer.FinishCaptures"/>, so a run of
    /// captures keeps <see cref="FramesInFlight"/> frames on the GPU, as a window does. The pixels are the same.
    /// </summary>
    public bool CapturesInFlight { get; set; }

    /// <summary>Present with FIFO (wait for vertical blank). Otherwise mailbox or immediate. Ignored when headless.</summary>
    public bool VSync { get; set; } = true;

    /// <summary>Output width in pixels. Required when headless; for a window, 0 means the framebuffer size.</summary>
    public int Width { get; set; }

    /// <summary>Output height in pixels. Required when headless; for a window, 0 means the framebuffer size.</summary>
    public int Height { get; set; }

    /// <summary>
    /// The side of each shadow cascade of every cascade set, in texels: 256 up to the device's largest image. Each set's
    /// four cascades take ShadowMapSize² × 16 bytes of device memory (64 MB at the default 2048).
    /// </summary>
    public int ShadowMapSize { get; set; } = ShadowMap.DefaultSize;

    /// <summary>How many far lights get cascaded shadow maps, 0 to 4. Each takes ShadowMapSize² × 16 bytes. The default is 1.</summary>
    public int CascadedLights { get; set; } = 1;

    /// <summary>Whether near lights cast shadows from a shared atlas. The default is <see cref="LocalShadowAtlas.Default"/>.</summary>
    public LocalShadowSetting LocalShadows { get; set; } = LocalShadowAtlas.Default;

    /// <summary>
    /// Which pairs of a pixel and a light the direct pass shades in full: every pair, or under <see cref="KeyAndFill"/>
    /// only each pixel's key lights, with the other lights' diffuse light from their fill response. The default is
    /// <see cref="KeyAndFill.Default"/>. Under <see cref="KeyAndFill"/>, the corner images and the tile records take
    /// about 5 bytes per render pixel, about 10 MB at 1920 × 1080 (ARCHITECTURE.md, "Key and fill").
    /// </summary>
    public LightPairSetting LightPairs { get; set; } = new EveryLightPair();

    /// <summary>
    /// Draws small objects as impostors: below a projected height of 200 px an object is replaced by a captured
    /// billboard, and above 300 px it returns to geometry. See <see cref="ImpostorTiles"/> for the memory it takes.
    /// </summary>
    public bool Impostors { get; set; } = true;

    /// <summary>
    /// The number of 512 × 512 tiles in the impostor atlas, which bounds how many objects can be impostors at once:
    /// 1 up to the device's largest image array. Each tile takes about 3.3 MB of device memory (HDR color, depth
    /// offset and mips), so the default 64, as many as a 4096 × 4096 atlas holds, takes about 213 MB. Ignored when
    /// <see cref="Impostors"/> is false.
    /// </summary>
    public int ImpostorTiles { get; set; } = ImpostorAtlas.DefaultTiles;

    /// <summary>
    /// How the renderer chooses the exposure: <see cref="AutoExposure"/> follows the surfaces in view over time, and
    /// <see cref="FixedExposure"/> never moves, for tests. The default is <see cref="AutoExposure.Default"/>.
    /// </summary>
    public ExposureSetting Exposure { get; set; } = AutoExposure.Default;

    /// <summary>The curve that maps the HDR image to the output, after the exposure.</summary>
    public ToneCurve ToneCurve { get; set; } = ToneCurve.AcesFitted;

    /// <summary>
    /// The glow around light brighter than white after the exposure, or <see cref="NoBloom"/>. On by default. The bloom
    /// images take about a third of the HDR image's memory (ARCHITECTURE.md, "Bloom").
    /// </summary>
    public BloomSetting Bloom { get; set; } = Rendering.Bloom.Default;

    /// <summary>
    /// The screen tracer, which gives every lit surface its reflections and its bounce light from what is on the screen
    /// (docs/plans/temporal-renderer.md, section 6), or <see cref="NoScreenTrace"/>, under which each surface takes the
    /// environment's closed forms with every direction open. The default is <see cref="ScreenTrace.Default"/>. Its images,
    /// its two signals and the depth pyramid take about 180 bytes per pixel, about 370 MB at 1920 × 1080 (ARCHITECTURE.md,
    /// "The screen tracer").
    /// </summary>
    public ScreenTraceSetting ScreenTrace { get; set; } = Rendering.ScreenTrace.Default;

    /// <summary>
    /// The warm-up draws of a headless still under <see cref="ScreenTrace"/>, 0 to <see cref="MaxWarmUpFrames"/>: the
    /// draws of the still's snapshot before the shown one, in which the tracer's signals converge
    /// (docs/plans/temporal-renderer.md, section 10, "The still rule"). The default is 32.
    /// </summary>
    public int WarmUpFrames { get; set; } = DefaultWarmUpFrames;

    /// <summary>
    /// The warm-up draws of a still unless the options give another count: the design's count
    /// (docs/plans/temporal-renderer.md, section 10, "The still rule").
    /// </summary>
    public const int DefaultWarmUpFrames = 32;

    /// <summary>The most warm-up draws: far past the signals' longest history, it bounds the time of a still.</summary>
    public const int MaxWarmUpFrames = 1024;

    /// <summary>
    /// Whether skin scatters its diffuse light under the surface, or <see cref="NoSubsurface"/>. On by default. The scatter
    /// image and the row image take 16 bytes per pixel, about 33 MB at 1920 × 1080 (docs/plans/skin-and-hair.md, "Cost").
    /// </summary>
    public SubsurfaceSetting Subsurface { get; set; } = SeparableSubsurface.Default;

    /// <summary>
    /// The ambient light: a map of the scene around the camera, or <see cref="NoEnvironmentMap"/> for the hemisphere of
    /// World.Ambient. The default is <see cref="SceneEnvironmentMap.Default"/>: 128 texels per face, one face per frame, about
    /// 2 MB (docs/plans/environment-map.md, "The setting").
    /// </summary>
    public EnvironmentMapSetting EnvironmentMap { get; set; } = SceneEnvironmentMap.Default;

    /// <summary>
    /// Whether posed instances draw their meshes deformed by their rig poses, in every pass. Off, each draws as uploaded.
    /// On by default. Each deformed instance takes 52 bytes of device memory per vertex (docs/plans/skinning-and-morphs.md,
    /// "Memory and cost").
    /// </summary>
    public bool Deformation { get; set; } = true;

    /// <summary>
    /// Whether the GPU timer writes a timestamp after every pass, so that <see cref="RenderStats.GpuTimings"/> times each
    /// pass. Off, it writes one after each of the render scale's cost groups (<see cref="RenderScaleController.CostGroupOf"/>),
    /// which the window's controller needs. On by default, for measured runs; the window turns it off
    /// (ARCHITECTURE.md, "GPU timings").
    /// </summary>
    public bool PassTimestamps { get; set; } = true;
}
