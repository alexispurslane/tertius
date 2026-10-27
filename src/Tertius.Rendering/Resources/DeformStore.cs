using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The sources of the deform pass: the deform vertices and the morph deltas of every deforming mesh, each in a
/// device-local storage buffer that grows by doubling (docs/plans/skinning-and-morphs.md, "Buffers and the region").
/// </summary>
/// <remarks>
/// <see cref="Add"/> places a mesh after the meshes before it, and <see cref="Upload"/> records the copy of every mesh
/// added since the upload before. The upload queue keeps each buffer within the device's maxStorageBufferRange.
/// </remarks>
internal sealed class DeformStore : IDisposable
{
    private const int InitialVertices = 4 * 1024;
    private const int InitialDeltas = 4 * 1024;

    private readonly List<GpuDeformVertex[]> pendingVertices = [];
    private readonly List<GpuMorphDelta[]> pendingDeltas = [];
    private int placedVertices;
    private int placedDeltas;

    /// <param name="maxBytes">The most bytes of each buffer: the device's maxStorageBufferRange.</param>
    public DeformStore(VulkanDevice device, DeviceAllocator allocator, long maxBytes)
    {
        Vertices = new(
            device: device,
            allocator: allocator,
            name: "deform vertices",
            initialCapacity: InitialVertices,
            maxBytes: maxBytes
        );
        Deltas = new(
            device: device,
            allocator: allocator,
            name: "morph deltas",
            initialCapacity: InitialDeltas,
            maxBytes: maxBytes
        );
    }

    public DeviceArray<GpuDeformVertex> Vertices { get; }
    public DeviceArray<GpuMorphDelta> Deltas { get; }

    /// <summary>Counts every replacement of either buffer, so that a descriptor set that points at them knows to follow.</summary>
    public int Version => Vertices.Version + Deltas.Version;

    /// <summary>
    /// Places <paramref name="packed"/> after the meshes added before it and queues its data for <see cref="Upload"/>.
    /// Returns its deformation with the mesh's first deform vertex and first delta in this store.
    /// </summary>
    public MeshDeform Add(PackedDeform packed)
    {
        MeshDeform placed = packed.Deform with { Source = placedVertices, Deltas = placedDeltas };
        placedVertices += packed.Vertices.Length;
        placedDeltas += packed.Deltas.Length;
        pendingVertices.Add(packed.Vertices);
        pendingDeltas.Add(packed.Deltas);
        return placed;
    }

    /// <summary>Records the upload of every mesh added since the upload before. Its data is usable by passes recorded after.</summary>
    public void Upload(
        CommandBuffer commandBuffer,
        StagingArena staging,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        Vertices.Append(
            commandBuffer: commandBuffer,
            parts: pendingVertices,
            staging: staging,
            barriers: barriers,
            releases: releases,
            frame: frame
        );
        Deltas.Append(
            commandBuffer: commandBuffer,
            parts: pendingDeltas,
            staging: staging,
            barriers: barriers,
            releases: releases,
            frame: frame
        );
        pendingVertices.Clear();
        pendingDeltas.Clear();
    }

    public void Dispose()
    {
        Deltas.Dispose();
        Vertices.Dispose();
    }
}

/// <summary>
/// A device-local storage array of <typeparamref name="T"/> that only grows: appends go after <see cref="Count"/>, and a
/// growth doubles the capacity up to a cap, copies the old elements on the GPU and retires the old buffer until the
/// frames that use it finish.
/// </summary>
internal sealed unsafe class DeviceArray<T> : IDisposable
    where T : unmanaged
{
    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly string name;
    private readonly int maxCapacity;
    private int capacity;

    /// <param name="maxBytes">The most bytes of the buffer, which caps the capacity.</param>
    public DeviceArray(VulkanDevice device, DeviceAllocator allocator, string name, int initialCapacity, long maxBytes)
    {
        this.device = device;
        this.allocator = allocator;
        this.name = name;
        maxCapacity = (int)Math.Min(int.MaxValue, maxBytes / sizeof(T));
        capacity = Math.Min(initialCapacity, maxCapacity);
        Buffer = Create(capacity);
    }

    public GpuBuffer Buffer { get; private set; }

    /// <summary>The elements on the GPU.</summary>
    public int Count { get; private set; }

    /// <summary>Counts the replacements of <see cref="Buffer"/>.</summary>
    public int Version { get; private set; }

    /// <summary>Records the copy of <paramref name="parts"/>, one after another, after the elements on the GPU.</summary>
    public void Append(
        CommandBuffer commandBuffer,
        List<T[]> parts,
        StagingArena staging,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        int added = 0;
        foreach (T[] part in parts)
            added += part.Length;
        if (added == 0)
            return;
        Reserve(
            commandBuffer: commandBuffer,
            count: Count + added,
            barriers: barriers,
            releases: releases,
            frame: frame
        );

        StagingSlice stage = staging.Allocate((ulong)added * (ulong)sizeof(T));
        var staged = new Span<T>(stage.Pointer, added);
        int cursor = 0;
        foreach (T[] part in parts)
        {
            part.CopyTo(staged[cursor..]);
            cursor += part.Length;
        }

        barriers.Buffer(Buffer, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var region = new BufferCopy(stage.Offset, (ulong)Count * (ulong)sizeof(T), stage.Size);
        device.Vk.CmdCopyBuffer(
            commandBuffer: commandBuffer,
            srcBuffer: stage.Buffer.Handle,
            dstBuffer: Buffer.Handle,
            regionCount: 1,
            pRegions: &region
        );
        Count += added;
    }

    /// <summary>Grows the buffer to hold <paramref name="count"/> elements, which the upload queue keeps within the cap.</summary>
    private void Reserve(
        CommandBuffer commandBuffer,
        int count,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        Contract.Requires(count <= maxCapacity);
        if (count <= capacity)
            return;
        long grown = capacity;
        while (grown < count)
            grown *= 2;
        int next = (int)Math.Min(grown, maxCapacity);
        GpuBuffer replacement = Create(next);
        if (Count > 0)
        {
            barriers.Buffer(Buffer, BufferUse.TransferSource);
            barriers.Buffer(replacement, BufferUse.TransferDestination);
            barriers.Flush(device.Vk, commandBuffer);
            var copy = new BufferCopy(0, 0, (ulong)Count * (ulong)sizeof(T));
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
        capacity = next;
        Version++;
    }

    private GpuBuffer Create(int elements) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: (ulong)elements * (ulong)sizeof(T),
            usage: BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: name
        );

    public void Dispose() => Buffer.Dispose();
}
