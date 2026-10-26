using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The radiance pyramid of the screen-space reflections, its level sets and the trace's set, which reads the depth pyramid
/// of <see cref="DepthPyramidImage"/> too (docs/plans/screen-space-reflections.md, "Images" and "Descriptor sets"). The
/// radiance pyramid stays in the General layout for its whole life, and has the screen's size and Vulkan's full mip
/// chain. <see cref="RenderTargets"/> owns one under <see cref="ConeTracedReflections"/>.
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
    /// Allocates the sets for the largest pyramid, and makes the radiance pyramid for a screen of
    /// <paramref name="extent"/>.
    /// </summary>
    /// <param name="depthPyramid">The depth pyramid of the same screen, which the trace reads.</param>
    public ReflectionTargets(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent,
        GpuImage hdr,
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
        Create(extent, hdr);
    }

    /// <summary>The depth pyramid that the trace reads. <see cref="RenderTargets"/> owns it.</summary>
    public DepthPyramidImage DepthPyramidImage { get; }

    /// <summary>The radiance pyramid, RGBA16F: mip k holds level k, from the screen's size to 1 × 1.</summary>
    public GpuImage RadianceImage { get; private set; }

    /// <summary>The radiance pyramid's top level, whose one texel covers the screen: its mip count − 1.</summary>
    public int RadianceTopLevel => (int)RadianceImage.MipLevels - 1;

    /// <summary>Set 3 of <see cref="DescriptorLayouts.ReflectionLayout"/>, written: both pyramids and the HDR image.</summary>
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
    /// Makes the radiance pyramid for a screen of <paramref name="extent"/> and writes every set in use, the trace's with
    /// the depth pyramid, which must have that size already. The device must be idle.
    /// </summary>
    [MemberNotNull(nameof(RadianceImage))]
    internal void Create(Extent2D extent, GpuImage hdr)
    {
        RadianceImage = CreateImage(
            extent: extent,
            format: RadianceFormat,
            mipLevels: (uint)BitOperations.Log2(Math.Max(extent.Width, extent.Height)) + 1,
            name: "radiance pyramid"
        );
        radianceMipViews = MipViews(RadianceImage);

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

        // The bindings of reflection_trace.comp; binding 2 is the immutable sampler.
        layouts.WriteImage(set: TraceSet, binding: 0, view: DepthPyramidImage.Image.View, layout: ImageLayout.General);
        layouts.WriteImage(set: TraceSet, binding: 1, view: RadianceImage.View, layout: ImageLayout.General);
        layouts.WriteStorageImage(set: TraceSet, binding: 3, image: hdr);
    }

    /// <summary>Destroys the views and the radiance pyramid. Safe to call twice. The device must be idle.</summary>
    internal void DestroyImages()
    {
        foreach (ImageView view in radianceMipViews)
            device.Vk.DestroyImageView(device.Device, view, null);
        radianceMipViews = [];
        RadianceImage.Dispose();
    }

    // The pool of the layouts frees the sets.
    public void Dispose() => DestroyImages();

    /// <summary>A pyramid image: built as a storage image, sampled by the build and the trace, copied out by the read hook.</summary>
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
