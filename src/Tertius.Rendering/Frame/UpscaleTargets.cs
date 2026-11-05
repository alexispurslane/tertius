using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>The opaque color, reactive mask and output-size HDR color of the temporal upscale.</summary>
internal sealed class UpscaleTargets : IDisposable
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    public GpuImage OpaqueOnly { get; private set; }
    public GpuImage Reactive { get; private set; }
    public GpuImage Output { get; private set; }

    public UpscaleTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        Extent2D largestRenderSize,
        Extent2D outputSize
    )
    {
        this.device = device;
        this.allocator = allocator;
        Resize(largestRenderSize, outputSize);
    }

    [MemberNotNull(nameof(OpaqueOnly), nameof(Reactive), nameof(Output))]
    public void Resize(Extent2D largestRenderSize, Extent2D outputSize)
    {
        if (
            OpaqueOnly is { } opaque
            && Reactive is not null
            && Output is { } output
            && opaque.Extent.Equals(largestRenderSize)
            && output.Extent.Equals(outputSize)
        )
            return;
        Dispose();
        OpaqueOnly = Create(largestRenderSize, RenderTargets.HdrFormat, "opaque color");
        Reactive = Create(largestRenderSize, Format.R8Unorm, "FSR reactive mask");
        Output = Create(outputSize, RenderTargets.HdrFormat, "upscaled HDR");
    }

    private GpuImage Create(Extent2D extent, Format format, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.SampledBit
                | ImageUsageFlags.StorageBit
                | ImageUsageFlags.TransferSrcBit
                | ImageUsageFlags.TransferDstBit,
            mipLevels: 1,
            name: name
        );

    public void Dispose()
    {
        OpaqueOnly?.Dispose();
        Reactive?.Dispose();
        Output?.Dispose();
    }
}
