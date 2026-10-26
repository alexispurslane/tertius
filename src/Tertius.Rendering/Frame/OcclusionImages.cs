using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The half-resolution images of the occlusion passes, the sector masks buffer and their sets
/// (docs/plans/screen-space-illumination.md, "Images and buffers"). The images stay in the General layout. Only
/// <see cref="VisibilityBitmaskLight"/> makes them.
/// </summary>
internal sealed class OcclusionImages : IDisposable
{
    public const Format ViewDepthFormat = Format.R32Sfloat;
    public const Format EdgesFormat = Format.R8Unorm;
    public const Format TermFormat = Format.R16G16B16A16Sfloat;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly int slices;

    /// <param name="slices">The slices of each texel, each of which keeps one mask in <see cref="SectorMasks"/>.</param>
    /// <exception cref="NotSupportedException">
    /// A format is not a storage image on the device, or the masks pass its maxStorageBufferRange.
    /// </exception>
    public OcclusionImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets,
        int slices
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.slices = slices;
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

    /// <summary>The estimate, rgb the bounce and a the occlusion: the sector pass's, then the result of the last denoise dispatch.</summary>
    public GpuImage TermA { get; private set; }

    /// <summary>The estimate after the first denoise dispatch.</summary>
    public GpuImage TermB { get; private set; }

    /// <summary>Slices words per half-resolution texel, row by row: each slice's mask of sectors blocked nearby.</summary>
    public GpuBuffer SectorMasks { get; private set; }

    /// <summary>
    /// Set 3 of <see cref="DescriptorLayouts.OcclusionLayout"/> with term A as the denoise source and term B as its
    /// destination: for every dispatch but the last denoise.
    /// </summary>
    public DescriptorSet SetAToB { get; }

    /// <summary>Set 3 with term B as the denoise source and term A as its destination: for the last denoise dispatch.</summary>
    public DescriptorSet SetBToA { get; }

    /// <summary>
    /// Recreates the images and the sector masks buffer at the half of the targets' size and rewrites both sets. The
    /// device must be idle.
    /// </summary>
    /// <exception cref="NotSupportedException">The masks at the new size pass the device's maxStorageBufferRange.</exception>
    public void Resize(RenderTargets targets)
    {
        // Always, even at the same half size: the sets point at the targets, which a resize recreates.
        DestroyImages();
        Create(targets);
    }

    [MemberNotNull(nameof(ViewDepth), nameof(Edges), nameof(TermA), nameof(TermB), nameof(SectorMasks))]
    private void Create(RenderTargets targets)
    {
        HalfExtent = new((targets.Extent.Width + 1) / 2, (targets.Extent.Height + 1) / 2);
        ulong masksSize = (ulong)HalfExtent.Width * HalfExtent.Height * (ulong)slices * sizeof(uint);
        CheckMasksSize(masksSize);
        ViewDepth = CreateImage(format: ViewDepthFormat, name: "occlusion view depth");
        Edges = CreateImage(format: EdgesFormat, name: "occlusion edges");
        TermA = CreateImage(format: TermFormat, name: "occlusion term A");
        TermB = CreateImage(format: TermFormat, name: "occlusion term B");
        SectorMasks = GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: masksSize,
            // Transfer source for the renderer's test hook, which reads the masks back.
            usage: BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "occlusion sector masks"
        );

        WriteSet(set: SetAToB, targets: targets, denoiseSource: TermA, denoiseDestination: TermB);
        WriteSet(set: SetBToA, targets: targets, denoiseSource: TermB, denoiseDestination: TermA);
    }

    /// <summary>
    /// Throws unless one storage buffer descriptor reaches all of the masks, which the sets bind whole
    /// (docs/plans/screen-space-illumination.md, "Portable Vulkan").
    /// </summary>
    private void CheckMasksSize(ulong masksSize)
    {
        uint range = device.Properties.Limits.MaxStorageBufferRange;
        if (masksSize > range)
            throw new NotSupportedException(
                $"The sector masks of {slices} slices at {HalfExtent.Width} × {HalfExtent.Height} half-resolution "
                    + $"texels take {masksSize} bytes, past maxStorageBufferRange, {range} bytes, on {device.DeviceName}."
            );
    }

    /// <summary>Writes the bindings of occlusion.glsl's set 3.</summary>
    private void WriteSet(DescriptorSet set, RenderTargets targets, GpuImage denoiseSource, GpuImage denoiseDestination)
    {
        layouts.WriteStorageImage(set: set, binding: 0, image: ViewDepth);
        layouts.WriteStorageImage(set: set, binding: 1, image: Edges);
        layouts.WriteStorageImage(set: set, binding: 2, image: TermA);
        layouts.WriteStorageImage(set: set, binding: 3, image: targets.Estimate);
        layouts.WriteBuffer(set: set, binding: 4, type: DescriptorType.StorageBuffer, buffer: SectorMasks);
        layouts.WriteStorageImage(set: set, binding: 6, image: denoiseSource);
        layouts.WriteStorageImage(set: set, binding: 7, image: denoiseDestination);
    }

    private GpuImage CreateImage(Format format, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: HalfExtent,
            format: format,
            // Transfer source for the renderer's test hooks, which read term A and the view depth back.
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
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
        SectorMasks.Dispose();
    }

    // The pool of the layouts frees the sets.
    public void Dispose() => DestroyImages();
}
