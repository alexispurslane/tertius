using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The screen-sized images of SMAA, shared by all frames in flight like the other <see cref="RenderTargets"/>, and the
/// descriptor set of the three passes (docs/plans/anti-aliasing.md, "Design"). Resizing recreates the images; the caller
/// makes sure the device is idle.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>tone-mapped</term><description>R8G8B8A8_SRGB with a mutable format: the tone map writes it through its
/// sRGB view. The edge pass reads the stored sRGB-encoded values through a UNORM view, and the blending pass reads linear
/// values through the sRGB view, so it blends linear light.</description></item>
/// <item><term>edges</term><description>R8G8_UNORM: an edge at the left of the pixel in r, above it in g.</description></item>
/// <item><term>weights</term><description>R8G8B8A8_UNORM: the blending weights of the pixel's four sides.</description></item>
/// </list>
/// </remarks>
internal sealed class SmaaTargets : IDisposable
{
    public const Format ToneMappedFormat = Format.R8G8B8A8Srgb;
    public const Format EncodedFormat = Format.R8G8B8A8Unorm;
    public const Format EdgesFormat = Format.R8G8Unorm;
    public const Format WeightsFormat = Format.R8G8B8A8Unorm;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private ImageView encodedView;

    public SmaaTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        SmaaTables tables,
        Extent2D extent
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        Tables = tables;
        Set = layouts.AllocateSmaaSet();
        // Written once: the tables never change, and resizing rewrites only the screen-sized bindings.
        layouts.WriteSampledView(set: Set, binding: 2, view: tables.Area.View);
        layouts.WriteSampledView(set: Set, binding: 3, view: tables.Search.View);
        Create(extent);
    }

    /// <summary>The two tables of the weight pass. The renderer owns them; these targets only read them.</summary>
    public SmaaTables Tables { get; }

    public GpuImage ToneMapped { get; private set; }
    public GpuImage Edges { get; private set; }
    public GpuImage Weights { get; private set; }

    /// <summary>The inputs of the three passes, as <see cref="DescriptorLayouts.SmaaSet"/> describes them.</summary>
    public DescriptorSet Set { get; }

    /// <summary>Recreates the images at a new size. The device must be idle.</summary>
    public void Resize(Extent2D extent)
    {
        DestroyImages();
        Create(extent);
    }

    [MemberNotNull(nameof(ToneMapped), nameof(Edges), nameof(Weights))]
    private void Create(Extent2D extent)
    {
        const ImageUsageFlags target = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit;
        ToneMapped = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: ToneMappedFormat,
            usage: target,
            mipLevels: 1,
            name: "smaa tone-mapped",
            mutableFormat: true
        );
        encodedView = ToneMapped.CreateFormatView(EncodedFormat);
        Edges = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: EdgesFormat,
            usage: target,
            mipLevels: 1,
            name: "smaa edges"
        );
        Weights = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: WeightsFormat,
            usage: target,
            mipLevels: 1,
            name: "smaa weights"
        );
        layouts.WriteSampledView(set: Set, binding: 0, view: encodedView);
        layouts.WriteSampledView(set: Set, binding: 1, view: Edges.View);
        layouts.WriteSampledView(set: Set, binding: 4, view: ToneMapped.View);
        layouts.WriteSampledView(set: Set, binding: 5, view: Weights.View);
    }

    private unsafe void DestroyImages()
    {
        Weights.Dispose();
        Edges.Dispose();
        device.Vk.DestroyImageView(device.Device, encodedView, null);
        ToneMapped.Dispose();
    }

    public void Dispose() => DestroyImages();
}
