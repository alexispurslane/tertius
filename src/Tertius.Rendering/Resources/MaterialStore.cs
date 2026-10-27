using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// What the batcher needs of a material slot: how its alpha draws, and whether the passes that shade draw its back faces
/// (docs/plans/skin-and-hair.md, "Both sides of a hair card").
/// </summary>
internal readonly record struct MaterialInfo(AlphaMode Alpha, bool TwoSided);

/// <summary>
/// All materials in one device-local storage buffer. Slot <see cref="DefaultSlot"/> holds the default material
/// (a white non-metal of roughness 0.5 and reflectance 0.5, the default textures and no normal map) that
/// <see cref="MaterialId.None"/> and ids this renderer did not hand out resolve to. Material n from <see cref="VulkanRenderer.CreateMaterial"/> lives in slot
/// n + <see cref="BuiltInCount"/>, so no <see cref="MaterialId"/> names the default and ids start at 0.
/// The buffer grows by doubling like the geometry buffers.
/// </summary>
internal sealed unsafe class MaterialStore : IDisposable
{
    /// <summary>The slot of the default material.</summary>
    public const uint DefaultSlot = 0;

    /// <summary>The slots the default material takes, before the first material that the renderer hands out.</summary>
    public const int BuiltInCount = 1;

    private const int InitialCapacity = 256;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;

    // One per slot on the GPU, written with the slot's row from one MaterialDesc, so the two never disagree.
    private readonly List<MaterialInfo> infos = [];
    private int capacity;

    public MaterialStore(VulkanDevice device, DeviceAllocator allocator)
    {
        this.device = device;
        this.allocator = allocator;
        capacity = InitialCapacity;
        Buffer = Create(capacity);
    }

    public GpuBuffer Buffer { get; private set; }

    /// <summary>Changes whenever <see cref="Buffer"/> is replaced, so descriptor sets know to rewrite it.</summary>
    public int Version { get; private set; }

    /// <summary>Slots on the GPU, the default material included.</summary>
    public int Count { get; private set; }

    /// <summary>The CPU's facts of each slot on the GPU, the default material's first.</summary>
    public ReadOnlySpan<MaterialInfo> Infos => CollectionsMarshal.AsSpan(infos);

    /// <summary>Materials from <see cref="VulkanRenderer.CreateMaterial"/> on the GPU.</summary>
    public int UserCount => Math.Max(Count - BuiltInCount, 0);

    /// <summary>The buffer slot of a material that the renderer handed out.</summary>
    public static uint Slot(MaterialId material) => (uint)(material.Value + BuiltInCount);

    /// <summary>
    /// The buffer slot an instance draws with, given <paramref name="count"/> slots on the GPU: the material's own,
    /// or <see cref="DefaultSlot"/> for <see cref="MaterialId.None"/> and for ids that are not on the GPU, such as
    /// one from another renderer.
    /// </summary>
    public static uint Resolve(MaterialId material, int count)
    {
        if (material.IsNone || material.Value < 0)
            return DefaultSlot;
        long slot = (long)material.Value + BuiltInCount;
        return slot < count ? (uint)slot : DefaultSlot;
    }

    public static GpuMaterial ToGpu(MaterialDesc desc) =>
        new()
        {
            // The colors and the factors reach the shaders in their ranges (ShaderInput), with a NaN as 0.
            BaseColor = ShaderInput.Fraction(desc.BaseColor),
            Emissive = ShaderInput.Radiance(desc.Emissive),
            Metallic = ShaderInput.Fraction(MetallicOf(desc.Model)),
            Roughness = ShaderInput.Fraction(desc.Roughness),
            Reflectance = ShaderInput.Fraction(desc.Reflectance),
            BaseColorMap = TextureStore.Resolve(desc.BaseColorMap, TextureStore.White),
            NormalMap = desc.NormalMap.IsNone ? GpuMaterial.NoMap : (uint)TextureStore.Slot(desc.NormalMap),
            MetallicRoughnessMap = TextureStore.Resolve(
                desc.MetallicRoughnessMap,
                TextureStore.DefaultMetallicRoughness
            ),
            Shading = ShadingCode.Word(desc.Model),
            AlphaCutoff = desc.Alpha.Cutoff ?? 0,
        };

    /// <summary>The metallic of a standard model, and 0 for skin and hair, which have none (docs/plans/skin-and-hair.md, decision 6).</summary>
    private static float MetallicOf(ShadingModel model) =>
        model switch
        {
            StandardShading(var metallic) => metallic,
            SkinShading or HairShading => 0,
        };

    /// <summary>
    /// Whether the passes that shade draw both sides of a surface of <paramref name="model"/>: a hair card is a sheet of
    /// strands with no back, and every other surface is solid, so its back faces are hidden (docs/plans/skin-and-hair.md,
    /// "Both sides of a hair card").
    /// </summary>
    internal static bool DrawsBothSides(ShadingModel model) =>
        model switch
        {
            HairShading => true,
            StandardShading or SkinShading => false,
        };

    /// <summary>Records the upload of queued materials, creating the default on the first call.</summary>
    public void Upload(
        CommandBuffer commandBuffer,
        List<(MaterialId Id, MaterialDesc Desc)> pending,
        StagingArena staging,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        int first = Count;
        int added = pending.Count + (Count == 0 ? 1 : 0);
        if (added == 0)
            return;

        if (Count + added > capacity)
        {
            int grown = capacity;
            while (grown < Count + added)
                grown *= 2;
            GpuBuffer replacement = Create(grown);
            if (Count > 0)
            {
                barriers.Buffer(Buffer, BufferUse.TransferSource);
                barriers.Buffer(replacement, BufferUse.TransferDestination);
                barriers.Flush(device.Vk, commandBuffer);
                var copy = new BufferCopy(0, 0, (ulong)Count * (ulong)sizeof(GpuMaterial));
                device.Vk.CmdCopyBuffer(
                    commandBuffer: commandBuffer,
                    srcBuffer: Buffer.Handle,
                    dstBuffer: replacement.Handle,
                    regionCount: 1,
                    pRegions: &copy
                );
            }

            releases.Retire(Buffer, frame);
            Buffer = replacement;
            capacity = grown;
            Version++;
        }

        StagingSlice stage = staging.Allocate((ulong)added * (ulong)sizeof(GpuMaterial));
        Span<GpuMaterial> staged = MemoryMarshal.Cast<byte, GpuMaterial>(stage.Bytes);
        int cursor = 0;
        if (Count == 0)
            Stage(staged, ref cursor, new(BaseColor: Vector4.One, Roughness: 0.5f));
        foreach ((MaterialId id, MaterialDesc desc) in pending)
        {
            // The upload queue hands out dense ids under one lock and drains them in order, so each id names the next
            // slot. If that broke, an instance would draw with another material.
            Contract.Assert(Slot(id) == (uint)(first + cursor));
            Stage(staged, ref cursor, desc);
        }

        barriers.Buffer(Buffer, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(stage.Offset, (ulong)first * (ulong)sizeof(GpuMaterial), stage.Size);
        device.Vk.CmdCopyBuffer(
            commandBuffer: commandBuffer,
            srcBuffer: stage.Buffer.Handle,
            dstBuffer: Buffer.Handle,
            regionCount: 1,
            pRegions: &region
        );
        Count += added;
        Contract.Assert(infos.Count == Count);
    }

    /// <summary>Writes the GPU row of <paramref name="desc"/> at <paramref name="cursor"/> and its slot's info next.</summary>
    private void Stage(Span<GpuMaterial> staged, ref int cursor, MaterialDesc desc)
    {
        staged[cursor++] = ToGpu(desc);
        infos.Add(new(Alpha: desc.Alpha, TwoSided: DrawsBothSides(desc.Model)));
    }

    private GpuBuffer Create(int materials) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: (ulong)materials * (ulong)sizeof(GpuMaterial),
            usage: BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "materials"
        );

    public void Dispose() => Buffer.Dispose();
}
