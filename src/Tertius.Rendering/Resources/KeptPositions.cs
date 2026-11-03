using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The kept deformed positions (docs/plans/temporal-renderer.md, "Kept deformed positions"): two device buffers that hold
/// three floats per vertex of the deform region. The deform pass writes the buffer of the parity of the drawn frames
/// (<see cref="PendingFrame.Parity"/>), and the G-buffer reads the other one, which the last drawn frame wrote. A frame
/// that throws does not advance the count, so the next frame writes the same buffer again and reads the last drawn
/// frame's.
/// </summary>
/// <remarks>
/// A vertex takes 12 bytes here and 52 in the region's window of the vertex buffer, so a buffer that holds the region
/// stays within the device's maxStorageBufferRange whenever the window does.
/// </remarks>
internal sealed class KeptPositions(VulkanDevice device, DeviceAllocator allocator) : IDisposable
{
    /// <summary>The bytes of one kept position: three floats.</summary>
    public const ulong VertexBytes = 3 * sizeof(float);

    private readonly GpuBuffer?[] buffers = new GpuBuffer?[2];

    /// <summary>The vertices that each buffer holds: 0 before the first deformed copy.</summary>
    public int Capacity { get; private set; }

    /// <summary>The buffer that the frame of <paramref name="parity"/> writes, or null before the first deformed copy.</summary>
    public GpuBuffer? Written(int parity) => buffers[parity];

    /// <summary>The buffer that the frame of <paramref name="parity"/> reads: the one that the last drawn frame wrote.</summary>
    public GpuBuffer? Read(int parity) => buffers[1 - parity];

    /// <summary>
    /// Grows both buffers to hold <paramref name="vertices"/>, the deform region's capacity, and copies their contents
    /// into the new ones, so that the next frame still reads the last drawn frame's positions. The old buffers are retired
    /// after <paramref name="frame"/>.
    /// </summary>
    public unsafe void Reserve(
        int vertices,
        CommandBuffer commandBuffer,
        BarrierBatch barriers,
        DeferredReleases releases,
        long frame
    )
    {
        if (vertices <= Capacity)
            return;
        for (int parity = 0; parity < buffers.Length; parity++)
        {
            var replacement = GpuBuffer.Create(
                device: device,
                allocator: allocator,
                size: (ulong)vertices * VertexBytes,
                usage: BufferUsageFlags.StorageBufferBit
                    | BufferUsageFlags.TransferDstBit
                    | BufferUsageFlags.TransferSrcBit,
                memoryUsage: MemoryUsage.GpuOnly,
                name: $"kept positions {parity}"
            );
            if (buffers[parity] is { } old)
            {
                barriers.Buffer(old, BufferUse.TransferSource);
                barriers.Buffer(replacement, BufferUse.TransferDestination);
                barriers.Flush(device.Vk, commandBuffer);
                var copy = new BufferCopy(0, 0, (ulong)Capacity * VertexBytes);
                device.Vk.CmdCopyBuffer(
                    commandBuffer: commandBuffer,
                    srcBuffer: old.Handle,
                    dstBuffer: replacement.Handle,
                    regionCount: 1,
                    pRegions: &copy
                );
                releases.Retire(old, frame);
            }

            buffers[parity] = replacement;
        }

        Capacity = vertices;
    }

    public void Dispose()
    {
        foreach (GpuBuffer? buffer in buffers)
            buffer?.Dispose();
    }
}
