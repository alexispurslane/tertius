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
internal sealed unsafe class GeometryStore : IDisposable
{
    private const ulong InitialVertices = 64 * 1024;
    private const ulong InitialIndices = 256 * 1024;

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly List<MeshInfo> meshes = [];
    private ulong vertexCapacity;
    private ulong indexCapacity;

    public GeometryStore(VulkanDevice device, DeviceAllocator allocator)
    {
        this.device = device;
        this.allocator = allocator;
        vertexCapacity = InitialVertices;
        indexCapacity = InitialIndices;
        VertexBuffer = CreateVertexBuffer(vertexCapacity);
        IndexBuffer = CreateIndexBuffer(indexCapacity);
    }

    public GpuBuffer VertexBuffer { get; private set; }
    public GpuBuffer IndexBuffer { get; private set; }

    /// <summary>Meshes on the GPU, indexed by <see cref="MeshId.Value"/>. <see cref="MeshId.None"/> has no entry.</summary>
    public ReadOnlySpan<MeshInfo> Meshes => CollectionsMarshal.AsSpan(meshes);

    public ulong VertexCount { get; private set; }

    public ulong IndexCount { get; private set; }

    /// <summary>Records the upload of queued meshes. They are usable by draws recorded after this call.</summary>
    public void Upload(
        CommandBuffer commandBuffer,
        List<(MeshId Id, MeshData Data)> pending,
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
        Stage(pending: pending, vertexStage: vertexStage, indexStage: indexStage);

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
    /// adds each mesh's range in the buffers to the mesh table.
    /// </summary>
    private void Stage(List<(MeshId Id, MeshData Data)> pending, StagingSlice vertexStage, StagingSlice indexStage)
    {
        ulong vertexCursor = 0,
            indexCursor = 0;
        foreach ((MeshId id, MeshData data) in pending)
        {
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
                    Radius: Radius(data.Vertices)
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
                replacement: CreateVertexBuffer(capacity),
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
            usage: BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
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
