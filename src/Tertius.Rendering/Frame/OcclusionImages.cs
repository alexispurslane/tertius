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
        Set = layouts.AllocateOcclusionSet();
        DenoiseAToB = layouts.AllocateDenoiseSet();
        DenoiseBToA = layouts.AllocateDenoiseSet();
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

    /// <summary>Set 0 of <see cref="DescriptorLayouts.OcclusionLayout"/>, for all four passes.</summary>
    public DescriptorSet Set { get; }

    /// <summary>Set 1 of the first denoise dispatch: term A to term B.</summary>
    public DescriptorSet DenoiseAToB { get; }

    /// <summary>Set 1 of the last denoise dispatch: term B to term A.</summary>
    public DescriptorSet DenoiseBToA { get; }

    /// <summary>Recreates the images at the half of the targets' size and rewrites the three sets. The device must be idle.</summary>
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

        // The bindings of occlusion.glsl.
        layouts.WriteImage(set: Set, binding: 0, arrayElement: 0, image: targets.Distance);
        layouts.WriteImage(set: Set, binding: 1, arrayElement: 0, image: targets.Normal);
        layouts.WriteStorageImage(set: Set, binding: 2, image: ViewDepth);
        layouts.WriteStorageImage(set: Set, binding: 3, image: Edges);
        layouts.WriteStorageImage(set: Set, binding: 4, image: TermA);
        layouts.WriteStorageImage(set: Set, binding: 5, image: targets.Estimate);
        layouts.WriteStorageImage(set: DenoiseAToB, binding: 0, image: TermA);
        layouts.WriteStorageImage(set: DenoiseAToB, binding: 1, image: TermB);
        layouts.WriteStorageImage(set: DenoiseBToA, binding: 0, image: TermB);
        layouts.WriteStorageImage(set: DenoiseBToA, binding: 1, image: TermA);
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
