using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The half-resolution images of the occlusion passes and their sets (docs/plans/ambient-occlusion.md, "Images"). The
/// images stay in the General layout. Only <see cref="GroundTruthOcclusion"/> makes them.
/// </summary>
internal sealed class OcclusionImages : IDisposable
{
    public const Format ViewDepthFormat = Format.R32Sfloat;
    public const Format EdgesFormat = Format.R8Unorm;
    public const Format TermFormat = Format.R16Sfloat;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    public OcclusionImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        CheckFormats(device);
        SetAToB = layouts.AllocateOcclusionSet();
        SetBToA = layouts.AllocateOcclusionSet();
        Create(targets);
    }

    /// <summary>⌈W / 2⌉ × ⌈H / 2⌉ for targets of W × H pixels: each texel stands for a 2 × 2 block of pixels.</summary>
    public Extent2D HalfExtent { get; private set; }

    /// <summary>The view depth of each texel's top-left pixel, in m.</summary>
    public GpuImage ViewDepth { get; private set; }

    /// <summary>XeGTAO's packed depth edges, which the denoise reads.</summary>
    public GpuImage Edges { get; private set; }

    /// <summary>The occlusion: the horizons pass's estimate, then the result of the last denoise dispatch.</summary>
    public GpuImage TermA { get; private set; }

    /// <summary>The occlusion after the first denoise dispatch.</summary>
    public GpuImage TermB { get; private set; }

    /// <summary>
    /// Set 3 of <see cref="DescriptorLayouts.OcclusionLayout"/> with term A as the denoise source and term B as its
    /// destination: for every dispatch but the last denoise.
    /// </summary>
    public DescriptorSet SetAToB { get; }

    /// <summary>Set 3 with term B as the denoise source and term A as its destination: for the last denoise dispatch.</summary>
    public DescriptorSet SetBToA { get; }

    /// <summary>Recreates the images at the half of the targets' size and rewrites both sets. The device must be idle.</summary>
    public void Resize(RenderTargets targets)
    {
        // Always, even at the same half size: the sets point at the targets, which a resize recreates.
        DestroyImages();
        Create(targets);
    }

    [MemberNotNull(nameof(ViewDepth), nameof(Edges), nameof(TermA), nameof(TermB))]
    private void Create(RenderTargets targets)
    {
        HalfExtent = new((targets.Extent.Width + 1) / 2, (targets.Extent.Height + 1) / 2);
        ViewDepth = CreateImage(format: ViewDepthFormat, name: "occlusion view depth");
        Edges = CreateImage(format: EdgesFormat, name: "occlusion edges");
        TermA = CreateImage(format: TermFormat, name: "occlusion term A");
        TermB = CreateImage(format: TermFormat, name: "occlusion term B");

        WriteSet(set: SetAToB, targets: targets, denoiseSource: TermA, denoiseDestination: TermB);
        WriteSet(set: SetBToA, targets: targets, denoiseSource: TermB, denoiseDestination: TermA);
    }

    /// <summary>Writes the bindings of occlusion.glsl's set 3.</summary>
    private void WriteSet(DescriptorSet set, RenderTargets targets, GpuImage denoiseSource, GpuImage denoiseDestination)
    {
        layouts.WriteStorageImage(set: set, binding: 0, image: ViewDepth);
        layouts.WriteStorageImage(set: set, binding: 1, image: Edges);
        layouts.WriteStorageImage(set: set, binding: 2, image: TermA);
        layouts.WriteStorageImage(set: set, binding: 3, image: targets.Estimate);
        layouts.WriteStorageImage(set: set, binding: 6, image: denoiseSource);
        layouts.WriteStorageImage(set: set, binding: 7, image: denoiseDestination);
    }

    private GpuImage CreateImage(Format format, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: HalfExtent,
            format: format,
            usage: ImageUsageFlags.StorageBit,
            mipLevels: 1,
            name: name
        );

    private static void CheckFormats(VulkanDevice device)
    {
        foreach (Format format in (Format[])[ViewDepthFormat, EdgesFormat, TermFormat])
            if ((device.FormatProperties(format).OptimalTilingFeatures & FormatFeatureFlags.StorageImageBit) == 0)
                throw new NotSupportedException($"{format} cannot be a storage image on {device.DeviceName}.");
    }

    private void DestroyImages()
    {
        ViewDepth.Dispose();
        Edges.Dispose();
        TermA.Dispose();
        TermB.Dispose();
    }

    // The pool of the layouts frees the sets.
    public void Dispose() => DestroyImages();
}
