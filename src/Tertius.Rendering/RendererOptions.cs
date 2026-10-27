namespace Tertius.Rendering;

/// <summary>Settings for creating a <see cref="VulkanRenderer"/>.</summary>
public sealed class RendererOptions
{
    /// <summary>The most far lights that get cascaded shadow maps.</summary>
    public const int MaxCascadedLights = 4;

    /// <summary>
    /// Enables VK_LAYER_KHRONOS_validation with synchronization validation, and records every warning and error in
    /// <see cref="VulkanRenderer.ValidationMessages"/>. Costs CPU time; use it in tests and debug builds.
    /// </summary>
    public bool Validation { get; set; }

    /// <summary>How many frames the CPU may record while the GPU still works on earlier ones. 1 to 4.</summary>
    public int FramesInFlight { get; set; } = 2;

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
    /// Whether the renderer estimates the crease shadowing and the bounce light from the screen: <see cref="VisibilityBitmaskLight"/> or
    /// <see cref="NoScreenSpaceLight"/>. The default is <see cref="VisibilityBitmaskLight.Default"/>.
    /// </summary>
    public ScreenSpaceLightSetting ScreenSpaceLight { get; set; } = VisibilityBitmaskLight.Default;

    /// <summary>
    /// The glow around light brighter than white after the exposure, or <see cref="NoBloom"/>. On by default. The bloom
    /// images take about a third of the HDR image's memory (ARCHITECTURE.md, "Bloom").
    /// </summary>
    public BloomSetting Bloom { get; set; } = Rendering.Bloom.Default;

    /// <summary>
    /// Reflections of what is on the screen in glossy surfaces, or <see cref="NoReflections"/>. On by default. The
    /// pyramids take about 33 MB at 1920 × 1080 (docs/plans/screen-space-reflections.md, "Images").
    /// </summary>
    public ReflectionSetting Reflections { get; set; } = ConeTracedReflections.Default;

    /// <summary>
    /// How the renderer smooths jagged edges after the tone map. SMAA takes 10 bytes of device memory per output pixel
    /// (about 21 MB at 1920 × 1080) and three full-screen passes.
    /// </summary>
    public AntiAliasing AntiAliasing { get; set; } = AntiAliasing.Smaa;

    /// <summary>
    /// The ambient light: a map of the scene around the camera, or <see cref="NoEnvironmentMap"/> for the hemisphere of
    /// World.Ambient. The default is <see cref="SceneEnvironmentMap.Default"/>: 128 texels per face, one face per frame, about
    /// 2 MB (docs/plans/environment-map.md, "The setting").
    /// </summary>
    public EnvironmentMapSetting EnvironmentMap { get; set; } = SceneEnvironmentMap.Default;
}
