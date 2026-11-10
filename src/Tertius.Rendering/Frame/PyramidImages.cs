using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// A pyramid of the screen tracer and its sets: the finest level, one R32F value per pixel, and levels 1 to L, each cell
/// from the up to four cells under it. The depth pyramid holds min-max bounds (docs/plans/temporal-renderer.md, section 6,
/// "The traversal"), the light pyramid sums (ARCHITECTURE.md, "Guided diffuse rays"). The images stay in General.
/// </summary>
/// <remarks>
/// A Vulkan mip halves its image and rounds down. A level halves the screen and rounds up, so the levels image is padded
/// until each mip holds its level (<see cref="ImageExtent"/>), and its builds fill the padding with the empty cell.
/// </remarks>
internal sealed unsafe class PyramidImages : IDisposable
{
    /// <summary>The depth pyramid's format: R the near bound, G the far bound, in 1/m. depth_pyramid.glsl declares rg32f.</summary>
    public const Format DepthFormat = Format.R32G32Sfloat;

    /// <summary>The light pyramid's format: the sum of the guide light under a cell. light_pyramid.glsl declares r32f.</summary>
    public const Format LightFormat = Format.R32Sfloat;

    /// <summary>The longest side of a screen with the pyramids, in px: the sets are allocated for <see cref="DepthPyramid.MaxLevels"/> levels.</summary>
    public const int MaxSide = 1 << DepthPyramid.MaxLevels;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly Format format;
    private readonly string name;

    /// <summary>The set of each level from 1 to <see cref="DepthPyramid.MaxLevels"/>, at index level − 1.</summary>
    private readonly DescriptorSet[] levelSets = new DescriptorSet[DepthPyramid.MaxLevels];

    // The view of each mip of the levels image, which the level sets read and write. Destroyed before the image.
    private ImageView[] mipViews = [];

    /// <summary>Allocates the sets for the largest pyramid, and makes the images for a screen of <paramref name="extent"/>.</summary>
    /// <param name="format">The levels' format: <see cref="DepthFormat"/> or <see cref="LightFormat"/>.</param>
    /// <param name="name">The pyramid's name in its images' names, such as "depth pyramid".</param>
    public PyramidImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        Extent2D extent,
        Format format,
        string name
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.format = format;
        this.name = name;
        for (int i = 0; i < levelSets.Length; i++)
            levelSets[i] = layouts.AllocatePyramidLevelSet();
        TailSet = layouts.AllocatePyramidTailSet();
        FinestSet = layouts.AllocatePyramidLevelSet();
        Create(extent);
    }

    /// <summary>The levels image: mip m holds level m + 1, for m from 0 to <see cref="TopLevel"/> − 1, padded.</summary>
    public GpuImage Image { get; private set; }

    /// <summary>
    /// The finest level, R32F at the screen's size: of the depth pyramid the inverse view depth w of each pixel's center,
    /// 0 for sky (docs/plans/temporal-renderer.md, decision 46), of the light pyramid each pixel's guide light.
    /// </summary>
    public GpuImage Finest { get; private set; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.PyramidLayout"/> for the finest level's dispatch: its image at
    /// binding 1, as a level set holds its level. Binding 0 holds the image that the finest shader reads: the light
    /// pyramid's owner, <see cref="ScreenTraceImages"/>, writes the direct diffuse light there, and the depth pyramid's
    /// finest shader reads the G-buffer alone, so its binding 0 stays unwritten.</summary>
    public DescriptorSet FinestSet { get; }

    /// <summary>The top level L, whose one cell covers the screen (<see cref="DepthPyramid.LevelCount"/>).</summary>
    public int TopLevel { get; private set; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.PyramidTailLayout"/>, written when the levels image has a tail (<see cref="PyramidTail"/>).</summary>
    public DescriptorSet TailSet { get; }

    /// <summary>Why the pyramid cannot cover a screen of <paramref name="width"/> by <paramref name="height"/> px, or null when it can.</summary>
    internal static string? ScreenProblem(long width, long height) =>
        Math.Max(width, height) > MaxSide
            ? $"The screen tracer covers a screen of at most {MaxSide} px a side, not {width} × {height}."
            : null;

    /// <summary>
    /// The smallest extent whose Vulkan mip chain holds each level of the pyramid of a screen of
    /// <paramref name="screen"/>: mip m at least as large as level m + 1 on each axis, with one mip per level.
    /// </summary>
    internal static Extent2D ImageExtent(Extent2D screen)
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

    /// <summary>The set of the dispatch that writes <paramref name="level"/>, from 1 to <see cref="TopLevel"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is outside 1 to <see cref="TopLevel"/>.</exception>
    public DescriptorSet LevelSet(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, TopLevel);
        return levelSets[level - 1];
    }

    /// <summary>Makes the images for a screen of <paramref name="extent"/>, and writes every set in use. The device must be idle.</summary>
    [MemberNotNull(nameof(Image), nameof(Finest))]
    internal void Create(Extent2D extent)
    {
        TopLevel = DepthPyramid.LevelCount((int)extent.Width, (int)extent.Height);
        Image = CreateImage(extent: ImageExtent(extent), format: format, mipLevels: (uint)TopLevel, name: name);
        Finest = CreateImage(extent: extent, format: Format.R32Sfloat, mipLevels: 1, name: $"{name} finest");
        mipViews = MipViews(Image);
        WriteLevelSets();
        layouts.WriteStorageImage(set: FinestSet, binding: 1, image: Finest);
        WriteTailSet();
    }

    /// <summary>Writes the level sets over the mip views of <see cref="Create"/>.</summary>
    private void WriteLevelSets()
    {
        // Level 1 reads the finest level. The depth pyramid's level 1 leaves it unread: it reads the distance target.
        for (int level = 1; level <= TopLevel; level++)
        {
            layouts.WriteImage(
                set: levelSets[level - 1],
                binding: 0,
                view: level > 1 ? mipViews[level - 2] : Finest.View,
                layout: ImageLayout.General
            );
            layouts.WriteStorageImage(set: levelSets[level - 1], binding: 1, view: mipViews[level - 1]);
        }
    }

    /// <summary>
    /// Destroys the views and the images, which <see cref="Create"/> makes again. Safe to call twice. The device must be
    /// idle. The pool of the layouts frees the sets.
    /// </summary>
    public void Dispose()
    {
        foreach (ImageView view in mipViews)
            device.Vk.DestroyImageView(device.Device, view, null);
        mipViews = [];
        Image.Dispose();
        Finest.Dispose();
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
    /// Writes the tail set: the mip before the tail, and the tail's mips from the first array element. The elements past
    /// the tail repeat its last mip: the shader never writes them, but each must hold a view.
    /// </summary>
    private void WriteTailSet()
    {
        int first = PyramidTail.FirstMip(Image);
        if (first >= mipViews.Length)
            return;
        layouts.WriteImage(set: TailSet, binding: 0, view: mipViews[first - 1], layout: ImageLayout.General);
        for (int element = 0; element < PyramidTail.Mips; element++)
            layouts.WriteStorageImage(
                set: TailSet,
                binding: 1,
                arrayElement: (uint)element,
                view: mipViews[Math.Min(first + element, mipViews.Length - 1)]
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
