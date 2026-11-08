using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The scratch images of the signals' accumulations, one per part, RGBA16F plus optional RGBA32F specular scratch: the temporal dispatch
/// writes its slow history into one, and the spatial dispatch reads its neighbors from it (docs/plans/temporal-renderer.md,
/// section 8, "Images"). The signals accumulate one after another, so they share these images.
/// </summary>
internal sealed class SignalScratch : IDisposable
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly RenderTargets targets;
    private readonly GpuImage[] images = new GpuImage[TemporalSignal.MostParts];
    private readonly GpuImage[] preciseImages;

    /// <param name="targets">The render targets, whose size the images take.</param>
    /// <param name="historyMode">The specular history, selected once per renderer.</param>
    /// <param name="instrumentation">The instrumentation of the specular history, which needs a precise history.</param>
    /// <exception cref="ArgumentException">
    /// The diagnostic or the accumulation trace is on with the original half-precision history.
    /// </exception>
    public SignalScratch(
        VulkanDevice device,
        DeviceAllocator allocator,
        RenderTargets targets,
        SpecularHistoryMode historyMode,
        HistoryInstrumentation instrumentation
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.targets = targets;
        if (instrumentation != HistoryInstrumentation.None && !historyMode.KeepsLightAge)
            throw new ArgumentException(
                $"TERTIUS_REFLECTION_HISTORY_AGREEMENT={instrumentation} needs a precise specular history, not TERTIUS_SPECULAR_HISTORY=original."
            );
        HistoryMode = historyMode;
        Instrumentation = instrumentation;
        preciseImages = HistoryMode == SpecularHistoryMode.Original ? [] : new GpuImage[TemporalSignal.MostParts];
        Create();
    }

    /// <summary>The history control selected once when this renderer creates its scratch images.</summary>
    public SpecularHistoryMode HistoryMode { get; }

    /// <summary>The instrumentation of the specular history, selected once with <see cref="HistoryMode"/>.</summary>
    public HistoryInstrumentation Instrumentation { get; }

    /// <summary>The scratch image of a signal's part.</summary>
    public GpuImage Part(int part, SpecularHistoryMode historyMode = SpecularHistoryMode.Original) =>
        historyMode == SpecularHistoryMode.Original ? images[part] : preciseImages[part];

    /// <summary>Recreates the images at the targets' size. The device must be idle, and the signals resize after it,
    /// because their sets point at these images.</summary>
    public void Resize()
    {
        Dispose();
        Create();
    }

    private void Create()
    {
        Create(images, TemporalSignal.SignalFormat);
        Create(preciseImages, Format.R32G32B32A32Sfloat);
    }

    private void Create(GpuImage[] destination, Format format)
    {
        for (int part = 0; part < destination.Length; part++)
            destination[part] = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: targets.Extent,
                format: format,
                usage: ImageUsageFlags.StorageBit,
                mipLevels: 1,
                name: $"signal scratch {part} {format}"
            );
    }

    public void Dispose()
    {
        foreach (GpuImage image in preciseImages)
            image.Dispose();
        foreach (GpuImage image in images)
            image.Dispose();
    }
}
