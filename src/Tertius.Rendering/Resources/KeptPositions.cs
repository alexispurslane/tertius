using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The kept deformed positions (docs/plans/temporal-renderer.md, "Kept deformed positions"): two device buffers that hold
/// three floats per vertex of the deform region. The deform pass writes the buffer of the parity of the drawn frames
/// (<see cref="FrameHistory.PendingFrame.Parity"/>), and the G-buffer reads the other one, which the last drawn frame
/// wrote. A frame that throws does not advance the count, so the next frame writes the same buffer again and reads the last
/// drawn frame's.
/// </summary>
/// <remarks>
/// A vertex takes 12 bytes here and 52 in the region's window of the vertex buffer, so a buffer that holds the region
/// stays within the device's maxStorageBufferRange whenever the window does. <see cref="GeometryStore.ReserveDeformed"/>,
/// the owner of the region, grows these buffers with it.
/// <para>
/// A growth replaces both buffers, and the frame that grows them copies the old ones into the new ones. The copy runs only
/// when that frame is submitted, so the old buffers stay the source of the copy until <see cref="Drew"/>: each frame
/// before it records the copy again. A frame that throws after a growth then leaves the next frame the last drawn frame's
/// positions, as a frame that throws must leave every stock (docs/plans/temporal-renderer.md, decision 20).
/// </para>
/// </remarks>
internal sealed class KeptPositions(VulkanDevice device, DeviceAllocator allocator) : IDisposable
{
    /// <summary>The bytes of one kept position: three floats.</summary>
    public const ulong VertexBytes = 3 * sizeof(float);

    private readonly GpuBuffer?[] buffers = new GpuBuffer?[2];

    /// <summary>
    /// The buffers that the last growth replaced, by parity, while no drawn frame has copied them yet, or null. They hold
    /// <see cref="sourceVertices"/> vertices each.
    /// </summary>
    private readonly GpuBuffer?[] sources = new GpuBuffer?[2];

    private int sourceVertices;

    /// <summary>The copies of a replaced buffer into its replacement that <see cref="Reserve"/> recorded so far. A test hook.</summary>
    internal int CopiesRecorded { get; private set; }

    /// <summary>The vertices that each buffer holds: 0 before the first deformed copy.</summary>
    public int Capacity { get; private set; }

    /// <summary>The buffer that the frame of <paramref name="parity"/> writes, or null before the first deformed copy.</summary>
    public GpuBuffer? Written(int parity) => buffers[parity];

    /// <summary>The buffer that the frame of <paramref name="parity"/> reads: the one that the last drawn frame wrote.</summary>
    public GpuBuffer? Read(int parity) => buffers[1 - parity];

    /// <summary>
    /// Grows both buffers to hold <paramref name="vertices"/>, the deform region's capacity, and records the copy of the
    /// last drawn frame's positions into them, in every frame from the growth to the first drawn one. Buffers that a
    /// growth replaced before any drawn frame copied them, and so hold nothing, retire after <paramref name="frame"/>.
    /// </summary>
    public void Reserve(
        int vertices,
        CommandBuffer commandBuffer,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        if (vertices > Capacity)
            Grow(vertices: vertices, releases: releases, frame: frame);
        if (sources[0] is not null)
            RecordCopies(commandBuffer: commandBuffer, barriers: barriers);
    }

    /// <summary>
    /// Commits the frame <paramref name="frame"/> after its submission: a copy that it recorded will run, so the buffers
    /// that the growth replaced retire after it.
    /// </summary>
    public void Drew(DeferredReleases releases, long frame)
    {
        for (int parity = 0; parity < sources.Length; parity++)
        {
            if (sources[parity] is { } source)
                releases.Retire(source, frame);
            sources[parity] = null;
        }
    }

    private void Grow(int vertices, DeferredReleases releases, long frame)
    {
        bool copyPending = sources[0] is not null;
        for (int parity = 0; parity < buffers.Length; parity++)
        {
            if (buffers[parity] is { } old)
            {
                // With a copy pending, the current buffer is the target of a copy that never ran, so the sources stay.
                if (copyPending)
                    releases.Retire(old, frame);
                else
                    sources[parity] = old;
            }

            buffers[parity] = GpuBuffer.Create(
                device: device,
                allocator: allocator,
                size: (ulong)vertices * VertexBytes,
                usage: BufferUsageFlags.StorageBufferBit
                    | BufferUsageFlags.TransferDstBit
                    | BufferUsageFlags.TransferSrcBit,
                memoryUsage: MemoryUsage.GpuOnly,
                name: $"kept positions {parity}"
            );
        }

        if (!copyPending)
            sourceVertices = Capacity;
        Capacity = vertices;
    }

    private unsafe void RecordCopies(CommandBuffer commandBuffer, BarrierBatch barriers)
    {
        for (int parity = 0; parity < buffers.Length; parity++)
        {
            // A growth with sources replaced both buffers of a parity, so either both are there or neither.
            if (sources[parity] is not { } source || buffers[parity] is not { } target)
                continue;
            barriers.Buffer(source, BufferUse.TransferSource);
            barriers.Buffer(target, BufferUse.TransferDestination);
            barriers.Flush(device.Vk, commandBuffer);
            var copy = new BufferCopy(0, 0, (ulong)sourceVertices * VertexBytes);
            device.Vk.CmdCopyBuffer(
                commandBuffer: commandBuffer,
                srcBuffer: source.Handle,
                dstBuffer: target.Handle,
                regionCount: 1,
                pRegions: &copy
            );
            CopiesRecorded++;
        }
    }

    public void Dispose()
    {
        foreach (GpuBuffer? buffer in (ReadOnlySpan<GpuBuffer?>)[.. buffers, .. sources])
            buffer?.Dispose();
    }
}
