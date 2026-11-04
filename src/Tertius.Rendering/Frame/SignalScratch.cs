using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The scratch images of the signals' accumulations, one per part, RGBA16F at the targets' size: the temporal dispatch
/// writes its slow history into one, and the spatial dispatch reads its neighbors from it (docs/plans/temporal-renderer.md,
/// section 8, "Images"). The signals accumulate one after another, so they share these images.
/// </summary>
internal sealed class SignalScratch : IDisposable
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly RenderTargets targets;
    private readonly GpuImage[] images = new GpuImage[TemporalSignal.MostParts];

    /// <param name="targets">The render targets, whose size the images take.</param>
    public SignalScratch(VulkanDevice device, DeviceAllocator allocator, RenderTargets targets)
    {
        this.device = device;
        this.allocator = allocator;
        this.targets = targets;
        Create();
    }

    /// <summary>The scratch image of a signal's part.</summary>
    public GpuImage Part(int part) => images[part];

    /// <summary>Recreates the images at the targets' size. The device must be idle, and the signals resize after it,
    /// because their sets point at these images.</summary>
    public void Resize()
    {
        Dispose();
        Create();
    }

    private void Create()
    {
        for (int part = 0; part < images.Length; part++)
            images[part] = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: targets.Extent,
                format: TemporalSignal.SignalFormat,
                usage: ImageUsageFlags.StorageBit,
                mipLevels: 1,
                name: $"signal scratch {part}"
            );
    }

    public void Dispose()
    {
        foreach (GpuImage image in images)
            image.Dispose();
    }
}
