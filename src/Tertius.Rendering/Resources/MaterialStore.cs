using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// All materials in one device-local storage buffer, indexed by <see cref="MaterialId.Value"/>. Index 0 is the
/// default material (white, specular 0.5, gloss 0.5) that <see cref="MaterialId.None"/> and unknown ids use.
/// The buffer grows by doubling like the geometry buffers.
/// </summary>
internal sealed unsafe class MaterialStore : IDisposable
{
    /// <summary>The first id that <see cref="VulkanRenderer.CreateMaterial"/> hands out.</summary>
    public const int FirstUserIndex = 1;
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

    /// <summary>Materials on the GPU, including the default.</summary>
    public int Count { get; private set; }

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
    public void Upload(CommandBuffer commandBuffer, List<(int Id, MaterialDesc Desc)> pending, StagingArena staging,
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
                var copy = new BufferCopy(0, 0, (ulong)(Count * sizeof(GpuMaterial)));
                device.Vk.CmdCopyBuffer(commandBuffer, Buffer.Handle, replacement.Handle, 1, &copy);
            }
            releases.Retire(Buffer, frame);
            Buffer = replacement;
            capacity = grown;
            Version++;
        }

        StagingSlice stage = staging.Allocate((ulong)(added * sizeof(GpuMaterial)));
        Span<GpuMaterial> staged = MemoryMarshal.Cast<byte, GpuMaterial>(stage.Bytes);
        int cursor = 0;
        if (Count == 0)
            staged[cursor++] = ToGpu(new MaterialDesc(Vector4.One, Albedo: TextureId.None, Normal: TextureId.None, SpecularGloss: TextureId.None));
        foreach ((int id, MaterialDesc desc) in pending)
        {
            if (id != first + cursor)
                throw new InvalidOperationException($"Material {id} arrived out of order; expected {first + cursor}.");
            staged[cursor++] = ToGpu(desc);
        }

        barriers.Buffer(Buffer, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(stage.Offset, (ulong)(first * sizeof(GpuMaterial)), stage.Size);
        device.Vk.CmdCopyBuffer(commandBuffer, stage.Buffer.Handle, Buffer.Handle, 1, &region);
        Count += added;
    }

    private GpuBuffer Create(int materials) => GpuBuffer.Create(device, allocator, (ulong)(materials * sizeof(GpuMaterial)),
        BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit, MemoryUsage.GpuOnly, "materials");

    public void Dispose() => Buffer.Dispose();
}
