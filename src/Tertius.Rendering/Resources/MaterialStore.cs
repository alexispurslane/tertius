using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// All materials in one device-local storage buffer. Slot <see cref="DefaultSlot"/> holds the default material
/// (white, specular 0.5, gloss 0.5, the default textures) that <see cref="MaterialId.None"/> and ids this renderer
/// did not hand out resolve to. Material n from <see cref="VulkanRenderer.CreateMaterial"/> lives in slot
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

    public static GpuMaterial ToGpu(MaterialDesc desc) => new()
    {
        BaseColor = desc.BaseColor,
        Emissive = Vector3.Max(desc.Emissive, Vector3.Zero),
        SpecularIntensity = Math.Clamp(desc.SpecularIntensity, 0f, 1f),
        Gloss = Math.Clamp(desc.Gloss, 0f, 1f),
        AlbedoTexture = TextureStore.Resolve(desc.Albedo, TextureStore.White),
        NormalTexture = TextureStore.Resolve(desc.Normal, TextureStore.FlatNormal),
        SpecularGlossTexture = TextureStore.Resolve(desc.SpecularGloss, TextureStore.DefaultSpecularGloss),
    };

    /// <summary>Records the upload of queued materials, creating the default on the first call.</summary>
    public void Upload(CommandBuffer commandBuffer, List<(MaterialId Id, MaterialDesc Desc)> pending, StagingArena staging,
        BarrierBatch barriers, DeferredReleases releases, long frame)
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
                device.Vk.CmdCopyBuffer(commandBuffer, Buffer.Handle, replacement.Handle, 1, &copy);
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
            staged[cursor++] = ToGpu(new MaterialDesc(Vector4.One));
        foreach ((MaterialId id, MaterialDesc desc) in pending)
        {
            // The upload queue hands out dense ids under one lock and drains them in order, so each id names the next
            // slot. If that broke, an instance would draw with another material.
            Contract.Assert(Slot(id) == (uint)(first + cursor));
            staged[cursor++] = ToGpu(desc);
        }

        barriers.Buffer(Buffer, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(stage.Offset, (ulong)first * (ulong)sizeof(GpuMaterial), stage.Size);
        device.Vk.CmdCopyBuffer(commandBuffer, stage.Buffer.Handle, Buffer.Handle, 1, &region);
        Count += added;
    }

    private GpuBuffer Create(int materials) => GpuBuffer.Create(device, allocator, (ulong)materials * (ulong)sizeof(GpuMaterial),
        BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit, MemoryUsage.GpuOnly, "materials");

    public void Dispose() => Buffer.Dispose();
}
