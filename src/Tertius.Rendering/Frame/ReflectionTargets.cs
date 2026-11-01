using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The two pyramids of the screen-space reflections, the image of their trace (<see cref="Trace"/>), the pyramids' level
/// sets and the trace's set (docs/plans/screen-space-reflections.md, "Images" and "Descriptor sets"). The
/// images stay in the General layout for their whole life. <see cref="RenderTargets"/> owns one under <see cref="ConeTracedReflections"/>.
/// </summary>
/// <remarks>
/// A Vulkan mip halves its image and rounds down. A level of the depth pyramid halves the screen and rounds up, so the
/// depth image is padded until each mip holds its level (<see cref="DepthImageExtent"/>), and its builds fill the padding
/// with the empty bounds. The radiance pyramid has the screen's size and Vulkan's full mip chain.
/// </remarks>
internal sealed unsafe class ReflectionTargets : IDisposable
{
    /// <summary>The depth pyramid's format: R the near bound, G the far bound, in 1/m. depth_pyramid.glsl declares rg32f.</summary>
    public const Format DepthFormat = Format.R32G32Sfloat;

    /// <summary>The radiance pyramid's format: premultiplied pre-exposed radiance and the surface coverage.</summary>
    public const Format RadianceFormat = Format.R16G16B16A16Sfloat;

    /// <summary>
    /// The format of the half-resolution trace's hit depths, in m. A half float would overflow past 65,504 m, short of a
    /// far planet. reflection_half_trace.comp and reflection_upsample.comp declare r32f.
    /// </summary>
    public const Format HitDepthFormat = Format.R32Sfloat;

    /// <summary>The longest side of a screen with reflections, in px: the sets are allocated for <see cref="DepthPyramid.MaxLevels"/> levels.</summary>
    public const int MaxSide = 1 << DepthPyramid.MaxLevels;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    /// <summary>The set of each depth level from 1 to <see cref="DepthPyramid.MaxLevels"/>, at index level − 1.</summary>
    private readonly DescriptorSet[] depthLevelSets = new DescriptorSet[DepthPyramid.MaxLevels];

    /// <summary>The set of each radiance level from 0 to <see cref="DepthPyramid.MaxLevels"/>.</summary>
    private readonly DescriptorSet[] radianceLevelSets = new DescriptorSet[DepthPyramid.MaxLevels + 1];

    // The view of each mip of the two images, which the level sets read and write. Destroyed before the images.
    private ImageView[] depthMipViews = [];
    private ImageView[] radianceMipViews = [];

    /// <summary>
    /// Allocates the sets for the largest pyramids, and makes the images for a screen of <paramref name="extent"/> and a
    /// trace at <paramref name="resolution"/>.
    /// </summary>
    public ReflectionTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent,
        GpuImage hdr,
        TraceResolution resolution
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        for (int i = 0; i < depthLevelSets.Length; i++)
            depthLevelSets[i] = layouts.AllocatePyramidLevelSet();
        for (int i = 0; i < radianceLevelSets.Length; i++)
            radianceLevelSets[i] = layouts.AllocatePyramidLevelSet();
        DepthTailSet = layouts.AllocateDepthTailSet();
        TraceSet = layouts.AllocateReflectionSet();
        Create(extent: extent, hdr: hdr, resolution: resolution);
    }

    /// <summary>The depth pyramid image, RG32F: mip m holds level m + 1, for m from 0 to <see cref="TopLevel"/> − 1, padded.</summary>
    public GpuImage DepthImage { get; private set; }

    /// <summary>The radiance pyramid, RGBA16F: mip k holds level k, from the screen's size to 1 × 1.</summary>
    public GpuImage RadianceImage { get; private set; }

    /// <summary>The trace's own image: none for <see cref="FullTrace"/>, or the half-resolution trace's. Remade on a resize, in the same case.</summary>
    public ReflectionTrace Trace { get; private set; }

    /// <summary>The resolution of <see cref="Trace"/>.</summary>
    public TraceResolution Resolution =>
        Trace switch
        {
            FullTrace => TraceResolution.Full,
            HalfTrace => TraceResolution.Half,
            null => throw new UnreachableException("Create sets a trace."),
        };

    /// <summary>The depth pyramid's top level L, whose one cell covers the screen (<see cref="DepthPyramid.LevelCount"/>).</summary>
    public int TopLevel { get; private set; }

    /// <summary>The radiance pyramid's top level, whose one texel covers the screen: its mip count − 1.</summary>
    public int RadianceTopLevel => (int)RadianceImage.MipLevels - 1;

    /// <summary>Set 3 of <see cref="DescriptorLayouts.DepthTailLayout"/>, written when the depth image has a tail (<see cref="DepthPyramidTail"/>).</summary>
    public DescriptorSet DepthTailSet { get; }

    /// <summary>
    /// Set 3 of <see cref="DescriptorLayouts.ReflectionLayout"/>, written: both pyramids, the HDR image and, for a
    /// <see cref="HalfTrace"/>, its images at bindings 4 and 5 and its own-ray list at binding 6.
    /// </summary>
    public DescriptorSet TraceSet { get; }

    /// <summary>Why the pyramids cannot cover a screen of <paramref name="width"/> by <paramref name="height"/> px, or null when they can.</summary>
    internal static string? ScreenProblem(long width, long height) =>
        Math.Max(width, height) > MaxSide
            ? $"Reflections cover a screen of at most {MaxSide} px a side, not {width} × {height}."
            : null;

    /// <summary>
    /// The smallest extent whose Vulkan mip chain holds each level of the depth pyramid of a screen of
    /// <paramref name="screen"/>: mip m at least as large as level m + 1 on each axis, with one mip per level.
    /// </summary>
    internal static Extent2D DepthImageExtent(Extent2D screen)
    {
        int width = (int)screen.Width,
            height = (int)screen.Height;
        int topLevel = DepthPyramid.LevelCount(width, height);
        uint imageWidth = 1,
            imageHeight = 1;
        // A side of 1 needs no padding: Vulkan's mips never fall below 1.
        for (int level = 1; level <= topLevel; level++)
        {
            (int levelWidth, int levelHeight) = DepthPyramid.LevelSize(width, height, level);
            if (levelWidth > 1)
                imageWidth = Math.Max(imageWidth, (uint)levelWidth << (level - 1));
            if (levelHeight > 1)
                imageHeight = Math.Max(imageHeight, (uint)levelHeight << (level - 1));
        }

        return new(imageWidth, imageHeight);
    }

    /// <summary>The set of the dispatch that writes depth <paramref name="level"/>, from 1 to <see cref="TopLevel"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is outside 1 to <see cref="TopLevel"/>.</exception>
    public DescriptorSet DepthLevelSet(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, TopLevel);
        return depthLevelSets[level - 1];
    }

    /// <summary>The set of the dispatch that writes radiance <paramref name="level"/>, from 0 to <see cref="RadianceTopLevel"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is outside 0 to <see cref="RadianceTopLevel"/>.</exception>
    public DescriptorSet RadianceLevelSet(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, RadianceTopLevel);
        return radianceLevelSets[level];
    }

    /// <summary>
    /// Makes the images for a screen of <paramref name="extent"/> and a trace at <paramref name="resolution"/>, and writes
    /// every set in use. The device must be idle.
    /// </summary>
    [MemberNotNull(nameof(DepthImage), nameof(RadianceImage))]
    internal void Create(Extent2D extent, GpuImage hdr, TraceResolution resolution)
    {
        TopLevel = DepthPyramid.LevelCount((int)extent.Width, (int)extent.Height);
        DepthImage = CreateImage(
            extent: DepthImageExtent(extent),
            format: DepthFormat,
            mipLevels: (uint)TopLevel,
            name: "depth pyramid"
        );
        RadianceImage = CreateImage(
            extent: extent,
            format: RadianceFormat,
            mipLevels: (uint)BitOperations.Log2(Math.Max(extent.Width, extent.Height)) + 1,
            name: "radiance pyramid"
        );
        Trace = resolution switch
        {
            TraceResolution.Full => new FullTrace(),
            TraceResolution.Half => new HalfTrace(
                Traced: CreateHalfImage(screen: extent, format: RadianceFormat, name: "reflection trace"),
                HitDepths: CreateHalfImage(screen: extent, format: HitDepthFormat, name: "reflection hit depths"),
                OwnRays: CreateOwnRayList(extent)
            ),
        };
        depthMipViews = MipViews(DepthImage);
        radianceMipViews = MipViews(RadianceImage);
        WriteLevelSets(hdr);
        WriteDepthTailSet();
        WriteTraceSet(hdr);
    }

    /// <summary>Writes the level sets of both pyramids over the mip views of <see cref="Create"/>.</summary>
    private void WriteLevelSets(GpuImage hdr)
    {
        // Level 1 reads the distance target from the G-buffer set, so its binding 0 stays unwritten.
        for (int level = 1; level <= TopLevel; level++)
        {
            if (level > 1)
                layouts.WriteImage(
                    set: depthLevelSets[level - 1],
                    binding: 0,
                    view: depthMipViews[level - 2],
                    layout: ImageLayout.General
                );
            layouts.WriteStorageImage(set: depthLevelSets[level - 1], binding: 1, view: depthMipViews[level - 1]);
        }

        layouts.WriteImage(
            set: radianceLevelSets[0],
            binding: 0,
            view: hdr.View,
            layout: ImageLayout.ShaderReadOnlyOptimal
        );
        for (int level = 0; level <= RadianceTopLevel; level++)
        {
            if (level > 0)
                layouts.WriteImage(
                    set: radianceLevelSets[level],
                    binding: 0,
                    view: radianceMipViews[level - 1],
                    layout: ImageLayout.General
                );
            layouts.WriteStorageImage(set: radianceLevelSets[level], binding: 1, view: radianceMipViews[level]);
        }
    }

    /// <summary>
    /// Writes the reflection set over the pyramids of <see cref="Create"/>, the HDR image <paramref name="hdr"/> and the
    /// image of a <see cref="HalfTrace"/>.
    /// </summary>
    private void WriteTraceSet(GpuImage hdr)
    {
        // The bindings of DescriptorLayouts.ReflectionLayout; binding 2 is the immutable sampler.
        layouts.WriteImage(set: TraceSet, binding: 0, view: DepthImage.View, layout: ImageLayout.General);
        layouts.WriteImage(set: TraceSet, binding: 1, view: RadianceImage.View, layout: ImageLayout.General);
        layouts.WriteStorageImage(set: TraceSet, binding: 3, image: hdr);
        switch (Trace)
        {
            case HalfTrace half:
                layouts.WriteStorageImage(set: TraceSet, binding: 4, image: half.Traced);
                layouts.WriteStorageImage(set: TraceSet, binding: 5, image: half.HitDepths);
                layouts.WriteBuffer(set: TraceSet, binding: 6, type: DescriptorType.StorageBuffer, buffer: half.OwnRays);
                break;
            case FullTrace:
                // The full-resolution trace writes the HDR image only, so bindings 4 and 5 stay unwritten.
                break;
            case null:
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>Destroys the views and the images. Safe to call twice. The device must be idle.</summary>
    internal void DestroyImages()
    {
        foreach (ImageView view in (ImageView[])[.. depthMipViews, .. radianceMipViews])
            device.Vk.DestroyImageView(device.Device, view, null);
        depthMipViews = [];
        radianceMipViews = [];
        DepthImage.Dispose();
        RadianceImage.Dispose();
        switch (Trace)
        {
            case HalfTrace half:
                half.OwnRays.Dispose();
                half.HitDepths.Dispose();
                half.Traced.Dispose();
                break;
            case FullTrace or null:
                break;
            default:
                throw new UnreachableException();
        }
    }

    // The pool of the layouts frees the sets.
    public void Dispose() => DestroyImages();

    /// <summary>An image of the half-resolution trace, ⌈W / 2⌉ × ⌈H / 2⌉ for a <paramref name="screen"/> of W × H: written and read as a storage image, copied out by the read hooks.</summary>
    private GpuImage CreateHalfImage(Extent2D screen, Format format, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: new((screen.Width + 1) / 2, (screen.Height + 1) / 2),
            format: format,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: name
        );

    /// <summary>
    /// The own-ray list of a <paramref name="screen"/>, in the layout of <see cref="OwnRayList"/>. The pass clears the
    /// header by a transfer, the upsample appends by atomic adds, the own-ray dispatch reads it as its indirect command,
    /// and the read hook copies it out.
    /// </summary>
    private GpuBuffer CreateOwnRayList(Extent2D screen) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: OwnRayList.Bytes(screen),
            usage: BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.IndirectBufferBit
                | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "reflection own rays"
        );

    /// <summary>A pyramid image: built as a storage image, sampled by the builds and the trace, copied out by the read hooks.</summary>
    private GpuImage CreateImage(Extent2D extent, Format format, uint mipLevels, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.SampledBit | ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: mipLevels,
            name: name
        );

    /// <summary>
    /// Writes the depth tail set: the mip before the tail, and the tail's mips from the first array element. The elements
    /// past the tail repeat its last mip: the shader never writes them, but each must hold a view.
    /// </summary>
    private void WriteDepthTailSet()
    {
        int first = DepthPyramidTail.FirstMip(DepthImage);
        if (first >= depthMipViews.Length)
            return;
        layouts.WriteImage(set: DepthTailSet, binding: 0, view: depthMipViews[first - 1], layout: ImageLayout.General);
        for (int element = 0; element < DepthPyramidTail.Mips; element++)
            layouts.WriteStorageImage(
                set: DepthTailSet,
                binding: 1,
                arrayElement: (uint)element,
                view: depthMipViews[Math.Min(first + element, depthMipViews.Length - 1)]
            );
    }

    private static ImageView[] MipViews(GpuImage image)
    {
        var views = new ImageView[image.MipLevels];
        for (uint mip = 0; mip < views.Length; mip++)
            views[mip] = image.CreateLayerView(layer: 0, mipLevel: mip);
        return views;
    }
}
