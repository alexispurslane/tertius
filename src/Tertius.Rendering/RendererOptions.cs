namespace Tertius.Rendering;

/// <summary>Settings for creating a <see cref="VulkanRenderer"/>.</summary>
public sealed class RendererOptions
{
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
    /// The side of each of the four sun shadow cascades, in texels: 256 up to the device's largest image. The maps
    /// take ShadowMapSize² × 16 bytes of device memory (64 MB at the default 2048).
    /// </summary>
    public int ShadowMapSize { get; set; } = ShadowMap.DefaultSize;

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

    /// <summary>The curve that maps the HDR image to the output, after the exposure.</summary>
    public ToneCurve ToneCurve { get; set; } = ToneCurve.AcesFitted;
}
