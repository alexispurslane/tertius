using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// SSR's depth pyramid with one owner, and the sets of its build (the level sets and the tail's set): made when the reflections or the screen-space light
/// read it (docs/plans/screen-space-illumination.md, decision 33). The image stays in the General layout for its whole
/// life. <see cref="RenderTargets"/> owns one when either reader is on.
/// </summary>
/// <remarks>
/// A Vulkan mip halves its image and rounds down. A level of the depth pyramid halves the screen and rounds up, so the
/// image is padded until each mip holds its level (<see cref="ImageExtent"/>), and its build fills the padding with the
/// empty bounds.
/// </remarks>
internal sealed unsafe class DepthPyramidImage : IDisposable
{
    /// <summary>The image's format: R the near bound, G the far bound, in 1/m. depth_pyramid.glsl reads rg.</summary>
    public const Format ImageFormat = Format.R32G32Sfloat;

    /// <summary>The longest side of a screen with a depth pyramid, in px: the sets are allocated for <see cref="DepthPyramid.MaxLevels"/> levels.</summary>
    public const int MaxSide = 1 << DepthPyramid.MaxLevels;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    /// <summary>The set of each level from 1 to <see cref="DepthPyramid.MaxLevels"/>, at index level − 1.</summary>
    private readonly DescriptorSet[] levelSets = new DescriptorSet[DepthPyramid.MaxLevels];

    // The view of each mip, which the level sets read and write. Destroyed before the image.
    private ImageView[] mipViews = [];

    /// <summary>Allocates the sets for the largest pyramid, and makes the image for a screen of <paramref name="extent"/>.</summary>
    /// <exception cref="NotSupportedException">
    /// The device cannot sample and store <see cref="ImageFormat"/>, or its tail binds more storage images in one stage
    /// than the device allows.
    /// </exception>
    public DepthPyramidImage(VulkanDevice device, DeviceAllocator allocator, DescriptorLayouts layouts, Extent2D extent)
    {
        // The tail binds more storage images in one stage than Vulkan guarantees.
        if (
            DepthPyramidTail.StorageImageProblem(device.Properties.Limits.MaxPerStageDescriptorStorageImages) is
            { } storageImages
        )
            throw new NotSupportedException($"{storageImages} ({device.DeviceName})");
        const FormatFeatureFlags needed = FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.StorageImageBit;
        if ((device.FormatProperties(ImageFormat).OptimalTilingFeatures & needed) != needed)
            throw new NotSupportedException($"{ImageFormat} cannot be sampled and stored on {device.DeviceName}.");
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        for (int i = 0; i < levelSets.Length; i++)
            levelSets[i] = layouts.AllocatePyramidLevelSet();
        TailSet = layouts.AllocateDepthTailSet();
        Create(extent);
    }

    /// <summary>The pyramid, RG32F: mip m holds level m + 1, for m from 0 to <see cref="TopLevel"/> − 1, padded.</summary>
    public GpuImage Image { get; private set; }

    /// <summary>The top level L, whose one cell covers the screen (<see cref="DepthPyramid.LevelCount"/>).</summary>
    public int TopLevel { get; private set; }

    /// <summary>Set 3 of <see cref="DescriptorLayouts.DepthTailLayout"/>, written when the image has a tail (<see cref="DepthPyramidTail"/>).</summary>
    public DescriptorSet TailSet { get; }

    /// <summary>Why the pyramid cannot cover a screen of <paramref name="width"/> by <paramref name="height"/> px, or null when it can.</summary>
    internal static string? ScreenProblem(long width, long height) =>
        Math.Max(width, height) > MaxSide
            ? $"The depth pyramid covers a screen of at most {MaxSide} px a side, not {width} × {height}."
            : null;

    /// <summary>
    /// The smallest extent whose Vulkan mip chain holds each level of the depth pyramid of a screen of
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

    /// <summary>
    /// Recreates the image for a screen of <paramref name="extent"/> and rewrites the level sets. The device must be
    /// idle. The sets of the readers point at the old image until their owners rewrite them.
    /// </summary>
    public void Resize(Extent2D extent)
    {
        DestroyImage();
        Create(extent);
    }

    /// <summary>Makes the image for a screen of <paramref name="extent"/> and writes the level sets in use.</summary>
    [MemberNotNull(nameof(Image))]
    private void Create(Extent2D extent)
    {
        TopLevel = DepthPyramid.LevelCount((int)extent.Width, (int)extent.Height);
        Image = GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: ImageExtent(extent),
            format: ImageFormat,
            // Built as a storage image, sampled by the build and the readers, copied out by the read hook.
            usage: ImageUsageFlags.SampledBit | ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: (uint)TopLevel,
            name: "depth pyramid"
        );
        mipViews = new ImageView[Image.MipLevels];
        for (uint mip = 0; mip < mipViews.Length; mip++)
            mipViews[mip] = Image.CreateLayerView(layer: 0, mipLevel: mip);

        // Level 1 reads the distance target from the G-buffer set, so its binding 0 stays unwritten.
        for (int level = 1; level <= TopLevel; level++)
        {
            if (level > 1)
                layouts.WriteImage(
                    set: levelSets[level - 1],
                    binding: 0,
                    view: mipViews[level - 2],
                    layout: ImageLayout.General
                );
            layouts.WriteStorageImage(set: levelSets[level - 1], binding: 1, view: mipViews[level - 1]);
        }

        WriteTailSet();
    }

    /// <summary>
    /// Writes the tail set: the mip before the tail, and the tail's mips from the first array element. The elements past
    /// the tail repeat its last mip: the shader never writes them, but each must hold a view.
    /// </summary>
    private void WriteTailSet()
    {
        int first = DepthPyramidTail.FirstMip(Image);
        if (first >= mipViews.Length)
            return;
        layouts.WriteImage(set: TailSet, binding: 0, view: mipViews[first - 1], layout: ImageLayout.General);
        for (int element = 0; element < DepthPyramidTail.Mips; element++)
            layouts.WriteStorageImage(
                set: TailSet,
                binding: 1,
                arrayElement: (uint)element,
                view: mipViews[Math.Min(first + element, mipViews.Length - 1)]
            );
    }

    /// <summary>Destroys the views and the image. The device must be idle.</summary>
    private void DestroyImage()
    {
        foreach (ImageView view in mipViews)
            device.Vk.DestroyImageView(device.Device, view, null);
        mipViews = [];
        Image.Dispose();
    }

    // The pool of the layouts frees the sets.
    public void Dispose() => DestroyImage();
}
