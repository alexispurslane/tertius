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
    public void Upload(CommandBuffer commandBuffer, List<(MeshId Id, MeshData Data)> pending, StagingArena staging,
        BarrierBatch barriers, DeferredReleases releases, long frame)
    {
        if (pending.Count == 0)
            return;
        ulong newVertices = 0, newIndices = 0;
        foreach ((_, MeshData data) in pending)
        {
            newVertices += (ulong)data.Vertices.Length;
            newIndices += (ulong)data.Indices.Length;
        }

        Grow(commandBuffer, VertexCount + newVertices, IndexCount + newIndices, barriers, releases, frame);

        int vertexSize = Unsafe.SizeOf<Vertex>();
        StagingSlice vertexStage = staging.Allocate(newVertices * (ulong)vertexSize);
        StagingSlice indexStage = staging.Allocate(newIndices * sizeof(uint));
        ulong vertexCursor = 0, indexCursor = 0;
        foreach ((MeshId id, MeshData data) in pending)
        {
            // The upload queue hands out dense ids under one lock and drains them in order, so each id names the next
            // slot. If that broke, an instance would draw another mesh.
            Contract.Assert(id.Value == meshes.Count);
            MemoryMarshal.AsBytes(data.Vertices.AsSpan()).CopyTo(
                new(vertexStage.Pointer + vertexCursor * (ulong)vertexSize, data.Vertices.Length * vertexSize));
            MemoryMarshal.AsBytes(data.Indices.AsSpan()).CopyTo(new(indexStage.Pointer + indexCursor * sizeof(uint),
                data.Indices.Length * sizeof(uint)));
            meshes.Add(new((uint)(IndexCount + indexCursor), (uint)data.Indices.Length,
                checked((int)(VertexCount + vertexCursor)), Radius(data.Vertices)));
            vertexCursor += (ulong)data.Vertices.Length;
            indexCursor += (ulong)data.Indices.Length;
        }

        barriers.Buffer(VertexBuffer, BufferUse.TransferDestination);
        barriers.Buffer(IndexBuffer, BufferUse.TransferDestination);
        barriers.Flush(device.Vk, commandBuffer);
        var vertexCopy = new BufferCopy(vertexStage.Offset, VertexCount * (ulong)vertexSize, vertexStage.Size);
        var indexCopy = new BufferCopy(indexStage.Offset, IndexCount * sizeof(uint), indexStage.Size);
        device.Vk.CmdCopyBuffer(commandBuffer, vertexStage.Buffer.Handle, VertexBuffer.Handle, 1, &vertexCopy);
        device.Vk.CmdCopyBuffer(commandBuffer, indexStage.Buffer.Handle, IndexBuffer.Handle, 1, &indexCopy);
        VertexCount += newVertices;
        IndexCount += newIndices;
    }

    private void Grow(CommandBuffer commandBuffer, ulong vertices, ulong indices, BarrierBatch barriers,
        DeferredReleases releases, long frame)
    {
        if (vertices > vertexCapacity)
        {
            ulong capacity = vertexCapacity;
            while (capacity < vertices)
                capacity *= 2;
            VertexBuffer = Replace(commandBuffer, VertexBuffer, CreateVertexBuffer(capacity),
                VertexCount * (ulong)Unsafe.SizeOf<Vertex>(), barriers, releases, frame);
            vertexCapacity = capacity;
        }

        if (indices > indexCapacity)
        {
            ulong capacity = indexCapacity;
            while (capacity < indices)
                capacity *= 2;
            IndexBuffer = Replace(commandBuffer, IndexBuffer, CreateIndexBuffer(capacity), IndexCount * sizeof(uint),
                barriers, releases, frame);
            indexCapacity = capacity;
        }
    }

    private GpuBuffer Replace(CommandBuffer commandBuffer, GpuBuffer old, GpuBuffer replacement, ulong usedBytes,
        BarrierBatch barriers, DeferredReleases releases, long frame)
    {
        if (usedBytes > 0)
        {
            barriers.Buffer(old, BufferUse.TransferSource);
            barriers.Buffer(replacement, BufferUse.TransferDestination);
            barriers.Flush(device.Vk, commandBuffer);
            var copy = new BufferCopy(0, 0, usedBytes);
            device.Vk.CmdCopyBuffer(commandBuffer, old.Handle, replacement.Handle, 1, &copy);
        }

        releases.Retire(old, frame);
        return replacement;
    }

    private GpuBuffer CreateVertexBuffer(ulong vertices) => GpuBuffer.Create(device, allocator,
        vertices * (ulong)Unsafe.SizeOf<Vertex>(),
        BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
        MemoryUsage.GpuOnly, "global vertices");

    private GpuBuffer CreateIndexBuffer(ulong indices) => GpuBuffer.Create(device, allocator, indices * sizeof(uint),
        BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
        MemoryUsage.GpuOnly, "global indices");

    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
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
