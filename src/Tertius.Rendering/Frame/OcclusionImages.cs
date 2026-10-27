using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The half-resolution images of the occlusion passes and their sets
/// (docs/plans/screen-space-illumination.md, "Images and buffers"). The images stay in the General layout. Only
/// <see cref="VisibilityBitmaskLight"/> makes them.
/// </summary>
internal sealed class OcclusionImages : IDisposable
{
    public const Format ViewDepthFormat = Format.R32Sfloat;
    public const Format EdgesFormat = Format.R8Unorm;
    public const Format TermFormat = Format.R16G16B16A16Sfloat;
    public const Format DirectFormat = Format.R16G16B16A16Sfloat;
    public const Format GeometricNormalFormat = Format.R16G16SNorm;

    /// <summary>The storage images of the occlusion set, which the four passes bind in one compute stage.</summary>
    public const int StorageImages = 8;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    /// <exception cref="NotSupportedException">
    /// A format is not a storage image on the device, or the device binds fewer than <see cref="StorageImages"/> storage
    /// images in one stage.
    /// </exception>
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
        CheckDevice(device);
        SetAToB = layouts.AllocateOcclusionSet();
        SetBToA = layouts.AllocateOcclusionSet();
        Create(targets);
    }

    /// <summary>
    /// Why a device that binds at most <paramref name="maxPerStageStorageImages"/> storage images in one shader stage
    /// cannot bind the occlusion set, or null when it can: the set binds <see cref="StorageImages"/>, and Vulkan
    /// guarantees only 4.
    /// </summary>
    internal static string? StorageImageProblem(uint maxPerStageStorageImages) =>
        maxPerStageStorageImages < StorageImages
            ? $"The occlusion set binds {StorageImages} storage images in one compute stage, above the device's maxPerStageDescriptorStorageImages of {maxPerStageStorageImages}."
            : null;

    /// <summary>⌈W / 2⌉ × ⌈H / 2⌉ for targets of W × H pixels: each texel stands for a 2 × 2 block of pixels.</summary>
    public Extent2D HalfExtent { get; private set; }

    /// <summary>The view depth of each texel's top-left pixel, in m.</summary>
    public GpuImage ViewDepth { get; private set; }

    /// <summary>
    /// The direct image's value at each texel's representative pixel, which the depth pass copies for the near steps
    /// (docs/plans/screen-space-illumination.md, decision 25).
    /// </summary>
    public GpuImage Direct { get; private set; }

    /// <summary>
    /// The octahedral world geometric normal at each texel's representative pixel, the normals target's BA, which the depth
    /// pass copies for the near steps.
    /// </summary>
    public GpuImage GeometricNormal { get; private set; }

    /// <summary>XeGTAO's packed depth edges, which the denoise reads.</summary>
    public GpuImage Edges { get; private set; }

    /// <summary>The estimate, rgb the bounce and a the occlusion: the sector pass's, then the result of the last denoise dispatch.</summary>
    public GpuImage TermA { get; private set; }

    /// <summary>The estimate after the first denoise dispatch.</summary>
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

    [MemberNotNull(
        nameof(ViewDepth),
        nameof(Direct),
        nameof(GeometricNormal),
        nameof(Edges),
        nameof(TermA),
        nameof(TermB)
    )]
    private void Create(RenderTargets targets)
    {
        HalfExtent = new((targets.Extent.Width + 1) / 2, (targets.Extent.Height + 1) / 2);
        ViewDepth = CreateImage(format: ViewDepthFormat, name: "occlusion view depth");
        Direct = CreateImage(format: DirectFormat, name: "occlusion direct");
        GeometricNormal = CreateImage(format: GeometricNormalFormat, name: "occlusion geometric normal");
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
        layouts.WriteStorageImage(set: set, binding: 4, image: denoiseSource);
        layouts.WriteStorageImage(set: set, binding: 5, image: denoiseDestination);
        layouts.WriteStorageImage(set: set, binding: 6, image: Direct);
        layouts.WriteStorageImage(set: set, binding: 7, image: GeometricNormal);
    }

    private GpuImage CreateImage(Format format, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: HalfExtent,
            format: format,
            // Transfer source for the renderer's test hooks, which read term A, the view depth and the copies back.
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: name
        );

    private static void CheckDevice(VulkanDevice device)
    {
        if (StorageImageProblem(device.Properties.Limits.MaxPerStageDescriptorStorageImages) is { } storageImages)
            throw new NotSupportedException($"{storageImages} ({device.DeviceName})");
        foreach (
            Format format in (Format[])[ViewDepthFormat, DirectFormat, GeometricNormalFormat, EdgesFormat, TermFormat]
        )
            if ((device.FormatProperties(format).OptimalTilingFeatures & FormatFeatureFlags.StorageImageBit) == 0)
                throw new NotSupportedException($"{format} cannot be a storage image on {device.DeviceName}.");
    }

    private void DestroyImages()
    {
        ViewDepth.Dispose();
        Direct.Dispose();
        GeometricNormal.Dispose();
        Edges.Dispose();
        TermA.Dispose();
        TermB.Dispose();
    }

    // The pool of the layouts frees the sets.
    public void Dispose() => DestroyImages();
}
