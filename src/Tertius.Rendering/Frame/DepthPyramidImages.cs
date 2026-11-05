using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The depth pyramid of the screen tracer and its sets (docs/plans/temporal-renderer.md, section 6, "The traversal"):
/// the min-max bounds of levels 1 to L, and the finest level, each pixel's center w (decision 46). The images stay in the
/// General layout for their whole life. <see cref="RenderTargets"/> owns one under <see cref="ScreenTrace"/>, and only
/// <see cref="DepthPyramidPass"/> writes it.
/// </summary>
/// <remarks>
/// A Vulkan mip halves its image and rounds down. A level of the depth pyramid halves the screen and rounds up, so the
/// depth image is padded until each mip holds its level (<see cref="DepthImageExtent"/>), and its builds fill the padding
/// with the empty bounds.
/// </remarks>
internal sealed unsafe class DepthPyramidImages : IDisposable
{
    /// <summary>The depth pyramid's format: R the near bound, G the far bound, in 1/m. depth_pyramid.glsl declares rg32f.</summary>
    public const Format DepthFormat = Format.R32G32Sfloat;

    /// <summary>The longest side of a screen with the depth pyramid, in px: the sets are allocated for <see cref="DepthPyramid.MaxLevels"/> levels.</summary>
    public const int MaxSide = 1 << DepthPyramid.MaxLevels;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    /// <summary>The set of each depth level from 1 to <see cref="DepthPyramid.MaxLevels"/>, at index level − 1.</summary>
    private readonly DescriptorSet[] depthLevelSets = new DescriptorSet[DepthPyramid.MaxLevels];

    // The view of each mip of the depth image, which the level sets read and write. Destroyed before the image.
    private ImageView[] depthMipViews = [];

    /// <summary>Allocates the sets for the largest pyramid, and makes the images for a screen of <paramref name="extent"/>.</summary>
    public DepthPyramidImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        for (int i = 0; i < depthLevelSets.Length; i++)
            depthLevelSets[i] = layouts.AllocatePyramidLevelSet();
        DepthTailSet = layouts.AllocateDepthTailSet();
        DepthFinestSet = layouts.AllocatePyramidLevelSet();
        Create(extent);
    }

    /// <summary>The depth pyramid image, RG32F: mip m holds level m + 1, for m from 0 to <see cref="TopLevel"/> − 1, padded.</summary>
    public GpuImage DepthImage { get; private set; }

    /// <summary>
    /// The depth pyramid's finest level, R32F at the screen's size: the inverse view depth w of each pixel's center, 0
    /// for sky (docs/plans/temporal-renderer.md, decision 46). Only <see cref="DepthPyramidPass"/> writes it.
    /// </summary>
    public GpuImage DepthFinest { get; private set; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.PyramidLayout"/> for the finest level's dispatch: its image at
    /// binding 1, as a level set holds its level.</summary>
    public DescriptorSet DepthFinestSet { get; }

    /// <summary>The depth pyramid's top level L, whose one cell covers the screen (<see cref="DepthPyramid.LevelCount"/>).</summary>
    public int TopLevel { get; private set; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.DepthTailLayout"/>, written when the depth image has a tail (<see cref="DepthPyramidTail"/>).</summary>
    public DescriptorSet DepthTailSet { get; }

    /// <summary>Why the pyramid cannot cover a screen of <paramref name="width"/> by <paramref name="height"/> px, or null when it can.</summary>
    internal static string? ScreenProblem(long width, long height) =>
        Math.Max(width, height) > MaxSide
            ? $"The screen tracer covers a screen of at most {MaxSide} px a side, not {width} × {height}."
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

    /// <summary>Makes the images for a screen of <paramref name="extent"/>, and writes every set in use. The device must be idle.</summary>
    [MemberNotNull(nameof(DepthImage), nameof(DepthFinest))]
    internal void Create(Extent2D extent)
    {
        TopLevel = DepthPyramid.LevelCount((int)extent.Width, (int)extent.Height);
        DepthImage = CreateImage(
            extent: DepthImageExtent(extent),
            format: DepthFormat,
            mipLevels: (uint)TopLevel,
            name: "depth pyramid"
        );
        DepthFinest = CreateImage(extent: extent, format: Format.R32Sfloat, mipLevels: 1, name: "depth pyramid finest");
        depthMipViews = MipViews(DepthImage);
        WriteLevelSets();
        layouts.WriteStorageImage(set: DepthFinestSet, binding: 1, image: DepthFinest);
        WriteDepthTailSet();
    }

    /// <summary>Writes the level sets over the mip views of <see cref="Create"/>.</summary>
    private void WriteLevelSets()
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
    }

    /// <summary>
    /// Destroys the views and the images, which <see cref="Create"/> makes again. Safe to call twice. The device must be
    /// idle. The pool of the layouts frees the sets.
    /// </summary>
    public void Dispose()
    {
        foreach (ImageView view in depthMipViews)
            device.Vk.DestroyImageView(device.Device, view, null);
        depthMipViews = [];
        DepthImage.Dispose();
        DepthFinest.Dispose();
    }

    /// <summary>A pyramid image: built as a storage image, sampled by the builds and the tracer, copied out by the read hooks.</summary>
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
