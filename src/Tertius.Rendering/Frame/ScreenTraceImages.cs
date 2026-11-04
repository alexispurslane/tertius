using System.Diagnostics.CodeAnalysis;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The images and the lists of the screen tracer (docs/plans/temporal-renderer.md, section 6, "The resolve"): the ray
/// plan's bits at each pixel, the records of each 2 × 2 block's ray and of each pixel's own ray of each kind, and the
/// own-ray list of each kind. Its two sets, one per parity of the drawn frames, also bind what the tracer reads: the depth
/// pyramid's finest level and its bounds, the direct light's two images, the surface test image, the motion target and the
/// two signals' images of that parity (<see cref="TemporalSignal.HitImages"/>), whose samples the resolve writes. Every
/// image holds the render targets' size, the block records a quarter of it, and each stays in the General layout.
/// </summary>
/// <remarks>
/// The depth pyramid's images still belong to <see cref="ReflectionTargets"/>, which builds them for the reflections and
/// for the tracer alike, until the reflections go (ter-gayt, part 6).
/// </remarks>
internal sealed class ScreenTraceImages : IDisposable
{
    /// <summary>The format of the plan's bits: bit 0 an own reflection ray, bit 1 an own diffuse ray (screen_trace.glsl).</summary>
    public const Format PlanFormat = Format.R8Uint;

    /// <summary>The format of a record's light: rgb the ray's light, a its hit distance.</summary>
    public const Format LightFormat = Format.R16G16B16A16Sfloat;

    /// <summary>The format of a record's ray: the direction's octahedral code, the half bits of log2 of its density, and RayTraced.</summary>
    public const Format RayFormat = Format.R16G16B16A16Uint;

    /// <summary>The two ray kinds, which are the layers of the record images: 0 reflection rays, 1 diffuse rays.</summary>
    public const int Kinds = 2;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly DescriptorLayouts layouts;
    private readonly RenderTargets targets;
    private readonly SurfaceHistory surfaces;
    private readonly ReflectionTargets pyramids;
    private readonly TemporalSignal diffuse;
    private readonly TemporalSignal specular;

    /// <param name="pyramids">The owner of the depth pyramid and its finest level.</param>
    /// <param name="diffuse">The diffuse signal, of one part, whose sample the resolve writes.</param>
    /// <param name="specular">The specular signal, of one part.</param>
    public ScreenTraceImages(
        VulkanDevice device,
        DeviceAllocator allocator,
        DescriptorLayouts layouts,
        RenderTargets targets,
        SurfaceHistory surfaces,
        ReflectionTargets pyramids,
        TemporalSignal diffuse,
        TemporalSignal specular
    )
    {
        this.device = device;
        this.allocator = allocator;
        this.layouts = layouts;
        this.targets = targets;
        this.surfaces = surfaces;
        this.pyramids = pyramids;
        this.diffuse = diffuse;
        this.specular = specular;
        Sets = [layouts.AllocateScreenTraceSet(), layouts.AllocateScreenTraceSet()];
        Create();
    }

    /// <summary>The plan's bits at each pixel.</summary>
    public GpuImage Plan { get; private set; }

    /// <summary>The light of each block's ray, one layer per kind.</summary>
    public GpuImage BlockLight { get; private set; }

    /// <summary>The direction, density and RayTraced of each block's ray, one layer per kind.</summary>
    public GpuImage BlockRay { get; private set; }

    /// <summary>The light of each pixel's own ray, one layer per kind.</summary>
    public GpuImage OwnLight { get; private set; }

    /// <summary>The direction, density and RayTraced of each pixel's own ray, one layer per kind.</summary>
    public GpuImage OwnRay { get; private set; }

    /// <summary>The own-ray list of each kind, in the layout of <see cref="OwnRayList"/>.</summary>
    public GpuBuffer[] OwnRays { get; private set; } = [];

    /// <summary>The tracer set of each parity of the drawn frames: the one that a draw of that parity binds.</summary>
    public DescriptorSet[] Sets { get; }

    /// <summary>The surface test image, whose pass bits a hit's history and the plan's lengths read.</summary>
    public GpuImage SurfaceTest => surfaces.Test;

    /// <summary>The G-buffer's motion target.</summary>
    public GpuImage Motion => targets.Motion;

    /// <summary>The diffuse signal.</summary>
    public TemporalSignal Diffuse => diffuse;

    /// <summary>The specular signal.</summary>
    public TemporalSignal Specular => specular;

    /// <summary>The images that the tracer writes and reads again: the plan and the four record images.</summary>
    public IEnumerable<GpuImage> Records => [Plan, BlockLight, BlockRay, OwnLight, OwnRay];

    /// <summary>
    /// Recreates the images and the lists at the targets' size and rewrites both sets. The device must be idle, and the
    /// targets, the surface history, the pyramids and both signals resize first, because the sets point at their images.
    /// </summary>
    public void Resize()
    {
        DestroyImages();
        Create();
    }

    [MemberNotNull(nameof(Plan), nameof(BlockLight), nameof(BlockRay), nameof(OwnLight), nameof(OwnRay))]
    private void Create()
    {
        Extent2D extent = targets.Extent;
        var blocks = new Extent2D((extent.Width + 1) / 2, (extent.Height + 1) / 2);
        Plan = CreateImage(extent: extent, format: PlanFormat, layers: 1, name: "screen trace plan");
        BlockLight = CreateImage(extent: blocks, format: LightFormat, layers: Kinds, name: "screen trace block light");
        BlockRay = CreateImage(extent: blocks, format: RayFormat, layers: Kinds, name: "screen trace block rays");
        OwnLight = CreateImage(extent: extent, format: LightFormat, layers: Kinds, name: "screen trace own light");
        OwnRay = CreateImage(extent: extent, format: RayFormat, layers: Kinds, name: "screen trace own rays");
        OwnRays = [CreateOwnRayList(extent, "reflection"), CreateOwnRayList(extent, "diffuse")];
        for (int parity = 0; parity < Sets.Length; parity++)
            WriteSet(parity);
    }

    /// <summary>Writes the set of <paramref name="parity"/> in the bindings of screen_trace.glsl.</summary>
    private void WriteSet(int parity)
    {
        DescriptorSet set = Sets[parity];
        (GpuImage diffuseSample, GpuImage lastFastDiffuse, GpuImage slowDiffuse) = diffuse.HitImages(part: 0, parity);
        (GpuImage specularSample, GpuImage lastFastSpecular, GpuImage slowSpecular) = specular.HitImages(part: 0, parity);
        layouts.WriteImage(set: set, binding: 0, view: pyramids.DepthFinest.View, layout: ImageLayout.General);
        layouts.WriteImage(set: set, binding: 1, view: pyramids.DepthImage.View, layout: ImageLayout.General);
        GpuImage[] storage =
        [
            targets.DirectDiffuse,
            targets.DirectSpecular,
            surfaces.Test,
            targets.Motion,
            lastFastDiffuse,
            lastFastSpecular,
            slowDiffuse,
            slowSpecular,
            Plan,
            BlockLight,
            BlockRay,
            OwnLight,
            OwnRay,
            diffuseSample,
            specularSample,
        ];
        for (int image = 0; image < storage.Length; image++)
            layouts.WriteStorageImage(set: set, binding: (uint)(2 + image), image: storage[image]);
        for (int kind = 0; kind < Kinds; kind++)
            layouts.WriteBuffer(
                set: set,
                binding: (uint)(2 + storage.Length + kind),
                type: DescriptorType.StorageBuffer,
                buffer: OwnRays[kind]
            );
    }

    /// <summary>A record image: written and read as a storage image, copied out by the read hooks.</summary>
    private GpuImage CreateImage(Extent2D extent, Format format, uint layers, string name) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: extent,
            format: format,
            usage: ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit,
            mipLevels: 1,
            name: name,
            arrayLayers: layers,
            arrayView: layers > 1
        );

    /// <summary>The own-ray list of a kind: appended by the plan, read by the indirect dispatch, cleared and read back by transfers.</summary>
    private GpuBuffer CreateOwnRayList(Extent2D screen, string kind) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: OwnRayList.Bytes(screen),
            usage: BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.IndirectBufferBit
                | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: $"screen trace {kind} own rays"
        );

    /// <summary>Destroys the images and the lists. The device must be idle.</summary>
    private void DestroyImages()
    {
        foreach (GpuImage image in Records)
            image.Dispose();
        foreach (GpuBuffer list in OwnRays)
            list.Dispose();
        OwnRays = [];
    }

    public void Dispose() => DestroyImages();
}
