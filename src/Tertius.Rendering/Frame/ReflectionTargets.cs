using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The radiance pyramid of the screen-space reflections, the image of their trace (<see cref="Trace"/>), the radiance
/// pyramid's level sets and the trace's set, which reads the depth pyramid of <see cref="DepthPyramidImage"/> too
/// (docs/plans/screen-space-reflections.md, "Images" and "Descriptor sets"). The images stay in the General layout for
/// their whole life. The radiance pyramid has the screen's size and Vulkan's full mip chain. <see cref="RenderTargets"/>
/// owns one under <see cref="ConeTracedReflections"/>.
/// </summary>
internal sealed unsafe class ReflectionTargets : IDisposable
{
    /// <summary>The radiance pyramid's format: premultiplied pre-exposed radiance and the surface coverage.</summary>
    public const Format RadianceFormat = Format.R16G16B16A16Sfloat;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    /// <summary>The set of each radiance level from 0 to <see cref="DepthPyramid.MaxLevels"/>.</summary>
    private readonly DescriptorSet[] radianceLevelSets = new DescriptorSet[DepthPyramid.MaxLevels + 1];

    // The view of each mip of the radiance pyramid, which the level sets read and write. Destroyed before the image.
    private ImageView[] radianceMipViews = [];

    /// <summary>
    /// Allocates the sets for the largest pyramid, and makes the images for a screen of <paramref name="extent"/> and a
    /// trace at <paramref name="resolution"/>.
    /// </summary>
    /// <param name="depthPyramid">The depth pyramid of the same screen, which the trace reads.</param>
    public ReflectionTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent,
        GpuImage hdr,
        TraceResolution resolution,
        DepthPyramidImage depthPyramid
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        DepthPyramidImage = depthPyramid;
        for (int i = 0; i < radianceLevelSets.Length; i++)
            radianceLevelSets[i] = layouts.AllocatePyramidLevelSet();
        TraceSet = layouts.AllocateReflectionSet();
        Create(extent: extent, hdr: hdr, resolution: resolution);
    }

    /// <summary>The depth pyramid that the trace reads. <see cref="RenderTargets"/> owns it.</summary>
    public DepthPyramidImage DepthPyramidImage { get; }

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

    /// <summary>The radiance pyramid's top level, whose one texel covers the screen: its mip count − 1.</summary>
    public int RadianceTopLevel => (int)RadianceImage.MipLevels - 1;

    /// <summary>
    /// Set 3 of <see cref="DescriptorLayouts.ReflectionLayout"/>, written: both pyramids, the HDR image and, for a
    /// <see cref="HalfTrace"/>, its image at binding 4.
    /// </summary>
    public DescriptorSet TraceSet { get; }

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
    /// every set in use, the trace's with the depth pyramid, which must have that size already. The device must be idle.
    /// </summary>
    [MemberNotNull(nameof(RadianceImage))]
    internal void Create(Extent2D extent, GpuImage hdr, TraceResolution resolution)
    {
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
                GpuImage.Create(
                    device: device,
                    allocator: allocator,
                    extent: new((extent.Width + 1) / 2, (extent.Height + 1) / 2),
                    format: RadianceFormat,
                    usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
                    mipLevels: 1,
                    name: "reflection trace"
                )
            ),
        };
        radianceMipViews = MipViews(RadianceImage);
        WriteLevelSets(hdr);
        WriteTraceSet(hdr);
    }

    /// <summary>Writes the radiance pyramid's level sets over the mip views of <see cref="Create"/>.</summary>
    private void WriteLevelSets(GpuImage hdr)
    {
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
    /// Writes the reflection set over the depth pyramid, the radiance pyramid of <see cref="Create"/>, the HDR image
    /// <paramref name="hdr"/> and the image of a <see cref="HalfTrace"/>.
    /// </summary>
    private void WriteTraceSet(GpuImage hdr)
    {
        // The bindings of DescriptorLayouts.ReflectionLayout; binding 2 is the immutable sampler.
        layouts.WriteImage(set: TraceSet, binding: 0, view: DepthPyramidImage.Image.View, layout: ImageLayout.General);
        layouts.WriteImage(set: TraceSet, binding: 1, view: RadianceImage.View, layout: ImageLayout.General);
        layouts.WriteStorageImage(set: TraceSet, binding: 3, image: hdr);
        switch (Trace)
        {
            case HalfTrace half:
                layouts.WriteStorageImage(set: TraceSet, binding: 4, image: half.Traced);
                break;
            case FullTrace:
                // The full-resolution trace writes the HDR image only, so binding 4 stays unwritten.
                break;
            case null:
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>Destroys the views and the images, but not the depth pyramid. Safe to call twice. The device must be idle.</summary>
    internal void DestroyImages()
    {
        foreach (ImageView view in radianceMipViews)
            device.Vk.DestroyImageView(device.Device, view, null);
        radianceMipViews = [];
        RadianceImage.Dispose();
        switch (Trace)
        {
            case HalfTrace half:
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

    /// <summary>A pyramid image: built as a storage image, sampled by the build and the trace, copied out by the read hooks.</summary>
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

    private static ImageView[] MipViews(GpuImage image)
    {
        var views = new ImageView[image.MipLevels];
        for (uint mip = 0; mip < views.Length; mip++)
            views[mip] = image.CreateLayerView(layer: 0, mipLevel: mip);
        return views;
    }
}
