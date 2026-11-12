using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The images and the tile records of key and fill (docs/plans/light-pairs.md, "Key and fill"), under
/// <see cref="KeyAndFill"/>: the candidate images that the corner pass draws, one texel per corner of the tile grid and
/// group of lights, and the buffer of one record per tile that the tile pass writes and the direct pass reads, at
/// <see cref="TilesBinding"/> of the G-buffer set. All have the size of the targets' largest render size. A frame of a
/// smaller render size uses the first texels and tiles of each: its copies of the corner grid and its tiles count from
/// the frame's own size (Shaders/key_fill.glsl).
/// </summary>
internal sealed unsafe class KeyFillImages : IDisposable
{
    /// <summary>The binding of the tile records in the G-buffer set.</summary>
    public const uint TilesBinding = 7;

    /// <summary>
    /// The bytes of one tile's record, TileWords words in key_fill.glsl: the four corners' distances, their normals, the
    /// entry of no light before the list, two words per entry, the word that ends a full list, and one unused word.
    /// </summary>
    public const ulong TileBytes = 4 * (4 + 4 + 2 + 2 * KeyAndFill.TileEntries + 1 + 1);

    /// <summary>
    /// The format of each candidate image: four words per texel, each a key or the bits of a float. Vulkan requires it
    /// as a color attachment and as a sampled image on every device.
    /// </summary>
    private const Format CandidateFormat = Format.R32G32B32A32Uint;

    /// <summary>The candidate images: the outputs of Shaders/key_fill_corners.frag.</summary>
    private const int CandidateImages = 4;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;

    public KeyFillImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets,
        KeyAndFill setting
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        Setting = setting;
        Set = layouts.AllocateKeyFillTilesSet();
        Create(targets);
    }

    /// <summary>The setting that the renderer was made with.</summary>
    public KeyAndFill Setting { get; }

    /// <summary>
    /// The candidates of each corner and group of lights, in the order of the outputs of Shaders/key_fill_corners.frag and
    /// of the bindings of <see cref="Set"/>: the losses of the first four candidates, their keys, the rest (the fifth
    /// candidate and the group's part of the corner's whole light), and the corner's surface (its distance and its
    /// normal).
    /// </summary>
    public GpuImage[] Candidates { get; } = new GpuImage[CandidateImages];

    /// <summary>The tile records, <see cref="TileBytes"/> each, in rows of the frame's tiles.</summary>
    public GpuBuffer Tiles { get; private set; }

    /// <summary>The set of <see cref="DescriptorLayouts.KeyFillTilesLayout"/>: the candidate images and the tile records.</summary>
    public DescriptorSet Set { get; }

    /// <summary>The tiles of a render size of <paramref name="extent"/> along each axis. The corners are one more.</summary>
    public static Extent2D TilesOf(Extent2D extent) =>
        new(
            (extent.Width + KeyAndFill.TileSize - 1) / KeyAndFill.TileSize,
            (extent.Height + KeyAndFill.TileSize - 1) / KeyAndFill.TileSize
        );

    /// <summary>
    /// The texels that the corner pass draws for a render size of <paramref name="extent"/>, along each axis:
    /// <see cref="KeyAndFill.GroupSide"/> copies of the corner grid, whose corners are one more than the tiles.
    /// </summary>
    public static Extent2D CandidatesOf(Extent2D extent)
    {
        Extent2D tiles = TilesOf(extent);
        return new((tiles.Width + 1) * KeyAndFill.GroupSide, (tiles.Height + 1) * KeyAndFill.GroupSide);
    }

    /// <summary>
    /// Recreates the images and the records at the targets' size and rewrites both sets. The device must be idle, and the
    /// targets resized first.
    /// </summary>
    public void Resize(RenderTargets targets)
    {
        Dispose();
        Create(targets);
    }

    [MemberNotNull(nameof(Tiles))]
    private void Create(RenderTargets targets)
    {
        Extent2D tiles = TilesOf(targets.Extent);
        for (int image = 0; image < Candidates.Length; image++)
        {
            Candidates[image] = GpuImage.Create(
                device: device,
                allocator: allocator,
                extent: CandidatesOf(targets.Extent),
                format: CandidateFormat,
                usage: ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                mipLevels: 1,
                name: $"key fill candidates {image}"
            );
            layouts.WriteImage(set: Set, binding: (uint)image, arrayElement: 0, image: Candidates[image]);
        }

        Tiles = GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: (ulong)tiles.Width * tiles.Height * TileBytes,
            usage: BufferUsageFlags.StorageBufferBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "key fill tiles"
        );
        layouts.WriteBuffer(
            set: Set,
            binding: CandidateImages,
            type: DescriptorType.StorageBuffer,
            buffer: Tiles
        );
        layouts.WriteBuffer(
            set: targets.GBufferSet,
            binding: TilesBinding,
            type: DescriptorType.StorageBuffer,
            buffer: Tiles
        );
    }

    /// <summary>Destroys the images and the records. The pool of the layouts frees the set.</summary>
    public void Dispose()
    {
        foreach (GpuImage image in Candidates)
            image.Dispose();
        Tiles.Dispose();
    }
}
