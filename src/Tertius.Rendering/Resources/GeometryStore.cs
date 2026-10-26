using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One global vertex buffer and one global index buffer. Each mesh is a range of both; indirect commands address
/// it with firstIndex and vertexOffset, so all meshes draw from the same bound buffers. Both buffers grow by
/// doubling: the old contents are copied on the GPU and the old buffer is retired until frames using it finish.
/// </summary>
/// <remarks>
/// The vertex buffer holds the static capacity, then the deform region, which the deform pass writes in every frame
/// (docs/plans/skinning-and-morphs.md, "Buffers and the region"). A growth of either part makes a new buffer and copies
/// only the static vertices.
/// </remarks>
internal sealed unsafe class GeometryStore : IDisposable
{
    private const ulong InitialVertices = 64 * 1024;
    private const ulong InitialIndices = 256 * 1024;

    /// <summary>
    /// The region's base and capacity are multiples of this many vertices: 768 bytes, a multiple of the 256 bytes of the
    /// spec's largest minStorageBufferOffsetAlignment.
    /// </summary>
    private const int RegionAlignment = 16;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly List<MeshInfo> meshes = [];

    /// <summary>The most vertices of the region: its window, a storage buffer range, stays within maxStorageBufferRange.</summary>
    private readonly int maxRegion;
    private ulong vertexCapacity;
    private ulong indexCapacity;
    private int regionCapacity;

    public GeometryStore(VulkanDevice device, DeviceAllocator allocator)
    {
        this.device = device;
        this.allocator = allocator;
        vertexCapacity = InitialVertices;
        indexCapacity = InitialIndices;
        maxRegion = MaxRegion(device.Properties.Limits.MaxStorageBufferRange);
        VertexBuffer = CreateVertexBuffer(vertexCapacity);
        IndexBuffer = CreateIndexBuffer(indexCapacity);
    }

    public GpuBuffer VertexBuffer { get; private set; }
    public GpuBuffer IndexBuffer { get; private set; }

    /// <summary>Meshes on the GPU, indexed by <see cref="MeshId.Value"/>. <see cref="MeshId.None"/> has no entry.</summary>
    public ReadOnlySpan<MeshInfo> Meshes => CollectionsMarshal.AsSpan(meshes);

    public ulong VertexCount { get; private set; }

    public ulong IndexCount { get; private set; }

    /// <summary>The deform region: right after the static capacity, empty until <see cref="ReserveDeformed"/> grows it.</summary>
    public DeformRegion Region => new(Base: checked((int)vertexCapacity), Capacity: regionCapacity);

    /// <summary>
    /// The most vertices of a region within <paramref name="maxStorageBufferRange"/> bytes, a multiple of
    /// <see cref="RegionAlignment"/>.
    /// </summary>
    internal static int MaxRegion(uint maxStorageBufferRange) =>
        (int)(maxStorageBufferRange / (uint)Unsafe.SizeOf<Vertex>() / RegionAlignment * RegionAlignment);

    /// <summary>
    /// The region's capacity for a need of <paramref name="vertices"/>: the next power of two, at least
    /// <see cref="RegionAlignment"/>, at most <paramref name="maxRegion"/>.
    /// </summary>
    internal static int RegionCapacityFor(int vertices, int maxRegion) =>
        (int)Math.Min(BitOperations.RoundUpToPowerOf2((uint)Math.Max(vertices, RegionAlignment)), (uint)maxRegion);

    /// <summary>
    /// Records the upload of queued meshes. They are usable by draws recorded after this call. Each deforming mesh of
    /// <paramref name="deforms"/>, at its mesh's index, is placed in <paramref name="deformStore"/>.
    /// </summary>
    public void Upload(
        CommandBuffer commandBuffer,
        List<(MeshId Id, MeshData Data)> pending,
        List<PackedDeform?> deforms,
        DeformStore deformStore,
        StagingArena staging,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        if (pending.Count == 0)
            return;
        ulong newVertices = 0,
            newIndices = 0;
        foreach ((_, MeshData data) in pending)
        {
            newVertices += (ulong)data.Vertices.Length;
            newIndices += (ulong)data.Indices.Length;
        }

        Grow(
            commandBuffer: commandBuffer,
            vertices: VertexCount + newVertices,
            indices: IndexCount + newIndices,
            barriers: barriers,
            releases: releases,
            frame: frame
        );

        int vertexSize = Unsafe.SizeOf<Vertex>();
        StagingSlice vertexStage = staging.Allocate(newVertices * (ulong)vertexSize);
        StagingSlice indexStage = staging.Allocate(newIndices * sizeof(uint));
        Stage(
            pending: pending,
            deforms: deforms,
            deformStore: deformStore,
            vertexStage: vertexStage,
            indexStage: indexStage
        );

        barriers.Buffer(VertexBuffer, BufferUse.TransferDestination);
        barriers.Buffer(IndexBuffer, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var vertexCopy = new BufferCopy(vertexStage.Offset, VertexCount * (ulong)vertexSize, vertexStage.Size);
        var indexCopy = new BufferCopy(indexStage.Offset, IndexCount * sizeof(uint), indexStage.Size);
        device.Vk.CmdCopyBuffer(
            commandBuffer: commandBuffer,
            srcBuffer: vertexStage.Buffer.Handle,
            dstBuffer: VertexBuffer.Handle,
            regionCount: 1,
            pRegions: &vertexCopy
        );
        device.Vk.CmdCopyBuffer(
            commandBuffer: commandBuffer,
            srcBuffer: indexStage.Buffer.Handle,
            dstBuffer: IndexBuffer.Handle,
            regionCount: 1,
            pRegions: &indexCopy
        );
        VertexCount += newVertices;
        IndexCount += newIndices;
    }

    /// <summary>
    /// Writes the vertices and indices of <paramref name="pending"/> into the staging slices, one mesh after another, and
    /// adds each mesh's range in the buffers and its deformation to the mesh table.
    /// </summary>
    private void Stage(
        List<(MeshId Id, MeshData Data)> pending,
        List<PackedDeform?> deforms,
        DeformStore deformStore,
        StagingSlice vertexStage,
        StagingSlice indexStage
    )
    {
        Contract.Requires(deforms.Count == pending.Count);
        ulong vertexCursor = 0,
            indexCursor = 0;
        for (int mesh = 0; mesh < pending.Count; mesh++)
        {
            (MeshId id, MeshData data) = pending[mesh];
            // The upload queue hands out dense ids under one lock and drains them in order, so each id names the next
            // slot. If that broke, an instance would draw another mesh.
            Contract.Assert(id.Value == meshes.Count);
            var staged = new Span<Vertex>((Vertex*)vertexStage.Pointer + vertexCursor, data.Vertices.Length);
            for (int i = 0; i < staged.Length; i++)
                staged[i] = Staged(data.Vertices[i]);
            MemoryMarshal
                .AsBytes(data.Indices.AsSpan())
                .CopyTo(new(indexStage.Pointer + indexCursor * sizeof(uint), data.Indices.Length * sizeof(uint)));
            meshes.Add(
                new(
                    FirstIndex: (uint)(IndexCount + indexCursor),
                    IndexCount: (uint)data.Indices.Length,
                    VertexOffset: checked((int)(VertexCount + vertexCursor)),
                    Radius: Radius(data.Vertices),
                    Deform: deforms[mesh] is { } packed ? deformStore.Add(packed) : null
                )
            );
            vertexCursor += (ulong)data.Vertices.Length;
            indexCursor += (ulong)data.Indices.Length;
        }
    }

    private void Grow(
        CommandBuffer commandBuffer,
        ulong vertices,
        ulong indices,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        if (vertices > vertexCapacity)
        {
            ulong capacity = vertexCapacity;
            while (capacity < vertices)
                capacity *= 2;
            VertexBuffer = Replace(
                commandBuffer: commandBuffer,
                old: VertexBuffer,
                replacement: CreateVertexBuffer(capacity + (ulong)regionCapacity),
                usedBytes: VertexCount * (ulong)Unsafe.SizeOf<Vertex>(),
                barriers: barriers,
                releases: releases,
                frame: frame
            );
            vertexCapacity = capacity;
        }

        if (indices > indexCapacity)
        {
            ulong capacity = indexCapacity;
            while (capacity < indices)
                capacity *= 2;
            IndexBuffer = Replace(
                commandBuffer: commandBuffer,
                old: IndexBuffer,
                replacement: CreateIndexBuffer(capacity),
                usedBytes: IndexCount * sizeof(uint),
                barriers: barriers,
                releases: releases,
                frame: frame
            );
            indexCapacity = capacity;
        }
    }

    /// <summary>
    /// Grows the deform region to hold <paramref name="vertices"/>, up to the most that its window allows, with a new
    /// vertex buffer that keeps the static vertices. The region needs no copy: every frame writes it anew.
    /// </summary>
    public void ReserveDeformed(
        int vertices,
        CommandBuffer commandBuffer,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        if (vertices <= regionCapacity)
            return;
        int capacity = RegionCapacityFor(vertices, maxRegion);
        if (capacity <= regionCapacity)
            return;
        VertexBuffer = Replace(
            commandBuffer: commandBuffer,
            old: VertexBuffer,
            replacement: CreateVertexBuffer(vertexCapacity + (ulong)capacity),
            usedBytes: VertexCount * (ulong)Unsafe.SizeOf<Vertex>(),
            barriers: barriers,
            releases: releases,
            frame: frame
        );
        regionCapacity = capacity;
    }

    private GpuBuffer Replace(
        CommandBuffer commandBuffer,
        GpuBuffer old,
        GpuBuffer replacement,
        ulong usedBytes,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        if (usedBytes > 0)
        {
            barriers.Buffer(old, BufferUse.TransferSource);
            barriers.Buffer(replacement, BufferUse.TransferDestination);
            barriers.Flush(device.Vk, commandBuffer);
            var copy = new BufferCopy(0, 0, usedBytes);
            device.Vk.CmdCopyBuffer(
                commandBuffer: commandBuffer,
                srcBuffer: old.Handle,
                dstBuffer: replacement.Handle,
                regionCount: 1,
                pRegions: &copy
            );
        }

        releases.Retire(old, frame);
        return replacement;
    }

    private GpuBuffer CreateVertexBuffer(ulong vertices) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: vertices * (ulong)Unsafe.SizeOf<Vertex>(),
            // Storage: the deform pass writes the region through a window of the buffer.
            usage: BufferUsageFlags.VertexBufferBit
                | BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "global vertices"
        );

    private GpuBuffer CreateIndexBuffer(ulong indices) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: indices * sizeof(uint),
            usage: BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: "global indices"
        );

    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }

    /// <summary>
    /// <paramref name="vertex"/> as the GPU gets it: its normal and the direction of its tangent at unit length, or zero
    /// when they have no direction. The shaders then never square a long normal past the float range, and give a zero
    /// normal a direction themselves (Shaders/vectors.glsl). The upload queue has refused a vertex with a number that is
    /// not finite.
    /// </summary>
    internal static Vertex Staged(in Vertex vertex)
    {
        Vector3 tangent = UnitOrZero(new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z));
        return vertex with { Normal = UnitOrZero(vertex.Normal), Tangent = new(tangent, vertex.Tangent.W) };
    }

    /// <summary>The direction of <paramref name="v"/>, computed in doubles, so that no square of a part overflows or underflows.</summary>
    private static Vector3 UnitOrZero(Vector3 v)
    {
        double length = Math.Sqrt((double)v.X * v.X + (double)v.Y * v.Y + (double)v.Z * v.Z);
        return length > 0 ? new((float)(v.X / length), (float)(v.Y / length), (float)(v.Z / length)) : Vector3.Zero;
    }

    /// <summary>The bounding radius of a mesh around its origin.</summary>
    public static float Radius(ReadOnlySpan<Vertex> vertices)
    {
        float radiusSquared = 0f;
        foreach (ref readonly Vertex vertex in vertices)
            radiusSquared = MathF.Max(radiusSquared, Vector3.Dot(vertex.Position, vertex.Position));
        return MathF.Sqrt(radiusSquared);
    }
}
