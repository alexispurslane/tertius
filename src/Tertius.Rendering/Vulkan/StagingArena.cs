using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// Linear allocator over host-visible buffers for one frame in flight's uploads. It is reset only after that frame's
/// fence has signaled, so the copies that read it are done. When a frame needs more space, another buffer is added;
/// at the next reset the buffers are merged into one buffer of the combined size.
/// </summary>
internal sealed unsafe class StagingArena(VulkanDevice device, DeviceAllocator allocator, string name) : IDisposable
{
    private const ulong MinimumSize = 4ul << 20;

    private readonly VulkanDevice device = device;
    private readonly DeviceAllocator allocator = allocator;
    private readonly string name = name;
    private readonly List<GpuBuffer> buffers = [];
    private ulong offset;

    /// <summary>The bytes handed out since the last reset.</summary>
    public ulong Used { get; private set; }

    /// <summary>Reserves <paramref name="size"/> bytes. Write them through <see cref="StagingSlice.Pointer"/> before submitting.</summary>
    public StagingSlice Allocate(ulong size, ulong alignment = 16)
    {
        ArgumentOutOfRangeException.ThrowIfZero(size);
        GpuBuffer? current = buffers.Count > 0 ? buffers[^1] : null;
        ulong aligned = (offset + alignment - 1) & ~(alignment - 1);
        if (current == null || aligned + size > current.Size)
        {
            ulong capacity = Math.Max(MinimumSize, Math.Max(size, current == null ? 0 : current.Size * 2));
            current = GpuBuffer.Create(
                device: device,
                allocator: allocator,
                size: capacity,
                usage: BufferUsageFlags.TransferSrcBit,
                memoryUsage: MemoryUsage.Upload,
                name: $"{name} staging {buffers.Count}"
            );
            buffers.Add(current);
            aligned = 0;
        }

        offset = aligned + size;
        Used += size;
        return new(buffer: current, offset: aligned, pointer: current.Mapped + aligned, size: size);
    }

    /// <summary>Starts over. The frame that used this arena must be complete.</summary>
    public void Reset()
    {
        if (buffers.Count > 1)
        {
            ulong total = 0;
            foreach (GpuBuffer buffer in buffers)
            {
                total += buffer.Size;
                buffer.Dispose();
            }

            buffers.Clear();
            buffers.Add(
                GpuBuffer.Create(
                    device: device,
                    allocator: allocator,
                    size: total,
                    usage: BufferUsageFlags.TransferSrcBit,
                    memoryUsage: MemoryUsage.Upload,
                    name: $"{name} staging"
                )
            );
        }

        offset = 0;
        Used = 0;
    }

    public void Dispose()
    {
        foreach (GpuBuffer buffer in buffers)
            buffer.Dispose();
        buffers.Clear();
    }
}

/// <summary>A reserved range of a staging buffer.</summary>
internal readonly unsafe struct StagingSlice(GpuBuffer buffer, ulong offset, byte* pointer, ulong size)
{
    public GpuBuffer Buffer { get; } = buffer;
    public ulong Offset { get; } = offset;
    public byte* Pointer { get; } = pointer;
    public ulong Size { get; } = size;

    public Span<byte> Bytes => new(Pointer, checked((int)Size));
}
